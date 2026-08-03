using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;

namespace Flynn.Environment
{
    /// <summary>
    /// Designated level-loader: picks a map-painter JSON export, paints it onto the
    /// ground tilemap per its <see cref="tileTypes"/>, and the island visual stack
    /// (TilemapToSpriteShape -> IslandSkirt / grass / foliage) renders it. Swap
    /// <see cref="mapJson"/> and regenerate to load another map. Rectangular-grid on
    /// purpose — the shader island reads better on the rect grid than the iso diamond.
    ///
    /// Ground types partition into STYLE GROUPS keyed by (styled child, height). Every
    /// group gets its own tilemap (offset up by height * heightStep) + a clone of the
    /// type's pre-styled child; unmapped ids render as the base footprint (Visual Stack
    /// Root). Base footprint stays solid under overlays like lakes — SpriteShape cannot
    /// cut holes, so water renders ON TOP of the land fill.
    ///
    /// The JSON "layers" block (resources, npcs, largeSprites) is gizmo-visualized
    /// only for now; it feeds the later spawn pass from the same asset.
    /// </summary>
    public class JsonMapLoader : MonoBehaviour, Flynn.Contracts.IElevationSource
    {
        [Tooltip("Map-painter JSON export (Assets/David/map_painter_david/data/maps/*.json). Swap and regenerate to load another map.")]
        public TextAsset mapJson;
        [Tooltip("Tilemap to paint. The same one TilemapToSpriteShape reads.")]
        public Tilemap tilemap;
        [Tooltip("Tile painted for ground ids without a tile override in their tile type.")]
        public TileBase defaultGroundTile;

        [Header("Tile definitions")]
        [Tooltip("Per ground-id definitions, each pointing at a pre-styled stack child under " +
                 "this loader (the hidden 'square per type'). The loader clones the type's " +
                 "Styled Stack directly, so it carries its own look with no external template/" +
                 "aesthetic references. Ids with no entry paint as plain base ground.")]
        public List<TileType> tileTypes = new List<TileType>();

        [Header("Scale")]
        [Tooltip("Cells painted per JSON tile (NxN block). 2 matches the world scale the " +
                 "designer painted at (1 painter tile ~ 1x0.5 world units on the lab grid).")]
        [Range(1, 4)] public int cellsPerTile = 2;

        [Header("Generated content")]
        [Tooltip("Scene object the loader builds its style-group stacks into. Created on demand and " +
                 "kept OUTSIDE this prefab: parenting generated stacks under the loader serialises " +
                 "them as prefab-instance ADDED objects, where they go stale (a clone keeps the card " +
                 "set / profile its template had at clone time) and pollute the rig prefab.")]
        public string generatedRootName = "LevelLoader_Generated";

        [Header("Elevation layers")]
        [Tooltip("World Y raise per elevation level. Each style group's tilemap sits at " +
                 "height * this, so its SpriteShape renders as a raised terrace.")]
        public float heightStep = 0.25f;
        [Tooltip("Sorting order the ground band STARTS at. The whole island — base stack, every " +
                 "raised terrace, their skirts and fringes — is packed into " +
                 "[groundOrderBase, groundOrderBase + maxHeight*10 + styles]. Keep it negative so " +
                 "everything standing ON the ground (props at order 0, Y-sorted actors at the " +
                 "project's 5000 band) is in front of it by construction instead of by luck.")]
        public int groundOrderBase = -100;
        [Tooltip("Put each elevation level's stack on its own sorting LAYER (Level0, Level1, ...) " +
                 "so a player driven by SortableSprite._levelSortingLayers cleanly crosses each " +
                 "platform roof. Off (default) = single layer + order bands; other scenes unaffected.")]
        public bool perLevelSortingLayers = false;
        [Tooltip("Sorting-layer name prefix for perLevelSortingLayers — level h → \"<prefix>h\".")]
        public string levelLayerPrefix = "Level";

        [Header("Editor")]
        [Tooltip("Regenerate immediately when a field changes in the inspector.")]
        public bool autoRegenerate = true;
        [Tooltip("Scene-view gizmos for the JSON layers block (resources / npcs / " +
                 "largeSprites / decals) at their mapped world positions, height offset " +
                 "included. Rebuilt on Regenerate; select the LevelLoader to see labels.")]
        public bool drawLayerGizmos = true;

        [Header("Troubleshooting")]
        [Tooltip("Render raw tiles on the style-group tilemaps — they draw OVER the " +
                 "SpriteShape aesthetics, troubleshooting only.")]
        public bool showRawGroupTiles = false;
        [Tooltip("Off = paint the tilemaps ONLY: TilemapToSpriteShape + IslandSkirt on " +
                 "every stack are disabled before painting, so nothing regenerates. " +
                 "Turn back on and Regenerate to re-enable and rebuild contour + skirt.")]
        public bool updateVisualStack = true;
        [Tooltip("BillboardGrass + IslandFoliage on every stack. Off while iterating " +
                 "on map loading — foliage alone instantiates thousands of prefabs on big maps.")]
        public bool enableGrassAndFoliage = false;
        [Tooltip("The Island GameObject holding the DEFAULT (land) stack — also the " +
                 "template cloned for style groups without a stackTemplate.")]
        public Transform visualStackRoot;

        [Tooltip("Fired after a Regenerate finishes rebuilding the stacks. Anything holding a " +
                 "reference INTO a stack must re-push it here — the stacks are destroyed and " +
                 "re-instantiated, so hand-assigned refs on them do not survive (GrassMaskBinder.Bind).")]
        public UnityEvent onRegenerated = new UnityEvent();

        private const int MaxLayers = 8;

        /// <summary>How a ground id is treated when painting.</summary>
        public enum TileMode
        {
            /// <summary>Part of the island footprint — painted into the ground tilemap.</summary>
            Ground,
            /// <summary>Not painted at all.</summary>
            Ignore,
        }

        /// <summary>
        /// A ground-type's self-contained definition: the metadata the loader needs plus a
        /// pre-styled stack (a hidden child under this loader) that IS the look. The loader
        /// clones these children directly — no external templates/aesthetics.
        /// </summary>
        [System.Serializable]
        public class TileType
        {
            [Tooltip("Ground type id from the JSON toolset.")]
            public int groundId;
            [Tooltip("Display note only.")]
            public string label;
            public TileMode mode = TileMode.Ground;
            [Tooltip("Elevation layer. 0 = base ground; N > 0 paints a raised layer shape.")]
            [Range(0, 8)] public int height;
            [Tooltip("Optional tile; empty = the loader's default ground tile.")]
            public TileBase tileOverride;
            [Tooltip("Contributes to the base island silhouette (keep ON for water overlays).")]
            public bool inBaseFootprint = true;
            [Tooltip("Hidden pre-styled stack child under this loader, cloned per group as this " +
                     "type's rendered look. Empty = base footprint only (renders via Visual " +
                     "Stack Root, no separate stack).")]
            public Transform styledStack;
        }

        /// <summary>One (styled child, height) partition of the map's cells.</summary>
        private class StyleGroup
        {
            public GameObject sourceObject;  // pre-styled tile-type child to clone; null = base land stack
            public int height;
            public string name;
            public int orderBump;            // sorting offset within/above the height band
            public readonly List<Vector3Int> cells = new List<Vector3Int>();
            public readonly List<TileBase> tiles = new List<TileBase>();
            public Tilemap map;
            public Transform instance;
        }

        private readonly List<StyleGroup> _groups = new List<StyleGroup>();

        private Transform _generatedRoot;

        /// <summary>Scene object holding the generated style-group stacks, or null when no Regenerate
        /// has built one yet. Anything that needs to reach INTO the generated stacks (GrassMaskBinder)
        /// must look here as well as under the loader. Resolved by name, so it survives domain reload.</summary>
        public Transform GeneratedRoot => FindGeneratedRoot();

        // Painted cell -> elevation level, rebuilt each Paint. Feeds the per-edge
        // terrace depth sampler (cliffs drop to the layer below THAT edge).
        private readonly Dictionary<Vector3Int, int> _cellHeights = new Dictionary<Vector3Int, int>();

        /// <summary>Which JSON "layers" block an item came from.</summary>
        public enum LayerKind { Resource, Npc, LargeSprite, Decal }

        /// <summary>
        /// One entry of the JSON layers block, already mapped to world space. This is the spawn
        /// seam: the loader owns the painter→world maths, consumers (resource spawner, npc
        /// spawner) just read positions. Loader knows nothing about them — deps point one way.
        /// </summary>
        public struct LayerItem
        {
            public LayerKind kind;
            public string id;        // stable per-placement id from the JSON ("resource_007")
            public int typeId;       // toolset type id (2000 = Tree, ...)
            public string typeName;  // toolset display name
            public Vector3 world;    // island-surface position, elevation included
            public int level;        // elevation level of the tile underneath
        }

        private readonly List<LayerItem> _layerItems = new List<LayerItem>();

        /// <summary>One row of the inspector's loaded-tile-types table.</summary>
        public struct GroundTypeInfo
        {
            public int id;
            public string name;
            public int tileCount;
            public string mode;
        }

        private readonly List<GroundTypeInfo> _loadedTypes = new List<GroundTypeInfo>();

        /// <summary>Ground types present in the last parsed JSON (id, toolset name, usage, resolved mode).</summary>
        public IReadOnlyList<GroundTypeInfo> LoadedGroundTypes => _loadedTypes;

        /// <summary>Map name from the last parsed JSON, empty if none loaded.</summary>
        public string LoadedMapName { get; private set; } = "";

        private TileType FindTileType(int id)
        {
            if (tileTypes != null)
                for (int i = 0; i < tileTypes.Count; i++)
                    if (tileTypes[i].groundId == id) return tileTypes[i];
            return null;
        }

        // No auto-load: the baked level is PERSISTENT (painted tilemap + saved group stacks).
        // Rebuild only on demand via Regenerate — play mode shows the saved level as-is, so
        // stacks aren't re-instantiated at runtime (which drops SpriteShape fill meshes).

        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            var map = Parse();
            if (map == null) return;

            BuildTypeInfo(map);

            if (tilemap == null || defaultGroundTile == null)
            {
                Debug.LogError("[JsonMapLoader] Assign tilemap and defaultGroundTile to paint.", this);
                return;
            }

            // The aesthetics render every surface — raw tiles (base tilemap included)
            // are troubleshooting-only.
            var baseTilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (baseTilemapRenderer != null) baseTilemapRenderer.enabled = showRawGroupTiles;

            Clear();
            Paint(map);

            // Stacks have just been re-instantiated, so every reference into them is stale.
            onRegenerated.Invoke();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (tilemap != null) tilemap.ClearAllTiles();
            DestroyGroupObjects();
        }

        private David.MapJsonData Parse()
        {
            if (mapJson == null)
            {
                Debug.LogError("[JsonMapLoader] No mapJson assigned.", this);
                return null;
            }
            var map = JsonUtility.FromJson<David.MapJsonData>(mapJson.text);
            if (map == null || map.islands == null || map.islands.Count == 0)
            {
                Debug.LogError($"[JsonMapLoader] '{mapJson.name}' failed to parse or has no islands.", this);
                return null;
            }
            LoadedMapName = string.IsNullOrEmpty(map.mapName) ? mapJson.name : map.mapName;
            return map;
        }

        private void BuildTypeInfo(David.MapJsonData map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                    counts[t.ground] = counts.TryGetValue(t.ground, out var c) ? c + 1 : 1;
            }

            var names = new Dictionary<int, string>();
            if (map.toolset != null && map.toolset.groundTypes != null)
                foreach (var g in map.toolset.groundTypes)
                    names[g.id] = g.name;

            _loadedTypes.Clear();
            foreach (var kvp in counts)
            {
                var tt = FindTileType(kvp.Key);
                string mode = (tt != null ? tt.mode : TileMode.Ground).ToString();
                int h = tt != null ? Mathf.Max(0, tt.height) : 0;
                if (h > 0) mode += $" h{h}";
                if (tt != null && tt.styledStack != null) mode += $" →{tt.styledStack.name}";
                _loadedTypes.Add(new GroundTypeInfo
                {
                    id = kvp.Key,
                    name = names.TryGetValue(kvp.Key, out var n) ? n : "(not in toolset)",
                    tileCount = kvp.Value,
                    mode = mode,
                });
            }
            _loadedTypes.Sort((a, b) => a.id.CompareTo(b.id));
        }

        // ---------------------------------------------------------------------
        // Painting
        // ---------------------------------------------------------------------

        private void Paint(David.MapJsonData map)
        {
            // Bounds in painter coords (y-down) across all islands.
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                {
                    if (t.x < minX) minX = t.x;
                    if (t.x > maxX) maxX = t.x;
                    if (t.y < minY) minY = t.y;
                    if (t.y > maxY) maxY = t.y;
                }
            }
            if (maxX < minX)
            {
                Debug.LogWarning("[JsonMapLoader] Map has no tiles.", this);
                return;
            }

            int n = Mathf.Max(1, cellsPerTile);
            int offX = -((maxX - minX + 1) * n) / 2;
            int offY = -((maxY - minY + 1) * n) / 2;
            int painted = 0, skipped = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Bucket cells: base footprint + one style group per distinct
            // (template, profile, height). Plain height layers are groups too
            // (default template, null profile) — one code path for everything.
            var basePositions = new List<Vector3Int>();
            var baseTiles = new List<TileBase>();
            var groupLookup = new Dictionary<(Object, int), StyleGroup>();
            var painterHeights = new Dictionary<Vector2Int, int>(); // painter (x,y) -> h, for gizmos
            _cellHeights.Clear();

            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                {
                    // Each type's look IS a pre-styled child of this loader (cloned below),
                    // so no external template/aesthetic ref. Ids with no tile type paint as
                    // plain base ground.
                    var tt = FindTileType(t.ground);
                    var mode = tt != null ? tt.mode : TileMode.Ground;
                    if (mode == TileMode.Ignore) { skipped++; continue; }

                    var tile = tt != null && tt.tileOverride != null ? tt.tileOverride : defaultGroundTile;
                    int h = tt != null ? Mathf.Clamp(tt.height, 0, MaxLayers) : 0;
                    bool inFootprint = tt == null || tt.inBaseFootprint;
                    var sourceObject = tt != null && tt.styledStack != null ? tt.styledStack.gameObject : null;
                    // A type with its own styled child (or any raised layer) becomes its own
                    // group; plain base ground (no child, z0) just paints the base footprint.
                    bool styled = sourceObject != null || h > 0;

                    painterHeights[new Vector2Int(t.x, t.y)] = h;
                    // (cells recorded below in the block loop for edge-depth lookups)

                    StyleGroup group = null;
                    if (styled)
                    {
                        var key = ((Object)sourceObject, h);
                        if (!groupLookup.TryGetValue(key, out group))
                        {
                            group = new StyleGroup { sourceObject = sourceObject, height = h };
                            string baseName = sourceObject != null ? sourceObject.name : "Land";
                            group.name = $"{baseName}_z{h}";
                            groupLookup[key] = group;
                            _groups.Add(group);
                        }
                    }

                    int bx = (t.x - minX) * n + offX;
                    int by = (maxY - t.y) * n + offY; // painter y-down -> world y-up
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                        {
                            var cell = new Vector3Int(bx + i, by + j, 0);
                            _cellHeights[cell] = h;
                            if (inFootprint)
                            {
                                basePositions.Add(cell);
                                baseTiles.Add(tile);
                            }
                            if (group != null)
                            {
                                group.cells.Add(cell);
                                group.tiles.Add(tile);
                            }
                        }
                    painted++;
                }
            }

            // Styled groups at a height sort ABOVE that height's band floor (+2 clears
            // the land fill and its +1 fringe/decals); pure height layers keep the
            // band floor so plain terraces look exactly as before. Fine ordering
            // between styles is authored in the styled child's renderer.
            int styleIndex = 0;
            foreach (var g in _groups)
                g.orderBump = g.sourceObject != null ? 2 + styleIndex++ : 0;

            CreateGroupObjects();
            ApplyBaseStackOrder();
            SetVisualStackEnabled(updateVisualStack, enableGrassAndFoliage);

            tilemap.SetTiles(basePositions.ToArray(), baseTiles.ToArray());
            foreach (var g in _groups)
                if (g.map != null)
                    g.map.SetTiles(g.cells.ToArray(), g.tiles.ToArray());

            // Sorting is applied AFTER painting on purpose. Grass and foliage only spawn once their
            // tilemap has tiles (tilemapTileChanged -> Generate), which is strictly later than
            // CreateGroupObjects — so a sorting sweep done there runs over an empty stack and every
            // plant that appears afterwards keeps its prefab's own layer.
            ApplyStackSorting();

            BuildLayerGizmos();

            Debug.Log($"[JsonMapLoader] '{LoadedMapName}': painted {painted} tiles " +
                      $"({basePositions.Count} base cells, {map.islands.Count} island(s), " +
                      $"{skipped} ignored, {_groups.Count} style group(s)) at {n}x{n} cells/tile " +
                      $"in {sw.ElapsedMilliseconds} ms " +
                      $"(contour+skirt {(updateVisualStack ? "ON" : "OFF")}, " +
                      $"grass+foliage {(enableGrassAndFoliage ? "ON" : "OFF")}).", this);
        }

        // ---------------------------------------------------------------------
        // Style group objects (persistent — saved with the scene, rebuilt on Regenerate)
        // ---------------------------------------------------------------------

        /// <summary>
        /// The scene object generated stacks are parented to, created on first use. It mirrors this
        /// loader's world transform, so a stack at localPosition zero lands exactly where it did when
        /// stacks were children of the loader — but it lives at the SCENE root, so Regenerate never
        /// adds objects to the rig prefab instance.
        /// </summary>
        private Transform EnsureGeneratedRoot()
        {
            if (FindGeneratedRoot() == null)
            {
                var go = new GameObject(generatedRootName);
                if (go.scene != gameObject.scene)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
                _generatedRoot = go.transform;
            }
            _generatedRoot.SetParent(null, false);
            _generatedRoot.SetPositionAndRotation(transform.position, transform.rotation);
            _generatedRoot.localScale = transform.lossyScale;
            return _generatedRoot;
        }

        /// <summary>Re-find the generated root by name without creating one — the cached reference is
        /// gone after a domain reload, and Clear() must still reach the stacks it left behind.</summary>
        private Transform FindGeneratedRoot()
        {
            if (_generatedRoot != null) return _generatedRoot;
            if (!gameObject.scene.IsValid()) return null;
            foreach (var go in gameObject.scene.GetRootGameObjects())
                if (go.name == generatedRootName) { _generatedRoot = go.transform; break; }
            return _generatedRoot;
        }

        private void CreateGroupObjects()
        {
            if (_groups.Count == 0 || tilemap == null) return;
            var parent = tilemap.transform.parent; // the Grid — layers need its cell layout
            if (parent == null)
            {
                Debug.LogWarning("[JsonMapLoader] Base tilemap has no Grid parent — style groups skipped.", this);
                _groups.Clear();
                return;
            }

            var stackParent = EnsureGeneratedRoot();
            var baseRenderer = tilemap.GetComponent<TilemapRenderer>();
            foreach (var g in _groups)
            {
                int order = g.height * 10 + g.orderBump;

                var go = new GameObject($"JsonLayer_{g.name}");
                go.transform.SetParent(parent, false);
                // Raising the TILEMAP raises the contour the stack traces — the group's
                // SpriteShape renders higher without any transform trickery on the stack.
                go.transform.localPosition =
                    tilemap.transform.localPosition + new Vector3(0f, g.height * heightStep, 0f);
                go.transform.localScale = tilemap.transform.localScale;

                g.map = go.AddComponent<Tilemap>();
                var tr = go.AddComponent<TilemapRenderer>();
                tr.enabled = showRawGroupTiles;
                if (baseRenderer != null)
                {
                    tr.sortingLayerID = baseRenderer.sortingLayerID;
                    tr.sortingOrder = baseRenderer.sortingOrder + order;
                    tr.sortOrder = baseRenderer.sortOrder;
                    tr.mode = baseRenderer.mode;
                }

                // Clone the type's pre-styled child (its look is baked in); a raised layer
                // with no styled child falls back to the base land stack.
                var source = g.sourceObject != null
                    ? g.sourceObject
                    : (visualStackRoot != null ? visualStackRoot.gameObject : null);
                if (source == null) continue;

                var stack = Instantiate(source, stackParent);
                stack.SetActive(true); // styled-def children live inactive; the clone must render
                stack.name = $"JsonLayerStack_{g.name}";
                stack.transform.localPosition = Vector3.zero;

                // The def carries a preview Swatch (grid + tilemap) for the inspector — the
                // baked group traces the real group tilemap, so drop the swatch from the clone.
                var swatch = stack.transform.Find("Swatch");
                if (swatch != null)
                {
                    if (Application.isPlaying) Destroy(swatch.gameObject);
                    else DestroyImmediate(swatch.gameObject);
                }

                // Elevated layers: drop the skirt the platform's full elevation so its bottom reaches
                // the base surface. DepthOverride is SERIALIZED so it survives domain reload / play —
                // a runtime DepthSampler closure is null after reload, which would leave the skirt on
                // the base profile thickness and sink it below the surface. Set BEFORE tracing so the
                // skirt bakes with the right depth.
                if (g.height > 0)
                {
                    float drop = g.height * heightStep;
                    foreach (var sk in stack.GetComponentsInChildren<IslandSkirt>(true))
                    {
                        sk.DepthOverride = drop;
                        // One texture band per grid step, so a 3-step terrace wears the same
                        // sized rock as a 1-step one instead of a stretched copy.
                        sk.RimBandHeight = heightStep;
                    }
                }

                var t2s = stack.GetComponent<TilemapToSpriteShape>();
                if (t2s != null) t2s.SetSourceTilemap(g.map);

                // IslandSkirt sorts its mesh/fringe/decal renderers RELATIVE to this
                // renderer (profile offsets like skirt -1), so the height band of 10
                // keeps every skirt above the whole layer below. groundOrderBase then
                // slides the entire band below zero so props never lose the order fight
                // against the surface they stand on. Safe to accumulate: the stack is a
                // fresh clone of its template on every Regenerate.
                var controller = stack.GetComponent<SpriteShapeController>();
                if (controller != null && controller.spriteShapeRenderer != null)
                    controller.spriteShapeRenderer.sortingOrder += groundOrderBase + order;

                // The styled child already carries its profile/shape (baked when the def
                // was authored) — nothing to push here.

                ApplyLevelSortingLayer(stack, g.height);

                g.instance = stack.transform;
            }
        }

        /// <summary>
        /// IElevationSource: elevation level of the surface under a world position — the highest
        /// style-group height whose (raised) tilemap has a tile at that cell. The group maps share
        /// the base grid's cell indices, so one WorldToCell on the base tilemap keys them all.
        /// Queries live data, so it stays correct across runtime regeneration.
        /// </summary>
        public int LevelAt(Vector3 worldPos)
        {
            if (tilemap == null) return 0;
            var grid = tilemap.transform.parent;
            if (grid == null) return 0;

            // Base grid cell for this world position — shared by every layer's tile data.
            var cell = tilemap.WorldToCell(worldPos);
            float baseY = tilemap.transform.localPosition.y;
            float step = Mathf.Max(0.0001f, heightStep);

            // Scan the LIVE group tilemaps (siblings under the Grid), inferring each one's height
            // from its Y-offset. Independent of the runtime `_groups` list (empty after a saved-scene
            // load), so it works at play as well as in the editor.
            int level = 0;
            foreach (Transform child in grid)
            {
                if (!child.name.StartsWith("JsonLayer_")) continue;
                var map = child.GetComponent<Tilemap>();
                if (map == null) continue;
                int h = Mathf.RoundToInt((child.localPosition.y - baseY) / step);
                if (h > level && map.HasTile(cell)) level = h;
            }
            return level;
        }

        /// <summary>
        /// Seat the base land stack at the bottom of the ground band. It is NOT re-cloned each
        /// Regenerate like the style groups are, so this assigns rather than accumulates.
        /// </summary>
        private void ApplyBaseStackOrder()
        {
            if (visualStackRoot == null) return;
            var controller = visualStackRoot.GetComponent<SpriteShapeController>();
            if (controller != null && controller.spriteShapeRenderer != null)
                controller.spriteShapeRenderer.sortingOrder = groundOrderBase;

            int top = groundOrderBase;
            foreach (var g in _groups) top = Mathf.Max(top, groundOrderBase + g.height * 10 + g.orderBump);
            if (top >= 0)
                Debug.LogWarning($"[JsonMapLoader] Ground band reaches order {top} — props authored at " +
                                 "order 0 will sink behind the surface. Lower Ground Order Base.", this);
        }

        /// <summary>
        /// Final sorting pass over every stack, run after the tilemaps are painted so it also
        /// catches the grass and foliage that painting just spawned. Pushes the stack's sorting
        /// layer into its scatterers (their own <c>SetSortingLayer</c>, which nothing called before)
        /// and rebuilds them, so decor lands on the layer of the terrace it grows on instead of
        /// defaulting to Default and sinking behind the island.
        /// </summary>
        private void ApplyStackSorting()
        {
            foreach (var g in _groups)
            {
                if (g.instance == null) continue;
                ApplyLevelSortingLayer(g.instance.gameObject, g.height);
                PushScatterSorting(g.instance, g.height);
            }
            PushScatterSorting(visualStackRoot, 0);
        }

        /// <summary>Grass + foliage own their sorting layer independently of the renderers
        /// <see cref="ApplyLevelSortingLayer"/> sweeps, because they respawn after it. Give them the
        /// layer directly and regenerate so the change lands on the instances they already made.</summary>
        private void PushScatterSorting(Transform root, int height)
        {
            if (root == null) return;
            string layer = perLevelSortingLayers ? LevelLayerName(height) : null;
            if (perLevelSortingLayers && layer == null) return;

            foreach (var b in root.GetComponentsInChildren<BillboardGrass>(true))
            {
                if (layer != null) b.SetSortingLayer(layer);
                if (b.enabled && b.gameObject.activeInHierarchy) b.Generate();
            }
            foreach (var b in root.GetComponentsInChildren<IslandFoliage>(true))
            {
                if (layer != null) b.SetSortingLayer(layer);
                if (b.enabled && b.gameObject.activeInHierarchy) b.Generate();
            }
        }

        /// <summary>Sorting-layer name for an elevation, or null when the project has no such layer.</summary>
        private string LevelLayerName(int height)
        {
            string layer = levelLayerPrefix + Mathf.Clamp(height, 0, MaxLayers);
            foreach (var l in SortingLayer.layers) if (l.name == layer) return layer;
            Debug.LogWarning($"[JsonMapLoader] Sorting layer '{layer}' missing — add it in " +
                             "Project Settings > Tags and Layers.", this);
            return null;
        }

        /// <summary>
        /// Move every renderer of a raised stack onto its elevation's sorting layer
        /// (Level{height}) so a SortableSprite-driven player crosses tiers cleanly. Opt-in;
        /// no-op unless <see cref="perLevelSortingLayers"/> is set and the layer exists.
        /// </summary>
        private void ApplyLevelSortingLayer(GameObject root, int height)
        {
            if (!perLevelSortingLayers || root == null) return;
            string layer = LevelLayerName(height);
            if (layer == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.sortingLayerName = layer;
        }


        /// <summary>
        /// Hang depth for a terrace cliff edge at a world position: the elevation
        /// difference to the painted layer directly BELOW that edge, in world units.
        /// Probes the base-grid cell half a cell under the (raised) contour point.
        /// </summary>
        private float TerraceDepthAt(Vector3 worldPos, int layerHeight)
        {
            if (tilemap == null) return layerHeight * heightStep;
            float cellH = Mathf.Abs(
                (tilemap.CellToWorld(new Vector3Int(0, 1, 0)) - tilemap.CellToWorld(Vector3Int.zero)).y);
            var probe = worldPos - new Vector3(0f, layerHeight * heightStep + cellH * 0.5f, 0f);
            var cell = tilemap.WorldToCell(probe);
            cell.z = 0;
            if (!_cellHeights.TryGetValue(cell, out int below)) below = 0;
            if (below >= layerHeight) below = layerHeight - 1; // never less than one step
            return (layerHeight - below) * heightStep;
        }

        private void DestroyGroupObjects()
        {
            _groups.Clear();
            if (tilemap != null) SweepChildren(tilemap.transform.parent, "JsonLayer_");
            // Stacks live under the generated root now. Sweep the loader too: scenes baked before
            // that change still carry their clones there, as prefab-instance added objects.
            SweepChildren(transform, "JsonLayerStack_");
            SweepChildren(FindGeneratedRoot(), "JsonLayerStack_");
        }

        private static void SweepChildren(Transform parent, string prefix)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var c = parent.GetChild(i);
                if (!c.name.StartsWith(prefix)) continue;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }

        // ---------------------------------------------------------------------
        // Stack toggles (troubleshooting)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Toggle the island-stack components on the base stack and every group
        /// instance (isle children included — they carry their own skirts). Disabled
        /// components unsubscribe from Tilemap.tilemapTileChanged, so painting skips
        /// them. Contour + skirt follow <see cref="updateVisualStack"/>; grass +
        /// foliage follow <see cref="enableGrassAndFoliage"/> separately. Templates
        /// without a component (water has no skirt) are naturally unaffected.
        /// </summary>
        private void SetVisualStackEnabled(bool contourAndSkirt, bool grassAndFoliage)
        {
            ToggleStack(visualStackRoot, contourAndSkirt, grassAndFoliage);
            foreach (var g in _groups) ToggleStack(g.instance, contourAndSkirt, grassAndFoliage);
        }

        private static void ToggleStack(Transform root, bool contourAndSkirt, bool grassAndFoliage)
        {
            if (root == null) return;
            foreach (var b in root.GetComponentsInChildren<TilemapToSpriteShape>(true)) b.enabled = contourAndSkirt;
            foreach (var b in root.GetComponentsInChildren<IslandSkirt>(true)) b.enabled = contourAndSkirt;
            // When grass/foliage is off, Generate() against the (still-empty) tilemap
            // first — it clears its own output, so no stale blades or flora instances
            // linger from the previous map or from cloning the base stack.
            foreach (var b in root.GetComponentsInChildren<BillboardGrass>(true))
            {
                if (!grassAndFoliage) b.Generate();
                b.enabled = grassAndFoliage;
            }
            foreach (var b in root.GetComponentsInChildren<IslandFoliage>(true))
            {
                if (!grassAndFoliage) b.Generate();
                b.enabled = grassAndFoliage;
            }
        }

        // ---------------------------------------------------------------------
        // Debug gizmos for the JSON layers block (pre-spawn-pass visualization)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Every entry of the JSON "layers" block, resolved to a world position on the built
        /// island. Self-contained: it re-parses the map and redoes the painter→world mapping
        /// rather than reading state left over from the last Paint, so a spawner can call it at
        /// runtime on a scene that was never regenerated this session.
        /// </summary>
        public List<LayerItem> BuildLayerItems()
        {
            var items = new List<LayerItem>();
            var map = Parse();
            if (map == null || map.layers == null || tilemap == null) return items;

            // Same bounds + centering Paint uses — the two must agree or items land off the island.
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            var painterHeights = new Dictionary<Vector2Int, int>();
            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                {
                    if (t.x < minX) minX = t.x;
                    if (t.x > maxX) maxX = t.x;
                    if (t.y < minY) minY = t.y;
                    if (t.y > maxY) maxY = t.y;
                    var tt = FindTileType(t.ground);
                    painterHeights[new Vector2Int(t.x, t.y)] =
                        tt != null ? Mathf.Clamp(tt.height, 0, MaxLayers) : 0;
                }
            }
            if (maxX < minX) return items;

            int n = Mathf.Max(1, cellsPerTile);
            int offX = -((maxX - minX + 1) * n) / 2;
            int offY = -((maxY - minY + 1) * n) / 2;

            Vector3 WorldOf(int x, int y, out int level)
            {
                int bx = (x - minX) * n + offX;
                int by = (maxY - y) * n + offY; // painter y-down -> world y-up
                Vector3 a = tilemap.GetCellCenterWorld(new Vector3Int(bx, by, 0));
                Vector3 b = tilemap.GetCellCenterWorld(new Vector3Int(bx + n - 1, by + n - 1, 0));
                Vector3 p = (a + b) * 0.5f; // center of the item's n×n cell block
                painterHeights.TryGetValue(new Vector2Int(x, y), out level);
                p.y += level * heightStep; // sit on the elevated plateau, like tiles do
                return p;
            }

            var typeNames = new Dictionary<(LayerKind, int), string>();
            if (map.toolset != null)
            {
                if (map.toolset.resourceTypes != null)
                    foreach (var t in map.toolset.resourceTypes) typeNames[(LayerKind.Resource, t.id)] = t.name;
                if (map.toolset.npcTypes != null)
                    foreach (var t in map.toolset.npcTypes) typeNames[(LayerKind.Npc, t.id)] = t.name;
                if (map.toolset.largeSpriteTypes != null)
                    foreach (var t in map.toolset.largeSpriteTypes) typeNames[(LayerKind.LargeSprite, t.id)] = t.name;
                if (map.toolset.decalTypes != null)
                    foreach (var t in map.toolset.decalTypes) typeNames[(LayerKind.Decal, t.id)] = t.name;
            }

            void Add(LayerKind kind, string id, int typeId, int x, int y)
            {
                Vector3 p = WorldOf(x, y, out int level);
                items.Add(new LayerItem
                {
                    kind = kind,
                    id = id,
                    typeId = typeId,
                    typeName = typeNames.TryGetValue((kind, typeId), out var s) ? s : kind.ToString(),
                    world = p,
                    level = level,
                });
            }

            if (map.layers.resources != null)
                foreach (var r in map.layers.resources) Add(LayerKind.Resource, r.id, r.typeId, r.x, r.y);
            if (map.layers.npcs != null)
                foreach (var r in map.layers.npcs) Add(LayerKind.Npc, r.id, r.typeId, r.x, r.y);
            if (map.layers.largeSprites != null)
                foreach (var r in map.layers.largeSprites) Add(LayerKind.LargeSprite, r.id, r.typeId, r.x, r.y);
            if (map.layers.decals != null)
                foreach (var r in map.layers.decals) Add(LayerKind.Decal, r.id, r.typeId, r.x, r.y);
            return items;
        }

        private void BuildLayerGizmos()
        {
            _layerItems.Clear();
            if (drawLayerGizmos) _layerItems.AddRange(BuildLayerItems());
        }

        private void OnDrawGizmos()
        {
            if (!drawLayerGizmos) return;
            foreach (var g in _layerItems)
            {
                switch (g.kind)
                {
                    case LayerKind.Resource:
                        Gizmos.color = new Color(0.3f, 0.9f, 0.4f);
                        Gizmos.DrawWireSphere(g.world, 0.2f);
                        break;
                    case LayerKind.Npc:
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawWireCube(g.world, new Vector3(0.4f, 0.4f, 0.01f));
                        break;
                    case LayerKind.LargeSprite:
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawWireCube(g.world, new Vector3(0.7f, 0.5f, 0.01f));
                        break;
                    case LayerKind.Decal:
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(g.world, 0.12f);
                        break;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawLayerGizmos) return;
            foreach (var g in _layerItems)
                UnityEditor.Handles.Label(g.world + Vector3.up * 0.25f, $"{g.typeName} [{g.typeId}]");
        }
#endif
    }
}
