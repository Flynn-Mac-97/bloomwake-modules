using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Bakes the ISLAND COASTLINE as a solid collider outline under one <c>PlatformBorders</c>
    /// root, so it can be prefabbed and culled independently of the visual loader.
    ///
    /// Scope note: this used to bake raised platforms too. Platforms now own their collision —
    /// Flynn.Modules.PlatformTiles emits a footprint Tilemap + CompositeCollider2D at the ground
    /// plane, the standard 2D-isometric way — so this builder handles level 0 only.
    ///
    /// Reads the base ground tilemap painted by <see cref="JsonMapLoader"/> and traces its boundary
    /// directly (marching-squares edge stitch) into closed <see cref="EdgeCollider2D"/> loops.
    /// Direct trace rather than TilemapCollider2D+Composite here because these points survive
    /// prefabbing, which the composite's tile refs do not.
    ///
    /// Editor-time generator: paint the level (JsonMapLoader.Regenerate), then run "Build Borders".
    /// Re-runnable — clears and rebuilds.
    /// </summary>
    public class PlatformBorderBuilder : MonoBehaviour
    {
        [Tooltip("The level loader whose painted tilemaps define the borders. Auto-found on this " +
                 "GameObject if left empty.")]
        [SerializeField] private JsonMapLoader _loader;

        [Tooltip("Direct base tilemap for LOADER-LESS authoring (PaintedStyleLayer masks): assign " +
                 "the ground mask tilemap and leave the loader empty.")]
        [SerializeField] private Tilemap _baseTilemap;
        [Tooltip("Elevation step when no loader is assigned — match PaintedStyleLayer.")]
        [SerializeField] private float _heightStep = 0.25f;

        [Tooltip("Parent for the generated Border_Level* children. Auto-created as a child named " +
                 "'PlatformBorders' under the Grid if left empty.")]
        [SerializeField] private Transform _bordersRoot;

        [Tooltip("Drop redundant midpoints along straight runs so each loop is corners-only.")]
        [SerializeField] private bool _simplifyCollinear = true;



        private const string RootName = "PlatformBorders";

        [ContextMenu("Build Borders")]
        public void BuildBorders()
        {
            if (_loader == null && _baseTilemap == null) _loader = GetComponent<JsonMapLoader>();
            var baseTilemap = _baseTilemap != null ? _baseTilemap
                            : _loader != null ? _loader.tilemap : null;
            if (baseTilemap == null)
            {
                Debug.LogError("[PlatformBorderBuilder] No base tilemap — assign the JsonMapLoader " +
                               "(and Regenerate) or the Base Tilemap directly.", this);
                return;
            }

            var baseT = baseTilemap.transform;
            var grid = baseT.parent;
            float baseY = baseT.localPosition.y;
            float step = Mathf.Max(0.0001f, _loader != null ? _loader.heightStep : _heightStep);

            // Occupied cells per elevation level: base ground = level 0, each JsonLayer_* group at
            // the level inferred from its Y-offset (same rule JsonMapLoader.LevelAt uses).
            var byLevel = new Dictionary<int, HashSet<Vector2Int>>();
            Gather(byLevel, 0, baseTilemap);
            foreach (Transform child in grid)
            {
                // JsonMapLoader groups or PaintedStyleLayer generated cell maps — same shape.
                if (!child.name.StartsWith("JsonLayer_") && !child.name.Contains("_cells_z")) continue;
                var tm = child.GetComponent<Tilemap>();
                if (tm == null || tm == baseTilemap) continue;
                int h = Mathf.RoundToInt((child.localPosition.y - baseY) / step);
                Gather(byLevel, h, tm);
            }

            var root = EnsureRoot(grid);
            ClearChildren(root);

            // One group: the island coastline traced on the base plane.
            var ground = NewGroup(root, "Ground");

            foreach (var kv in byLevel)
            {
                int level = kv.Key;
                // Raised platforms own their own collision now: Flynn.Modules.PlatformTiles emits a
                // footprint Tilemap + CompositeCollider2D at the ground plane, the standard 2D-iso
                // way. Emitting them here too would double up — and this builder's inflated half-cube
                // outline blocks earlier, so it would mask the tile collider entirely.
                // This builder is now the ISLAND COASTLINE only.
                if (level > 0) continue;

                var loops = TraceLoops(kv.Value);
                EmitLoops(ground, $"Ground_Level{level}", loops, baseTilemap, 0f, GroundBorderLayer);
            }

            Debug.Log($"[PlatformBorderBuilder] Built the island coastline under '{RootName}' " +
                      "(raised platforms are collided by PlatformTiles).", root);
            MarkDirty();
        }

        private static Transform NewGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // Emit one EdgeCollider2D child per loop under a named group, projected to yOffset. Every
        // emitted object lands on `layer` so PlayerMover.blockers can filter ground vs raised.
        private void EmitLoops(Transform parent, string name, List<List<Vector2Int>> loops, Tilemap baseTm, float yOffset, int layer)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);   // identity — points baked in world
            if (layer >= 0) group.layer = layer;
            int i = 0;
            foreach (var loop in loops)
            {
                var pts = ToLocalPoints(loop, baseTm, group.transform, yOffset, _simplifyCollinear);
                if (pts.Length < 4) continue;
                var go = new GameObject($"Edge{i++}");
                go.transform.SetParent(group.transform, false);
                if (layer >= 0) go.layer = layer;
                go.AddComponent<EdgeCollider2D>().points = pts;
            }
        }

        // Resolve the project's border layers once; -1 (layer absent) leaves objects on Default.
        private static int GroundBorderLayer => LayerMask.NameToLayer("GroundBorder");

        [ContextMenu("Clear Borders")]
        public void ClearBorders()
        {
            var root = _bordersRoot != null ? _bordersRoot : FindRoot();
            if (root != null) DestroyImmediate(root.gameObject);
            MarkDirty();
        }

        private static void Gather(Dictionary<int, HashSet<Vector2Int>> map, int level, Tilemap tm)
        {
            if (!map.TryGetValue(level, out var set)) { set = new HashSet<Vector2Int>(); map[level] = set; }
            foreach (var pos in tm.cellBounds.allPositionsWithin)
                if (tm.GetTile(pos) != null) set.Add(new Vector2Int(pos.x, pos.y));
        }

        // Trace the boundary of a filled cell region into closed corner loops. Each occupied cell
        // contributes a directed edge for any side whose neighbour is empty, wound so the region
        // sits on the left; edges then stitch head-to-tail into loops (outer boundary, holes, and
        // each disconnected platform become separate loops).
        private static List<List<Vector2Int>> TraceLoops(HashSet<Vector2Int> cells)
        {
            var edges = new Dictionary<Vector2Int, List<Vector2Int>>();
            void Add(Vector2Int a, Vector2Int b)
            {
                if (!edges.TryGetValue(a, out var l)) { l = new List<Vector2Int>(); edges[a] = l; }
                l.Add(b);
            }

            foreach (var c in cells)
            {
                int x = c.x, y = c.y;
                if (!cells.Contains(new Vector2Int(x, y - 1))) Add(new Vector2Int(x, y), new Vector2Int(x + 1, y));         // bottom  +x
                if (!cells.Contains(new Vector2Int(x + 1, y))) Add(new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1)); // right   +y
                if (!cells.Contains(new Vector2Int(x, y + 1))) Add(new Vector2Int(x + 1, y + 1), new Vector2Int(x, y + 1)); // top     -x
                if (!cells.Contains(new Vector2Int(x - 1, y))) Add(new Vector2Int(x, y + 1), new Vector2Int(x, y));         // left    -y
            }

            var loops = new List<List<Vector2Int>>();
            foreach (var start in new List<Vector2Int>(edges.Keys))
            {
                while (edges.TryGetValue(start, out var outs) && outs.Count > 0)
                {
                    var loop = new List<Vector2Int> { start };
                    var cur = start;
                    while (edges.TryGetValue(cur, out var lst) && lst.Count > 0)
                    {
                        var nxt = lst[lst.Count - 1];
                        lst.RemoveAt(lst.Count - 1);
                        cur = nxt;
                        if (cur == start) break;                  // closed
                        loop.Add(cur);
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
            }
            return loops;
        }

        // Project each cell corner through the tilemap's own CellToWorld (respects the ISOMETRIC
        // cell layout + the tilemap's scale/offset), then express it in the border child's local
        // space so the outline lands on the visible iso footprint. yOffset raises the whole loop
        // for the non-footprint (raised-plane) option.
        private static Vector2[] ToLocalPoints(List<Vector2Int> loop, Tilemap baseTm, Transform borderChild, float yOffset, bool simplify)
        {
            var corners = loop;
            if (simplify && loop.Count > 2)
            {
                corners = new List<Vector2Int>();
                for (int i = 0; i < loop.Count; i++)
                {
                    var prev = loop[(i - 1 + loop.Count) % loop.Count];
                    var here = loop[i];
                    var next = loop[(i + 1) % loop.Count];
                    var d1 = here - prev;
                    var d2 = next - here;
                    // keep only true corners (direction change); the iso projection is linear, so
                    // cell-collinear runs stay collinear in world.
                    if (d1.x * d2.y - d1.y * d2.x != 0) corners.Add(here);
                }
                if (corners.Count < 3) corners = loop;
            }

            var pts = new Vector2[corners.Count + 1];
            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 w = baseTm.CellToWorld(new Vector3Int(corners[i].x, corners[i].y, 0));
                w.y += yOffset;
                Vector3 lp = borderChild.InverseTransformPoint(w);
                pts[i] = new Vector2(lp.x, lp.y);
            }
            pts[corners.Count] = pts[0];                          // close the loop
            return pts;
        }

        private Transform EnsureRoot(Transform grid)
        {
            if (_bordersRoot != null) return _bordersRoot;
            var existing = FindRoot();
            if (existing != null) { _bordersRoot = existing; return existing; }
            var go = new GameObject(RootName);
            go.transform.SetParent(grid, false);
            _bordersRoot = go.transform;
            return _bordersRoot;
        }

        private Transform FindRoot()
        {
            if (_loader == null) return null;
            var grid = _loader.tilemap != null ? _loader.tilemap.transform.parent : null;
            return grid != null ? grid.Find(RootName) : null;
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
