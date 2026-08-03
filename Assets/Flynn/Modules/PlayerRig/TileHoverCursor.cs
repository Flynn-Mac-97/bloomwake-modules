using UnityEngine;
using Flynn.Contracts;
using Flynn.Player.Interaction;

namespace Flynn.Modules.PlayerRig
{
    /// Focus feedback: hidden until the mouse is over something interactable (hittable node,
    /// machine, prop), then an iso-square focus frame — four white curved corner brackets,
    /// projected onto the 2:1 ground grid — snaps to the target's BASE so it reads as sitting
    /// on the tile the thing stands on. Other components read CurrentPoint / LockedTarget
    /// (swing ground impact), which keep updating even while the frame is hidden.
    public class TileHoverCursor : MonoBehaviour
    {
        [Tooltip("Auto-found from the scene when empty.")]
        [SerializeField] private Grid _grid;
        [SerializeField] private Color _lockColor = Color.white;
        [Tooltip("Focus-frame footprint in grid cells. 1 = exactly one iso tile.")]
        [SerializeField] private float _cellSpan = 1f;
        [Tooltip("Gentle breathe on the locked frame so it reads alive, not painted on.")]
        [SerializeField] private float _breatheAmount = 0.05f;
        [SerializeField] private float _breatheHz = 1.2f;
        [SerializeField] private string _sortingLayer = "Default";
        [SerializeField] private int _sortingOrder = 4000;

        public Vector3 CurrentPoint { get; private set; }
        public Transform LockedTarget { get; private set; }

        private static Sprite _brackets;
        private Transform _iso;        // squashes to the grid aspect (x : y = cell)
        private Transform _marks;      // rotated 45° inside — a ground-plane square
        private SpriteRenderer _sr;
        private float _spriteWidth = 1f;
        private static readonly Collider2D[] _overlaps = new Collider2D[16];

        /// Four white quarter-circle corner brackets on a transparent square — the classic
        /// camera-focus frame. Generated once; rotated+squashed into iso by the hierarchy.
        private static Sprite BracketSprite()
        {
            if (_brackets != null) return _brackets;
            const int S = 256;          // texture size
            const float R = 46f;        // arc radius
            const float T = 18f;        // stroke thickness
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            // Arc centre per corner, plus the direction that points AT that corner — only the
            // quarter of the ring between its centre and the corner is kept, which is what makes
            // it read as a focus bracket instead of a full circle.
            var centers = new Vector2[]
            {
                new Vector2(R, R), new Vector2(S - R, R),
                new Vector2(R, S - R), new Vector2(S - R, S - R)
            };
            var toCorner = new Vector2[]
            {
                new Vector2(-1, -1), new Vector2(1, -1),
                new Vector2(-1, 1), new Vector2(1, 1)
            };
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                for (int c = 0; c < 4; c++)
                {
                    var rel = p - centers[c];
                    // keep only the quadrant facing the corner
                    if (rel.x * toCorner[c].x < 0f || rel.y * toCorner[c].y < 0f) continue;
                    float d = Mathf.Abs(rel.magnitude - R);
                    if (d < T * 0.5f)
                    {
                        float a = 1f - Mathf.SmoothStep(0.6f, 1f, d / (T * 0.5f));
                        int i = y * S + x;
                        if (a > px[i].a) px[i] = new Color(1f, 1f, 1f, a);
                        break;
                    }
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            // FullRect, not the default Tight: tight mesh generation on a mostly-transparent
            // texture with thin arcs produces degenerate geometry — the sprite renders nothing.
            _brackets = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S,
                                      0, SpriteMeshType.FullRect);
            return _brackets;
        }

        private void Awake()
        {
            if (_grid == null) _grid = FindObjectOfType<Grid>();

            var iso = new GameObject("hover_iso");
            iso.transform.SetParent(transform, false);
            _iso = iso.transform;

            var marks = new GameObject("hover_marks");
            marks.transform.SetParent(_iso, false);
            marks.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _marks = marks.transform;

            _sr = marks.AddComponent<SpriteRenderer>();
            _sr.sprite = BracketSprite();
            _spriteWidth = Mathf.Max(0.0001f, _sr.sprite.bounds.size.x);
            if (!string.IsNullOrEmpty(_sortingLayer))
            {
                if (SortingLayer.NameToID(_sortingLayer) == 0 && _sortingLayer != "Default")
                    Debug.LogWarning($"[TileHoverCursor] Sorting layer '{_sortingLayer}' does not " +
                                     "exist — cursor stays on Default.", this);
                else
                    _sr.sortingLayerName = _sortingLayer;
            }
            _sr.sortingOrder = _sortingOrder;
            _sr.color = _lockColor;
            _sr.enabled = false;
        }

        private void Update()
        {
            var cam = Camera.main;
            if (cam == null || _sr == null) return;
            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;

            LockedTarget = FindLockTarget(world);

            Vector2 cellSize = _grid != null
                ? new Vector2(Mathf.Max(0.01f, _grid.cellSize.x), Mathf.Max(0.01f, _grid.cellSize.y))
                : new Vector2(1f, 0.5f);

            // The frame only exists while something interactable is under the mouse.
            if (LockedTarget == null)
            {
                CurrentPoint = _grid != null
                    ? (Vector2)_grid.GetCellCenterWorld(_grid.WorldToCell(world))
                    : (Vector2)world;
                _sr.enabled = false;
                return;
            }

            CurrentPoint = LockedTarget.position;
            _iso.position = BasePoint(LockedTarget);

            // A ground square rotated 45° spans √2 × its side; fit that span to the cell width,
            // and let the parent's y squash (cellY/cellX) hand back the grid's 2:1 aspect.
            float breathe = 1f + _breatheAmount * Mathf.Sin(Time.time * _breatheHz * Mathf.PI * 2f);
            float side = cellSize.x * _cellSpan / 1.41421356f;
            _marks.localScale = Vector3.one * (side / _spriteWidth) * breathe;
            _iso.localScale = new Vector3(1f, cellSize.y / cellSize.x, 1f);

            _sr.color = _lockColor;
            _sr.enabled = true;
        }

        /// Where the thing STANDS: bottom-centre of its visual bounds, so the frame sits on the
        /// ground tile at its feet instead of floating at the sprite's pivot.
        private static Vector3 BasePoint(Transform target)
        {
            var r = target.GetComponentInChildren<Renderer>();
            if (r == null) return target.position;
            var b = r.bounds;
            return new Vector3(b.center.x, b.min.y + 0.06f, 0f);
        }

        /// Nearest wins, and a hittable always beats a plain interactable. Picking the first collider
        /// the physics query happened to return means overlapping props lock whichever one the
        /// broadphase felt like — which reads as "hover is broken" the moment two nodes touch.
        private static Transform FindLockTarget(Vector2 point)
        {
            int n = Physics2D.OverlapPointNonAlloc(point, _overlaps);
            Transform bestHittable = null, bestInteractable = null;
            float hittableDist = float.MaxValue, interactableDist = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                var hittable = _overlaps[i].GetComponentInParent<IHittable>() as MonoBehaviour;
                if (hittable != null)
                {
                    float d = ((Vector2)hittable.transform.position - point).sqrMagnitude;
                    if (d < hittableDist) { hittableDist = d; bestHittable = hittable.transform; }
                    continue;
                }
                var interactable = _overlaps[i].GetComponentInParent<Interactable>();
                if (interactable != null)
                {
                    float d = ((Vector2)interactable.transform.position - point).sqrMagnitude;
                    if (d < interactableDist) { interactableDist = d; bestInteractable = interactable.transform; }
                }
            }
            return bestHittable != null ? bestHittable : bestInteractable;
        }
    }
}
