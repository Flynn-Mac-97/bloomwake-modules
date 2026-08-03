using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Scene-glue slot-in: routes a swing direction to the matching facing block and
    /// fires the sheet slash with that facing's dialed overrides (rotation / flip /
    /// offset / behind-player sorting). Hook any module's swing UnityEvent(Vector2) to
    /// PlaySlash in the inspector - no code coupling between modules. Facing thresholds
    /// mirror the game's clip pick: up = Back, straight down = Front, everything else
    /// the 45. Lives on (or under) the character so the slash spawns at them.
    /// </summary>
    public class SlashFacingRouter : MonoBehaviour
    {
        public FXLabTuning tuning;
        public SheetAnimFX sheetSlash;
        [Tooltip("Character renderer - behind-player facings sort the slash just under it (resolved per fire, Y-sort safe).")]
        public SpriteRenderer characterRenderer;

        [Tooltip("Back FX only when aim.y exceeds this. KEEP IN SYNC with CritterAnimator.backThreshold, else the body plays a 45 swing while the FX uses the back block.")]
        [Range(0.4f, 0.95f)] public float backThreshold = 0.75f;
        [Tooltip("Below this |aim.x| counts as straight down (front block).")]
        public float frontThreshold = 0.35f;
        [Tooltip("The 45 block was dialed while aiming LEFT (matches art that faces left). " +
                 "Aims on the other side get mirrored flip + offset + angle. Untick if it was dialed aiming right.")]
        public bool fortyFiveTunedLeft = true;

        public void PlaySlash(Vector2 dir)
        {
            if (tuning == null || sheetSlash == null || dir.sqrMagnitude < 0.0001f) return;
            var f = dir.normalized;
            bool is45 = false;
            SwingFacingSettings s;
            if (f.y > backThreshold) s = tuning.facingBack;
            else if (Mathf.Abs(f.x) < frontThreshold) s = tuning.facingFront;
            else { s = tuning.facing45; is45 = true; }

            // the 45 block is dialed for one side — mirror everything for the other
            bool mirror = is45 && (fortyFiveTunedLeft ? f.x > 0f : f.x < 0f);
            bool flipY = s.slashFlipY ^ mirror;
            float angle = mirror ? -s.slashAngleDeg : s.slashAngleDeg;
            Vector2 offset = s.slashOffset;
            if (mirror) offset.x = -offset.x;

            int order = 50;
            if (s.slashBehindPlayer && characterRenderer != null)
                order = characterRenderer.sortingOrder - 1;

            sheetSlash.Play(f, angle, flipY, offset, order);
        }
    }
}
