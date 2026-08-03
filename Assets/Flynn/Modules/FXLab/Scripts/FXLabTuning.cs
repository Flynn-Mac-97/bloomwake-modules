using System;
using System.Collections.Generic;
using Flynn.Feel;
using UnityEngine;
using UnityEngine.Serialization;

namespace Flynn.Modules.FXLab
{
    [Serializable]
    public class SfxSlot
    {
        [Tooltip("Authored SoundSO - WINS over the clip below. Brings its own volume/pitch/" +
                 "jitter/region/mixer routing, so a sound designed once is reused everywhere. " +
                 "The slot's volume still scales it and its delay still staggers it.")]
        public SoundSO sound;
        [Tooltip("Raw clip picked from the lab's library. Used when no SoundSO is slotted.")]
        public AudioClip clip;
        [Tooltip("Optional alternates - each play picks randomly from clip + these. Variation beats jitter alone.")]
        public AudioClip[] variants = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Random pitch offset per play, +/- this. Kills machine-gun repetition.")]
        [Range(0f, 0.3f)] public float pitchJitter = 0.06f;
        [Tooltip("Start playback here, as a fraction of clip length (0 = clip start).")]
        [Range(0f, 1f)] public float trimStart = 0f;
        [Tooltip("Stop playback here, as a fraction of clip length (1 = play to end).")]
        [Range(0f, 1f)] public float trimEnd = 1f;
        [Tooltip("Hold this sound back by N seconds. Stacked layers that all hit on the same " +
                 "frame read as ONE louder sound; a few tens of ms apart they read as a blend.")]
        [Range(0f, 0.5f)] public float delay = 0f;

        /// <summary>True when a SoundSO owns this slot - the clip/trim knobs are bypassed.</summary>
        public bool UsesSound => sound != null && sound.clip != null;

        /// <summary>Random pick from clip + variants (nulls skipped).</summary>
        public AudioClip Pick()
        {
            int pool = (clip != null ? 1 : 0);
            if (variants != null)
                foreach (var v in variants)
                    if (v != null) pool++;
            if (pool == 0) return null;
            int pick = UnityEngine.Random.Range(0, pool);
            if (clip != null && pick-- == 0) return clip;
            foreach (var v in variants)
                if (v != null && pick-- == 0) return v;
            return clip;
        }
    }

    [Serializable]
    public class FlashSettings
    {
        [Tooltip("Warm flash, not harsh white - cozy contact, VibeSpec light-warm base.")]
        public Color color = new Color(1f, 0.92f, 0.75f, 1f);
        [Range(0f, 1f)] public float peak = 1f;
        [Tooltip("Seconds to reach peak. 0 = instant impact frame.")]
        public float attack = 0.02f;
        public float decay = 0.14f;
        public SfxSlot sfx = new SfxSlot();

        /// <summary>Throwaway copy for a per-moment recolor - the primitives read their
        /// settings object every tween frame, so the dialed asset must never be mutated.</summary>
        public FlashSettings Clone() => (FlashSettings)MemberwiseClone();
    }

    [Serializable]
    public class SquashSettings
    {
        [Tooltip("Peak deformation as a fraction of base scale. X widens, Y shortens (volume-ish preserved).")]
        [Range(0f, 0.6f)] public float punch = 0.2f;
        public float duration = 0.28f;
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Keep the sprite's bottom edge planted while it squashes. Art with a CENTRE " +
                 "pivot otherwise deforms about its middle and sinks into the ground. " +
                 "No-op when the pivot already sits at the base.")]
        public bool anchorBottom = true;
        public SfxSlot sfx = new SfxSlot();

        public SquashSettings Clone() => (SquashSettings)MemberwiseClone();
    }

    [Serializable]
    public class PuffSettings
    {
        [Range(1, 24)] public int count = 8;
        [Tooltip("Debris sprites - each mote picks one at random. Empty = soft-dot placeholder.")]
        public Sprite[] sprites = new Sprite[0];
        [Tooltip("Debris tints - leaves/petals, not shrapnel. Set to white when using real sprite art.")]
        public Color[] colors = new Color[0];
        public float speed = 1.1f;
        [Tooltip("Downward pull, world units/s^2. Negative = falls.")]
        public float gravity = -2.6f;
        public float life = 0.55f;
        [Tooltip("Debris size in world units.")]
        public float size = 0.05f;
        [Tooltip("0 = burst in all directions, 1 = strongly upward.")]
        [Range(0f, 1f)] public float upBias = 0.6f;
        [Tooltip("Tumble speed, degrees/sec (each chip gets a random fraction + direction). Sells 'chipped off'.")]
        public float spinDegrees = 360f;
        [Tooltip("Fraction of life spent scaling up from nothing. 0 = appear at full size (debris). " +
                 "~0.3 = the toon read: each sprite pops open from the centre as it flies out.")]
        [Range(0f, 1f)] public float scaleIn = 0f;
        [Tooltip("How much of its size a mote loses over its life. 0 = keeps full size, 1 = shrinks away.")]
        [Range(0f, 1f)] public float shrink = 0.6f;
        [Tooltip("Normalised life where alpha starts falling to 0. 1 = never fades (it just vanishes).")]
        [Range(0f, 1f)] public float fadeStart = 0.6f;
        [Tooltip("Optional frame animation played by EACH mote (a puff of dust that blooms and " +
                 "dissipates, not a static dot). Empty sheet = the plain sprite/dot behaviour. " +
                 "Assign via the VFX browser's 'Puff sheet' target, or by hand here.")]
        public SheetFXSettings anim = new SheetFXSettings();
        [Tooltip("Fit one playthrough to the mote's life (puff dies as its anim ends). " +
                 "Off = play at the sheet's own fps and hold the last frame.")]
        public bool animOverLife = true;
        public SfxSlot sfx = new SfxSlot();

        public PuffSettings Clone() => (PuffSettings)MemberwiseClone();
    }

    [Serializable]
    public class RingSettings
    {
        public Color color = new Color(1f, 0.85f, 0.53f, 0.8f);
        public float startRadius = 0.06f;
        public float endRadius = 0.42f;
        public float duration = 0.35f;
        [Tooltip("Alpha over normalized life. Left = spawn, right = end.")]
        public AnimationCurve fade = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public SfxSlot sfx = new SfxSlot();

        public RingSettings Clone() => (RingSettings)MemberwiseClone();
    }

    [Serializable]
    public class SparkleSettings
    {
        [Range(1, 16)] public int count = 6;
        public Color color = new Color(1f, 0.85f, 0.53f, 1f);
        public float riseSpeed = 0.35f;
        public float life = 0.9f;
        [Tooltip("Twinkles per second.")]
        public float twinkleHz = 5f;
        public float size = 0.04f;
        public SfxSlot sfx = new SfxSlot();

        public SparkleSettings Clone() => (SparkleSettings)MemberwiseClone();
    }

    /// <summary>
    /// Rising icon aura - the "something good just happened here" language: plus signs, hearts,
    /// leaves drifting up off a thing you tended and fading out. Icons EMIT over a window rather
    /// than bursting all at once, which reads as ongoing care instead of an impact; each one
    /// sways as it climbs so the column never looks like a straight line of clones.
    /// See FloatAuraFX.
    /// </summary>
    [Serializable]
    public class FloatAuraSettings
    {
        [Range(1, 16)] public int count = 5;
        [Tooltip("Icon sprites - each spawn picks one at random. Empty = soft-dot placeholder.")]
        public Sprite[] sprites = new Sprite[0];
        [Tooltip("Tints - each spawn picks one at random. Empty = white.")]
        public Color[] colors = new Color[0];
        [Tooltip("Seconds the spawns are spread over. 0 = all at once (a burst, not a stream).")]
        public float emitOver = 0.45f;
        [Tooltip("How far an icon climbs, world units.")]
        public float riseHeight = 0.5f;
        [Tooltip("Seconds one icon takes to climb and fade.")]
        public float riseDuration = 0.9f;
        [Tooltip("Horizontal scatter of the spawn points, world units.")]
        public float spread = 0.18f;
        [Tooltip("Sideways drift amplitude on the way up - the bubble wobble.")]
        public float swayAmp = 0.05f;
        [Tooltip("Sway cycles per second.")]
        public float swayHz = 1.2f;
        public float size = 0.09f;
        [Tooltip("Fraction of life spent popping up to full size.")]
        [Range(0f, 1f)] public float scaleIn = 0.18f;
        [Tooltip("Normalised life where the icon starts fading out.")]
        [Range(0f, 1f)] public float fadeStart = 0.55f;
        [Tooltip("Random tilt per icon, degrees - keeps them from looking stamped.")]
        public float tiltDegrees = 8f;
        public int sortingOrder = 70;
        public SfxSlot sfx = new SfxSlot();

        public FloatAuraSettings Clone() => (FloatAuraSettings)MemberwiseClone();
    }

    /// <summary>
    /// The inverse of a puff: pieces start on a ring and are drawn INWARD to the centre, spiral-
    /// ling and shrinking as they arrive. Absorption language - a thing taking something in
    /// rather than throwing it off. Cogs and nuts pulling into a machine being repaired, motes
    /// soaking into soil. See ConvergeFX.
    /// </summary>
    [Serializable]
    public class ConvergeSettings
    {
        [Range(1, 24)] public int count = 8;
        [Tooltip("Piece sprites - each picks one at random. Empty = soft-dot placeholder.")]
        public Sprite[] sprites = new Sprite[0];
        [Tooltip("Tints - each picks one at random. Empty = white.")]
        public Color[] colors = new Color[0];
        [Tooltip("Ring radius the pieces start from, world units.")]
        public float startRadius = 0.55f;
        [Tooltip("Random +/- on each piece's starting radius, so the ring is not mechanical.")]
        public float radiusJitter = 0.08f;
        [Tooltip("Random +/- on each piece's angle, degrees. 0 = perfectly even spacing.")]
        public float angleJitter = 10f;
        [Tooltip("Seconds one piece takes to travel in.")]
        public float duration = 0.55f;
        [Tooltip("Delay between successive pieces - a small value makes them arrive as a run " +
                 "rather than a thud. 0 = all together.")]
        public float stagger = 0.04f;
        [Tooltip("How the radius closes over the travel. Ease-in (slow out there, quick at the " +
                 "end) is the satisfying one - it reads as being pulled.")]
        public AnimationCurve pull = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Degrees each piece sweeps around the centre while travelling - the spiral.")]
        public float swirlDegrees = 45f;
        [Tooltip("Self-rotation over the travel, degrees. Cogs want this.")]
        public float spinDegrees = 180f;
        public float size = 0.06f;
        [Tooltip("Size on arrival as a fraction of the start size - shrink as it is absorbed.")]
        [Range(0f, 1.5f)] public float endScale = 0.2f;
        [Tooltip("Normalised travel where a piece starts fading. 1 = never fades (it just lands).")]
        [Range(0f, 1f)] public float fadeStart = 0.75f;
        public int sortingOrder = 65;
        public SfxSlot sfx = new SfxSlot();

        public ConvergeSettings Clone() => (ConvergeSettings)MemberwiseClone();
    }

    [Serializable]
    public class ArcWipeSettings
    {
        public float duration = 0.22f;
        [Tooltip("Visible sweep window, degrees.")]
        public float spanDegrees = 140f;
        [Tooltip("Fading tail behind the head, degrees.")]
        public float tailDegrees = 70f;
        [Tooltip("Ring band in quad-UV units (quad is 2x2 UV space, so 0..1).")]
        public float innerRadius = 0.30f;
        public float outerRadius = 0.48f;
        public float edgeSoft = 0.05f;
        [Tooltip("Radius wobble - brushstroke edge breakup.")]
        public float wobble = 0.02f;
        public Color color = new Color(1f, 0.85f, 0.53f, 0.9f);
        [Tooltip("World size of the wipe quad.")]
        public float worldSize = 1.6f;
        public SfxSlot sfx = new SfxSlot();
    }

    /// <summary>
    /// Parametric crescent ribbon (ArcRibbonFX). Deterministic: the same values produce the same
    /// arc every swing, so the reach is learnable. Everything is authored in the aim's frame -
    /// +X is where the character is swinging.
    /// </summary>
    [Serializable]
    public class ArcRibbonSettings
    {
        [Tooltip("Sweep time once the arc opens, seconds.")]
        public float duration = 0.2f;
        [Tooltip("Silence before the arc opens - lets the character's anticipation frames read " +
                 "before the crescent paints over them.")]
        public float windup = 0.06f;
        [Tooltip("Arc length in degrees. Sign is the handedness - negative sweeps over the top " +
                 "and down, which is the way a tool swing reads.")]
        public float sweepDeg = -120f;
        [Tooltip("Where along the sweep the aim direction sits. 0.5 = centred (reads as a fan); " +
                 "higher opens before the aim and whips past it (reads as a strike).")]
        [Range(0f, 1f)] public float contactAt = 0.55f;
        [Tooltip("Arm's length, world units.")]
        public float radius = 0.62f;
        [Tooltip("Ribbon thickness at its fattest, world units.")]
        public float width = 0.16f;
        [Tooltip("How much of the thickness is soft edge rather than solid core.")]
        [Range(0f, 0.9f)] public float feather = 0.45f;
        [Tooltip("Iso foreshortening. 0.5 = matches the 1x0.5 iso grid exactly, 1 = a flat " +
                 "screen-plane flourish (and disables the aim conversion, which is then a no-op).")]
        [Range(0.05f, 1f)] public float groundSquash = 0.62f;
        [Tooltip("The facing angle is a SCREEN angle, so convert it to a ground angle before " +
                 "projecting. Untick only if the caller already hands over a ground-space angle.")]
        public bool aimIsScreenSpace = true;
        [Tooltip("Arc smoothness. More = rounder, and it costs nothing much at this size.")]
        [Range(6, 64)] public int segments = 28;

        [Header("Sweep light")]
        [Tooltip("How much of the arc stays lit behind the head, as a fraction of its length.")]
        [Range(0.05f, 1f)] public float tailFrac = 0.45f;
        [Tooltip("Tail falloff shape. 1 = linear, higher = the light clings closer to the head.")]
        [Range(0.2f, 4f)] public float tailShape = 1.6f;
        [Tooltip("Extra brightness right behind the tip - the contact accent.")]
        [Range(0f, 2f)] public float headBoost = 0.35f;
        [Tooltip("How far behind the tip that brightening reaches.")]
        [Range(0.02f, 0.5f)] public float headWidth = 0.18f;
        public Color colorHead = new Color(1f, 0.93f, 0.72f, 1f);
        public Color colorTail = new Color(1f, 0.78f, 0.45f, 1f);
        [Range(0f, 1f)] public float alpha = 0.9f;
        [Tooltip("Sweep speed along the arc. Ease-in-out reads as a gather then a follow-through.")]
        public AnimationCurve sweepEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Thickness along the arc: thin at the open, fat at contact, thin at the finish.")]
        public AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.15f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.1f));
        public SfxSlot sfx = new SfxSlot();
    }

    [Serializable]
    public class HitStopSettings
    {
        [Tooltip("Freeze length, real-time seconds. Cozy thunk = short.")]
        public float duration = 0.055f;
        [Tooltip("Time.timeScale during the stop. 0 = full freeze.")]
        [Range(0f, 0.5f)] public float timeScale = 0.05f;

        public HitStopSettings Clone() => (HitStopSettings)MemberwiseClone();
    }

    [Serializable]
    public class NudgeSettings
    {
        [Tooltip("Position kick, world units. Cozy = pixels, not earthquakes.")]
        public float amplitude = 0.035f;
        [Tooltip("Rotation kick, degrees. Tiny rotation is what makes shake read as force.")]
        public float rotationDeg = 0.3f;
        public float duration = 0.2f;
        [Tooltip("Oscillations per second while decaying.")]
        public float frequency = 16f;

        public NudgeSettings Clone() => (NudgeSettings)MemberwiseClone();
    }

    /// <summary>One-shot frame anim: either individual sprite assets (frames) or a grid
    /// sheet sliced at runtime. Frames wins when non-empty.</summary>
    [Serializable]
    public class SheetFXSettings
    {
        [Tooltip("Individual sprite frames, in play order - OVERRIDES the grid sheet when non-empty. One sprite = single-frame flash.")]
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Grid sheet texture (e.g. the 96x96 pack sheets in Modules/FXLab/VFX). Used only when Frames is empty.")]
        public Texture2D sheet;
        public int cellSize = 96;
        [Tooltip("Cell height when frames aren't square. 0 = same as cellSize.")]
        public int cellHeight = 0;
        [Tooltip("Explicit pixel rects on the sheet (VFX browser segmentation writes these). OVERRIDES row/cellSize when non-empty; Frames still wins over both.")]
        public Rect[] cellRects = new Rect[0];
        [Tooltip("Row from the TOP of the sheet, 0-based. Scrub live to find each anim.")]
        public int row = 1;
        public int frameCount = 7;
        public float fps = 24f;
        public Color tint = Color.white;
        [Tooltip("Mirror the frames vertically (e.g. slash art that should arc downward).")]
        public bool flipY = false;
        [Tooltip("Extra rotation (deg) on top of the aim - corrects art whose baked angle doesn't face +X.")]
        public float angleOffsetDeg = 0f;
        [Tooltip("Rendered size of one frame, world units.")]
        public float worldSize = 1f;
        [Tooltip("Spawn offset along the aim direction (slash reads ahead of the player).")]
        public float forwardOffset = 0f;
        [Tooltip("World-space vertical spawn offset - lift/drop the anim relative to the pivot.")]
        public float offsetY = 0f;

        [Header("Layers (glow + afterimages, like the procedural sweep)")]
        public bool glow = false;
        public Color glowTint = new Color(1f, 0.85f, 0.53f, 1f);
        [Range(0f, 1f)] public float glowAlpha = 0.5f;
        public float glowScale = 1.25f;
        [Tooltip("Trailing afterimages - each shows the frame N steps earlier (onion-skin trail).")]
        [Range(0, 4)] public int ghostCount = 0;
        [Tooltip("How many frames behind each successive ghost lags.")]
        [Range(1, 4)] public int ghostFrameLag = 1;
        [Range(0f, 1f)] public float ghostAlphaFalloff = 0.45f;
        public Color ghostTint = new Color(1f, 1f, 1f, 0.6f);
        public SfxSlot sfx = new SfxSlot();

        public SheetFXSettings Clone() => (SheetFXSettings)MemberwiseClone();
    }

    /// <summary>Which swing art plays. Sweep (0) was culled 2026-07-27; the numbers are pinned
    /// so recipes already holding Wipe/Sheet keep meaning what they meant.</summary>
    public enum ArcVariant { Wipe = 1, Sheet = 2, Ribbon = 3 }

    /// <summary>
    /// How the swing sits on the character: the effect is parented to the player, centred on the
    /// player's ORIGIN, and rotated on Z by a single 0-360 facing angle. One continuous value
    /// replaces the old three-direction picker - the character just hands over the angle it is
    /// facing and the arc points there. See ArcWipeFX.PlayAtAngle.
    /// </summary>
    [Serializable]
    public class SwingRigSettings
    {
        [Tooltip("Local offset from the player's origin before rotating - nudge the arc off " +
                 "centre (e.g. up to chest height). Rotates WITH the swing.")]
        public Vector2 pivotOffset = Vector2.zero;
        [Tooltip("Constant added to the facing angle - corrects art whose neutral pose is not " +
                 "drawn pointing along +X.")]
        public float angleOffsetDeg = 0f;
        [Tooltip("Draw the arc behind the character rather than in front.")]
        public bool behindPlayer = false;
        [Tooltip("Sorting order when not drawn behind the player.")]
        public int sortingOrder = 50;
    }

    /// <summary>Which player swing animation the lab fires - and how the slash flash
    /// reorients for it.</summary>
    public enum SwingFacing { Swing45, Front, Back }

    /// <summary>Per-facing overrides: the slash art is authored once, each facing
    /// re-aims and re-rotates it. Dial per facing in the tune panel.</summary>
    [Serializable]
    public class SwingFacingSettings
    {
        [Tooltip("Aim for the lunge + slash placement when this facing is selected. Swing45 ignores this and aims at the focus target.")]
        public Vector2 aim = Vector2.right;
        [Tooltip("Extra slash rotation for this facing, added on top of the aim and the slash block's own angle.")]
        public float slashAngleDeg = 0f;
        [Tooltip("Mirror the slash vertically for this facing (XORs with the slash block's flip).")]
        public bool slashFlipY = false;
        [Tooltip("World-space nudge for the slash spawn position, per facing.")]
        public Vector2 slashOffset = Vector2.zero;
        [Tooltip("Sort the slash behind the player for this facing (swinging up/away).")]
        public bool slashBehindPlayer = false;
    }

    /// <summary>
    /// Swing body language on the character sprite: pull back + gather (anticipation),
    /// lunge toward the swing with a stretch, settle home. Sells momentum the frame
    /// animation alone doesn't have.
    /// </summary>
    [Serializable]
    public class LungeSettings
    {
        [Tooltip("Wind-up seconds - the gather before the push.")]
        public float anticipation = 0.07f;
        [Tooltip("Pull-back distance during wind-up, world units.")]
        public float backOffset = 0.04f;
        [Tooltip("Forward push distance at the lunge peak.")]
        public float lungeOffset = 0.1f;
        public float lungeDuration = 0.12f;
        public float settleDuration = 0.22f;
        [Tooltip("Stretch along the swing direction at the peak (wind-up squashes the same amount).")]
        [Range(0f, 0.5f)] public float stretch = 0.18f;
        [Tooltip("Lean into the swing, degrees - wind-up tilts away, lunge tilts in.")]
        [Range(0f, 25f)] public float torqueDegrees = 8f;
        [Tooltip("Delay before the swing FRAMES start, so the wind-up plays on the idle pose. Match to wind-up.")]
        public float swingAnimDelay = 0.07f;
    }

    /// <summary>
    /// A composed material-hit moment. The swing is the TOOL (shared variant choice);
    /// everything else here is what the MATERIAL answers back with at contact.
    /// </summary>
    [Serializable]
    public class HitMomentSettings
    {
        [Tooltip("Which swing-arc variant opens the hit.")]
        public ArcVariant arcVariant = ArcVariant.Sheet;
        [Tooltip("Seconds after the swing starts before contact lands.")]
        public float contactDelay = 0.14f;
        [Tooltip("Material impact sound - fill variants for repeat-hit variety.")]
        public SfxSlot sfx = new SfxSlot();
        public FlashSettings flash = new FlashSettings();
        public SquashSettings squash = new SquashSettings();
        [Tooltip("Material chips/debris - color says what you hit.")]
        public PuffSettings puff = new PuffSettings();
        [Tooltip("Pack burst at contact. Leave sheet empty to skip.")]
        public SheetFXSettings burst = new SheetFXSettings();
        public bool hitStop = true;
        public bool cameraNudge = true;
    }

    /// <summary>Occlusion fade: sprite drops to a see-through alpha while the player
    /// stands behind it (trees, roofs). Stateful - driven by OverlayFadeFX.SetFaded,
    /// no player math in the effect.</summary>
    [Serializable]
    public class OverlayFadeSettings
    {
        [Tooltip("Alpha while the player is behind - see-through, not gone.")]
        [Range(0f, 1f)] public float fadedAlpha = 0.35f;
        [Tooltip("Seconds to fade DOWN when the player steps behind. Quick - occlusion should answer fast.")]
        public float fadeOutDuration = 0.15f;
        [Tooltip("Seconds to restore when the player steps out. Slightly lazier reads softer.")]
        public float fadeInDuration = 0.3f;
        public SfxSlot sfx = new SfxSlot();
    }

    /// <summary>One ambience in the "always alive" floor: falling leaves, fireflies,
    /// pollen, steam - same component (AmbientDrifterFX), different drift/wobble/flicker.</summary>
    [Serializable]
    public class AmbientDrifterSettings
    {
        [Range(1, 24)] public int count = 8;
        [Tooltip("Mote sprites - leaves/petals. Empty = soft-dot placeholder (fine for fireflies/pollen).")]
        public Sprite[] sprites = new Sprite[0];
        public Color[] colors = new Color[0];
        [Tooltip("Spawn region size around the center, world units.")]
        public Vector2 region = new Vector2(2.5f, 1.8f);
        [Tooltip("Constant drift, world units/s. Leaves = down-left, steam = up, fireflies = ~zero.")]
        public Vector2 drift = new Vector2(-0.08f, -0.12f);
        [Tooltip("Sideways sway amplitude, world units.")]
        public float wobbleAmp = 0.05f;
        public float wobbleHz = 0.4f;
        [Tooltip("Alpha pulse depth 0-1. Fireflies high, leaves zero.")]
        [Range(0f, 1f)] public float flickerAmp = 0f;
        public float flickerHz = 1.2f;
        [Tooltip("Seconds before a mote respawns elsewhere in the region.")]
        public float life = 7f;
        public float size = 0.035f;
        [Range(0f, 1f)] public float sizeJitter = 0.4f;
        public int sortingOrder = 40;
    }

    public enum DrifterVariant { Leaves, Fireflies, Pollen, Steam }

    /// <summary>Stateful color tint: wet soil, wilt desat, scan highlight - see TintStateFX.</summary>
    [Serializable]
    public class TintStateSettings
    {
        [Tooltip("Color pulled toward while tinted (RGB only - alpha untouched).")]
        public Color tint = new Color(0.42f, 0.33f, 0.24f);   // wet-soil dark
        [Range(0f, 1f)] public float blend = 0.55f;
        public float inDuration = 0.25f;
        public float outDuration = 0.6f;
        [Tooltip("Seconds before the tint unwinds by itself (wet soil drying). 0 = stays until cleared.")]
        public float autoRevert = 0f;
        public SfxSlot sfx = new SfxSlot();

        public TintStateSettings Clone() => (TintStateSettings)MemberwiseClone();
    }

    /// <summary>Periodic wink on a sprite: solar shimmer, tool-ready, hover ack - see GlintFX.</summary>
    [Serializable]
    public class GlintSettings
    {
        public float interval = 2.5f;
        [Tooltip("Random fraction of the interval, +/-. Keeps winks from metronoming.")]
        [Range(0f, 1f)] public float intervalJitter = 0.4f;
        public Color color = Color.white;
        public float size = 0.06f;
        public float duration = 0.35f;
        public SfxSlot sfx = new SfxSlot();
    }

    /// <summary>The emotions a non-talking critter/NPC can say with one icon. Callers name
    /// the feeling; the icon that carries it is art, swapped in the settings block.</summary>
    public enum EmoteIcon { Affection, Alarm, Curious, Content, Sleep, Anger, Laugh }

    /// <summary>One emotion's art. Sprite empty = the fallback dot in the emotion's colour,
    /// so a bubble still reads while icon art is pending.</summary>
    [Serializable]
    public class EmoteIconSlot
    {
        public EmoteIcon id;
        public Sprite sprite;
        public Color tint = Color.white;
    }

    /// <summary>Critter/NPC emote pop (heart ! ? note zZz anger laugh) - see EmoteBubbleFX.</summary>
    [Serializable]
    public class EmoteSettings
    {
        [Tooltip("Art per emotion. Missing entry = fallback dot tinted by emotion.")]
        public List<EmoteIconSlot> iconSet = new List<EmoteIconSlot>();
        [Tooltip("Legacy index-picked sprites - kept for callers that still pass an int.")]
        public Sprite[] icons = new Sprite[0];
        [Tooltip("Optional bubble background behind the icon.")]
        public Sprite bubble;
        public Color iconTint = Color.white;
        public float size = 0.16f;
        [Tooltip("Height above the play position - clears the head.")]
        public float riseOffset = 0.22f;
        public float popIn = 0.12f;
        public float hold = 0.9f;
        public float popOut = 0.15f;
        [Tooltip("Pop-in overshoot scale - the squash-pop.")]
        public float overshoot = 1.15f;
        public int sortingOrder = 120;
        public SfxSlot sfx = new SfxSlot();

        public EmoteIconSlot Find(EmoteIcon id)
        {
            if (iconSet == null) return null;
            foreach (var slot in iconSet)
                if (slot != null && slot.id == id) return slot;
            return null;
        }

        public EmoteSettings Clone() => (EmoteSettings)MemberwiseClone();
    }

    /// <summary>Watering droplet arc - see DropletSprayFX.</summary>
    [Serializable]
    public class DropletSettings
    {
        [Range(1, 24)] public int count = 10;
        public Color color = new Color(
            VibeTokens.InteractPassive.r, VibeTokens.InteractPassive.g,
            VibeTokens.InteractPassive.b, 0.9f);   // water = interact-passive token
        public float speed = 1.3f;
        [Tooltip("Cone width around the aim, degrees.")]
        public float spreadDegrees = 32f;
        [Tooltip("Downward pull, world units/s^2 (negative).")]
        public float gravity = -4.5f;
        public float life = 0.55f;
        public float size = 0.03f;
        [Tooltip("Scale along the velocity - reads as falling water.")]
        public float stretch = 1.7f;
        public int sortingOrder = 60;
        public SfxSlot sfx = new SfxSlot();

        public DropletSettings Clone() => (DropletSettings)MemberwiseClone();
    }

    /// <summary>Progress-on-the-object glow (repair %, growth %) - see ProgressGlowFX.</summary>
    [Serializable]
    public class ProgressGlowSettings
    {
        [Tooltip("Warm work-in-progress color (light-warm token).")]
        public Color color = VibeTokens.LightWarm;
        [Range(0f, 1f)] public float maxAlpha = 0.4f;
        [Tooltip("Pulse depth on top of the base glow.")]
        [Range(0f, 0.5f)] public float pulseAmp = 0.15f;
        [Tooltip("Pulse speed at 0% -> 100% progress. Quickens as work nears done.")]
        public float pulseHzStart = 0.7f;
        public float pulseHzEnd = 2.2f;
    }

    /// <summary>The anti-hit: mend feedback. Deliberately no hitstop / no shake - care, not impact.</summary>
    [Serializable]
    public class RepairSettings
    {
        public FlashSettings glow = new FlashSettings
            { color = new Color(0.8f, 1f, 0.82f), peak = 0.7f, attack = 0.05f, decay = 0.3f };
        public SquashSettings bounce = new SquashSettings { punch = 0.12f, duration = 0.3f };
        public SparkleSettings motes = new SparkleSettings { count = 6 };
        public RingSettings pulse = new RingSettings
            { color = new Color(0.71f, 0.87f, 0.54f, 0.7f), endRadius = 0.5f, duration = 0.5f };
        public SfxSlot sfx = new SfxSlot();
    }

    [Serializable]
    public class DropSettings
    {
        [Tooltip("Item art. Empty = plain square placeholder.")]
        public Sprite itemSprite;
        [Tooltip("Interact-hero red - pops against the green ground. Set white when using real art.")]
        public Color itemTint = new Color(0.855f, 0.369f, 0.306f);
        public float itemSize = 0.12f;
        [Tooltip("How far the item hops away from the source.")]
        public float hopDistance = 0.4f;
        [Tooltip("Random fan around the away direction, +/- degrees - keeps multi-drops from stacking.")]
        [Range(0f, 90f)] public float spreadDegrees = 35f;
        public float hopHeight = 0.35f;
        public float hopDuration = 0.4f;
        [Tooltip("Settle bounces after landing, shrinking each time.")]
        [Range(0, 3)] public int bounces = 2;
        public PuffSettings landPuff = new PuffSettings
            { count = 4, size = 0.03f, speed = 0.5f, life = 0.4f, upBias = 0.3f };
        [Tooltip("Hand off to the idle bob after settling (the real game flow). Off = rest then fade.")]
        public bool restToIdle = true;
        [Tooltip("Seconds the item rests before fading - only used when restToIdle is off.")]
        public float restSeconds = 0.8f;
        public SfxSlot sfx = new SfxSlot();
    }

    /// <summary>
    /// Dropped-item idle: RPG ground-loot grammar. The fixed, inversely-breathing ground
    /// shadow is what makes the bob read as "sitting here" instead of "flying". Glint =
    /// periodic wink, not constant emission. Glow stays WHITE - items aren't always rare.
    /// Item sprite/tint/size come from the Drop block so the life-cycle is one look.
    /// </summary>
    [Serializable]
    public class IdleSettings
    {
        [Header("Bob")]
        [Tooltip("Base float height above the ground point.")]
        public float hoverHeight = 0.08f;
        public float bobAmplitude = 0.05f;
        public float bobPeriod = 1.8f;
        [Tooltip("Lazy rotation sway, degrees.")]
        public float swayDegrees = 2.5f;

        [Header("Ground shadow (the anchor)")]
        [Tooltip("Shadow width, world units.")]
        public float shadowSize = 0.16f;
        [Range(0f, 1f)] public float shadowAlpha = 0.25f;
        [Tooltip("Shadow height as a fraction of its width.")]
        public float shadowSquish = 0.4f;
        [Tooltip("How strongly the shadow shrinks + lightens as the item rises.")]
        [Range(0f, 1f)] public float shadowBreathe = 0.35f;

        [Header("Glint (periodic wink)")]
        public bool glint = true;
        public float glintInterval = 2.5f;
        public Color glintColor = Color.white;

        [Header("Glow (off by default; keep white if enabled - items are not always rare)")]
        public bool glow = false;
        public Color glowColor = Color.white;
        [Range(0f, 0.6f)] public float glowAlpha = 0.18f;
        [Tooltip("Glow size relative to the item sprite.")]
        public float glowScale = 1.6f;
        public float glowPulsePeriod = 2.2f;
    }

    [Serializable]
    public class PickupSettings
    {
        [Tooltip("Item art. Empty = plain square placeholder.")]
        public Sprite itemSprite;
        [Tooltip("Interact-hero red - pops against the green ground. Set white when using real art.")]
        public Color itemTint = new Color(0.855f, 0.369f, 0.306f);
        public float itemSize = 0.12f;
        [Tooltip("Anticipation hop height before the magnet pull - the wind-up that sells it.")]
        public float anticipation = 0.08f;
        public float flyDuration = 0.35f;
        [Tooltip("Magnet pull ease over the flight. Steepen the end = accelerating suck-in.")]
        public AnimationCurve flyEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Sideways arc height during the flight.")]
        public float arcHeight = 0.22f;
        public SparkleSettings absorb = new SparkleSettings { count = 5, life = 0.6f };
        [Tooltip("Receive ack: gentle uniform scale swell on the receiver - a soft 'got it', not the directional impact squish.")]
        [Range(1f, 1.3f)] public float receiverPopScale = 1.06f;
        public float receiverPopDuration = 0.25f;
        public SfxSlot sfx = new SfxSlot();
    }

    /// <summary>
    /// Stand-in art for dialing an effect on the sprite it will really play on: assign a
    /// tree here and the lab swaps the focus prop's sprite to that tree while the effect is
    /// selected, so chip colours / squash / flash are judged against the real silhouette
    /// instead of a coloured square. Purely a lab affordance - the prop restores itself.
    /// </summary>
    [Serializable]
    public class PreviewSlot
    {
        [Tooltip("Sprite to wear while this effect is selected. Empty = keep the prop's own art.")]
        public Sprite sprite;
        [Tooltip("Scale multiplier while worn - real art is rarely the prop's size.")]
        [Range(0.1f, 6f)] public float scale = 1f;
    }

    /// <summary>A preview slot bound to one primitive/moment in the fire-list (recipes carry
    /// their own slot on the recipe itself).</summary>
    [Serializable]
    public class KindPreview
    {
        public FXKind kind;
        public PreviewSlot preview = new PreviewSlot();
    }

    /// <summary>
    /// Every FXLab knob in one asset. Edits made during play mode PERSIST (ScriptableObject),
    /// so the loop is: press Play, click buttons, drag sliders until it feels right - done.
    /// The scene is disposable; this asset is the work product. Proven blocks graduate to
    /// Flynn.Feel as per-effect profiles (or FeedbackSO slots).
    /// </summary>
    [CreateAssetMenu(fileName = "FXLabTuning", menuName = "Flynn/FXLab/Tuning")]
    public class FXLabTuning : ScriptableObject
    {
        [Header("Contact stack")]
        public FlashSettings flash = new FlashSettings();
        public SquashSettings squash = new SquashSettings();
        public HitStopSettings hitStop = new HitStopSettings();
        public NudgeSettings nudge = new NudgeSettings();

        [Header("World response")]
        public PuffSettings puff = new PuffSettings();
        [Tooltip("Second puff, sprite-sheet flavoured: each mote plays a little dust animation.")]
        [FormerlySerializedAs("puff2")]
        public PuffSettings puffSheet = new PuffSettings { count = 4, size = 0.03f, speed = 0.6f, life = 0.4f, upBias = 0.3f };
        public RingSettings ring = new RingSettings();
        public SparkleSettings sparkle = new SparkleSettings();

        [Header("Swing arcs (three variants - compare, cull the losers)")]
        public ArcWipeSettings arcWipe = new ArcWipeSettings();
        public SheetFXSettings sheetSlash = new SheetFXSettings();
        public ArcRibbonSettings arcRibbon = new ArcRibbonSettings();

        [Header("Sheet burst (pack hit effects, plays at the focus prop)")]
        public SheetFXSettings sheetBurst = new SheetFXSettings();

        [Header("Material hits (swing = tool, contact = material)")]
        public HitMomentSettings hitWood = new HitMomentSettings
        {
            sfx = new SfxSlot { pitchJitter = 0.08f },
            flash = new FlashSettings { color = new Color(1f, 0.9f, 0.7f) },
            squash = new SquashSettings { punch = 0.22f },
            puff = new PuffSettings
            {
                colors = new[]
                {
                    new Color(0.54f, 0.42f, 0.28f),   // chipped wood
                    VibeTokens.GroundAccent,          // leaf
                    VibeTokens.FoliageDeep
                }
            }
        };
        public HitMomentSettings hitMetal = new HitMomentSettings
        {
            sfx = new SfxSlot { pitchJitter = 0.05f },
            flash = new FlashSettings { color = new Color(1f, 1f, 0.92f), decay = 0.1f },
            squash = new SquashSettings { punch = 0.12f, duration = 0.2f },   // metal is stiff
            puff = new PuffSettings
            {
                count = 6, speed = 1.5f, life = 0.4f, gravity = -4f, size = 0.035f,
                colors = new[]
                {
                    VibeTokens.LightWarm,             // sparks
                    VibeTokens.UiText,
                    new Color(0.6f, 0.63f, 0.68f)     // steel fleck
                }
            }
        };
        public HitMomentSettings hitStone = new HitMomentSettings
        {
            sfx = new SfxSlot { pitchJitter = 0.06f },
            flash = new FlashSettings { color = new Color(0.95f, 0.95f, 0.9f) },
            squash = new SquashSettings { punch = 0.15f },
            puff = new PuffSettings
            {
                speed = 0.9f, gravity = -5f,          // heavy grit falls fast
                colors = new[]
                {
                    new Color(0.55f, 0.57f, 0.6f),
                    new Color(0.4f, 0.42f, 0.45f),
                    VibeTokens.GroundAccent           // moss fleck
                }
            }
        };

        [Header("Swing body language (on the character)")]
        public LungeSettings swingLunge = new LungeSettings();

        [Header("Swing facing as ONE angle (arc wipe) - 0 = +X, counter-clockwise")]
        [Tooltip("Direction the character is swinging toward. Later this comes from the character; " +
                 "the lab drives it with the panel dial.")]
        [Range(0f, 360f)] public float swingAngleDeg = 0f;
        public SwingRigSettings swingRig = new SwingRigSettings();

        [Header("Swing facing (lab player anim + sheet slash re-aim)")]
        public SwingFacing swingFacing = SwingFacing.Swing45;
        public SwingFacingSettings facing45 = new SwingFacingSettings();
        public SwingFacingSettings facingFront = new SwingFacingSettings { aim = Vector2.down };
        public SwingFacingSettings facingBack = new SwingFacingSettings { aim = Vector2.up };

        [Header("Occlusion (player running behind trees/props)")]
        public OverlayFadeSettings overlayFade = new OverlayFadeSettings();

        [Header("State FX (wet soil / highlight, progress, shimmer)")]
        public TintStateSettings tintState = new TintStateSettings();
        public ProgressGlowSettings progressGlow = new ProgressGlowSettings();
        public GlintSettings glint = new GlintSettings();

        [Header("Care FX (positive aura, absorption)")]
        public FloatAuraSettings floatAura = new FloatAuraSettings();
        public ConvergeSettings converge = new ConvergeSettings();

        [Header("Spawner FX (emotes, watering)")]
        public EmoteSettings emote = new EmoteSettings();
        [Tooltip("Emotion the lab's Emote entry pops - the panel's icon row sets this.")]
        public EmoteIcon emoteIcon = EmoteIcon.Affection;
        public DropletSettings droplets = new DropletSettings();

        [Header("Ambience floor (AmbientDrifterFX variants - lab dial via Drifter entry)")]
        public DrifterVariant drifterVariant = DrifterVariant.Leaves;
        public AmbientDrifterSettings driftLeaves = new AmbientDrifterSettings
        {
            colors = new[] { VibeTokens.GroundAccent, VibeTokens.FoliageDeep, VibeTokens.Ground }
        };
        public AmbientDrifterSettings driftFireflies = new AmbientDrifterSettings
        {
            count = 6, drift = new Vector2(0.02f, 0f), wobbleAmp = 0.12f, wobbleHz = 0.25f,
            flickerAmp = 0.85f, flickerHz = 0.9f, size = 0.02f, life = 9f,
            colors = new[] { VibeTokens.LightWarm }
        };
        public AmbientDrifterSettings driftPollen = new AmbientDrifterSettings
        {
            count = 10, drift = new Vector2(0.05f, 0.02f), wobbleAmp = 0.08f,
            flickerAmp = 0.35f, flickerHz = 0.5f, size = 0.012f, life = 8f,
            colors = new[] { new Color(1f, 0.97f, 0.85f, 0.55f) }   // sunlit dust
        };
        public AmbientDrifterSettings driftSteam = new AmbientDrifterSettings
        {
            count = 5, region = new Vector2(0.25f, 0.3f), drift = new Vector2(0.03f, 0.35f),
            wobbleAmp = 0.06f, wobbleHz = 0.6f, size = 0.05f, life = 2.2f,
            colors = new[] { new Color(1f, 1f, 1f, 0.28f) }
        };

        [Header("Care + items")]
        public RepairSettings repair = new RepairSettings();
        public DropSettings drop = new DropSettings();
        public IdleSettings idle = new IdleSettings();
        public PickupSettings pickup = new PickupSettings();

        [Header("Recipes (moments as block combos - new moment = new list entry, zero code)")]
        public List<FXRecipe> recipes = new List<FXRecipe>();
        [Tooltip("Registry names already seeded from FXRecipeLibrary - keeps dialed/deleted recipes untouched.")]
        public List<string> seededNames = new List<string>();

        [Header("Preview art (dial effects on the sprite they will really play on)")]
        [Tooltip("Selecting an effect swaps the focus prop to its preview sprite (and back when it has none).")]
        public bool autoApplyPreview = true;
        [Tooltip("Per-primitive stand-in art. Recipes carry their own slot on the recipe.")]
        public List<KindPreview> kindPreviews = new List<KindPreview>();

        [Header("Global")]
        [Tooltip("Master speed for ALL effects (drives Time.timeScale in the lab). 1 = as dialed, lower = slower/softer.")]
        [Range(0.3f, 1.5f)] public float globalSpeed = 1f;

        [Header("Sfx library - the in-scene tune panel cycles through these")]
        public AudioClip[] clipLibrary = new AudioClip[0];

        /// <summary>The facing block the swing currently answers to - shared by the lab
        /// board and any SwingArcBlock, so an arc aims the same however it was fired.</summary>
        public SwingFacingSettings ActiveFacing() =>
            swingFacing == SwingFacing.Front ? facingFront :
            swingFacing == SwingFacing.Back ? facingBack : facing45;

        /// <summary>The preview slot for a fire-list entry, created on first ask so the
        /// designer has a sprite field to drop art into (the panel's "+ preview slot").</summary>
        public PreviewSlot PreviewFor(FXKind kind, bool create = false)
        {
            if (kindPreviews == null) kindPreviews = new List<KindPreview>();
            foreach (var kp in kindPreviews)
                if (kp != null && kp.kind == kind) return kp.preview;
            if (!create) return null;
            var added = new KindPreview { kind = kind };
            kindPreviews.Add(added);
            return added.preview;
        }

        public FXRecipe FindRecipe(string recipeName)
        {
            if (recipes == null || string.IsNullOrEmpty(recipeName)) return null;
            foreach (var r in recipes)
                if (r != null && string.Equals(r.name, recipeName, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        /// <summary>
        /// Break any block instance that two recipes (or two slots in one recipe) both point at.
        ///
        /// Duplicating a recipe in the inspector copies the `[SerializeReference]` REFERENCE, so
        /// the copy shares the original's blocks and editing either edits both. Nobody ever wants
        /// that, so it is repaired on load rather than left as a trap: the first holder keeps the
        /// instance, later ones get their own deep copy. Idempotent.
        /// </summary>
        int SplitSharedBlocks()
        {
            if (recipes == null) return 0;
            var seen = new HashSet<FXBlock>();   // FXBlock has no Equals override = identity
            int split = 0;
            foreach (var r in recipes)
            {
                if (r?.blocks == null) continue;
                for (int i = 0; i < r.blocks.Count; i++)
                {
                    var b = r.blocks[i];
                    if (b == null || seen.Add(b)) continue;
                    var copy = FXCopy.DeepBlock(b);
                    r.blocks[i] = copy;
                    seen.Add(copy);
                    split++;
                }
            }
            return split;
        }

        void OnEnable()
        {
            // seed each registry recipe ONCE: new library entries arrive on load,
            // dialed values are never overwritten, deletions stay deleted
            if (recipes == null) recipes = new List<FXRecipe>();
            if (seededNames == null) seededNames = new List<string>();
            foreach (var (recipeName, build) in FXRecipeLibrary.Builtin)
            {
                if (seededNames.Contains(recipeName)) continue;
                if (FindRecipe(recipeName) == null) recipes.Add(build());
                seededNames.Add(recipeName);   // pre-registry assets: existing recipe claims its name
            }

            int split = SplitSharedBlocks();
            if (split > 0)
                Debug.Log($"FXLab: {split} recipe block(s) were shared between recipes " +
                          "(inspector duplication copies the reference) - each now has its own copy.");
        }
    }
}
