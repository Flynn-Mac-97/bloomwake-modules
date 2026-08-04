using TMPro;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// ALL player-HUD tuning (SO-first). The HUD builds its own UI from these numbers at
    /// runtime — no scene anchor wiring, tune everything live here. Colors come from VibeTokens
    /// in code; only the two builtin sprites are assigned by hand.
    ///
    /// Lives in the PlayerHUD module; namespace Flynn.Feel = graduation candidate.
    /// </summary>
    [CreateAssetMenu(fileName = "HUDStyle", menuName = "Flynn/Feel/HUD Style")]
    public class HUDStyle : ScriptableObject
    {
        [Header("Sprites (builtin: UISprite + Knob)")]
        [Tooltip("Rounded 9-slice for slot frames / bars / placeholder icons.")]
        public Sprite panelSprite;
        [Tooltip("Soft circle — the battery sun icon.")]
        public Sprite circleSprite;

        [Header("Parchment skin (matches CozyDialogue theme.uss)")]
        [Tooltip("Page face for HUD numerals + hotkeys. Same family as the dialogue box (Alegreya). " +
                 "Null = TMP default.")]
        public TMP_FontAsset font;
        [Tooltip("Rule weight on slot borders. Higher = thinner hairline (Image pixelsPerUnitMultiplier).")]
        [Range(0.5f, 6f)] public float ruleWeight = 2.2f;
        [Tooltip("How far an empty pocket recedes. The tool loop ignores this — it stays legible " +
                 "while empty, because 'no tool yet' is a state the player must be able to read.")]
        [Range(0f, 1f)] public float emptySlotAlpha = 0.45f;

        [Header("Bars layout (canvas units @1920x1080)")]
        public float slotSize = 72f;
        public float slotGap = 10f;
        [Tooltip("Main action bar slots, hotkeys 1..N (max 9).")]
        public int mainSlots = 6;
        [Tooltip("Tool bar slots, hotkeys Z/X/C (max 3). Tools pin here and never move.")]
        public int toolSlots = 1;
        public float barBottomMargin = 28f;
        [Tooltip("Gap between the tool bar and the main bar.")]
        public float barGapBetween = 30f;
        public float hotkeySize = 18f;
        public float countSize = 20f;
        [Tooltip("Icon inset inside the slot frame.")]
        public float iconInset = 10f;

        [Header("Battery (top-left sun meter)")]
        public float batteryWidth = 280f;
        public float batteryHeight = 24f;
        public float batteryMargin = 28f;
        [Range(0f, 1f)] public float batteryStart = 0.7f;
        [Tooltip("Auto-fill per second while charging.")]
        public float chargeRate = 0.12f;
        [Tooltip("Below this the meter goes sleepy (droop + slow breathe) — never an alarm.")]
        [Range(0f, 0.5f)] public float lowThreshold = 0.2f;
        [Tooltip("Seconds the white chip-ghost waits before following a drain down.")]
        public float chipLagDelay = 0.35f;
        public float chipLagDuration = 0.5f;
        [Tooltip("Charging glow breath period, seconds.")]
        public float chargePulsePeriod = 1.6f;

        [Header("Motion (VibeSpec §12 bands; all amplitudes × MotionScale)")]
        [Tooltip("Ghost-icon flight time (pickup / drop / move).")]
        public float flyDuration = 0.45f;
        [Tooltip("Arc height of the flight, canvas units.")]
        public float flyArcHeight = 90f;
        [Tooltip("Slot squash-pop on a landed icon, fraction of base scale.")]
        public float landPunch = 0.22f;
        public float punchDuration = 0.3f;
        [Tooltip("Gentle no-wiggle on refused moves, canvas units.")]
        public float refuseShakeAmplitude = 9f;
        public float refuseShakeDuration = 0.35f;
        [Tooltip("Dragged ghost lift scale (grab ack, Contract §6).")]
        public float dragLiftScale = 1.12f;

        [Header("Audio (Dustyroom placeholders; VibeSpec §8 volume caps)")]
        public AudioClip pickupSfx;
        public AudioClip dropSfx;
        public AudioClip grabSfx;
        public AudioClip placeSfx;
        public AudioClip refuseSfx;
        public AudioClip selectSfx;
        [Tooltip("Battery gain tick.")]
        public AudioClip gainTickSfx;
        [Tooltip("Battery reaches full — Success tier, always plays.")]
        public AudioClip fullChimeSfx;
        [Range(0f, 0.7f)] public float actionVolume = 0.55f;
        [Range(0f, 0.4f)] public float infoVolume = 0.3f;
        [Range(0f, 0.3f)] public float pitchJitter = 0.08f;
        [Tooltip("Spam guard — same-frame bursts share one sound (Contract §16).")]
        public float sfxMinInterval = 0.07f;
    }
}
