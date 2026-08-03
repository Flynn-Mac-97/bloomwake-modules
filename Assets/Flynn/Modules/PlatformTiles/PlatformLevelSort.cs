using UnityEngine;

namespace Flynn.Modules.PlatformTiles
{
    /// <summary>
    /// Orders the player in the same space the platform tiles use, so occlusion works the way a
    /// traditional 2D isometric game does:
    ///
    ///     order = baseOrder + round(-groundY * depthSteps) * HeightSlots + elevation * 2 + 1
    ///
    /// Depth comes from the player's transform, which by design never leaves the ground plane —
    /// <see cref="PlayerElevation"/> fakes height on a visual pivot instead. On an isometric grid
    /// world-Y is proportional to (cellX + cellY), so that position IS the iso row. Elevation only
    /// breaks ties inside a row, and the trailing +1 wins the tie against the tile being stood on.
    ///
    /// Consequences, with no special cases:
    ///   - behind a platform -> further-back row -> the platform draws over you.
    ///   - on its near side  -> nearer row       -> you draw over it.
    ///   - stood on top      -> same row, +1     -> you draw over the tile under you.
    ///
    /// The sorting values MUST equal the matching fields on PlatformTileBuilder.
    /// </summary>
    public class PlatformLevelSort : MonoBehaviour
    {
        [Tooltip("Supplies the level the player is on. Auto-found on this GameObject if empty.")]
        [SerializeField] PlayerElevation elevation;
        [Tooltip("Foot offset from this transform: where depth is sampled.")]
        [SerializeField] Vector2 footOffset = new Vector2(0f, -0.14f);

        [Header("Sorting — MATCH PlatformTileBuilder")]
        [Tooltip("Constant added to every order.")]
        [SerializeField] int baseOrder = 0;
        [Tooltip("Order units per world unit of depth.")]
        [SerializeField] float depthSteps = 20f;

        /// <summary>Order slots reserved per iso row for elevation. MUST equal
        /// PlatformTileBuilder.HeightSlots.</summary>
        const int HeightSlots = 16;

        SpriteRenderer[] _renderers;
        int[] _authoredOrder;

        public int CurrentLevel => elevation != null ? elevation.CurrentLevel : 0;

        void Awake()
        {
            if (elevation == null) elevation = GetComponent<PlayerElevation>();
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _authoredOrder = new int[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _authoredOrder[i] = _renderers[i].sortingOrder;
        }

        void LateUpdate()
        {
            if (_renderers == null) return;

            float groundY = transform.position.y + footOffset.y;
            int order = baseOrder
                        + Mathf.RoundToInt(-groundY * depthSteps) * HeightSlots
                        + CurrentLevel * 2 + 1;

            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].sortingOrder = order + _authoredOrder[i];
        }
    }
}
