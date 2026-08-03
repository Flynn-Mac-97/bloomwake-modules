using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Covers the island surface with grass cards — scatters upright card
    /// quads over every painted cell of the tilemap (same source rule as
    /// IslandFoliage: assign a dedicated grass tilemap to paint exact zones,
    /// or leave empty for the whole island via TilemapToSpriteShape). Mask
    /// cells painted at tile-palette Z 1+ grow elevated grass (terraces):
    /// world Y lifts by Z x elevationStep and the Y-band sort follows. All
    /// cards merge into ONE procedural mesh (single draw call — no per-blade
    /// GameObjects).
    ///
    /// All movement/coloring happens in the Flynn/GrassBlades shader:
    /// each card picks one of three colors and sways with the global wind
    /// set by WindManager (_WindStrength/_WindSpeed/_WindDirection).
    /// Per-vertex payload: COLOR = (color rand, phase rand, sway weight, 1),
    /// UV1 = card base position in world space (wind phase, matching the
    /// per-sprite pivot convention in Wind.hlsl).
    ///
    /// Placement is a pure function of (cell, seed): painting new tiles adds
    /// or removes grass locally without reshuffling what's already placed.
    /// The mesh lives on a DontSave container child — nothing serialized;
    /// regenerates live as the tilemap is painted.
    /// </summary>
    [ExecuteAlways]
    public class BillboardGrass : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Tilemap whose painted cells receive grass. If unassigned, taken from the TilemapToSpriteShape on this GameObject (whole-island cover).")]
        [SerializeField] private Tilemap _tilemap;
        [Tooltip("World Y added per cell Z — paint mask cells at Z 1 (Tile Palette Z Position) to grow grass on a raised terrace. Match JsonMapLoader heightStep.")]
        [SerializeField] private float _elevationStep = 0.25f;

        [Header("Procedural masks (cells must share this grid's layout)")]
        [Tooltip("Water paint masks (e.g. WaterGrid): water replaces the surface — blades culled where a water cell's top Z >= the blade's elevation. Editing the mask re-flows grass live.")]
        [SerializeField] private Tilemap[] _waterMasks;
        [Tooltip("Platform paint masks (e.g. PlatformGrid): platforms carry the surface up — blades under a platform lift to its top Z instead of being buried, and blades whose screen band falls on a platform's skirt/raised edge are culled (occlusion). Editing the mask re-flows grass live.")]
        [SerializeField] private Tilemap[] _platformMasks;

        [Header("Cards")]
        [Tooltip("Sliced sprites from grass_blades.png — one is picked per card, UVs from the sprite rect.")]
        [SerializeField] private Sprite[] _cards;
        [Tooltip("Spawn attempts per painted tile.")]
        [SerializeField, Range(1, 8)] private int _cardsPerCell = 3;
        [Tooltip("Spawn probability per attempt.")]
        [SerializeField, Range(0f, 1f)] private float _density = 0.6f;
        [Tooltip("World size of the clumping noise patches — meadows and bare spots.")]
        [SerializeField] private float _clusterScale = 5f;
        [Tooltip("How strongly the clump noise gates spawning (0 = uniform, 1 = only inside clumps).")]
        [SerializeField, Range(0f, 1f)] private float _clusterStrength = 0.5f;
        [Tooltip("Damp spawning on border tiles — the rim belongs to the fringe.")]
        [SerializeField, Range(0f, 1f)] private float _edgeAvoid = 0.3f;
        [Tooltip("Random spread inside the tile (fraction of a cell).")]
        [SerializeField, Range(0f, 1f)] private float _spread = 0.9f;
        [SerializeField] private float _scaleMin = 0.8f;
        [SerializeField] private float _scaleMax = 1.25f;
        [Tooltip("Chance to mirror a card horizontally.")]
        [SerializeField, Range(0f, 1f)] private float _flipChance = 0.5f;
        [Tooltip("Multiply card size by the tilemap's world scale — island maps are scaled (parity with IslandFoliage).")]
        [SerializeField] private bool _matchTilemapScale = true;
        [Tooltip("Deterministic layout seed — same seed + same tiles = same grass.")]
        [SerializeField] private int _seed = 918;

        [Header("Colors (picked per card)")]
        [SerializeField] private Color _colorA = new Color(0.45f, 0.75f, 0.32f);
        [SerializeField] private Color _colorB = new Color(0.33f, 0.62f, 0.26f);
        [SerializeField] private Color _colorC = new Color(0.55f, 0.70f, 0.25f);

        [Header("Wind")]
        [Tooltip("Multiplier on the global WindManager sway for this patch.")]
        [SerializeField, Range(0f, 2f)] private float _windResponse = 1f;

        [Header("Rendering")]
        [Tooltip("Pixels below this alpha are clipped instead of blended — overdraw relief.")]
        [SerializeField, Range(0f, 0.5f)] private float _alphaClip = 0.08f;
        [Tooltip("Material override; empty = runtime material from Flynn/GrassBlades.")]
        [SerializeField] private Material _material;
        [Tooltip("Sorting layer for the grass mesh — use the island's layer so cards draw above the top fill. Empty = Default.")]
        [SerializeField] private string _sortingLayer = "";
        [SerializeField] private int _sortingOrder = 0;

        /// <summary>Set by JsonMapLoader so grass lands on the correct sorting layer for its
        /// elevation. See IslandFoliage.SetSortingLayer for the same pattern.</summary>
        public void SetSortingLayer(string layer) => _sortingLayer = layer;

        [Header("Y-sort vs props")]
        [Tooltip("Split cards into Y bands, one renderer each, so blades interleave with Y-sorted prop sprites. Off = single mesh at Sorting Order.")]
        [SerializeField] private bool _ySortBands;
        [Tooltip("World height of one band — smaller = more accurate interleaving, more draw calls.")]
        [SerializeField] private float _bandHeight = 0.4f;
        [Tooltip("Order at world Y 0 — project convention 5000 (blockout standard).")]
        [SerializeField] private int _ySortBase = 5000;
        [Tooltip("Sorting-order change per world unit of Y (project convention 50).")]
        [SerializeField] private int _orderPerUnit = 50;

        private const string ContainerName = "GrassCards";

        // ---------------------------------------------------------------------
        // VibeLayers Layer_Grass hook — live tuning from the aesthetic stack.
        // Setters only store; the layer controller calls Generate() once after
        // pushing everything (Generate reapplies the MPB too).
        // ---------------------------------------------------------------------
        public float Density { get => _density; set => _density = Mathf.Clamp01(value); }
        public int CardsPerCell { get => _cardsPerCell; set => _cardsPerCell = Mathf.Clamp(value, 1, 8); }
        public float ClusterScale { get => _clusterScale; set => _clusterScale = value; }
        public float ClusterStrength { get => _clusterStrength; set => _clusterStrength = Mathf.Clamp01(value); }
        public float EdgeAvoid { get => _edgeAvoid; set => _edgeAvoid = Mathf.Clamp01(value); }
        public float Spread { get => _spread; set => _spread = Mathf.Clamp01(value); }
        public float ScaleMin { get => _scaleMin; set => _scaleMin = value; }
        public float ScaleMax { get => _scaleMax; set => _scaleMax = value; }
        public float FlipChance { get => _flipChance; set => _flipChance = Mathf.Clamp01(value); }
        public Color ColorA { get => _colorA; set => _colorA = value; }
        public Color ColorB { get => _colorB; set => _colorB = value; }
        public Color ColorC { get => _colorC; set => _colorC = value; }
        public float WindResponse { get => _windResponse; set => _windResponse = value; }
        public float AlphaClip { get => _alphaClip; set => _alphaClip = Mathf.Clamp(value, 0f, 0.5f); }
        public bool YSortBands { get => _ySortBands; set => _ySortBands = value; }
        public float BandHeight { get => _bandHeight; set => _bandHeight = Mathf.Max(value, 0.1f); }
        public int YSortBase { get => _ySortBase; set => _ySortBase = value; }
        public int OrderPerUnit { get => _orderPerUnit; set => _orderPerUnit = value; }

        // ---------------------------------------------------------------------
        // Source rebinding — for GrassMaskBinder. The loader destroys and re-instantiates
        // every stack on Regenerate, so these refs cannot be hand-assigned in the inspector
        // and survive; something has to push them back afterwards. Setters only store, the
        // binder calls Generate() once at the end.
        // ---------------------------------------------------------------------
        public Tilemap SourceTilemap { get => _tilemap; set => _tilemap = value; }
        public float ElevationStep { get => _elevationStep; set => _elevationStep = value; }
        public Tilemap[] PlatformMasks { get => _platformMasks; set => _platformMasks = value; }
        public Tilemap[] WaterMasks { get => _waterMasks; set => _waterMasks = value; }
        public Sprite[] Cards { get => _cards; set => _cards = value; }
        public Material CardMaterial { get => _material; set => _material = value; }

        /// <summary>
        /// Adopt another patch's card set and look wholesale. Every stack the loader clones carries
        /// its OWN copy of these values, so retuning one island silently leaves the rest on whatever
        /// the template shipped with — that is how a level ends up drawing two different grasses over
        /// the same painted cells. GrassMaskBinder calls this so one patch is the whole level's
        /// source of truth. Setter only; the caller drives Generate().
        /// </summary>
        public void CopyLookFrom(BillboardGrass src)
        {
            if (src == null || src == this) return;
            _cards = src._cards;
            _material = src._material;
            _cardsPerCell = src._cardsPerCell;
            _density = src._density;
            _clusterScale = src._clusterScale;
            _clusterStrength = src._clusterStrength;
            _edgeAvoid = src._edgeAvoid;
            _spread = src._spread;
            _scaleMin = src._scaleMin;
            _scaleMax = src._scaleMax;
            _flipChance = src._flipChance;
            _colorA = src._colorA;
            _colorB = src._colorB;
            _colorC = src._colorC;
            _windResponse = src._windResponse;
            _alphaClip = src._alphaClip;
            _ySortBands = src._ySortBands;
            _bandHeight = src._bandHeight;
            _ySortBase = src._ySortBase;
            _orderPerUnit = src._orderPerUnit;
        }

        private static readonly int ColorAID = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBID = Shader.PropertyToID("_ColorB");
        private static readonly int ColorCID = Shader.PropertyToID("_ColorC");
        private static readonly int WindResponseID = Shader.PropertyToID("_WindResponse");
        private static readonly int AlphaClipID = Shader.PropertyToID("_AlphaClip");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

        private struct CardSpawn
        {
            public Vector3 basePos;
            public int hx, hy, channel;
        }

        private Transform _container;
        private Mesh _mesh;
        private readonly List<Mesh> _bandMeshes = new List<Mesh>();
        private Material _runtimeMaterial;

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void OnEnable()
        {
            Tilemap.tilemapTileChanged += OnTilemapTileChanged;
            ResolveSources();
            Generate();
        }

        private void OnDisable()
        {
            Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
        }

        private void OnDestroy()
        {
            if (_mesh != null) DestroySafe(_mesh);
            foreach (var m in _bandMeshes)
                if (m != null) DestroySafe(m);
            _bandMeshes.Clear();
            if (_runtimeMaterial != null) DestroySafe(_runtimeMaterial);
        }

        private void OnTilemapTileChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (_tilemap == null) return;
            if (map != _tilemap && !IsMask(map)) return;
            Generate();
        }

        private bool IsMask(Tilemap map)
        {
            if (_waterMasks != null)
                for (int i = 0; i < _waterMasks.Length; i++)
                    if (_waterMasks[i] == map) return true;
            if (_platformMasks != null)
                for (int i = 0; i < _platformMasks.Length; i++)
                    if (_platformMasks[i] == map) return true;
            return false;
        }

        /// <summary>
        /// Collapse every mask cell painted at Z != 0 down to Z 0 (dedupes onto any
        /// existing Z0 cell). Rescues cells the Tile Palette eraser can't reach —
        /// the eraser only hits the toolbar's current Z Position — and migrates old
        /// hand-elevated grass to the procedural mask workflow (platforms lift
        /// blades themselves now).
        /// </summary>
        [ContextMenu("Flatten Mask To Z0")]
        private void FlattenMaskToZ0()
        {
            if (_tilemap == null) return;
            var clear = new List<Vector3Int>();
            var set = new List<Vector3Int>();
            var tiles = new List<TileBase>();
            foreach (var pos in _tilemap.cellBounds.allPositionsWithin)
            {
                if (pos.z == 0) continue;
                var t = _tilemap.GetTile(pos);
                if (t == null) continue;
                clear.Add(pos);
                set.Add(new Vector3Int(pos.x, pos.y, 0));
                tiles.Add(t);
            }
            if (clear.Count == 0)
            {
                Debug.Log("[BillboardGrass] Mask already flat — no Z != 0 cells.", this);
                return;
            }
            _tilemap.SetTiles(clear.ToArray(), new TileBase[clear.Count]);
            _tilemap.SetTiles(set.ToArray(), tiles.ToArray());
            Debug.Log($"[BillboardGrass] Flattened {clear.Count} mask cell(s) to Z0.", this);
        }

        /// <summary>Highest painted Z per (x,y) across the given masks. Null when empty.</summary>
        private static Dictionary<Vector2Int, int> BuildTopZ(Tilemap[] masks)
        {
            if (masks == null) return null;
            Dictionary<Vector2Int, int> top = null;
            foreach (var m in masks)
            {
                if (m == null) continue;
                foreach (var pos in m.cellBounds.allPositionsWithin)
                {
                    if (!m.HasTile(pos)) continue;
                    top ??= new Dictionary<Vector2Int, int>();
                    var xy = new Vector2Int(pos.x, pos.y);
                    if (!top.TryGetValue(xy, out int t) || pos.z > t) top[xy] = pos.z;
                }
            }
            return top;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                ResolveSources();
                Generate();
            };
        }
#endif

        private void ResolveSources()
        {
            if (_tilemap == null)
            {
                var t2s = GetComponent<TilemapToSpriteShape>();
                if (t2s != null) _tilemap = t2s.SourceTilemap;
            }
        }

        // ---------------------------------------------------------------------
        // Generation
        // ---------------------------------------------------------------------

        /// <summary>Rebuild the grass mesh from the tilemap. Safe at runtime.</summary>
        [ContextMenu("Generate Grass")]
        public void Generate()
        {
            EnsureContainer();

            if (_tilemap == null || _cards == null || _cards.Length == 0)
            {
                ApplyMesh(null);
                return;
            }

            // Cell basis so spread follows the grid in any layout (iso incl.).
            Vector3 v00 = _tilemap.CellToWorld(Vector3Int.zero);
            Vector3 stepX = _tilemap.CellToWorld(new Vector3Int(1, 0, 0)) - v00;
            Vector3 stepY = _tilemap.CellToWorld(new Vector3Int(0, 1, 0)) - v00;
            float clusterScale = Mathf.Max(_clusterScale, 0.01f);

            var spawns = new List<CardSpawn>();
            var waterTop = BuildTopZ(_waterMasks);
            var platTop = BuildTopZ(_platformMasks);
            // Screen-band math for platform occlusion: one elevation step shifts a
            // surface up stepRatio cell-rows on screen; a platform cell at (x, y', zp)
            // renders into bands [y' .. y' + zp*stepRatio] (wall + raised surface).
            float cellWorldH = Mathf.Abs(stepY.y) > 1e-4f ? Mathf.Abs(stepY.y) : 1f;
            float stepRatio = _elevationStep / cellWorldH;
            int occlRows = 0;
            if (platTop != null)
                foreach (var t in platTop.Values)
                    occlRows = Mathf.Max(occlRows, Mathf.CeilToInt(t * stepRatio));
            var cb = _tilemap.cellBounds;
            foreach (var cell in cb.allPositionsWithin)
            {
                if (!_tilemap.HasTile(cell)) continue;

                // Procedural mask pass: platforms carry the surface up (blades ride
                // the top instead of being buried), water replaces the surface at
                // its level (no blades). Author the grass mask once — editing
                // water/platform masks re-flows the blades.
                int z = cell.z;
                var xy = new Vector2Int(cell.x, cell.y);
                if (platTop != null && platTop.TryGetValue(xy, out int pz) && pz > z) z = pz;
                if (waterTop != null && waterTop.TryGetValue(xy, out int wz) && wz >= z) continue;
                // Occlusion: a platform column in FRONT (lower y) draws its skirt +
                // raised edge over this blade's screen band — blade would float on
                // the wall, cull it. Covered when zp*stepRatio - k >= z*stepRatio.
                if (platTop != null && occlRows > 0)
                {
                    bool hidden = false;
                    for (int k = 1; k <= occlRows && !hidden; k++)
                        if (platTop.TryGetValue(new Vector2Int(cell.x, cell.y - k), out int fz)
                            && fz * stepRatio - k >= z * stepRatio - 0.001f)
                            hidden = true;
                    if (hidden) continue;
                }

                Vector3 center = _tilemap.GetCellCenterWorld(cell);
                // Elevation: grid cellSize.z is 0, so cell Z moves nothing in world —
                // lift the card base ourselves. The Y-band sort then buckets by the
                // LIFTED position, which is what interleaves blades above a terrace.
                center.y += z * _elevationStep;
                // Mix Z into the layout hash so a terrace cell over a base cell
                // (same x,y) doesn't duplicate the exact same card layout.
                int hz = cell.y + z * 7919;

                // Clump noise: meadows and bare spots (same recipe as IslandFoliage).
                float noise = Mathf.PerlinNoise(
                    center.x / clusterScale + _seed * 0.173f,
                    center.y * 2f / clusterScale);
                float p = _density * Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, noise), _clusterStrength);

                bool edge = !_tilemap.HasTile(cell + Vector3Int.right)
                         || !_tilemap.HasTile(cell + Vector3Int.left)
                         || !_tilemap.HasTile(cell + Vector3Int.up)
                         || !_tilemap.HasTile(cell + Vector3Int.down);
                if (edge) p *= 1f - _edgeAvoid;

                for (int k = 0; k < _cardsPerCell; k++)
                {
                    int ch = k * 8;
                    if (Hash(cell.x, hz, _seed, ch) >= p) continue;

                    float jx = (Hash(cell.x, hz, _seed, ch + 1) - 0.5f) * _spread;
                    float jy = (Hash(cell.x, hz, _seed, ch + 2) - 0.5f) * _spread;
                    spawns.Add(new CardSpawn
                    {
                        basePos = center + stepX * jx + stepY * jy,
                        hx = cell.x,
                        hy = hz,
                        channel = ch,
                    });
                }
            }

            if (spawns.Count == 0)
            {
                ApplyMesh(null);
                ClearBands();
                return;
            }

            // Back-to-front for alpha blending: higher cards are further away
            // in top-down view, draw them first so nearer cards overlap on top.
            spawns.Sort((a, b) => b.basePos.y.CompareTo(a.basePos.y));

            Vector3 cardScale = _matchTilemapScale
                ? _tilemap.transform.lossyScale
                : Vector3.one;

            if (!_ySortBands)
            {
                ClearBands();
                ApplyMesh(BuildMesh(spawns, cardScale, "GrassCards", ref _mesh));
                return;
            }

            // Band mode: bucket cards by base Y; each band renders through its own
            // child renderer whose sortingOrder follows the project Y-sort formula,
            // so blades interleave with Y-sorted prop sprites. ~island-height/band
            // extra draw calls, same material, still zero per-frame CPU.
            ApplyMesh(null);
            ClearBands();
            float bandH = Mathf.Max(_bandHeight, 0.1f);
            var buckets = new SortedDictionary<int, List<CardSpawn>>();
            foreach (var s in spawns)
            {
                int band = Mathf.FloorToInt(s.basePos.y / bandH);
                if (!buckets.TryGetValue(band, out var list))
                    buckets[band] = list = new List<CardSpawn>();
                list.Add(s);
            }
            foreach (var kv in buckets)
            {
                Mesh mesh = null;
                mesh = BuildMesh(kv.Value, cardScale, "GrassBand_" + kv.Key, ref mesh);
                _bandMeshes.Add(mesh);
                // Sort at the band's TOP edge so props standing in the grass render in front.
                float bandTopY = (kv.Key + 1) * bandH;
                int order = _ySortBase - Mathf.RoundToInt(bandTopY * _orderPerUnit);
                var go = new GameObject("band_" + kv.Key) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(_container, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                ConfigureRenderer(go.AddComponent<MeshRenderer>(), order);
            }
        }

        private Mesh BuildMesh(List<CardSpawn> spawns, Vector3 cardScale, string name, ref Mesh reuse)
        {
            var verts = new List<Vector3>(spawns.Count * 4);
            var uvs = new List<Vector2>(spawns.Count * 4);
            var pivots = new List<Vector2>(spawns.Count * 4);
            var colors = new List<Color>(spawns.Count * 4);
            var tris = new List<int>(spawns.Count * 6);

            foreach (var s in spawns)
                AddCard(s, cardScale, verts, uvs, pivots, colors, tris);

            if (reuse == null)
                reuse = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            reuse.Clear();
            reuse.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            reuse.SetVertices(verts);
            reuse.SetUVs(0, uvs);
            reuse.SetUVs(1, pivots);
            reuse.SetColors(colors);
            reuse.SetTriangles(tris, 0);
            reuse.RecalculateBounds();

            // Wind displaces vertices in-shader; pad bounds so swaying tips
            // don't get culled at the screen edge.
            var bounds = reuse.bounds;
            bounds.Expand(1f);
            reuse.bounds = bounds;
            return reuse;
        }

        private void ClearBands()
        {
            foreach (var m in _bandMeshes)
                if (m != null) DestroySafe(m);
            _bandMeshes.Clear();
            if (_container == null) return;
            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                var ch = _container.GetChild(i);
                if (ch.name.StartsWith("band_")) DestroySafe(ch.gameObject);
            }
        }

        private void AddCard(CardSpawn s, Vector3 cardScale,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> pivots,
            List<Color> colors, List<int> tris)
        {
            int variant = Mathf.Min(
                (int)(Hash(s.hx, s.hy, _seed, s.channel + 3) * _cards.Length),
                _cards.Length - 1);
            Sprite card = _cards[variant];
            if (card == null) return;

            float scale = Mathf.Lerp(_scaleMin, _scaleMax, Hash(s.hx, s.hy, _seed, s.channel + 4));
            float w = card.rect.width / card.pixelsPerUnit * scale * cardScale.x;
            float h = card.rect.height / card.pixelsPerUnit * scale * cardScale.y;
            bool flip = Hash(s.hx, s.hy, _seed, s.channel + 5) < _flipChance;

            // Upright billboard: authored in world space, stored in local so
            // the mesh follows the island transform exactly.
            Vector3 bl = s.basePos + Vector3.left * (w * 0.5f);
            Vector3 br = s.basePos + Vector3.right * (w * 0.5f);
            Vector3 tl = bl + Vector3.up * h;
            Vector3 tr = br + Vector3.up * h;

            int start = verts.Count;
            verts.Add(transform.InverseTransformPoint(bl));
            verts.Add(transform.InverseTransformPoint(br));
            verts.Add(transform.InverseTransformPoint(tr));
            verts.Add(transform.InverseTransformPoint(tl));

            Rect r = card.textureRect;
            Texture tex = card.texture;
            float u0 = r.xMin / tex.width, u1 = r.xMax / tex.width;
            float v0 = r.yMin / tex.height, v1 = r.yMax / tex.height;
            if (flip) { float t = u0; u0 = u1; u1 = t; }
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            // Wind phase pivot — world position, same convention as _WindPivot.
            Vector2 pivot = s.basePos;
            for (int i = 0; i < 4; i++) pivots.Add(pivot);

            // r = color pick, g = extra phase, b = sway weight (base anchored).
            float colorRand = Hash(s.hx, s.hy, _seed, s.channel + 6);
            float phaseRand = Hash(s.hx, s.hy, _seed, s.channel + 7);
            colors.Add(new Color(colorRand, phaseRand, 0f, 1f));
            colors.Add(new Color(colorRand, phaseRand, 0f, 1f));
            colors.Add(new Color(colorRand, phaseRand, 1f, 1f));
            colors.Add(new Color(colorRand, phaseRand, 1f, 1f));

            tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
            tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
        }

        // ---------------------------------------------------------------------
        // Container / renderer plumbing
        // ---------------------------------------------------------------------

        private void ApplyMesh(Mesh mesh)
        {
            var filter = _container.GetComponent<MeshFilter>();
            var renderer = _container.GetComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.enabled = mesh != null;
            if (mesh == null) return;
            ConfigureRenderer(renderer, _sortingOrder);
        }

        private void ConfigureRenderer(MeshRenderer renderer, int order)
        {
            renderer.sharedMaterial = ResolveMaterial();
            // A layer name the project does not define drops the mesh onto Default — the LOWEST
            // layer — i.e. behind the island it is meant to cover. Keep what we have instead.
            if (!string.IsNullOrEmpty(_sortingLayer) && LayerExists(_sortingLayer))
                renderer.sortingLayerName = _sortingLayer;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorAID, _colorA);
            mpb.SetColor(ColorBID, _colorB);
            mpb.SetColor(ColorCID, _colorC);
            mpb.SetFloat(WindResponseID, _windResponse);
            mpb.SetFloat(AlphaClipID, _alphaClip);
            // One atlas for the whole patch: take it from the first card that HAS one. Indexing
            // _cards[0] blindly leaves the patch untextured whenever slot 0 happens to be empty.
            for (int i = 0; i < _cards.Length; i++)
                if (_cards[i] != null && _cards[i].texture != null)
                {
                    mpb.SetTexture(MainTexID, _cards[i].texture);
                    break;
                }
            renderer.SetPropertyBlock(mpb);
        }

        private Material ResolveMaterial()
        {
            if (_material != null) return _material;
            if (_runtimeMaterial == null)
            {
                var shader = Shader.Find("Flynn/GrassBlades");
                if (shader == null)
                {
                    Debug.LogWarning($"{nameof(BillboardGrass)}: Flynn/GrassBlades shader not found.", this);
                    return null;
                }
                _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _runtimeMaterial;
        }

        private void EnsureContainer()
        {
            if (_container != null) return;
            Transform t = transform.Find(ContainerName);
            if (t == null)
            {
                var go = new GameObject(ContainerName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            else
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
            }
            if (t.GetComponent<MeshFilter>() == null) t.gameObject.AddComponent<MeshFilter>();
            if (t.GetComponent<MeshRenderer>() == null) t.gameObject.AddComponent<MeshRenderer>();
            _container = t;
        }

        private static void DestroySafe(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private bool LayerExists(string name)
        {
            foreach (var l in SortingLayer.layers) if (l.name == name) return true;
            Debug.LogWarning($"[{nameof(BillboardGrass)}] Sorting layer '{name}' does not exist — " +
                             "grass keeps its current layer.", this);
            return false;
        }

        /// <summary>Deterministic 0..1 hash — stable layout across regens.</summary>
        private static float Hash(int x, int y, int seed, int channel)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791 ^ channel * 374761393);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
