using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.PlatformTiles
{
    /// <summary>
    /// A walkable ramp between two elevation levels. The standard isometric solution: a volume at the
    /// stairs that owns the transition, rather than any per-tile cleverness.
    ///
    /// Place it over the stair art, rotate it so its local +Y points UP the stairs, and set the size
    /// to cover the treads. While the player is inside, level boundaries are suspended so they can
    /// cross the platform edge, and their height is driven continuously by how far along they are —
    /// which is what stops the sprite popping halfway up, the classic bug with this feature.
    ///
    /// No physics: it is a plain rectangle test against the player's foot point, so it needs no
    /// collider and no Rigidbody2D on the player.
    /// </summary>
    public class StairZone : MonoBehaviour
    {
        [Tooltip("Elevation at the BOTTOM of the stairs (local -Y end).")]
        public int lowLevel = 0;
        [Tooltip("Elevation at the TOP of the stairs (local +Y end).")]
        public int highLevel = 1;
        [Tooltip("Size of the walkable ramp in local units. Local +Y is up the stairs.")]
        public Vector2 size = new Vector2(1f, 2f);

        static readonly List<StairZone> Zones = new List<StairZone>();

        void OnEnable() { if (!Zones.Contains(this)) Zones.Add(this); }
        void OnDisable() { Zones.Remove(this); }

        /// <summary>The zone containing this world point, or null.</summary>
        public static StairZone ZoneAt(Vector2 worldPoint)
        {
            for (int i = 0; i < Zones.Count; i++)
                if (Zones[i] != null && Zones[i].Contains(worldPoint)) return Zones[i];
            return null;
        }

        public bool Contains(Vector2 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
        }

        /// <summary>0 at the bottom of the ramp, 1 at the top.</summary>
        public float Progress(Vector2 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Clamp01((local.y + size.y * 0.5f) / Mathf.Max(0.0001f, size.y));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
            // arrow pointing up the stairs
            Gizmos.color = Color.green;
            Vector3 top = new Vector3(0f, size.y * 0.5f, 0f);
            Gizmos.DrawLine(new Vector3(0f, -size.y * 0.5f, 0f), top);
            Gizmos.DrawLine(top, top + new Vector3(-size.x * 0.15f, -size.y * 0.12f, 0f));
            Gizmos.DrawLine(top, top + new Vector3(size.x * 0.15f, -size.y * 0.12f, 0f));
        }
    }
}
