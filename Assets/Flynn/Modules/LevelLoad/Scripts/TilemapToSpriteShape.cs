using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>Boundary tracing style.</summary>
    public enum BoundaryMode
    {
        /// <summary>Points at tile edge midpoints — smooth-ish contour (marching squares).</summary>
        EdgeMidpoints,
        /// <summary>Points at tile corner vertices — clean square/blocky contour.</summary>
        TileCorners,
    }

    /// <summary>
    /// Reads a Tilemap and sets a SpriteShape spline to trace the boundary of
    /// all filled tiles. The tilemap is the editing/painting layer; the
    /// SpriteShape is the runtime visual.
    ///
    /// Regenerates automatically when the tilemap is painted (edit and play
    /// mode) via Tilemap.tilemapTileChanged. Consumers that need the exact
    /// boundary polygon (e.g. IslandSkirt) read <see cref="ContourWorld"/> and
    /// subscribe to <see cref="ContourChanged"/> instead of re-sampling the
    /// rendered spline. The contour is always counter-clockwise.
    ///
    /// Works with any cell layout (rectangle, isometric, hexagonal) because it
    /// uses the tilemap's own CellToWorld conversion for coordinate transforms.
    /// </summary>
    [ExecuteAlways]
    public class TilemapToSpriteShape : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private Tilemap _tilemap;

        [Header("Target")]
        [Tooltip("If unassigned, looks for a SpriteShapeController on this GameObject.")]
        [SerializeField] private SpriteShapeController _spriteShape;

        [Header("Settings")]
        [SerializeField] private bool _generateOnStart = true;
        [Tooltip("Regenerate automatically when the tilemap is painted or inspector values change.")]
        [SerializeField] private bool _autoRegenerateInEditor = true;
        [Tooltip("EdgeMidpoints = smooth contour through tile edge midpoints.\nTileCorners = blocky contour through tile corner vertices.")]
        [SerializeField] private BoundaryMode _boundaryMode = BoundaryMode.EdgeMidpoints;
        [Tooltip("Tangent mode for spline control points.")]
        [SerializeField] private ShapeTangentMode _tangentMode = ShapeTangentMode.Continuous;
        [Tooltip("Auto-inset vertices to compensate for bezier bulge on curved tangents.\n1 = full compensation (curve lands on grid boundary), 0 = no inset.")]
        [SerializeField, Range(0f, 3f)] private float _inset = 1f;
        [Tooltip("Tangent length as a fraction of the neighbour distance.\n0.33 = Unity default (1/3), 0 = sharp corners, 1 = very curvy.")]
        [SerializeField, Range(0f, 1f)] private float _tangentLength = 1f / 3f;

        /// <summary>Raised after the spline has been rebuilt from the tilemap.</summary>
        public event System.Action ContourChanged;

        /// <summary>
        /// The final boundary polygon (inset applied) in world space, CCW.
        /// Null until the first successful generation.
        /// </summary>
        public IReadOnlyList<Vector3> ContourWorld => _contourWorld.Count >= 3 ? _contourWorld : null;

        /// <summary>The tilemap that defines the island (for co-components like IslandFoliage).</summary>
        public Tilemap SourceTilemap => _tilemap;

        /// <summary>
        /// Rebind to another tilemap (used by JsonMapLoader when it clones the island
        /// stack per elevation layer). Caller triggers regeneration by painting.
        /// </summary>
        public void SetSourceTilemap(Tilemap map) => _tilemap = map;

        /// <summary>The SpriteShapeController this component writes to.</summary>
        public SpriteShapeController Controller
        {
            get { ResolveController(); return _controller; }
        }

        // ---------------------------------------------------------------------
        // Marching squares lookup table
        // ---------------------------------------------------------------------
        // For each case (0–15) we list flat pairs of edge indices to connect.
        // Corner bit layout: BL=1, BR=2, TR=4, TL=8
        // Edge indices: T=0, R=1, B=2, L=3
        // ---------------------------------------------------------------------
        private static readonly int[][] MsCases =
        {
            new int[0],                    // 0:  empty
            new[] { 3, 2 },               // 1:  BL
            new[] { 2, 1 },               // 2:  BR
            new[] { 3, 1 },               // 3:  BL+BR
            new[] { 0, 1 },               // 4:  TR
            new[] { 3, 2, 0, 1 },         // 5:  BL+TR  (two segments)
            new[] { 2, 0 },               // 6:  BR+TR
            new[] { 3, 0 },               // 7:  BL+BR+TR
            new[] { 3, 0 },               // 8:  TL
            new[] { 0, 2 },               // 9:  TL+BL
            new[] { 3, 0, 2, 1 },         // 10: TL+BR  (two segments)
            new[] { 0, 1 },               // 11: TL+BL+BR
            new[] { 3, 1 },               // 12: TL+TR
            new[] { 2, 1 },               // 13: TL+TR+BL
            new[] { 3, 2 },               // 14: TL+TR+BR
            new int[0]                     // 15: all filled
        };

        private SpriteShapeController _controller;
        private readonly List<Vector3> _contourWorld = new List<Vector3>();

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            ResolveController();
        }

        private void OnEnable()
        {
            Tilemap.tilemapTileChanged += OnTilemapTileChanged;
#if UNITY_EDITOR
            // Cold start (scene open / domain reload): the DontSave isle
            // children for extra disconnected blobs aren't serialized —
            // rebuild them from the tilemap.
            if (!Application.isPlaying && _autoRegenerateInEditor)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null || !isActiveAndEnabled || _tilemap == null) return;
                    GenerateBoundary();
                };
            }
#endif
        }

        private void OnDisable()
        {
            Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (_generateOnStart) GenerateBoundary();
        }

        private void OnTilemapTileChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (map != _tilemap) return;
            if (!Application.isPlaying && !_autoRegenerateInEditor) return;
            GenerateBoundary();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!_autoRegenerateInEditor) return;

            // Defer: mutating the SpriteShape inside OnValidate triggers
            // SendMessage warnings and misbehaves in prefab isolation.
            // Runs in play mode too — inspector tweaks regenerate live.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                if (_tilemap != null) GenerateBoundary();
            };
        }
#endif

        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------

        /// <summary>
        /// Push contour styling from an island profile. No-op unless a value
        /// actually changes, so callers may push freely (IslandSkirt pushes
        /// on profile swap/edit). Keeps this component fully usable
        /// standalone — the fields stay serialized for non-island shapes.
        /// </summary>
        public void ApplyContourSettings(IslandVisualProfile.ContourSettings s)
        {
            if (s == null) return;
            if (_boundaryMode == s.boundaryMode
                && _tangentMode == s.tangentMode
                && Mathf.Approximately(_inset, s.inset)
                && Mathf.Approximately(_tangentLength, s.tangentLength))
                return;

            _boundaryMode = s.boundaryMode;
            _tangentMode = s.tangentMode;
            _inset = s.inset;
            _tangentLength = s.tangentLength;
            if (_tilemap != null) GenerateBoundary();
        }

        /// <summary>
        /// Read the tilemap and rebuild the spline to match its boundary.
        /// Safe to call at runtime.
        /// </summary>
        [ContextMenu("Generate Boundary")]
        public void GenerateBoundary()
        {
            if (_tilemap == null)
            {
                Debug.LogWarning($"{nameof(TilemapToSpriteShape)}: No tilemap assigned.", this);
                return;
            }
            ResolveController();
            if (_controller == null)
            {
                Debug.LogWarning($"{nameof(TilemapToSpriteShape)}: No SpriteShapeController found.", this);
                return;
            }

            var contours = ExtractContours();
            if (contours == null || contours.Count == 0)
            {
                Debug.LogWarning($"{nameof(TilemapToSpriteShape)}: No boundary found in tilemap.", this);
                return;
            }

            if (_tangentMode != ShapeTangentMode.Linear && _inset > 0f)
                for (int i = 0; i < contours.Count; i++)
                    contours[i] = InsetContour(contours[i]);

            // Largest blob keeps the existing controller (stable identity for
            // everything referencing this GO); every other disconnected blob
            // gets a procedural isle child with its own shape + skirt.
            ApplyToSpline(_controller, contours[0], true);
            SyncIsleChildren(contours);
            PersistEdits();
        }

        /// <summary>
        /// Make the generated spline survive a scene save.
        ///
        /// <c>Spline</c> is written through the runtime API, which never flags the object for
        /// Unity's dirty tracking — and when this component lives on a PREFAB INSTANCE (the
        /// LevelLoaderRig case) the override system never hears about it either, so
        /// <c>SaveScene</c> silently drops the whole regeneration. That is how the ground island
        /// stayed at a stale 2-cells-per-tile outline on disk while looking correct in the editor:
        /// the auto-regenerate-on-open was rebuilding it in memory every single time, and nothing
        /// was ever written back.
        /// </summary>
        private void PersistEdits()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            UnityEditor.EditorUtility.SetDirty(_controller);
            UnityEditor.EditorUtility.SetDirty(gameObject);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(_controller))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(_controller);
#endif
        }

        private void ResolveController()
        {
            if (_spriteShape != null) _controller = _spriteShape;
            else if (_controller == null) _controller = GetComponent<SpriteShapeController>();
        }

        // ---------------------------------------------------------------------
        // Contour extraction
        // ---------------------------------------------------------------------

        /// <summary>
        /// All disconnected island outlines in the tilemap, largest first, in
        /// the controller's local space, CW winding. Hole outlines (loops
        /// enclosed by another loop — empty pockets inside an island) are
        /// dropped: SpriteShape fill can't cut holes anyway.
        /// </summary>
        private List<List<Vector3>> ExtractContours()
        {
            var filled = new HashSet<Vector3Int>();
            var cb = _tilemap.cellBounds;

            foreach (var pos in cb.allPositionsWithin)
            {
                if (_tilemap.HasTile(pos))
                    filled.Add(pos);
            }

            if (filled.Count == 0) return null;

            // CellToWorld differences work for any cell layout (rect, iso, hex).
            Vector3 v00   = _tilemap.CellToWorld(Vector3Int.zero);
            Vector3 stepX = _tilemap.CellToWorld(new Vector3Int(1, 0, 0)) - v00;
            Vector3 stepY = _tilemap.CellToWorld(new Vector3Int(0, 1, 0)) - v00;

            var adj = _boundaryMode == BoundaryMode.TileCorners
                ? BuildCornerAdjacency(filled, cb)
                : BuildMidpointAdjacency(filled, cb);

            var loops = TraceAllContours(adj);
            if (loops == null || loops.Count == 0) return null;

            // Convert keys → world → sprite-shape local space.
            // EdgeMidpoints uses doubled keys (×0.5); TileCorners uses direct keys (×1).
            float scale = _boundaryMode == BoundaryMode.TileCorners ? 1f : 0.5f;
            var contours = new List<List<Vector3>>(loops.Count);
            foreach (var keys in loops)
            {
                if (keys.Count < 3) continue;
                var contour = new List<Vector3>(keys.Count);
                foreach (var k in keys)
                {
                    Vector3 world = v00 + (k.x * scale) * stepX + (k.y * scale) * stepY;
                    Vector3 local = _controller.transform.InverseTransformPoint(world);
                    contour.Add(local);
                }

                // Trace direction depends on dictionary iteration order — enforce
                // CLOCKWISE: Unity's own default SpriteShape spline is CW, and
                // fill tessellation + edge-sprite angle ranges assume it.
                // (IslandSkirt re-normalizes winding internally, so it's agnostic.)
                if (SignedArea(contour) > 0f)
                    contour.Reverse();

                contours.Add(contour);
            }

            // Drop holes: a loop with a vertex inside another loop is an
            // inner boundary (its vertices never touch the outer outline).
            var outers = new List<List<Vector3>>(contours.Count);
            for (int a = 0; a < contours.Count; a++)
            {
                bool enclosed = false;
                for (int b = 0; b < contours.Count && !enclosed; b++)
                    if (b != a && PointInPolygon(contours[a][0], contours[b]))
                        enclosed = true;
                if (!enclosed) outers.Add(contours[a]);
            }

            outers.Sort((x, y) =>
                Mathf.Abs(SignedArea(y)).CompareTo(Mathf.Abs(SignedArea(x))));
            return outers;
        }

        private static bool PointInPolygon(Vector3 p, List<Vector3> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector3 a = poly[i], b = poly[j];
                if ((a.y <= p.y) == (b.y <= p.y)) continue;
                if (p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x))
                    inside = !inside;
            }
            return inside;
        }

        private static float SignedArea(List<Vector3> pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                Vector3 q = pts[(i + 1) % pts.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        // ---------------------------------------------------------------------
        // EdgeMidpoints: marching squares on edge midpoints
        // ---------------------------------------------------------------------

        private static Dictionary<Vector2Int, List<Vector2Int>> BuildMidpointAdjacency(
            HashSet<Vector3Int> filled, BoundsInt cb)
        {
            var adj = new Dictionary<Vector2Int, List<Vector2Int>>();
            int xMin = cb.xMin - 1, xMax = cb.xMax + 1;
            int yMin = cb.yMin - 1, yMax = cb.yMax + 1;

            for (int vx = xMin; vx <= xMax; vx++)
            for (int vy = yMin; vy <= yMax; vy++)
            {
                bool bl = filled.Contains(new Vector3Int(vx - 1, vy - 1, 0));
                bool br = filled.Contains(new Vector3Int(vx,     vy - 1, 0));
                bool tl = filled.Contains(new Vector3Int(vx - 1, vy,     0));
                bool tr = filled.Contains(new Vector3Int(vx,     vy,     0));

                int idx = (bl ? 1 : 0) | (br ? 2 : 0) | (tr ? 4 : 0) | (tl ? 8 : 0);
                var entry = MsCases[idx];
                if (entry.Length == 0) continue;

                // Edge midpoint keys (doubled so all stay integer):
                //   T = (vx*2,   vy*2+1)     R = (vx*2+1, vy*2)
                //   B = (vx*2,   vy*2-1)     L = (vx*2-1, vy*2)
                Vector2Int[] e = new Vector2Int[4];
                e[0] = new Vector2Int(vx * 2,     vy * 2 + 1);
                e[1] = new Vector2Int(vx * 2 + 1, vy * 2);
                e[2] = new Vector2Int(vx * 2,     vy * 2 - 1);
                e[3] = new Vector2Int(vx * 2 - 1, vy * 2);

                for (int s = 0; s < entry.Length; s += 2)
                    AddEdge(adj, e[entry[s]], e[entry[s + 1]]);
            }

            return adj;
        }

        // ---------------------------------------------------------------------
        // TileCorners: trace actual grid edges between filled/empty cells
        // ---------------------------------------------------------------------

        private static Dictionary<Vector2Int, List<Vector2Int>> BuildCornerAdjacency(
            HashSet<Vector3Int> filled, BoundsInt cb)
        {
            var adj = new Dictionary<Vector2Int, List<Vector2Int>>();

            foreach (var cell in filled)
            {
                int x = cell.x, y = cell.y;

                // For each empty neighbour, the shared edge becomes a boundary
                // segment connecting two corner vertices.
                // Corner (cx, cy) is the vertex at grid position (cx, cy).

                if (!filled.Contains(new Vector3Int(x - 1, y, 0)))
                    AddEdge(adj, new Vector2Int(x, y), new Vector2Int(x, y + 1));     // left

                if (!filled.Contains(new Vector3Int(x + 1, y, 0)))
                    AddEdge(adj, new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1)); // right

                if (!filled.Contains(new Vector3Int(x, y - 1, 0)))
                    AddEdge(adj, new Vector2Int(x, y), new Vector2Int(x + 1, y));     // bottom

                if (!filled.Contains(new Vector3Int(x, y + 1, 0)))
                    AddEdge(adj, new Vector2Int(x, y + 1), new Vector2Int(x + 1, y + 1)); // top
            }

            return adj;
        }

        // ---------------------------------------------------------------------
        // Inset: compensate for bezier bulge by moving vertices inward
        // ---------------------------------------------------------------------

        /// <summary>
        /// For each vertex, computes the expected outward bulge of the two
        /// adjacent cubic bezier segments and moves the vertex inward by that
        /// amount so the curve lands approximately on the original grid boundary.
        /// </summary>
        private List<Vector3> InsetContour(List<Vector3> contour)
        {
            int n = contour.Count;
            var result = new List<Vector3>(n);

            // Inset must never eat the shape: on tiny contours (single-tile blobs,
            // style-lab swatches) the bulge compensation is comparable to the whole
            // shape and collapses it to a sliver. Cap per-vertex displacement to a
            // fraction of the smaller bounding-box dimension.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = contour[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            float maxOffset = 0.25f * Mathf.Min(maxX - minX, maxY - minY);

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = contour[(i - 1 + n) % n];
                Vector3 cur  = contour[i];
                Vector3 next = contour[(i + 1) % n];

                Vector3 vLeft  = prev - cur;
                Vector3 vRight = next - cur;

                if (vLeft.sqrMagnitude < 0.0001f || vRight.sqrMagnitude < 0.0001f)
                {
                    result.Add(cur);
                    continue;
                }

                // Tangent direction (Unity's Continuous algorithm):
                // rightDir = Cross(Cross(vLeft, vRight), vLeft.norm + vRight.norm).norm
                Vector3 leftNorm  = vLeft.normalized;
                Vector3 rightNorm = vRight.normalized;
                Vector3 sum       = leftNorm + rightNorm;

                if (sum.sqrMagnitude < 0.0001f)
                {
                    result.Add(cur); // 180° turn, no bulge
                    continue;
                }

                Vector3 cross    = Vector3.Cross(vLeft, vRight);
                Vector3 rightDir = Vector3.Cross(cross, sum).normalized;

                // --- Bulge from segment cur→next (right tangent at cur) ---
                Vector3 chordNext = next - cur;
                Vector3 chordNextDir = chordNext.normalized;
                Vector3 tRight = rightDir * (chordNext.magnitude * _tangentLength);
                // Perpendicular component of tangent relative to chord
                Vector3 tRightPerp = tRight - chordNextDir * Vector3.Dot(tRight, chordNextDir);

                // --- Bulge from segment prev→cur (left tangent at cur) ---
                Vector3 chordPrev = cur - prev;
                Vector3 chordPrevDir = chordPrev.normalized;
                Vector3 tLeft = -rightDir * (chordPrev.magnitude * _tangentLength);
                Vector3 tLeftPerp = tLeft - chordPrevDir * Vector3.Dot(tLeft, chordPrevDir);

                // Total bulge vector at t=0.5 of each segment:
                // For a cubic bezier, max deviation = (3/8) * tangent_perp
                Vector3 bulge = (3f / 8f) * (tRightPerp + tLeftPerp);

                // Move vertex inward (opposite to bulge direction)
                result.Add(cur - Vector3.ClampMagnitude(bulge * _inset, maxOffset));
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // Spline application
        // ---------------------------------------------------------------------

        private void ApplyToSpline(SpriteShapeController controller, List<Vector3> contour, bool publish)
        {
            var spline = controller.spline;
            spline.Clear();

            int n = contour.Count;
            for (int i = 0; i < n; i++)
            {
                spline.InsertPointAt(i, contour[i]);
                spline.SetTangentMode(i, _tangentMode);
            }

            // Replicate Unity's own SetTangentMode(Continuous) algorithm:
            // For each point with zero tangents (just inserted), compute the
            // tangent direction using the cross-product of neighbour vectors,
            // scaled by magnitude / 3.
            if (_tangentMode == ShapeTangentMode.Continuous)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 prev = contour[(i - 1 + n) % n];
                    Vector3 next = contour[(i + 1) % n];
                    Vector3 cur  = contour[i];

                    Vector3 vLeft  = prev - cur;
                    Vector3 vRight = next - cur;

                    if (vLeft.sqrMagnitude < 0.0001f || vRight.sqrMagnitude < 0.0001f)
                        continue;

                    Vector3 leftNorm  = vLeft.normalized;
                    Vector3 rightNorm = vRight.normalized;
                    Vector3 sum       = leftNorm + rightNorm;

                    if (sum.sqrMagnitude < 0.0001f)
                        continue; // 180° turn, skip

                    // rightDirection = Cross(Cross(vLeft, vRight), sum).normalized
                    Vector3 cross      = Vector3.Cross(vLeft, vRight);
                    Vector3 rightDir   = Vector3.Cross(cross, sum).normalized;

                    spline.SetLeftTangent(i,  vLeft.magnitude  * _tangentLength * -rightDir);
                    spline.SetRightTangent(i, vRight.magnitude * _tangentLength *  rightDir);
                }
            }
            else if (_tangentMode == ShapeTangentMode.Broken)
            {
                // For Broken mode, tangents point directly at prev/next, scaled by 1/3.
                for (int i = 0; i < n; i++)
                {
                    Vector3 prev = contour[(i - 1 + n) % n];
                    Vector3 next = contour[(i + 1) % n];
                    Vector3 cur  = contour[i];

                    Vector3 vLeft  = prev - cur;
                    Vector3 vRight = next - cur;

                    if (vLeft.sqrMagnitude >= 0.0001f)
                        spline.SetLeftTangent(i, vLeft.normalized * (vLeft.magnitude * _tangentLength));

                    if (vRight.sqrMagnitude >= 0.0001f)
                        spline.SetRightTangent(i, vRight.normalized * (vRight.magnitude * _tangentLength));
                }
            }

            spline.isOpenEnded = false;
            controller.RefreshSpriteShape();

            if (!publish) return; // isle children publish nothing

            // Publish the final polygon for consumers (IslandSkirt etc.).
            _contourWorld.Clear();
            var tr = controller.transform;
            for (int i = 0; i < n; i++)
                _contourWorld.Add(tr.TransformPoint(contour[i]));

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    controller.gameObject.scene);
#endif

            ContourChanged?.Invoke();
        }

        // ---------------------------------------------------------------------
        // Isle children: one procedural shape + skirt per extra disconnected blob
        // ---------------------------------------------------------------------

        /// <summary>
        /// Contours beyond the largest each get a DontSave child GO carrying
        /// its own SpriteShapeController + IslandSkirt (profile, tint and
        /// sorting inherited from this island). Children are matched by name
        /// and created/destroyed to follow the painted blob count — nothing
        /// is serialized; a cold start regenerates them from the tilemap.
        /// </summary>
        private void SyncIsleChildren(List<List<Vector3>> contours)
        {
            Transform parent = _controller.transform;
            var srcSkirt = GetComponent<IslandSkirt>();
            var srcRend = _controller.spriteShapeRenderer;

            for (int i = 1; i < contours.Count; i++)
            {
                string childName = "Isle_" + i;
                Transform t = parent.Find(childName);
                GameObject go;
                if (t == null)
                {
                    go = new GameObject(childName);
                    go.transform.SetParent(parent, false);
                }
                else
                {
                    go = t.gameObject;
                }
                // Match the parent stack's persistence. Under a SAVED group stack the isle
                // serializes, so it exists at scene load and tessellates its fill on OnEnable
                // like the main blob — a SpriteShapeController created fresh at runtime never
                // tessellates (bounds stay at the uninitialised sentinel, top never appears).
                // Under a scratch/DontSave stack (StyleLab swatches) it stays ephemeral.
                go.hideFlags = parent.gameObject.hideFlags;

                var ctrl = go.GetComponent<SpriteShapeController>();
                if (ctrl == null) ctrl = go.AddComponent<SpriteShapeController>();
                ctrl.spriteShape = _controller.spriteShape;
                ctrl.fillPixelsPerUnit = _controller.fillPixelsPerUnit;

                var rend = ctrl.spriteShapeRenderer;
                if (srcRend != null && rend != null)
                {
                    rend.sortingLayerName = srcRend.sortingLayerName;
                    rend.sortingOrder = srcRend.sortingOrder;
                    rend.color = srcRend.color;
                    // Materials too — a runtime-created SpriteShapeRenderer starts with
                    // a broken default (magenta fill on styled stacks like water).
                    rend.sharedMaterials = srcRend.sharedMaterials;
                }

                // Contours are in the parent controller's local space; the
                // child sits at identity under it, so they transfer 1:1.
                ApplyToSpline(ctrl, contours[i], false);

                // Runtime-created SpriteShapes don't tessellate on their own
                // in play mode (no cached geometry, and the lazy generation
                // path never fires — bounds stay at the uninitialised
                // sentinel, so the top surface simply never appears). Force
                // a synchronous bake; the editor loop covers edit mode.
                if (Application.isPlaying)
                    ctrl.BakeMesh().Complete();

                // Skirt ONLY if the source stack has one — skirtless templates
                // (water) must produce skirtless isle blobs.
                var skirt = go.GetComponent<IslandSkirt>();
                if (srcSkirt != null)
                {
                    if (skirt == null) skirt = go.AddComponent<IslandSkirt>();
                    skirt.ProfileAsset = srcSkirt.ProfileAsset;
                    skirt.DepthOverride = srcSkirt.DepthOverride; // isle blobs on elevated layers reach base too
                    skirt.DepthSampler = srcSkirt.DepthSampler;
                    skirt.FringeMask = srcSkirt.FringeMask;
                    skirt.Generate();
                }
                else if (skirt != null)
                {
                    if (Application.isPlaying) Destroy(skirt);
                    else DestroyImmediate(skirt);
                }

                // Same for the water surface — each pond blob bakes its own
                // shore-distance mask off its own spline.
                var srcWater = GetComponent<WaterSurface>();
                var water = go.GetComponent<WaterSurface>();
                if (srcWater != null)
                {
                    if (water == null) water = go.AddComponent<WaterSurface>();
                    water.profile = srcWater.profile;
                }
                else if (water != null)
                {
                    if (Application.isPlaying) Destroy(water);
                    else DestroyImmediate(water);
                }
            }

            // Surplus children from a previous paint (blob merged/erased).
            for (int i = Mathf.Max(contours.Count, 1); ; i++)
            {
                Transform t = parent.Find("Isle_" + i);
                if (t == null) break;
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
        }

        // ---------------------------------------------------------------------
        // Segment chaining helpers
        // ---------------------------------------------------------------------

        private static void AddEdge(
            Dictionary<Vector2Int, List<Vector2Int>> adj,
            Vector2Int a, Vector2Int b)
        {
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<Vector2Int>();
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<Vector2Int>();
            la.Add(b);
            lb.Add(a);
        }

        /// <summary>
        /// Traces every CLOSED contour in the adjacency graph — one per
        /// disconnected island (plus hole outlines, filtered by the caller).
        /// Each point in a simple contour has exactly 2 connections.
        /// </summary>
        private static List<List<Vector2Int>> TraceAllContours(
            Dictionary<Vector2Int, List<Vector2Int>> adj)
        {
            var visited = new HashSet<Vector2Int>();
            var loops = new List<List<Vector2Int>>();

            foreach (var start in adj.Keys)
            {
                if (visited.Contains(start)) continue;
                if (adj[start].Count < 2) { visited.Add(start); continue; }

                var path = new List<Vector2Int> { start };
                visited.Add(start);

                Vector2Int prev = start;
                Vector2Int cur  = adj[start][0];
                bool closed = true;
                int limit = adj.Count + 1; // hard bound: no walk is longer than the graph

                while (cur != start)
                {
                    if (path.Count > limit) { closed = false; break; } // malformed graph
                    path.Add(cur);
                    visited.Add(cur);

                    var nb = adj[cur];
                    // Prefer an unvisited neighbor (or start, closing the
                    // loop). Diagonally-touching tiles (marching-squares
                    // cases 5/10) make 4-valent vertices — a naive
                    // "anything but prev" pick can oscillate forever there.
                    Vector2Int next = prev;
                    foreach (var n in nb)
                    {
                        if (n != prev && (n == start || !visited.Contains(n))) { next = n; break; }
                    }
                    if (next == prev)
                    {
                        foreach (var n in nb)
                        {
                            if (n != prev) { next = n; break; }
                        }
                    }
                    if (next == prev) { closed = false; break; } // dead-end (open contour)

                    prev = cur;
                    cur  = next;
                }

                if (closed && path.Count >= 3)
                    loops.Add(path);
            }

            return loops;
        }
    }
}
