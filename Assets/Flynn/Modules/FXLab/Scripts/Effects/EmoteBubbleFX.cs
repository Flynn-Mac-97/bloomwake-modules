using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Critter/NPC emote pop: an icon squash-pops in above a head, holds with a tiny bob,
    /// shrinks away. How a thing that cannot talk says what it feels. Spawner - one instance
    /// serves the scene: PlayAt(position, EmoteIcon). Callers name the EMOTION; which sprite
    /// carries it is art, assigned in the settings block. No art yet = a dot in the emotion's
    /// colour, so the six feelings stay distinguishable while dialing.
    /// </summary>
    public class EmoteBubbleFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the short PlayAt overload.")]
        public FXLabTuning tuning;

        /// <summary>Fallback colours: warm for affection, alarm red, curious blue, content
        /// green, sleep lilac, anger orange-red, laugh yellow. Art replaces these.</summary>
        static readonly Color[] FallbackTints =
        {
            new Color(1f, 0.45f, 0.55f),      // Affection
            new Color(1f, 0.85f, 0.3f),       // Alarm
            new Color(0.55f, 0.8f, 1f),       // Curious
            new Color(0.68f, 0.9f, 0.6f),     // Content
            new Color(0.75f, 0.7f, 0.95f),    // Sleep
            new Color(1f, 0.42f, 0.25f),      // Anger
            new Color(1f, 0.93f, 0.5f)        // Laugh
        };

        public void PlayAt(Vector3 pos, EmoteIcon icon)
            => PlayAt(pos, icon, tuning != null ? tuning.emote : null);

        public void PlayAt(Vector3 pos, EmoteIcon icon, EmoteSettings s)
        {
            if (s == null) return;
            var art = s.Find(icon);
            Color tint = art != null && art.sprite != null
                ? art.tint * s.iconTint
                : FallbackTints[Mathf.Clamp((int)icon, 0, FallbackTints.Length - 1)];
            Pop(pos, art != null ? art.sprite : null, tint, s);
        }

        public void PlayAt(Vector3 pos, int iconIndex)
            => PlayAt(pos, iconIndex, tuning != null ? tuning.emote : null);

        public void PlayAt(Vector3 pos, int iconIndex, EmoteSettings s)
        {
            if (s == null) return;
            Sprite art = s.icons != null && s.icons.Length > 0
                ? s.icons[Mathf.Clamp(iconIndex, 0, s.icons.Length - 1)] : null;
            Pop(pos, art, s.iconTint, s);
        }

        void Pop(Vector3 pos, Sprite art, Color iconTint, EmoteSettings s)
        {
            FXAudio.Play(s.sfx, pos);

            var root = new GameObject("Emote");
            root.transform.position = pos + Vector3.up * s.riseOffset;

            SpriteRenderer bubSr = null;
            if (s.bubble != null)
            {
                bubSr = new GameObject("Bubble").AddComponent<SpriteRenderer>();
                bubSr.transform.SetParent(root.transform, false);
                bubSr.sprite = s.bubble;
                bubSr.color = Color.white;
                bubSr.sortingOrder = s.sortingOrder;
            }

            var icon = new GameObject("Icon").AddComponent<SpriteRenderer>();
            icon.transform.SetParent(root.transform, false);
            icon.sprite = art != null ? art : FXSprites.SoftCircle;
            icon.color = iconTint;
            icon.sortingOrder = s.sortingOrder + 1;

            float total = s.popIn + s.hold + s.popOut;
            Vector3 basePos = root.transform.position;
            Tween.Custom(0f, total, total, onValueChange: t =>
            {
                if (root == null) return;
                float scale;
                float alpha = 1f;
                if (t < s.popIn)
                {
                    // overshoot in - the squash-pop
                    float n = t / Mathf.Max(0.01f, s.popIn);
                    scale = Mathf.Lerp(0f, s.overshoot, 1f - (1f - n) * (1f - n));
                }
                else if (t < s.popIn + s.hold)
                {
                    float n = (t - s.popIn) / Mathf.Max(0.01f, s.hold);
                    scale = Mathf.Lerp(s.overshoot, 1f, Mathf.Min(1f, n * 4f));
                    root.transform.position = basePos
                        + Vector3.up * (Mathf.Sin(n * Mathf.PI * 2f) * 0.015f);   // tiny bob
                }
                else
                {
                    // toon exit: a small anticipation stretch, THEN collapse away while fading.
                    // Shrinking straight to nothing reads as a bug; the little wind-up first
                    // reads as the thought being withdrawn.
                    float n = Mathf.Clamp01((t - s.popIn - s.hold) / Mathf.Max(0.01f, s.popOut));
                    const float anticipation = 0.25f;   // fraction of popOut spent stretching
                    float peak = 1f + (s.overshoot - 1f) * 0.6f;
                    scale = n < anticipation
                        ? Mathf.Lerp(1f, peak, n / anticipation)
                        : Mathf.Lerp(peak, 0f, Mathf.Pow((n - anticipation) / (1f - anticipation), 2f));
                    alpha = 1f - Mathf.Pow(n, 1.5f);   // fade trails the shrink slightly
                }
                root.transform.localScale = Vector3.one * (s.size * scale);
                Fade(icon, iconTint, alpha);
                Fade(bubSr, Color.white, alpha);
            });
            Destroy(root, total + 0.1f);
        }

        static void Fade(SpriteRenderer sr, Color baseCol, float alpha)
        {
            if (sr == null) return;
            sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * alpha);
        }
    }
}
