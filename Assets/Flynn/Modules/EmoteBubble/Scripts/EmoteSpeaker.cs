using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Gives any actor a voice: a world-attached emote bubble above its head. One bubble per
    /// speaker, replace-don't-stack, min interval (Contract §16 aggregation). All no-arg /
    /// string-arg methods are UnityEvent-friendly, so other modules trigger emotes through
    /// inspector wiring — zero asmdef coupling, both modules stay cullable.
    /// </summary>
    public class EmoteSpeaker : MonoBehaviour
    {
        [Tooltip("Shared emote tuning (timings, tones, stock vocalizations).")]
        public EmoteProfile profile;
        [Tooltip("Bubble prefab (EmoteBubble on the root).")]
        public EmoteBubble bubblePrefab;
        [Tooltip("Height above this transform where the bubble sits (world units). " +
                 "~0.9 for player-sized, ~0.55 for small critters.")]
        public float anchorHeight = 0.7f;

        EmoteBubble _bubble;
        float _lastSay = -999f;

        // ── UnityEvent-friendly API ─────────────────────────────────────────

        /// <summary>Short neutral line (≤5 words — VibeSpec §14).</summary>
        public void Say(string text) => Show(text, profile != null ? profile.neutral : default);

        /// <summary>Warm/happy line.</summary>
        public void SayHappy(string text) => Show(text, profile != null ? profile.happy : default);

        /// <summary>Startled vocalization ("eek!!").</summary>
        public void Eek() { if (profile != null) Show(profile.eekText, profile.alarmed); }

        /// <summary>Affection ("<3").</summary>
        public void Heart() { if (profile != null) Show(profile.heartText, profile.happy); }

        /// <summary>Sleepy/content ("zZz").</summary>
        public void Sleep() { if (profile != null) Show(profile.sleepText, profile.muted); }

        /// <summary>Curious ("?").</summary>
        public void Curious() { if (profile != null) Show(profile.curiousText, profile.neutral); }

        // ── Internals ───────────────────────────────────────────────────────

        void Show(string text, EmoteProfile.ToneStyle tone)
        {
            if (profile == null || bubblePrefab == null || string.IsNullOrEmpty(text)) return;
            if (Time.time - _lastSay < profile.minInterval) return;   // spam rule
            _lastSay = Time.time;

            if (_bubble == null)
            {
                _bubble = Instantiate(bubblePrefab, transform);
                _bubble.transform.localPosition = new Vector3(0f, anchorHeight, 0f);
            }
            _bubble.Show(text, tone.textColor, profile.popIn, profile.hold, profile.fadeOut);

            if (tone.sfx != null)
                AudioSource.PlayClipAtPoint(tone.sfx, transform.position, profile.sfxVolume);
        }
    }
}
