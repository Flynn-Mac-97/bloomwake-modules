using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.PlatformTiles
{
    /// <summary>
    /// Marks one generated collision outline as belonging to elevation <see cref="level"/> — the
    /// boundary around every cell at that level or higher.
    ///
    /// One outline does two jobs, which is why this stays simple: from OUTSIDE it stops a lower
    /// player walking up onto the platform, and from INSIDE it stops a player on the platform
    /// walking off the edge. <see cref="PlayerElevation"/> enables only the two that matter for the
    /// level the player is on.
    /// </summary>
    public class LevelBoundary : MonoBehaviour
    {
        [Tooltip("Elevation this outline encloses (cells at this level or higher).")]
        public int level = 1;

        static readonly List<LevelBoundary> All = new List<LevelBoundary>();
        public static IReadOnlyList<LevelBoundary> Boundaries => All;

        Collider2D[] _colliders;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            _colliders = GetComponentsInChildren<Collider2D>(true);
        }

        void OnDisable() => All.Remove(this);

        /// <summary>Turn this boundary's colliders on or off without touching the GameObject, so the
        /// generated tilemap keeps rendering state and nothing is destroyed.</summary>
        public void SetBlocking(bool on)
        {
            if (_colliders == null || _colliders.Length == 0)
                _colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = on;
        }
    }
}
