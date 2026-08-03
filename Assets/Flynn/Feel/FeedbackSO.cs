using PrimeTween;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Feedback hierarchy tier (VibeSpec §8 bounds the treatment per tier;
    /// FEEDBACK_CONTRACT.md says which moments need which tier). Critical is reserved — unused
    /// in pure cozy.
    /// </summary>
    public enum FeedbackTier { Ambient, Informational, Action, Success, Milestone }

    /// <summary>
    /// Data-driven "juice" asset. Holds a small stack of cozy feedback effects and plays them
    /// on a target Transform. Tune every value in the inspector by feel — no code changes.
    ///
    /// Motion is PrimeTween-driven (zero-alloc, destroy-safe) — no host coroutine needed.
    ///
    /// GRADUATED 2026-07-12 from the Mend module into the shared Flynn.Feel assembly — modules
    /// reference "Flynn.Feel" in their asmdef to use it (kept separate from Flynn.Runtime so
    /// modules don't inherit legacy core deps).
    /// </summary>
    [CreateAssetMenu(fileName = "Feedback", menuName = "Flynn/Feel/Feedback")]
    public class FeedbackSO : ScriptableObject
    {
        [Tooltip("Hierarchy tier — VibeSpec §8 bounds punch/duration/sfx per tier. " +
                 "Success requires sfx + ≥2 channels; Milestone requires sfx + ≥3.")]
        public FeedbackTier tier = FeedbackTier.Action;

        [Header("Scale punch (squash / bounce)")]
        [Tooltip("Extra scale at the peak of the punch, as a fraction of the object's base scale.")]
        public float scalePunch = 0.25f;
        [Tooltip("Seconds for the punch to rise to peak and settle back.")]
        public float punchDuration = 0.35f;
        [Tooltip("Shapes the punch. Left = start/end, right = peak.")]
        public AnimationCurve punchCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Particles (optional)")]
        [Tooltip("Spawned once at the target position, auto-destroyed after 3s.")]
        public GameObject particles;

        [Header("Sound (optional)")]
        public AudioClip sfx;
        [Range(0f, 1f)] public float sfxVolume = 1f;

        /// <summary>Play every configured feedback on <paramref name="target"/>.</summary>
        public void Play(Transform target)
        {
            if (target == null) return;

            if (sfx != null)
                AudioSource.PlayClipAtPoint(sfx, target.position, sfxVolume);

            if (particles != null)
            {
                var fx = Instantiate(particles, target.position, Quaternion.identity);
                Destroy(fx, 3f);
            }

            if (scalePunch > 0f && punchDuration > 0f)
            {
                Vector3 baseScale = target.localScale;
                Vector3 peakScale = baseScale * (1f + scalePunch * VibeTokens.MotionScale);
                // Drive a 0->1->0 punch factor; PrimeTween owns the lifetime (allocation-free).
                Tween.Custom(0f, 1f, punchDuration, onValueChange: t =>
                {
                    if (target == null) return;
                    float k = Mathf.Sin(t * Mathf.PI);              // 0 -> 1 -> 0
                    float eased = punchCurve.Evaluate(k);
                    target.localScale = Vector3.LerpUnclamped(baseScale, peakScale, eased);
                });
            }
        }
    }
}
