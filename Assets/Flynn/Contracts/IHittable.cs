using UnityEngine;

namespace Flynn.Contracts
{
    /// Cross-module hit seam. Hitter modules resolve this via GetComponent on the
    /// overlapped collider — never a concrete type from another module.
    public interface IHittable
    {
        void ApplyHit(int damage, Vector2 hitDirection);
    }
}
