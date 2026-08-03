using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Designer paint layer: paint ANY tile on this GameObject's Tilemap and the
    /// aesthetic's stack template (LandStack / WaterStack) generates the real
    /// visuals over the painted cells — the tiles are a mask, never shown.
    /// Tile Palette Z Position = elevation: cells painted at Z n spawn their own
    /// stack raised n × elevationStep. Only the TOP Z per column renders (buried
    /// cells are ignored); land skirts drop per edge to the painted layer below
    /// that edge (base ground when none) so stacked layers read as terraces, and
    /// the fringe lip is suppressed where a higher layer sits on the join. Water
    /// stacks have no skirt — their Y is simply set by the same rule (plus
    /// baseYOffset). One component per ground type
    /// (WaterGrid, PlatformGrid, ...); sibling GameObject names must be unique.
    /// Same authoring loop as BillboardGrass's GrassGrid; same stack machinery
    /// as JsonMapLoader, minus the JSON.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Tilemap))]
    public class PaintedStyleLayer : MonoBehaviour
    {
        [Tooltip("Visual identity for this layer (stack template + profile), e.g. Aesthetic_Water / Aesthetic_HighlandIsle.")]
        [SerializeField] private TileAestheticSO _aesthetic;

        [Tooltip("World Y added per painted cell Z (Tile Palette Z Position). Match JsonMapLoader heightStep.")]
        [SerializeField] private float _elevationStep = 0.25f;

        [Tooltip("Flat world-Y offset added to every spawned layer (lets water sit exactly where you set it). Not part of skirt depth.")]
        [SerializeField] private float _baseYOffset = 0f;

        [Tooltip("Added to the template's SpriteShapeRenderer sorting order, plus z × 10 per elevation. Give each PaintedStyleLayer a distinct value so same-elevation layers never tie.")]
        [SerializeField] private int _sortingOrderBase = 10;

        [Tooltip("Disable BillboardGrass/IslandFoliage inside spawned stacks — scatter is authored on the GrassGrid mask instead.")]
        [SerializeField] private bool _disableTemplateScatter = true;

        private Tilemap _map;

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void OnEnable()
        {
            _map = GetComponent<Tilemap>();
            Tilemap.tilemapTileChanged += OnTilemapTileChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorTick;
#endif
            Rebuild();
        }

        private void OnDisable()
        {
            Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            // Layer toggle = visuals gone; DontSave children rebuild on enable.
            // Deferred in edit mode: OnDisable can fire while the parent hierarchy
            // is deactivating, where DestroyImmediate throws.
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null || isActiveAndEnabled) return;
                    Sweep(null);
                };
                return;
            }
#endif
            Sweep(null);
        }

        private void OnTilemapTileChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (map != _map || _map == null) return; // own child maps also fire this — ignore
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Debounce a brush drag — rebuilding contour + skirt per painted cell chugs on
                // island-sized masks. One rebuild per stroke instead.
                _rebuildAt = UnityEditor.EditorApplication.timeSinceStartup + 0.3;
                _rebuildPending = true;
                return;
            }
#endif
            Rebuild();
        }

#if UNITY_EDITOR
        private double _rebuildAt;
        private bool _rebuildPending;

        private void EditorTick()
        {
            if (!_rebuildPending || UnityEditor.EditorApplication.timeSinceStartup < _rebuildAt) return;
            _rebuildPending = false;
            if (this != null && isActiveAndEnabled) Rebuild();
        }
#endif

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                Rebuild();
            };
        }
#endif

        // ---------------------------------------------------------------------
        // Generation
        // ---------------------------------------------------------------------

        /// <summary>Rebuild all elevation layers from the painted mask. Safe at runtime.</summary>
        [ContextMenu("Rebuild Layers")]
        public void Rebuild()
        {
            if (_map == null) _map = GetComponent<Tilemap>();
            var parent = transform.parent;
            if (parent == null)
            {
                Debug.LogWarning($"[{nameof(PaintedStyleLayer)}] '{name}' needs a Grid parent — layers skipped.", this);
                return;
            }
            if (_map == null || _aesthetic == null || _aesthetic.stackTemplate == null)
            {
                Sweep(null);
                return;
            }

            // Only the TOP painted Z per (x,y) renders — a z2 platform is authored
            // as z2 alone, nothing underneath; its skirt drops the full way to the
            // ground. Lower cells under a higher cell are simply ignored.
            var topZ = new Dictionary<Vector2Int, int>();
            var painted = new List<(Vector3Int pos, TileBase tile)>();
            foreach (var pos in _map.cellBounds.allPositionsWithin)
            {
                var tile = _map.GetTile(pos);
                if (tile == null) continue;
                painted.Add((pos, tile));
                var xy = new Vector2Int(pos.x, pos.y);
                if (!topZ.TryGetValue(xy, out int t) || pos.z > t) topZ[xy] = pos.z;
            }

            var byZ = new Dictionary<int, (List<Vector3Int> cells, List<TileBase> tiles)>();
            foreach (var (pos, tile) in painted)
            {
                if (topZ[new Vector2Int(pos.x, pos.y)] != pos.z) continue; // buried
                if (!byZ.TryGetValue(pos.z, out var b))
                    byZ[pos.z] = b = (new List<Vector3Int>(), new List<TileBase>());
                b.cells.Add(new Vector3Int(pos.x, pos.y, 0));
                b.tiles.Add(tile);
            }

            var keep = new HashSet<string>();
            foreach (var kvp in byZ)
            {
                int z = kvp.Key;
                string mapName = $"{name}_cells_z{z}";
                string stackName = $"{name}_stack_z{z}";
                keep.Add(mapName);
                keep.Add(stackName);

                // Cell layer: raising the TILEMAP raises the contour the stack
                // traces — the SpriteShape renders higher with no transform trickery.
                var mapT = parent.Find(mapName);
                Tilemap layerMap;
                if (mapT == null)
                {
                    var go = new GameObject(mapName) { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(parent, false);
                    layerMap = go.AddComponent<Tilemap>();
                    mapT = go.transform;
                }
                else layerMap = mapT.GetComponent<Tilemap>();
                mapT.localPosition = transform.localPosition
                    + new Vector3(0f, z * _elevationStep + _baseYOffset, 0f);
                mapT.localScale = transform.localScale;
                layerMap.ClearAllTiles();
                layerMap.SetTiles(kvp.Value.cells.ToArray(), kvp.Value.tiles.ToArray());

                // Stack instance (structure); parented to the Grid so lossyScale stays 1.
                var stackT = parent.Find(stackName);
                bool fresh = stackT == null;
                GameObject stack;
                if (fresh)
                {
                    stack = Instantiate(_aesthetic.stackTemplate, parent);
                    stack.name = stackName;
                    stack.hideFlags = HideFlags.DontSave;
                    stack.transform.localPosition = Vector3.zero;
                }
                else stack = stackT.gameObject;

                var t2s = stack.GetComponent<TilemapToSpriteShape>();
                if (t2s != null) t2s.SetSourceTilemap(layerMap);

                // Land stacks: each layer is ONE step tall — the skirt drops per
                // edge to whatever this mask painted below (base ground when
                // nothing), so stacked layers read as terraces. The fringe lip is
                // suppressed along contour stretches where a HIGHER layer sits
                // right across the join — it would poke out of that wall.
                {
                    int layerZ = z;
                    var heights = topZ;
                    foreach (var sk in stack.GetComponentsInChildren<IslandSkirt>(true))
                    {
                        if (layerZ != 0)
                            sk.DepthSampler = p => DropDepthAt(p, layerZ, heights);
                        sk.FringeMask = (p, outward) => CoveredAcross(p, outward, layerZ, heights);
                    }
                }

                if (_disableTemplateScatter)
                {
                    foreach (var g in stack.GetComponentsInChildren<BillboardGrass>(true)) g.enabled = false;
                    foreach (var f in stack.GetComponentsInChildren<IslandFoliage>(true)) f.enabled = false;
                }

                if (fresh)
                {
                    var ctrl = stack.GetComponent<UnityEngine.U2D.SpriteShapeController>();
                    if (ctrl != null && ctrl.spriteShapeRenderer != null)
                        ctrl.spriteShapeRenderer.sortingOrder += _sortingOrderBase + z * 10;
                }

                _aesthetic.ApplyTo(stack); // VALUES axis: profile + shape override
                if (t2s != null) t2s.GenerateBoundary();
            }

            Sweep(keep);
        }

        /// <summary>
        /// Hang depth for a cliff edge at a world position: elevation difference
        /// to this mask's painted layer directly below that edge, in world units.
        /// Probes half a cell under the (raised) contour point — same recipe as
        /// JsonMapLoader.TerraceDepthAt.
        /// </summary>
        private float DropDepthAt(Vector3 worldPos, int layerZ, Dictionary<Vector2Int, int> topZ)
        {
            float cellH = Mathf.Abs(
                (_map.CellToWorld(new Vector3Int(0, 1, 0)) - _map.CellToWorld(Vector3Int.zero)).y);
            var probe = worldPos - new Vector3(0f, layerZ * _elevationStep + _baseYOffset + cellH * 0.5f, 0f);
            var cell = _map.WorldToCell(probe);
            int below = 0;
            if (topZ.TryGetValue(new Vector2Int(cell.x, cell.y), out int t)) below = t;
            if (below >= layerZ) below = layerZ - 1; // never less than one step
            return (layerZ - below) * _elevationStep;
        }

        /// <summary>
        /// True when the painted column just ACROSS this contour point (half a
        /// cell outward) is higher than this layer — a platform sits on the
        /// join, so the fringe lip must not poke out of its wall.
        /// </summary>
        private bool CoveredAcross(Vector3 worldPos, Vector3 outward, int layerZ, Dictionary<Vector2Int, int> topZ)
        {
            Vector3 v00 = _map.CellToWorld(Vector3Int.zero);
            float cellW = Mathf.Abs((_map.CellToWorld(new Vector3Int(1, 0, 0)) - v00).x);
            float cellH = Mathf.Abs((_map.CellToWorld(new Vector3Int(0, 1, 0)) - v00).y);
            var probe = worldPos - new Vector3(0f, layerZ * _elevationStep + _baseYOffset, 0f);
            probe += new Vector3(outward.x * cellW * 0.45f, outward.y * cellH * 0.45f, 0f);
            var cell = _map.WorldToCell(probe);
            return topZ.TryGetValue(new Vector2Int(cell.x, cell.y), out int t) && t > layerZ;
        }

        /// <summary>Destroy child layers not in <paramref name="keep"/> (null = all).</summary>
        private void Sweep(HashSet<string> keep)
        {
            var parent = transform.parent;
            if (parent == null) return;
            string cellsPrefix = name + "_cells_z";
            string stackPrefix = name + "_stack_z";
            var kill = new List<GameObject>();
            foreach (Transform c in parent)
                if ((c.name.StartsWith(cellsPrefix) || c.name.StartsWith(stackPrefix))
                    && (keep == null || !keep.Contains(c.name)))
                    kill.Add(c.gameObject);
            foreach (var go in kill)
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }
    }
}
