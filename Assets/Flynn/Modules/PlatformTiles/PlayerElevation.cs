using UnityEngine;

namespace Flynn.Modules.PlatformTiles
{
    /// <summary>
    /// Owns which elevation level the player is on, and is the only thing allowed to change it.
    ///
    /// The player's TRANSFORM always stays on the ground plane — that is what depth sorting and
    /// collision both read, and raising it would make standing high read as standing far away. Height
    /// is purely visual: <see cref="elevationPivot"/> is lifted instead. (It must be a separate
    /// object from the sprite PlayerMover animates, or the hop would fight this offset.)
    ///
    /// Level changes only ever happen on a <see cref="StairZone"/>. Everywhere else the player is
    /// fenced in by <see cref="LevelBoundary"/> outlines: the one at their level stops them walking
    /// off the edge, the one above stops them walking up a wall. Stepping onto a stair suspends both
    /// so they can cross, and the ramp drives their height continuously so nothing pops halfway.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerElevation : MonoBehaviour
    {
        [Tooltip("Child lifted to fake height. Must NOT be the sprite PlayerMover bounces — put an " +
                 "ElevationPivot between the player root and the visual.")]
        [SerializeField] private Transform elevationPivot;
        [Tooltip("World-Y per elevation level. Match the platform art's cube body height.")]
        [SerializeField] private float riseWorld = 0.5f;
        [Tooltip("Foot offset from this transform — where the stairs are sampled. Match PlayerMover.")]
        [SerializeField] private Vector2 footOffset = new Vector2(0f, -0.14f);
        [Tooltip("How fast the visual settles to a new height, higher = snappier.")]
        [SerializeField] private float heightLerp = 14f;

        /// <summary>Discrete level, used by sorting and collision. Starts at 0 — the ground.</summary>
        public int CurrentLevel { get; private set; }

        /// <summary>Fractional level including partway up a ramp — visuals only.</summary>
        public float VisualLevel { get; private set; }

        float _pivotBaseY;

        void Awake()
        {
            if (elevationPivot != null) _pivotBaseY = elevationPivot.localPosition.y;
        }

        void OnEnable() => ApplyBoundaries(false);

        void Update()
        {
            Vector2 foot = (Vector2)transform.position + footOffset;
            var stair = StairZone.ZoneAt(foot);

            int wanted = CurrentLevel;
            float targetVisual;

            if (stair != null)
            {
                float t = stair.Progress(foot);
                targetVisual = Mathf.Lerp(stair.lowLevel, stair.highLevel, t);
                // Commit the discrete level at the halfway point, but the HEIGHT is continuous, so
                // the commit is invisible — this is the classic stair pop, avoided.
                wanted = t >= 0.5f ? stair.highLevel : stair.lowLevel;
            }
            else
            {
                targetVisual = CurrentLevel;
            }

            CurrentLevel = wanted;
            // Boundaries are suspended while on a ramp so the player can cross the platform edge.
            ApplyBoundaries(stair != null);

            VisualLevel = Mathf.Lerp(VisualLevel, targetVisual, 1f - Mathf.Exp(-heightLerp * Time.deltaTime));
            if (elevationPivot != null)
            {
                var lp = elevationPivot.localPosition;
                lp.y = _pivotBaseY + VisualLevel * riseWorld;
                elevationPivot.localPosition = lp;
            }
        }

        // Applied every frame rather than cached on (level, suspended): the boundary set can be
        // rebuilt underneath us, and a cache would then leave stale colliders enabled. There are only
        // a handful of boundaries, so this is free.
        void ApplyBoundaries(bool suspended)
        {
            var all = LevelBoundary.Boundaries;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null) continue;
                // Fenced in by our own level's outline, fenced out of the one above.
                bool on = !suspended && (b.level == CurrentLevel || b.level == CurrentLevel + 1);
                b.SetBlocking(on);
            }
        }

        /// <summary>Force a level (debug / teleports). Snaps the visual immediately.</summary>
        public void SetLevel(int level)
        {
            CurrentLevel = Mathf.Max(0, level);
            VisualLevel = CurrentLevel;
            ApplyBoundaries(false);
        }
    }
}
