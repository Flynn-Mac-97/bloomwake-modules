using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Modules.PlatformTiles
{
    /// <summary>
    /// Draws raised platforms as isometric cube tiles, the way a traditional 2D iso game does it.
    ///
    /// THE SORT. Depth and height are separate things and must never be mixed into one number —
    /// that conflation is what makes iso sorting go wrong. A tile's depth is decided purely by its
    /// FOOTPRINT on the ground (its iso row), never by how high it is drawn:
    ///
    ///     order = baseOrder + round(-footprintY * depthSteps) * HeightSlots + elevation * 2
    ///
    /// Because world-Y on an iso grid is proportional to (cellX + cellY), the footprint Y IS the iso
    /// row. Rows nearer the camera sit lower on screen, get a larger order, and draw last. Elevation
    /// only breaks ties inside one row, so a tall stack never steals depth from its neighbours.
    ///
    /// The player uses the same formula (see PlatformLevelSort) with its feet projected back to the
    /// ground plane, plus 1, so it wins ties against the tile it stands on. That gives exactly the
    /// two behaviours you want, with no special cases:
    ///   - stand behind a platform -> your row is further back -> the platform draws over you.
    ///   - run down its near side  -> your row is nearer       -> you draw over it.
    ///
    /// TILE SIZE. One tile spans a 2x2 block of base grid cells, matching how the maps are authored
    /// (they paint N base cells per map tile), so a platform reads as chunky iso blocks rather than
    /// a fine mosaic.
    ///
    /// CULLING. Only surfaces that can actually be seen are emitted: every column draws its TOP
    /// tile, plus side tiles down to whichever of its two front neighbours is lower. Interior
    /// columns of a plateau therefore cost exactly one tile instead of one per elevation.
    ///
    /// Reads the platform cells the level painter already produced: sibling tilemaps under the Grid
    /// named "JsonLayer_*", each raised by height * heightStep. Coupled by NAME only, so this module
    /// keeps no code dependency on the loader.
    ///
    /// Editor-time generator: paint the level, then run "Build Tiles". Children are normal saved
    /// objects, so they survive play and domain reload with no runtime rebuild.
    /// </summary>
    public class PlatformTileBuilder : MonoBehaviour
    {
        /// <summary>Order slots reserved per iso row for elevation tie-breaking. Must exceed
        /// (maxElevation * 2 + 1) so a row's stack can never spill into the next row.</summary>
        public const int HeightSlots = 16;

        /// <summary>Base grid cells per platform tile, on each axis.</summary>
        public const int CellsPerTile = 2;

        [Header("Source")]
        [Tooltip("The BASE ground tilemap — supplies the iso cell layout. Auto-found on this GameObject.")]
        [SerializeField] private Tilemap _baseTilemap;
        [Tooltip("Fallback local-Y rise per elevation level. Normally measured from the painted layers, " +
                 "so this is only used when there is a single layer and nothing to measure.")]
        [SerializeField] private float _heightStep = 0.25f;

        [Header("Look")]
        [Tooltip("Draw platforms as iso cube sprites. OFF = the island system draws them (SpriteShape " +
                 "contour + skirt, via JsonMapLoader's raised stacks) and this component only builds " +
                 "collision and the level boundaries that stairs rely on.")]
        [SerializeField] private bool _drawTiles = false;
        [Tooltip("Iso cube sprite. Pivot (0.5, 0) = bottom-centre, so it seats on the tile's bottom vertex.")]
        [SerializeField] private Sprite _tileSprite;
        [Tooltip("Tint applied to every tile — cite a VibeSpec role, don't pick raw colours.")]
        [SerializeField] private Color _tint = Color.white;
        [Tooltip("Sorting layer for the tiles. Match the layer the player sorts on.")]
        [SerializeField] private string _sortingLayer = "Default";
        [Tooltip("Nudge applied to every tile after automatic seating — use it if the art's cube body " +
                 "doesn't exactly equal one elevation rise.")]
        [SerializeField] private Vector2 _tileOffset = Vector2.zero;

        [Header("Sorting — MATCH PlatformLevelSort on the player")]
        [Tooltip("Constant added to every order.")]
        [SerializeField] private int _baseOrder = 0;
        [Tooltip("Order units per world unit of DEPTH (footprint Y). Must equal PlatformLevelSort.")]
        [SerializeField] private float _depthSteps = 20f;

        [Header("Collision")]
        [Tooltip("Invisible tile whose Collider Type is Grid — on an isometric grid that yields the " +
                 "cell's DIAMOND, which CompositeCollider2D merges into one footprint outline. " +
                 "Empty = skip collision.")]
        [SerializeField] private TileBase _collisionTile;
        [Tooltip("Layer for the collision tilemap. Must be in the player's blockers mask.")]
        [SerializeField] private string _collisionLayer = "GroundBorder";

        private const string RootName = "PlatformTiles";
        private const string CollisionName = "PlatformCollision";

        /// <summary>Raised painted-cell layers this builder reads: JsonMapLoader's "JsonLayer_*"
        /// groups or PaintedStyleLayer's generated "*_cells_z*" maps. Name-coupled on purpose —
        /// no code dependency on the loader module.</summary>
        public static bool IsPaintedLayer(string name)
            => name.StartsWith("JsonLayer_") || name.Contains("_cells_z");

        /// <summary>World-Y between two stacked levels as actually DRAWN — derived from the art's body
        /// height, not the painter's heightStep. PlayerElevation.riseWorld must match it or the
        /// player's feet won't land on the platform surface. Set during the last build.</summary>
        public float DrawnRise { get; private set; }

        [ContextMenu("Build Tiles")]
        public void BuildTiles()
        {
            if (_baseTilemap == null) _baseTilemap = GetComponent<Tilemap>();
            if (_baseTilemap == null) { Debug.LogError("[PlatformTiles] No base tilemap.", this); return; }
            if (_drawTiles && _tileSprite == null) { Debug.LogError("[PlatformTiles] No tile sprite.", this); return; }

            var baseT = _baseTilemap.transform;
            var grid = baseT.parent;
            if (grid == null) { Debug.LogError("[PlatformTiles] Base tilemap has no Grid parent.", this); return; }

            var cellLevel = ReadPaintedCells(grid, baseT, out float riseWorld);
            var tileLevel = ToTileBlocks(cellLevel);

            var root = EnsureChild(transform, RootName);
            ClearChildren(root);

            int drawn = (_drawTiles && tileLevel.Count > 0) ? EmitTiles(root, tileLevel, riseWorld) : 0;
            // When the island stacks draw the platforms, the height a player gains per level is the
            // map's own elevation, not any sprite's body height.
            if (!_drawTiles) DrawnRise = riseWorld;
            int shapes = BuildCollision(grid, baseT, cellLevel);

            if (_drawTiles)
                Debug.Log($"[PlatformTiles] {tileLevel.Count} column(s) -> {drawn} tile(s) drawn " +
                          $"(hidden faces culled); collision shapes: {shapes}. " +
                          $"Drawn rise = {DrawnRise:0.###} — set PlayerElevation.riseWorld to this.", root);
            else
                Debug.Log($"[PlatformTiles] Drawing OFF (island stacks draw the platforms); " +
                          $"built collision + {shapes} boundary shape(s) for {tileLevel.Count} column(s).", root);
            MarkDirty();
        }

        // ---------------------------------------------------------------------
        // Read
        // ---------------------------------------------------------------------

        /// <summary>Top elevation per base cell, from the painted JsonLayer_* tilemaps. Level 0 is
        /// the island base and is not a platform, so it is not collected.</summary>
        private Dictionary<Vector2Int, int> ReadPaintedCells(Transform grid, Transform baseT, out float riseWorld)
        {
            float baseLocalY = baseT.localPosition.y;
            float step = Mathf.Max(0.0001f, MeasureHeightStep(grid, baseLocalY));
            riseWorld = 0f;

            var levels = new Dictionary<Vector2Int, int>();
            int raisedLayers = 0;
            foreach (Transform child in grid)
            {
                if (!IsPaintedLayer(child.name)) continue;
                var map = child.GetComponent<Tilemap>();
                if (map == null) continue;
                if (child.localPosition.y - baseLocalY > 0.0001f) raisedLayers++;

                int level = Mathf.RoundToInt((child.localPosition.y - baseLocalY) / step);
                if (level <= 0) continue;

                // World rise per level, measured rather than assumed — the Grid carries its own scale.
                riseWorld = (child.position.y - baseT.position.y) / level;

                foreach (var p in map.cellBounds.allPositionsWithin)
                {
                    if (map.GetTile(p) == null) continue;
                    var c = new Vector2Int(p.x, p.y);
                    if (!levels.TryGetValue(c, out int cur) || level > cur) levels[c] = level;
                }
            }

            // A heightStep that disagrees with the painter rounds real layers down to level 0 and
            // drops them without a word — make that loud instead.
            if (raisedLayers > 0 && levels.Count == 0)
                Debug.LogError($"[PlatformTiles] {raisedLayers} raised layer(s) found but none resolved " +
                               $"to an elevation — _heightStep ({_heightStep}) does not match the " +
                               "painter's heightStep.", this);
            return levels;
        }

        /// <summary>
        /// Work out one elevation step from the painted layers themselves: the smallest gap between
        /// their distinct Y offsets. Different maps use different heightSteps (0.25 here, 0.5 there),
        /// and a copy of that number living on this component silently rounds real layers to level 0
        /// and drops them the moment the two disagree. Measuring is not guesswork — the gap between
        /// consecutive tiers IS the step, even for a map whose lowest tier is missing.
        /// Falls back to the serialized value when there is only one layer to look at.
        /// </summary>
        private float MeasureHeightStep(Transform grid, float baseLocalY)
        {
            var offsets = new List<float> { 0f };
            foreach (Transform child in grid)
            {
                if (!IsPaintedLayer(child.name)) continue;
                if (child.GetComponent<Tilemap>() == null) continue;
                float dy = child.localPosition.y - baseLocalY;
                if (dy <= 0.0001f) continue;
                bool seen = false;
                foreach (var o in offsets) if (Mathf.Abs(o - dy) < 0.0001f) { seen = true; break; }
                if (!seen) offsets.Add(dy);
            }
            if (offsets.Count < 2) return _heightStep;

            offsets.Sort();
            float step = float.MaxValue;
            for (int i = 1; i < offsets.Count; i++) step = Mathf.Min(step, offsets[i] - offsets[i - 1]);
            return step;
        }

        /// <summary>Collapse base cells into CellsPerTile-sized blocks, each taking the highest
        /// elevation it covers.</summary>
        private static Dictionary<Vector2Int, int> ToTileBlocks(Dictionary<Vector2Int, int> cellLevel)
        {
            var blocks = new Dictionary<Vector2Int, int>();
            foreach (var kv in cellLevel)
            {
                var b = new Vector2Int(
                    Mathf.FloorToInt(kv.Key.x / (float)CellsPerTile),
                    Mathf.FloorToInt(kv.Key.y / (float)CellsPerTile));
                if (!blocks.TryGetValue(b, out int cur) || kv.Value > cur) blocks[b] = kv.Value;
            }
            return blocks;
        }

        // ---------------------------------------------------------------------
        // Draw
        // ---------------------------------------------------------------------

        private int EmitTiles(Transform root, Dictionary<Vector2Int, int> tileLevel, float riseWorld)
        {
            // Block geometry in world units, measured off the tilemap so any Grid scale is respected.
            Vector3 o = CellCentre(0, 0);
            float tileW = Mathf.Abs(CellCentre(CellsPerTile, 0).x - o.x) * 2f;
            float tileH = Mathf.Abs(CellCentre(CellsPerTile, 0).y - o.y) * 2f;
            if (tileW <= 0.0001f) { Debug.LogError("[PlatformTiles] Degenerate cell size.", this); return 0; }

            Vector2 spriteSize = _tileSprite.rect.size / _tileSprite.pixelsPerUnit;
            float fit = tileW / spriteSize.x;

            // Seat the art by its BOTTOM-CENTRE whatever pivot it was imported with, so swapping in
            // a sprite pivoted centre (Unity's default) doesn't sink every tile half a block.
            Vector2 pivotWorld = _tileSprite.pivot / _tileSprite.pixelsPerUnit;
            Vector3 pivotFix = (Vector3)((pivotWorld - new Vector2(spriteSize.x * 0.5f, 0f)) * fit);

            // Stack by the art's own BODY height (total sprite height minus the top diamond), not by
            // the painter's logical heightStep. If the two disagree the cubes only partly clear each
            // other and every level leaves a sliver of the one below showing down the cliff face.
            // This is the value PlayerElevation.riseWorld must use, so feet land on the top surface.
            float drawRise = spriteSize.y * fit - tileH;
            if (drawRise <= 0.0001f) drawRise = riseWorld;   // art with no body: fall back to the data
            DrawnRise = drawRise;

            var groups = new Dictionary<int, Transform>();
            int drawn = 0;

            foreach (var kv in tileLevel)
            {
                Vector2Int b = kv.Key;
                int top = kv.Value;

                // Front = the two neighbours one iso row nearer the camera. Their height hides
                // everything below it, so the side is only exposed down to the LOWER of the two.
                int hidden = Mathf.Min(LevelAt(tileLevel, b.x - 1, b.y), LevelAt(tileLevel, b.x, b.y - 1));
                int lowest = Mathf.Clamp(hidden + 1, 1, top);

                // Footprint depth: the block's ground-plane bottom vertex. Never the drawn height.
                Vector3 centre = BlockCentre(b);
                float depthY = centre.y - tileH * 0.5f;
                int rowOrder = _baseOrder + Mathf.RoundToInt(-depthY * _depthSteps) * HeightSlots;

                for (int level = lowest; level <= top; level++)
                {
                    if (!groups.TryGetValue(level, out var group))
                    {
                        group = EnsureChild(root, $"Level{level}");
                        groups[level] = group;
                    }

                    var go = new GameObject($"Tile_{b.x}_{b.y}");
                    go.transform.SetParent(group, false);
                    go.transform.position = centre
                                            + Vector3.down * (tileH * 0.5f)
                                            + Vector3.up * ((level - 1) * drawRise)
                                            + pivotFix
                                            + (Vector3)_tileOffset;
                    go.transform.localScale = LocalScaleFor(group, fit);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = _tileSprite;
                    sr.color = _tint;
                    sr.sortingLayerName = _sortingLayer;
                    sr.sortingOrder = rowOrder + level * 2;
                    drawn++;
                }
            }
            return drawn;
        }

        private static int LevelAt(Dictionary<Vector2Int, int> tileLevel, int x, int y)
            => tileLevel.TryGetValue(new Vector2Int(x, y), out int l) ? l : 0;

        private Vector3 CellCentre(int x, int y)
            => _baseTilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));

        /// <summary>World centre of a block = midpoint of its first and last base cell centres.</summary>
        private Vector3 BlockCentre(Vector2Int b)
        {
            Vector3 lo = CellCentre(b.x * CellsPerTile, b.y * CellsPerTile);
            Vector3 hi = CellCentre(b.x * CellsPerTile + CellsPerTile - 1, b.y * CellsPerTile + CellsPerTile - 1);
            return (lo + hi) * 0.5f;
        }

        /// <summary>`fit` is a world scale — divide out whatever scale the parents carry.</summary>
        private static Vector3 LocalScaleFor(Transform parent, float fit)
        {
            Vector3 p = parent.lossyScale;
            return new Vector3(
                fit / (Mathf.Abs(p.x) > 0.0001f ? p.x : 1f),
                fit / (Mathf.Abs(p.y) > 0.0001f ? p.y : 1f), 1f);
        }

        // ---------------------------------------------------------------------
        // Collide
        // ---------------------------------------------------------------------

        /// <summary>
        /// Collision the standard 2D-isometric way, one outline per elevation. `Bound_Level{L}` traces
        /// every cell at level L OR HIGHER, at the GROUND plane (the player walks on the ground, so it
        /// is blocked by where a platform stands, not where its top face is drawn). Tiles carry the
        /// grid diamond as their collider shape and CompositeCollider2D merges them.
        ///
        /// Geometry is OUTLINES, not polygons: an edge blocks from both sides, so the same loop keeps
        /// a low player from walking up AND keeps a player on the platform from walking off the edge.
        /// PlayerElevation enables only the two that apply to the level they are on.
        /// </summary>
        private int BuildCollision(Transform grid, Transform baseT, Dictionary<Vector2Int, int> footprint)
        {
            var old = grid.Find(CollisionName);
            if (old != null) DestroyImmediate(old.gameObject);
            if (_collisionTile == null || footprint.Count == 0) return 0;

            var root = new GameObject(CollisionName);
            root.transform.SetParent(grid, false);

            int layer = LayerMask.NameToLayer(_collisionLayer);
            if (layer < 0) Debug.LogWarning($"[PlatformTiles] Layer '{_collisionLayer}' missing.", root);

            int maxLevel = 0;
            foreach (var kv in footprint) if (kv.Value > maxLevel) maxLevel = kv.Value;

            int shapes = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                var go = new GameObject($"Bound_Level{level}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = baseT.localPosition;
                go.transform.localRotation = baseT.localRotation;
                go.transform.localScale = baseT.localScale;
                if (layer >= 0) go.layer = layer;

                var tm = go.AddComponent<Tilemap>();
                go.AddComponent<TilemapRenderer>().enabled = false;   // colliders only
                int cells = 0;
                foreach (var kv in footprint)
                {
                    if (kv.Value < level) continue;                    // this level or higher
                    tm.SetTile(new Vector3Int(kv.Key.x, kv.Key.y, 0), _collisionTile);
                    cells++;
                }
                if (cells == 0) { DestroyImmediate(go); continue; }

                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                go.AddComponent<TilemapCollider2D>().usedByComposite = true;
                var cc = go.AddComponent<CompositeCollider2D>();
                cc.geometryType = CompositeCollider2D.GeometryType.Outlines;
                // The composite defers its bake and under-fills in edit mode — force it now.
                Physics2D.SyncTransforms();
                cc.GenerateGeometry();
                shapes += cc.shapeCount;

                go.AddComponent<LevelBoundary>().level = level;
            }
            return shapes;
        }

        // ---------------------------------------------------------------------
        // Plumbing
        // ---------------------------------------------------------------------

        [ContextMenu("Clear Tiles")]
        public void ClearTiles()
        {
            var root = transform.Find(RootName);
            if (root != null) DestroyImmediate(root.gameObject);
            if (_baseTilemap != null && _baseTilemap.transform.parent != null)
            {
                var col = _baseTilemap.transform.parent.Find(CollisionName);
                if (col != null) DestroyImmediate(col.gameObject);
            }
            MarkDirty();
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyImmediate(root.GetChild(i).gameObject);
        }

        private void MarkDirty()
        {
#if UNITY_EDITOR
            if (this != null) UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }
    }
}
