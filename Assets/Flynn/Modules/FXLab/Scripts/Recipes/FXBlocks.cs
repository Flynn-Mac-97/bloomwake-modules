using System;
using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    // The concrete block set. Wrapper blocks reuse the existing settings classes and
    // primitive components 1:1 - dialing a block dials the same knobs as the typed path.
    // Self-executing blocks (fade) carry their own fields. New block = new class here
    // + nothing else; recipes pick it up as data.

    /// <summary>Recolouring for blocks running under a moment tint. Always works on a CLONE
    /// of the dialed settings: the primitives read their settings object every tween frame,
    /// so mutating the asset would recolour effects already in flight.</summary>
    static class FXTint
    {
        /// <summary>Take the moment's hue, keep the dialed alpha.</summary>
        public static Color Rgb(Color dialed, Color tint) => new Color(tint.r, tint.g, tint.b, dialed.a);

        /// <summary>Debris ramp: the material colour plus a darker and a lighter chip, so a
        /// burst still reads as torn-off fragments instead of one flat swatch.</summary>
        public static Color[] Chips(Color tint) => new[]
        {
            tint,
            new Color(tint.r * 0.72f, tint.g * 0.72f, tint.b * 0.72f, tint.a),
            new Color(Mathf.Min(1f, tint.r * 1.25f), Mathf.Min(1f, tint.g * 1.25f),
                      Mathf.Min(1f, tint.b * 1.25f), tint.a)
        };
    }

    /// <summary>One-shot sound at the recipe position.</summary>
    [Serializable]
    public class SfxBlock : FXBlock
    {
        public SfxSlot sfx = new SfxSlot();

        public override void Play(FXContext ctx) => FXAudio.Play(sfx, ctx.pos);
    }

    /// <summary>Target flash - needs FlashFX (+ the flash shader material) on the target.</summary>
    [Serializable]
    public class FlashBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Flash primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public FlashSettings flash = new FlashSettings();
        [Tooltip("Hold the first frame crisp - hit language. Off for soft glows.")]
        public bool impactFrame = true;
        [Tooltip("Recolour from the moment's material tint instead of the dialed colour.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Flash";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) flash = FXCopy.Deep(t.flash); }

        public override void Play(FXContext ctx)
        {
            if (ctx.target == null) return;
            var fx = ctx.target.GetComponent<FlashFX>();
            if (fx == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.flash : flash;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.color = FXTint.Rgb(s.color, ctx.tint);
                s = recolored;
            }
            fx.Play(s, impactFrame);
        }
    }

    /// <summary>Target squash-and-stretch - needs SquashFX on the target.</summary>
    [Serializable]
    public class SquashBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Squash primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public SquashSettings squash = new SquashSettings();

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Squash";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) squash = FXCopy.Deep(t.squash); }

        public override void Play(FXContext ctx)
        {
            if (ctx.target == null) return;
            var fx = ctx.target.GetComponent<SquashFX>();
            if (fx != null) fx.Play(usePrimitive && ctx.tuning != null ? ctx.tuning.squash : squash);
        }
    }

    /// <summary>Debris/dust burst at the recipe position.</summary>
    [Serializable]
    public class PuffBlock : FXBlock, IPrimitiveLinked
    {
        /// <summary>Which dialed puff this mirrors - the plain one or the sprite-sheet one.</summary>
        public enum Source { Puff, PuffSpriteSheet }

        [Tooltip("Run a dialed Puff primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public Source source = Source.Puff;
        public PuffSettings puff = new PuffSettings();
        public Vector2 offset = Vector2.zero;
        [Tooltip("Chip colours come from the moment's material tint (3-shade ramp) instead of the dialed array.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => source == Source.PuffSpriteSheet ? "PuffSpriteSheet" : "Puff";
        public void CopyFromPrimitive(FXLabTuning t)
        {
            if (t != null) puff = FXCopy.Deep(Primitive(t));
        }

        PuffSettings Primitive(FXLabTuning t) =>
            source == Source.PuffSpriteSheet ? t.puffSheet : t.puff;

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.puffer == null) return;
            var s = usePrimitive && ctx.tuning != null ? Primitive(ctx.tuning) : puff;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.colors = FXTint.Chips(ctx.tint);
                s = recolored;
            }
            ctx.services.puffer.PlayAt(ctx.pos + (Vector3)offset, s, ctx.target);
        }
    }

    /// <summary>Expanding pulse ring at the recipe position.</summary>
    [Serializable]
    public class RingBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Ring primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public RingSettings ring = new RingSettings();
        public Vector2 offset = Vector2.zero;
        [Tooltip("Recolour from the moment's material tint instead of the dialed colour.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Ring";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) ring = FXCopy.Deep(t.ring); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.ringer == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.ring : ring;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.color = FXTint.Rgb(s.color, ctx.tint);
                s = recolored;
            }
            ctx.services.ringer.PlayAt(ctx.pos + (Vector3)offset, s);
        }
    }

    /// <summary>Rising twinkle motes at the recipe position.</summary>
    [Serializable]
    public class SparkleBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Sparkle primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public SparkleSettings sparkle = new SparkleSettings();
        public Vector2 offset = Vector2.zero;
        [Tooltip("Recolour from the moment's material tint instead of the dialed colour.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Sparkle";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) sparkle = FXCopy.Deep(t.sparkle); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.sparkler == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.sparkle : sparkle;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.color = FXTint.Rgb(s.color, ctx.tint);
                s = recolored;
            }
            ctx.services.sparkler.PlayAt(ctx.pos + (Vector3)offset, s);
        }
    }

    /// <summary>
    /// Plays another recipe as one step of this one - the composition block.
    ///
    /// This is how a moment stops being a flat pile of primitives: author the tool swing
    /// ONCE as its own recipe, then every material hit is just [SwingTool, ContactWood] with
    /// the contact carrying the delay. Re-dial the swing and every hit that references it
    /// changes. Referenced by NAME, so a recipe list stays plain serialisable data.
    /// </summary>
    [Serializable]
    public class RecipeBlock : FXBlock
    {
        [Tooltip("Name of the recipe on the same tuning asset. Case-insensitive.")]
        public string recipe = "";

        /// <summary>A recipe that reaches itself (directly or through a chain) would spin
        /// forever - three levels is deeper than any real moment needs.</summary>
        const int MaxDepth = 3;

        public override void Play(FXContext ctx)
        {
            if (ctx.tuning == null || string.IsNullOrEmpty(recipe)) return;
            if (ctx.depth >= MaxDepth)
            {
                Debug.LogWarning($"FXLab: recipe nesting too deep at '{recipe}' - is it inside itself?");
                return;
            }
            var sub = ctx.tuning.FindRecipe(recipe);
            if (sub == null) return;

            // fresh context so the nested depth (and any tint it resolves) never leaks back
            FXRecipeRunner.Run(sub, new FXContext
            {
                target = ctx.target,
                pos = ctx.pos,
                dir = ctx.dir,
                host = ctx.host,
                services = ctx.services,
                tuning = ctx.tuning,
                hasTint = ctx.hasTint,
                tint = ctx.tint,
                depth = ctx.depth + 1
            });
        }
    }

    /// <summary>
    /// Where a swing points, as a direction. The swing's facing is ONE continuous angle now, so
    /// the body lunge reads it from the same place the arc does - otherwise the character would
    /// lunge one way while the arc swept another.
    /// </summary>
    static class FXSwing
    {
        public static float AngleDeg(FXContext ctx) =>
            ctx.hasSwingAngle ? ctx.swingAngleDeg
                : ctx.tuning != null ? ctx.tuning.swingAngleDeg
                : 0f;

        public static Vector2 Aim(FXContext ctx)
        {
            if (ctx.tuning == null && !ctx.hasSwingAngle) return ctx.dir;
            float rad = AngleDeg(ctx) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }

    /// <summary>The tool arc: sweep / wipe / sheet, aimed along the moment's direction.
    /// Same dispatch the typed hit path uses, so a swing authored as a recipe behaves
    /// exactly like the hard-coded one it replaces.</summary>
    [Serializable]
    public class SwingArcBlock : FXBlock
    {
        public ArcVariant arc = ArcVariant.Sheet;
        [Tooltip("Override the facing for this layer instead of using the tuning dial / the " +
                 "angle the caller passed in. 0-360, 0 = +X.")]
        public bool overrideAngle = false;
        [Range(0f, 360f)] public float angleDeg = 0f;

        public override void Play(FXContext ctx)
        {
            var s = ctx.services;
            if (s == null || ctx.tuning == null) return;

            // one continuous facing, in priority order: this layer's override, then whatever the
            // caller supplied (a real character, later), then the lab's dial
            float facing = overrideAngle ? angleDeg
                : ctx.hasSwingAngle ? ctx.swingAngleDeg
                : ctx.tuning.swingAngleDeg;
            var rig = ctx.tuning.swingRig;

            switch (arc)
            {
                case ArcVariant.Wipe:
                    if (s.arcWipe != null) s.arcWipe.PlayAtAngle(facing, s.player, rig);
                    break;
                case ArcVariant.Ribbon:
                    if (s.arcRibbon != null) s.arcRibbon.PlayAtAngle(facing, s.player, rig);
                    break;
                default:   // Sheet (and any pre-cull Sweep value still sitting in an asset)
                    if (s.sheetSlash != null)
                        s.sheetSlash.PlayAtAngle(facing, s.player, ctx.tuning.sheetSlash, rig);
                    break;
            }
        }
    }

    /// <summary>
    /// Character body language for the swing: the lunge on whoever owns the tool, plus the
    /// scene's swing hook (anim triggers). Both are FOUND, never created - a swing belongs to
    /// a character, not a spawner, so a scene without one simply skips this step.
    /// </summary>
    [Serializable]
    public class LungeBlock : FXBlock
    {
        public override void Play(FXContext ctx)
        {
            if (ctx.services == null) return;
            Vector2 aim = FXSwing.Aim(ctx);
            if (ctx.services.lunge != null) ctx.services.lunge.Play(aim);
            ctx.services.onSwing?.Invoke(aim);
        }
    }

    /// <summary>Emotion bubble above the target's head - how a non-talking critter answers.
    /// Pops at the top of the target's sprite (not its pivot), so it clears tall art.</summary>
    [Serializable]
    public class EmoteBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Emote primitive (icon art lives there). Off = own copy below.")]
        public bool usePrimitive = false;
        public EmoteIcon icon = EmoteIcon.Affection;
        public EmoteSettings emote = new EmoteSettings();

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Emote";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) emote = FXCopy.Deep(t.emote); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.emote == null) return;
            Vector3 at = ctx.pos;
            if (ctx.target != null)
            {
                var sr = ctx.target.GetComponent<SpriteRenderer>();
                if (sr != null) at = new Vector3(sr.bounds.center.x, sr.bounds.max.y, ctx.pos.z);
            }
            ctx.services.emote.PlayAt(at, icon,
                usePrimitive && ctx.tuning != null ? ctx.tuning.emote : emote);
        }
    }

    /// <summary>Generic pack frame-anim burst at the recipe position (grid sheet, explicit
    /// rects, or individual frames - same SheetFXSettings the browser assigns onto).
    /// Skips silently while no art is assigned.</summary>
    [Serializable]
    public class BurstBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Sheet Burst primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public SheetFXSettings anim = new SheetFXSettings();
        [Tooltip("Rotate the anim along the recipe direction. Off = unrotated burst.")]
        public bool aimAlongDir = false;
        public Vector2 offset = Vector2.zero;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Sheet Burst";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) anim = FXCopy.Deep(t.sheetBurst); }

        public override void Play(FXContext ctx)
        {
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.sheetBurst : anim;
            bool hasArt = s != null && (s.sheet != null
                || (s.frames != null && s.frames.Length > 0));
            if (!hasArt || ctx.services == null || ctx.services.sheet == null) return;
            ctx.services.sheet.PlayAt(ctx.pos + (Vector3)offset,
                aimAlongDir ? ctx.dir : Vector2.zero, s);
        }
    }

    /// <summary>Rising icon aura - the "that helped" answer. Spawns from the target's middle
    /// so the icons leave the thing itself, not the ground under it.</summary>
    [Serializable]
    public class FloatAuraBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Float Aura primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public FloatAuraSettings aura = new FloatAuraSettings();
        public Vector2 offset = Vector2.zero;
        [Tooltip("Recolour from the moment's material tint instead of the dialed colours.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Float Aura";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) aura = FXCopy.Deep(t.floatAura); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.floatAura == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.floatAura : aura;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.colors = new[] { ctx.tint };
                s = recolored;
            }

            Vector3 at = ctx.pos;
            if (ctx.target != null)
            {
                var sr = ctx.target.GetComponent<SpriteRenderer>();
                if (sr != null) at = sr.bounds.center;
            }
            ctx.services.floatAura.PlayAt(at + (Vector3)offset, s);
        }
    }

    /// <summary>Pieces drawn inward to the target - absorption. The counterpart to Puff:
    /// where that throws debris off, this pulls it in.</summary>
    [Serializable]
    public class ConvergeBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Converge primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public ConvergeSettings converge = new ConvergeSettings();
        public Vector2 offset = Vector2.zero;
        [Tooltip("Recolour from the moment's material tint instead of the dialed colours.")]
        public bool useMomentTint = false;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Converge";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) converge = FXCopy.Deep(t.converge); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.converge == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.converge : converge;
            if (useMomentTint && ctx.hasTint)
            {
                var recolored = s.Clone();
                recolored.colors = new[] { ctx.tint };
                s = recolored;
            }

            Vector3 at = ctx.pos;
            if (ctx.target != null)
            {
                var sr = ctx.target.GetComponent<SpriteRenderer>();
                if (sr != null) at = sr.bounds.center;   // pieces land ON the thing, not at its feet
            }
            ctx.services.converge.PlayAt(at + (Vector3)offset, s);
        }
    }

    /// <summary>Watering droplet arc along the recipe direction.</summary>
    [Serializable]
    public class DropletBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Droplets primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public DropletSettings droplets = new DropletSettings();
        public Vector2 offset = Vector2.zero;

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Droplets";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) droplets = FXCopy.Deep(t.droplets); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services == null || ctx.services.droplets == null) return;
            var s = usePrimitive && ctx.tuning != null ? ctx.tuning.droplets : droplets;
            ctx.services.droplets.PlayAt(ctx.pos + (Vector3)offset, ctx.dir, s);
        }
    }

    /// <summary>Stateful tint on the target (wet soil, scan highlight). Self-provisions
    /// TintStateFX - autoRevert in the settings unwinds it (or a later recipe clears it).</summary>
    [Serializable]
    public class TintBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Tint State primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public TintStateSettings tint = new TintStateSettings { autoRevert = 6f };

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Tint State";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) tint = FXCopy.Deep(t.tintState); }

        public override void Play(FXContext ctx)
        {
            if (ctx.target == null) return;
            var fx = ctx.target.GetComponent<TintStateFX>();
            if (fx == null) fx = ctx.target.gameObject.AddComponent<TintStateFX>();
            fx.SetTinted(true, usePrimitive && ctx.tuning != null ? ctx.tuning.tintState : tint);
        }
    }

    /// <summary>Camera kick along the recipe direction.</summary>
    [Serializable]
    public class NudgeBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Cam Nudge primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public NudgeSettings nudge = new NudgeSettings();

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Cam Nudge";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) nudge = FXCopy.Deep(t.nudge); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services != null && ctx.services.camNudge != null)
                ctx.services.camNudge.Play(ctx.dir,
                    usePrimitive && ctx.tuning != null ? ctx.tuning.nudge : nudge);
        }
    }

    /// <summary>Micro time-freeze. Put LAST in a hit recipe so the impact frame freezes at peak.</summary>
    [Serializable]
    public class HitStopBlock : FXBlock, IPrimitiveLinked
    {
        [Tooltip("Run the dialed Hit Stop primitive. Off = this block's own copy below.")]
        public bool usePrimitive = false;
        public HitStopSettings hitStop = new HitStopSettings();

        public bool UsePrimitive { get => usePrimitive; set => usePrimitive = value; }
        public string PrimitiveName => "Hit Stop";
        public void CopyFromPrimitive(FXLabTuning t) { if (t != null) hitStop = FXCopy.Deep(t.hitStop); }

        public override void Play(FXContext ctx)
        {
            if (ctx.services != null && ctx.services.hitStop != null)
                ctx.services.hitStop.Play(usePrimitive && ctx.tuning != null
                    ? ctx.tuning.hitStop : hitStop);
        }
    }

    /// <summary>
    /// Fades the target's SpriteRenderer alpha - despawn/ghost/consume language.
    /// restoreDelay &gt; 0 brings the alpha back after the fade (lab-friendly repeat);
    /// 0 = stays faded. Self-executing: needs no FX component on the target.
    /// </summary>
    [Serializable]
    public class FadeOutBlock : FXBlock
    {
        [Range(0f, 1f)] public float endAlpha = 0f;
        public float duration = 0.35f;
        [Tooltip("Seconds after the fade completes before alpha restores. 0 = stay faded.")]
        public float restoreDelay = 0.8f;
        public float restoreDuration = 0.25f;
        public SfxSlot sfx = new SfxSlot();

        public override void Play(FXContext ctx)
        {
            if (ctx.target == null) return;
            var sr = ctx.target.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            FXAudio.Play(sfx, ctx.pos);

            float startAlpha = sr.color.a;
            Tween.Custom(startAlpha, endAlpha, duration, onValueChange: a => SetAlpha(sr, a));
            if (restoreDelay > 0f)
                Tween.Delay(duration + restoreDelay, () =>
                {
                    if (sr != null)
                        Tween.Custom(endAlpha, startAlpha, restoreDuration,
                            onValueChange: a => SetAlpha(sr, a));
                });
        }

        static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }
}
