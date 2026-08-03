using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// The slot-in conductor for composed FX moments (hit, repair). This is the component
    /// another scene adds to consume what the lab dialed in: assign the tuning asset and
    /// the spawner refs, then call PlayHit / PlayContact / PlayRepair from game code (or
    /// UnityEvents). Target-side ingredients (FlashFX, SquashFX) are discovered on the
    /// target transform, so any object with those components can receive a moment.
    /// FXLabBoard self-provisions one of these at Awake - the lab consumes the exact
    /// component the game will.
    /// </summary>
    public class FXMomentPlayer : MonoBehaviour
    {
        static FXMomentPlayer _live;

        /// <summary>
        /// The scene's FX rig, for callers that cannot hold an inspector reference to it.
        /// A prefab (a resource node, an NPC) cannot serialise a ref to a SCENE object without
        /// it becoming a per-instance override, which defeats the point of shipping the mechanic
        /// as a prefab — so those callers reach the rig through here instead. Scene objects that
        /// CAN see the rig should still use a direct ref; this is the escape hatch, not the norm.
        /// Null when no rig is present, so every call site must null-check.
        /// </summary>
        public static FXMomentPlayer Current => _live;

        [Tooltip("The dialed values. Swap the asset to restyle every moment at once.")]
        public FXLabTuning tuning;

        [Header("Shared spawners (any scene object, usually children of this one)")]
        public SheetAnimFX sheetSlash;
        public SheetAnimFX sheetBurst;
        public ArcWipeFX arcWipe;
        public ArcRibbonFX arcRibbon;
        public PuffBurstFX puffer;
        public SparkleFX sparkler;
        public RingFX ringer;

        [Header("Optional globals (camera / clock) - moments skip them when unassigned")]
        public CamNudgeFX camNudge;
        public HitStopFX hitStop;

        [Tooltip("Fires when a hit's swing opens - hook the character rig here (SwingLungeFX.Play, anim triggers) without code coupling.")]
        public UnityEvent<Vector2> onSwing = new UnityEvent<Vector2>();

        // convenience entries reading the tuning blocks (UnityEvent-friendly)
        public void PlayHitWood(Transform target) => PlayHit(tuning != null ? tuning.hitWood : null, target, Aim(target));
        public void PlayHitMetal(Transform target) => PlayHit(tuning != null ? tuning.hitMetal : null, target, Aim(target));
        public void PlayHitStone(Transform target) => PlayHit(tuning != null ? tuning.hitStone : null, target, Aim(target));
        public void PlayRepair(Transform target) => PlayRepair(tuning != null ? tuning.repair : null, target);

        // contact only - for callers whose swing already happened and who just need the answer at
        // impact. The PlayHit* twins above re-open a swing arc and hold the reaction back by the
        // block's contactDelay, which is right in the lab and wrong behind a real swing.
        public void PlayContactWood(Transform target) => PlayContact(tuning != null ? tuning.hitWood : null, target, Aim(target));
        public void PlayContactMetal(Transform target) => PlayContact(tuning != null ? tuning.hitMetal : null, target, Aim(target));
        public void PlayContactStone(Transform target) => PlayContact(tuning != null ? tuning.hitStone : null, target, Aim(target));

        // First rig in wins, so a lab scene that self-provisions one does not steal the slot from
        // a deliberately-placed FXRig. Mirrors the FXServices._live pattern.
        void OnEnable()
        {
            if (_live == null) _live = this;
            ResolveSpawners();
        }

        void OnDisable() { if (_live == this) _live = null; }

        /// <summary>
        /// Fill any spawner left empty from the shared <see cref="FXServices"/> rig.
        ///
        /// Without this the conductor only works inside the lab, where FXLabBoard copies its own
        /// serialized refs across at Awake — drop the component into a game scene and every
        /// optional ingredient (puff, burst, sparkle, ring, shake, hitstop) silently no-ops,
        /// leaving a hit with sfx and flash but none of the splash. Explicit assignments always
        /// win; this only fills the gaps, so a prefab can ship with nothing wired and still work.
        /// </summary>
        void ResolveSpawners()
        {
            var s = FXServices.Get();
            if (s == null) return;

            if (puffer == null) puffer = s.puffer;
            if (sparkler == null) sparkler = s.sparkler;
            if (ringer == null) ringer = s.ringer;
            // FXServices keeps ONE sheet spawner. It goes to the burst: the impact is what a hit
            // moment needs, while the slash arc belongs to whatever rig owns the swing.
            if (sheetBurst == null) sheetBurst = s.sheet;
            if (camNudge == null) camNudge = s.camNudge;
            if (hitStop == null) hitStop = s.hitStop;
        }

        Vector2 Aim(Transform target) =>
            target != null ? ((Vector2)(target.position - transform.position)).normalized : Vector2.right;

        /// <summary>
        /// Full hit moment: swing arc opens (slash + onSwing), contact lands after the
        /// block's contactDelay. Slash overrides = the per-facing knobs from the lab.
        /// </summary>
        public void PlayHit(HitMomentSettings s, Transform target, Vector2 dir,
            float slashAngleDeg = 0f, bool slashFlipY = false,
            Vector2 slashOffset = default, int slashOrder = 50)
        {
            if (s == null || target == null) return;
            StartCoroutine(Hit(s, target, dir, slashAngleDeg, slashFlipY, slashOffset, slashOrder));
        }

        IEnumerator Hit(HitMomentSettings s, Transform target, Vector2 dir,
            float slashAngleDeg, bool slashFlipY, Vector2 slashOffset, int slashOrder)
        {
            onSwing.Invoke(dir);
            FireArc(s.arcVariant, dir, slashAngleDeg, slashFlipY, slashOffset, slashOrder);

            yield return new WaitForSeconds(s.contactDelay);
            if (target == null) yield break;
            PlayContact(s, target, dir);
        }

        void FireArc(ArcVariant variant, Vector2 dir,
            float slashAngleDeg, bool slashFlipY, Vector2 slashOffset, int slashOrder)
        {
            switch (variant)
            {
                case ArcVariant.Wipe:
                    if (arcWipe != null) arcWipe.Play(dir);
                    break;
                case ArcVariant.Ribbon:
                    if (arcRibbon != null) arcRibbon.Play(dir);
                    break;
                default:   // Sheet, and any pre-cull Sweep value still in an asset
                    if (sheetSlash != null)
                        sheetSlash.Play(dir, slashAngleDeg, slashFlipY, slashOffset, slashOrder);
                    break;
            }
        }

        /// <summary>
        /// Fire a block recipe at a target - the data-driven moment path. Target-side
        /// blocks find their components on the target; world blocks route through the
        /// shared FXServices (auto-provisioned, reuses this scene's camNudge/hitStop).
        /// </summary>
        /// <param name="swingDeg">Where the character is actually swinging, 0-360. Null = fall
        /// back to the tuning dial, which is what the lab uses.</param>
        public void PlayRecipe(FXRecipe recipe, Transform target, Vector2 dir, Color? tint = null,
            float? swingDeg = null)
        {
            if (recipe == null) return;
            FXRecipeRunner.Run(recipe, new FXContext
            {
                hasSwingAngle = swingDeg.HasValue,
                swingAngleDeg = swingDeg ?? 0f,
                target = target,
                pos = target != null ? target.position : transform.position,
                dir = dir,
                host = this,
                services = FXServices.Get(),
                tuning = tuning,   // nested RecipeBlocks resolve their name against this asset
                hasTint = tint.HasValue,
                tint = tint ?? Color.white
            });
        }

        /// <summary>
        /// Fire a recipe as a full moment, honoring its own swing metadata: swingOpens =
        /// arc + onSwing fire first and blocks land after the recipe's contactDelay;
        /// otherwise the blocks fire immediately. Slash overrides = the per-facing knobs.
        /// </summary>
        public void PlayRecipeMoment(FXRecipe recipe, Transform target, Vector2 dir,
            float slashAngleDeg = 0f, bool slashFlipY = false,
            Vector2 slashOffset = default, int slashOrder = 50, Color? tint = null,
            float? swingDeg = null)
        {
            if (recipe == null || target == null) return;
            if (!recipe.swingOpens)
            {
                PlayRecipe(recipe, target, dir, tint, swingDeg);
                return;
            }
            StartCoroutine(RecipeMoment(recipe, target, dir,
                slashAngleDeg, slashFlipY, slashOffset, slashOrder, tint, swingDeg));
        }

        IEnumerator RecipeMoment(FXRecipe recipe, Transform target, Vector2 dir,
            float slashAngleDeg, bool slashFlipY, Vector2 slashOffset, int slashOrder, Color? tint,
            float? swingDeg)
        {
            onSwing.Invoke(dir);
            FireArc(recipe.arc, dir, slashAngleDeg, slashFlipY, slashOffset, slashOrder);

            yield return new WaitForSeconds(recipe.contactDelay);
            if (target == null) yield break;
            PlayRecipe(recipe, target, dir, tint, swingDeg);
        }

        /// <summary>
        /// Contact only - no swing arc. Use when the game drives its own swing (player
        /// controller anim) and just needs the material answer at impact time.
        /// Hitstop fires LAST so the impact-frame flash freezes at peak.
        /// </summary>
        public void PlayContact(HitMomentSettings s, Transform target, Vector2 dir)
        {
            if (s == null || target == null) return;
            Vector3 at = target.position;

            FXAudio.Play(s.sfx, at);
            var flash = target.GetComponent<FlashFX>();
            if (flash != null) flash.Play(s.flash, impactFrame: true);
            var squash = target.GetComponent<SquashFX>();
            if (squash != null) squash.Play(s.squash);
            if (puffer != null) puffer.PlayAt(at, s.puff, target);
            bool hasBurst = s.burst != null && (s.burst.sheet != null
                || (s.burst.frames != null && s.burst.frames.Length > 0));
            if (hasBurst && sheetBurst != null)
                sheetBurst.PlayAt(at, Vector2.zero, s.burst);
            if (s.cameraNudge && camNudge != null) camNudge.Play(dir);
            if (s.hitStop && hitStop != null) hitStop.Play();
        }

        /// <summary>The anti-hit: glow + bounce + motes + pulse. No hitstop, no shake.</summary>
        public void PlayRepair(RepairSettings s, Transform target)
        {
            if (s == null || target == null) return;
            Vector3 at = target.position;

            FXAudio.Play(s.sfx, at);
            var flash = target.GetComponent<FlashFX>();
            if (flash != null) flash.Play(s.glow);
            var squash = target.GetComponent<SquashFX>();
            if (squash != null) squash.Play(s.bounce);
            if (sparkler != null) sparkler.PlayAt(at + Vector3.up * 0.1f, s.motes);
            if (ringer != null) ringer.PlayAt(at, s.pulse);
        }
    }
}
