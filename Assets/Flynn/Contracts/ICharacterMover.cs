using UnityEngine;

namespace Flynn.Contracts
{
    /// Cross-module mover seam. Sprite-animator modules read Facing/CurrentSpeed and claim
    /// the renderer via ExternalSpriteDriver — never a concrete mover type from another module.
    public interface ICharacterMover
    {
        /// Normalized last-moved direction, for aiming/animation.
        Vector2 Facing { get; }

        /// Current speed in world units/sec.
        float CurrentSpeed { get; }

        /// Set true by an animator that owns the SpriteRenderer, so the mover stops flipping it.
        bool ExternalSpriteDriver { get; set; }

        /// When true the animator skips run clips and loops an idle instead (e.g. blown, not walking).
        bool SuppressLocomotion { get; }
    }
}
