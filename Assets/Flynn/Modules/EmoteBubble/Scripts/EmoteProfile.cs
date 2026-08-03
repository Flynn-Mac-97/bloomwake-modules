using System;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// ALL emote-bubble tuning (SO-first). Tone styles map to VibeSpec tokens — text color +
    /// sound per tone; the bubble surface is always ui-surface (set in code, not here).
    /// Tier: Informational (VibeSpec §8) — sfx volume stays ≤ 0.4.
    ///
    /// Lives in the EmoteBubble module; namespace Flynn.Feel = graduation candidate
    /// (same pattern FeedbackSO followed).
    /// </summary>
    [CreateAssetMenu(fileName = "EmoteProfile", menuName = "Flynn/Feel/Emote Profile")]
    public class EmoteProfile : ScriptableObject
    {
        [Serializable]
        public struct ToneStyle
        {
            [Tooltip("Text color — a VibeSpec token value.")]
            public Color textColor;
            [Tooltip("Optional short sound for this tone (Informational tier: quiet).")]
            public AudioClip sfx;
        }

        [Header("Timing (VibeSpec §12 bands)")]
        [Tooltip("Pop-in seconds (appear = OutBack).")]
        public float popIn = VibeTokens.DurQuick;
        [Tooltip("Seconds the bubble stays before fading.")]
        public float hold = 2f;
        [Tooltip("Shrink-out seconds (exit = InQuad).")]
        public float fadeOut = 0.3f;
        [Tooltip("Spam rule (Contract §16): a speaker won't emote more often than this.")]
        public float minInterval = 0.4f;

        [Header("Sound")]
        [Range(0f, 0.4f)]
        [Tooltip("Informational-tier volume cap is 0.4 (VibeSpec §8).")]
        public float sfxVolume = 0.35f;

        [Header("Tones (colors = VibeSpec tokens)")]
        public ToneStyle neutral = new ToneStyle { textColor = new Color(0.875f, 1f, 1f) };          // ui-text
        public ToneStyle happy   = new ToneStyle { textColor = new Color(1f, 0.847f, 0.529f) };      // char-player warm
        public ToneStyle alarmed = new ToneStyle { textColor = new Color(0.855f, 0.369f, 0.306f) };  // interact-hero
        public ToneStyle muted   = new ToneStyle { textColor = new Color(0.875f, 1f, 1f, 0.6f) };    // ui-text-muted

        [Header("Stock vocalizations (character voice — glyphs as text for prototype)")]
        public string eekText = "eek!!";
        public string heartText = "<3";
        public string sleepText = "zZz";
        public string curiousText = "?";
    }
}
