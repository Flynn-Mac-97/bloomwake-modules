using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Scatters decoration prefabs directly over the painted cells of a
    /// tilemap — assign the island's ground map for a whole-island scatter,
    /// or a dedicated foliage tilemap to paint exact zones with the tile
    /// palette. One component per tilemap; everything is configured right
    /// here (no profile, no mask logic).
    ///
    /// Placement is a pure function of (cell, seed): painting new tiles adds
    /// or removes foliage locally without reshuffling what's already placed.
    /// Spawn probability = density × clump noise (Perlin patches → meadows
    /// and bare spots) × border damping (edge tiles stay clearer).
    ///
    /// Instances go under a DontSave container child — nothing serialized;
    /// regenerates live as the tilemap is painted.
    /// </summary>
    [ExecuteAlways]
    public class IslandFoliage : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Tilemap whose painted cells receive foliage — paint a dedicated FloraGrid to place " +
                 "plants by hand. If unassigned, taken from the TilemapToSpriteShape on this " +
                 "GameObject (whole-island scatter). For exact hand placement set Density 1 and " +
                 "Edge Avoid 0, otherwise the scatter rules will thin out what you painted.")]
        [SerializeField] private Tilemap _tilemap;
        [Tooltip("World Y added per cell Z — paint mask cells at Z 1 (Tile Palette Z Position) to " +
                 "grow flora on a raised terrace. Match JsonMapLoader heightStep.")]
        [SerializeField] private float _elevationStep = 0.25f;

        [Header("Procedural masks (cells must share this grid's layout)")]
        [Tooltip("Water paint masks (e.g. WaterGrid): water replaces the surface — spawns culled " +
                 "where a water cell's top Z >= the spawn's elevation. Editing the mask re-flows flora.")]
        [SerializeField] private Tilemap[] _waterMasks;
        [Tooltip("Platform paint masks (e.g. PlatformGrid): platforms carry the surface up — spawns " +
                 "under a platform lift to its top Z instead of being buried, and spawns whose screen " +
                 "band falls on a platform's skirt are culled. Editing the mask re-flows flora.")]
        [SerializeField] private Tilemap[] _platformMasks;

        [Header("Scatter")]
        [Tooltip("Prefab variants — picked deterministically per tile.")]
        [SerializeField] private GameObject[] _prefabs;
        [Tooltip("Base spawn probability per tile.")]
        [SerializeField, Range(0f, 1f)] private float _density = 0.35f;
        [Tooltip("World size of the clumping noise patches — meadows and bare spots.")]
        [SerializeField] private float _clusterScale = 5f;
        [Tooltip("How strongly the clump noise gates spawning (0 = uniform scatter, 1 = only inside clumps).")]
        [SerializeField, Range(0f, 1f)] private float _clusterStrength = 0.65f;
        [Tooltip("Damp spawning on border tiles of the map (1 = never on border tiles).")]
        [SerializeField, Range(0f, 1f)] private float _edgeAvoid = 0.5f;
        [Tooltip("Random offset inside the tile (fraction of a cell) — kills the grid look.")]
        [SerializeField, Range(0f, 0.5f)] private float _jitter = 0.35f;
        [SerializeField] private float _scaleMin = 0.85f;
        [SerializeField] private float _scaleMax = 1.15f;
        [Tooltip("Chance to mirror a spawn horizontally.")]
        [SerializeField, Range(0f, 1f)] private float _flipChance = 0.5f;
        [Tooltip("Deterministic layout seed — same seed + same tiles = same foliage.")]
        [SerializeField] private int _seed = 1234;

        [Header("Sorting")]
        [Tooltip("If set, spawned renderers move to this sorting layer — use the island's layer (e.g. Level0) so foliage draws above the island top. Empty = keep the prefab's own sorting.")]
        [SerializeField] private string _sortingLayer = "";
        [Tooltip("Sorting order offset. Spawns with a SortableSprite get its depth-computed order plus this; others get this value directly.")]
        [SerializeField] private int _sortingOrder = 0;

        /// <summary>Set by JsonMapLoader so spawned decor lands on the correct sorting layer
        /// for its elevation, regardless of when Generate() fires (OnEnable, OnValidate delayCall,
        /// tilemapTileChanged). Without this, decor defaults to the Default layer and sinks behind
        /// the island surface.</summary>
        public void SetSortingLayer(string layer) => _sortingLayer = layer;

        // ---------------------------------------------------------------------
        // Source rebinding — for GrassMaskBinder. JsonMapLoader destroys and re-instantiates every
        // stack on Regenerate, so a mask reference assigned by hand on a stack does not survive;
        // something has to push it back. Setters only store, the binder calls Generate() once.
        // ---------------------------------------------------------------------
        public Tilemap SourceTilemap { get => _tilemap; set => _tilemap = value; }
        public float ElevationStep { get => _elevationStep; set => _elevationStep = value; }
        public Tilemap[] PlatformMasks { get => _platformMasks; set => _platformMasks = value; }
        public Tilemap[] WaterMasks { get => _waterMasks; set => _waterMasks = value; }

        /// <summary>
        /// Adopt another scatterer's prefab set and placement rules. Each stack the loader clones
        /// carries its own copy of these, so tuning one island leaves the raised terraces on whatever
        /// the template shipped with — and for HAND-PAINTED flora that silently means "half the cells
        /// I painted grew nothing". GrassMaskBinder calls this so one component is the level's source
        /// of truth. Setter only; the caller drives Generate().
        /// </summary>
        public void CopyLookFrom(IslandFoliage src)
        {
            if (src == null || src == this) return;
            _prefabs = src._prefabs;
            _density = src._density;
            _clusterScale = src._clusterScale;
            _clusterStrength = src._clusterStrength;
            _edgeAvoid = src._edgeAvoid;
            _jitter = src._jitter;
            _scaleMin = src._scaleMin;
            _scaleMax = src._scaleMax;
            _flipChance = src._flipChance;
            _seed = src._seed;
            _playerContact = src._playerContact;
            _matchTilemapScale = src._matchTilemapScale;
        }

        [Header("Gameplay")]
        [Tooltip("Add a FloraContactHandler to each spawn at runtime — slows the player inside and shakes the sprite (same path as FloraRuntimeManager). Edit-mode previews stay inert; ambient wind sway registers via the prefab's own ResourceNode.")]
        [SerializeField] private bool _playerContact = true;
        [Tooltip("Multiply spawn scale by the tilemap's world scale — island maps are scaled, and prefabs are authored for unscaled space (parity with FloraRuntimeManager).")]
        [SerializeField] private bool _matchTilemapScale = true;

        private const string ContainerName = "Foliage";

        private Transform _container;

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

        /// <summary>Rebuild all foliage instances from the tilemap. Safe at runtime.</summary>
        [ContextMenu("Generate Foliage")]
        public void Generate()
        {
            EnsureContainer();
            ClearContainer();

            if (_tilemap == null || _prefabs == null || _prefabs.Length == 0) return;

            // Cell basis so jitter follows the grid in any layout (iso incl.).
            Vector3 v00 = _tilemap.CellToWorld(Vector3Int.zero);
            Vector3 stepX = _tilemap.CellToWorld(new Vector3Int(1, 0, 0)) - v00;
            Vector3 stepY = _tilemap.CellToWorld(new Vector3Int(0, 1, 0)) - v00;
            float clusterScale = Mathf.Max(_clusterScale, 0.01f);

            var waterTop = BuildTopZ(_waterMasks);
            var platTop = BuildTopZ(_platformMasks);
            // Screen-band math for platform occlusion: one elevation step shifts a surface up
            // stepRatio cell-rows on screen, so a platform cell at (x, y', zp) renders into bands
            // [y' .. y' + zp*stepRatio] (wall + raised surface).
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

                // Procedural mask pass, identical to BillboardGrass so blades and plants agree on
                // where the surface is: platforms carry the surface up, water replaces it.
                int z = cell.z;
                var xy = new Vector2Int(cell.x, cell.y);
                if (platTop != null && platTop.TryGetValue(xy, out int pz) && pz > z) z = pz;
                if (waterTop != null && waterTop.TryGetValue(xy, out int wz) && wz >= z) continue;
                if (platTop != null && occlRows > 0)
                {
                    bool hidden = false;
                    for (int k = 1; k <= occlRows && !hidden; k++)
                        if (platTop.TryGetValue(new Vector2Int(cell.x, cell.y - k), out int fz)
                            && fz * stepRatio - k >= z * stepRatio - 0.001f)
                            hidden = true;
                    if (hidden) continue;
                }

                float p = _density;

                // Clump noise: round patches on screen (iso squash on Y).
                Vector3 center = _tilemap.GetCellCenterWorld(cell);
                // Grid cellSize.z is 0, so cell Z moves nothing in world — lift ourselves.
                center.y += z * _elevationStep;
                // Mix Z into the layout hash so a terrace cell over a base cell (same x,y) does not
                // reproduce the exact same pick.
                int hz = cell.y + z * 7919;
                float noise = Mathf.PerlinNoise(
                    center.x / clusterScale + _seed * 0.173f,
                    center.y * 2f / clusterScale);
                p *= Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, noise), _clusterStrength);

                // Border tiles stay clearer — the lip belongs to the fringe. On a HAND-PAINTED mask
                // every cell is near an edge, so leave Edge Avoid at 0 there.
                bool edge = !_tilemap.HasTile(cell + Vector3Int.right)
                         || !_tilemap.HasTile(cell + Vector3Int.left)
                         || !_tilemap.HasTile(cell + Vector3Int.up)
                         || !_tilemap.HasTile(cell + Vector3Int.down);
                if (edge) p *= 1f - _edgeAvoid;

                if (Hash(cell.x, hz, _seed, 0) >= p) continue;

                int variant = Mathf.Min(
                    (int)(Hash(cell.x, hz, _seed, 1) * _prefabs.Length),
                    _prefabs.Length - 1);
                GameObject prefab = _prefabs[variant];
                if (prefab == null) continue;

                float jx = (Hash(cell.x, hz, _seed, 2) - 0.5f) * 2f * _jitter;
                float jy = (Hash(cell.x, hz, _seed, 3) - 0.5f) * 2f * _jitter;

                GameObject go = Instantiate(prefab, _container);
                go.hideFlags = HideFlags.DontSave;
                go.transform.position = center + stepX * jx + stepY * jy;
                float s = Mathf.Lerp(_scaleMin, _scaleMax, Hash(cell.x, hz, _seed, 4));
                Vector3 ls = go.transform.localScale * s;
                if (_matchTilemapScale)
                    ls = Vector3.Scale(ls, _tilemap.transform.lossyScale);
                if (Hash(cell.x, hz, _seed, 5) < _flipChance) ls.x = -ls.x;
                go.transform.localScale = ls;

                // Player interaction (slow + shake) — runtime only, same
                // attach-at-spawn path FloraRuntimeManager uses.
                if (_playerContact && Application.isPlaying)
                    go.AddComponent<Flynn.World.FloraContactHandler>();

                // Depth sorting: SortableSprite only runs in play mode, so
                // stamp its computed order at spawn time (same formula — no
                // pop when its LateUpdate takes over in play). Plants at the
                // same constant order stack arbitrarily.
                int order = _sortingOrder;
                var sortable = go.GetComponentInChildren<Flynn.Common.SortableSprite>(true);
                if (sortable != null) order += sortable.ComputeBaseOrder();

                // Only move layers when the project actually defines the one we were given: a stale
                // name (an "Actors0" left over from a retired scheme) otherwise drops every spawn
                // onto Default, which is the LOWEST layer — i.e. behind the whole island.
                bool useLayer = !string.IsNullOrEmpty(_sortingLayer) && LayerExists(_sortingLayer);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (useLayer) r.sortingLayerName = _sortingLayer;
                    r.sortingOrder = order;
                }
                foreach (var g in go.GetComponentsInChildren<UnityEngine.Rendering.SortingGroup>(true))
                {
                    if (useLayer) g.sortingLayerName = _sortingLayer;
                    g.sortingOrder = order;
                }
            }

            // Fake-shadow system discovers casters only on its own OnEnable —
            // freshly spawned foliage never registers without this.
            var shadowMgr = FindObjectOfType<Flynn.Shadow2D.Shadow2DManager>();
            if (shadowMgr != null) shadowMgr.RefreshCasters();
        }

        private void EnsureContainer()
        {
            // One container per component; suffixed by the source map so
            // several IslandFoliage components (one per foliage tilemap)
            // can coexist on one GameObject.
            string name = _tilemap != null ? ContainerName + "_" + _tilemap.name : ContainerName;
            if (_container != null && _container.name != name)
            {
                var stale = _container.gameObject;
                _container = null;
                if (Application.isPlaying) Destroy(stale);
                else DestroyImmediate(stale);
            }
            if (_container != null) return;
            Transform t = transform.Find(name);
            if (t == null)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            else
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
            }
            _container = t;
        }

        private void ClearContainer()
        {
            if (_container == null) return;
            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                var child = _container.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private bool LayerExists(string name)
        {
            foreach (var l in SortingLayer.layers) if (l.name == name) return true;
            Debug.LogWarning($"[{nameof(IslandFoliage)}] Sorting layer '{name}' does not exist — " +
                             "foliage keeps its prefab's layer.", this);
            return false;
        }

        /// <summary>Deterministic 0..1 hash of a cell — stable layout across regens.</summary>
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
