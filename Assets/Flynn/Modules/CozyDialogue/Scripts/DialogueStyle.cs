using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// ALL dialogue-box tuning (SO-first). The box builds its own UI from these numbers at
    /// runtime — no scene anchor wiring, tune everything live here. Colors come from VibeTokens
    /// in code; only the rounded panel sprite is assigned by hand.
    ///
    /// Lives in the CozyDialogue module; namespace Flynn.Feel = graduation candidate.
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueStyle", menuName = "Flynn/Feel/Dialogue Style")]
    public class DialogueStyle : ScriptableObject
    {
        [Header("Panel (canvas units @1920x1080)")]
        [Tooltip("Rounded 9-slice sprite for panel + name plate (builtin 'UISprite' is fine).")]
        public Sprite panelSprite;
        public float panelWidth = 1100f;
        public float panelHeight = 230f;
        [Tooltip("Gap between panel bottom and screen bottom.")]
        public float bottomMargin = 48f;
        public float padding = 28f;

        [Header("Type roles (VibeSpec §10)")]
        public float nameSize = 24f;
        public float bodySize = 30f;

        [Header("Typewriter (the cozy 'spoken' feel)")]
        [Tooltip("Base reveal speed, characters per second.")]
        public float charsPerSecond = 30f;
        [Tooltip("Pause multiplier after . ! ? — makes text breathe like speech.")]
        public float sentencePauseMult = 6f;
        [Tooltip("Pause multiplier after , ; : — small breath.")]
        public float commaPauseMult = 3f;

        [Header("Speech blips (Animal-Crossing-style voice)")]
        [Tooltip("Short soft clip; played per revealed character.")]
        public AudioClip blip;
        [Tooltip("Blip every Nth visible character (2 = classic).")]
        public int blipEveryN = 2;
        [Range(0f, 0.3f)]
        [Tooltip("Random pitch wobble around 1.0 — gives the voice life.")]
        public float blipPitchJitter = 0.1f;
        [Range(0f, 0.4f)]
        [Tooltip("Informational-tier volume cap 0.4 (VibeSpec §8).")]
        public float blipVolume = 0.3f;

        [Header("Motion (VibeSpec §12: appear=OutBack, exit=InQuad)")]
        public float appearDuration = 0.25f;
        public float exitDuration = 0.2f;
        [Tooltip("Panel scale dip on each advance press (input ack, Contract §6).")]
        public float pressDip = 0.98f;

        [Header("Continue arrow")]
        [Tooltip("Bob amplitude in canvas units.")]
        public float arrowBobAmplitude = 7f;
        public float arrowBobPeriod = 0.9f;
    }
}
