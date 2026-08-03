using UnityEngine;
using Flynn.Common;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Lean fake-Z elevation for the current player rig — the standalone replacement for the
    /// legacy PlayerHeightState (no PlayerController2D / PlayerConfig dependency).
    ///
    /// The body root stays on the ground plane (foot-sort + physics unaffected); only
    /// <see cref="_visualRoot"/> is lifted up screen-Y by the eased surface height. The eased
    /// elevation + discrete level are pushed into a <see cref="SortableSprite"/> so the player
    /// sorts correctly across raised platforms. The level itself is chosen by
    /// <see cref="PlayerElevationProbe"/>.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class PlayerElevation : MonoBehaviour
    {
        [Tooltip("DEDICATED elevation root lifted up screen-Y — parent of the animated visual, NOT the " +
                 "visual itself (PlayerMover hops the visual; separate transforms avoid a lift feedback loop).")]
        [SerializeField] private Transform _visualRoot;
        [Tooltip("SortableSprite biased by elevation so a higher surface draws in front.")]
        [SerializeField] private SortableSprite _sortable;
        [Tooltip("World-Y lift per elevation level — match JsonMapLoader heightStep (0.25).")]
        [SerializeField] private float _unitsPerLevel = 0.25f;
        [Tooltip("Elevation easing speed (higher = snappier step up/down).")]
        [SerializeField] private float _lerpSpeed = 12f;

        private float _elevation, _targetElevation;
        private int _level, _sortLevel;
        private bool _snapNext;
        private Vector3 _baseLocal;

        public float Elevation => _elevation;
        public int Level => _level;

        /// <summary>
        /// Set the elevation. <paramref name="liftLevel"/> is the surface UNDER the feet (drives the
        /// visual lift), <paramref name="sortLevel"/> is the layer the player draws on (may be higher
        /// when standing in front of a raised platform so it renders over it). Snap skips the ease.
        /// </summary>
        public void SetLevel(int liftLevel, int sortLevel, bool snap = false)
        {
            _level = liftLevel;
            _sortLevel = sortLevel;
            _targetElevation = liftLevel * _unitsPerLevel;
            if (snap) _snapNext = true;
        }

        private void Awake()
        {
            if (_visualRoot != null) _baseLocal = _visualRoot.localPosition;
        }

        private void Update()
        {
            if (_snapNext) { _elevation = _targetElevation; _snapNext = false; }
            else _elevation = Mathf.Lerp(_elevation, _targetElevation,
                                         1f - Mathf.Exp(-_lerpSpeed * Time.deltaTime));

            if (_sortable != null)
            {
                _sortable.Elevation = _elevation;
                _sortable.ElevationLevel = _sortLevel;   // which layer to draw on (may exceed lift level)
            }
        }

        private void LateUpdate()
        {
            // Absolute lift of the DEDICATED elevation root. PlayerMover hops the visual child on a
            // different transform, so hop + elevation compose via hierarchy with no feedback.
            if (_visualRoot != null)
                _visualRoot.localPosition = _baseLocal + new Vector3(0f, _elevation, 0f);
        }
    }
}
