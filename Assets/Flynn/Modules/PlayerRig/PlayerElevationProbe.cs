using UnityEngine;
using Flynn.Contracts;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Reads the player's current elevation LEVEL from an <see cref="IElevationSource"/> (the map
    /// loader) and feeds it to <see cref="PlayerElevation"/> — data-consistent with the geometry,
    /// no trigger zones. Queries live each frame, so runtime map regeneration can't stale it.
    /// </summary>
    [RequireComponent(typeof(PlayerElevation))]
    public class PlayerElevationProbe : MonoBehaviour
    {
        [Tooltip("The elevation source — any component implementing IElevationSource (the map loader).")]
        [SerializeField] private MonoBehaviour _elevationSource;
        [Tooltip("Foot offset from this transform in world units — sample the feet, not the pivot.")]
        [SerializeField] private Vector3 _footOffset = Vector3.zero;

        [Header("Front-edge sort promotion")]
        [Tooltip("Direction INTO the screen (behind/north) in world space — the player is promoted to " +
                 "draw over a raised platform found this way (they're standing in front of it).")]
        [SerializeField] private Vector2 _lookDir = new Vector2(0f, 1f);
        [Tooltip("How far into the screen to look for a platform the player is standing in front of (world units). " +
                 "0 = no promotion (pure per-level layers).")]
        [SerializeField] private float _lookAhead = 1.2f;
        [Tooltip("Samples along the look-ahead ray.")]
        [SerializeField] private int _lookSteps = 4;

        [Tooltip("Snap the very first sample so the player starts at the correct level with no ease.")]
        [SerializeField] private bool _snapFirst = true;

        [Tooltip("Let the player visually climb onto raised surfaces. Off = occlusion sort only — " +
                 "the player stays on the ground (no platform changing).")]
        [SerializeField] private bool _enableLift = true;

        private PlayerElevation _elevation;
        private IElevationSource _source;
        private bool _first = true;

        private void Awake()
        {
            _elevation = GetComponent<PlayerElevation>();
            _source = _elevationSource as IElevationSource;
        }

        private void Update()
        {
            if (_source == null) return;
            Vector3 foot = transform.position + _footOffset;

            int surface = _source.LevelAt(foot);            // surface under the feet
            int liftLevel = _enableLift ? surface : 0;       // visual lift (off = stay grounded, no climb)
            int sortLevel = surface;                         // layer to draw on → promote if in front of a platform

            if (_lookAhead > 0f && _lookSteps > 0)
            {
                Vector3 dir = new Vector3(_lookDir.x, _lookDir.y, 0f).normalized;
                for (int i = 1; i <= _lookSteps; i++)
                {
                    float d = _lookAhead * i / _lookSteps;
                    int lv = _source.LevelAt(foot + dir * d);
                    if (lv > sortLevel) sortLevel = lv;
                }
            }

            _elevation.SetLevel(liftLevel, sortLevel, _first && _snapFirst);
            _first = false;
        }
    }
}
