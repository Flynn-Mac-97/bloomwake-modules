using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Locates the tagged "Player" once and answers proximity queries. Static so any
    /// interaction trigger can gate on range without a serialized player ref or a
    /// singleton MonoBehaviour. The cache self-heals across scene loads (a destroyed
    /// player nulls out and is re-found on the next query).
    /// </summary>
    public static class PlayerLocator
    {
        private static Transform _player;

        public static Transform Player
        {
            get
            {
                if (_player == null)
                {
                    var go = GameObject.FindWithTag("Player");
                    if (go != null) _player = go.transform;
                }
                return _player;
            }
        }

        /// <summary>True when the tagged player is within <paramref name="radius"/> of the position.</summary>
        public static bool InRange(Vector3 worldPos, float radius)
        {
            var p = Player;
            return p != null && (p.position - worldPos).sqrMagnitude <= radius * radius;
        }
    }
}
