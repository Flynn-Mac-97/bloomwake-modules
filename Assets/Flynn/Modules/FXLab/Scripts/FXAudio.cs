using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// One-shot sfx for the block/effect layer.
    ///
    /// Every play gets its OWN throwaway AudioSource, so stacked layers genuinely mix - a new
    /// sound never cuts off the one before it. What DOES ruin a stack is sameness: identical
    /// clips starting on the same frame sum into one louder copy of themselves instead of a
    /// blend. Two things fight that here - the slot's own `delay` for deliberate staggering,
    /// and a sub-frame scatter applied automatically so simultaneous layers never sit in
    /// perfect phase.
    ///
    /// A slotted <see cref="SoundSO"/> wins over the raw clip and brings its own level, pitch,
    /// jitter, region and mixer routing; the slot's volume then scales it.
    /// </summary>
    public static class FXAudio
    {
        /// <summary>Largest automatic scatter, seconds. Small enough to stay "the same moment",
        /// big enough to stop identical layers phase-locking.</summary>
        const float MaxScatter = 0.012f;

        public static void Play(SfxSlot slot, Vector3 position)
        {
            if (slot == null) return;

            float wait = Mathf.Max(0f, slot.delay) + Random.Range(0f, MaxScatter);

            if (slot.UsesSound)
            {
                PlaySound(slot, position, wait);
                return;
            }

            var clip = slot.Pick();
            if (clip == null) return;

            var go = new GameObject("FXAudio_" + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = slot.volume;
            src.pitch = 1f + Random.Range(-slot.pitchJitter, slot.pitchJitter);
            src.spatialBlend = 0f;

            // trimmed window: start into the clip, hard-stop at the out point
            float start = Mathf.Clamp01(slot.trimStart) * clip.length;
            float end = Mathf.Clamp01(slot.trimEnd) * clip.length;
            if (end <= start) end = clip.length;
            src.time = start;
            if (wait > 0f) src.PlayDelayed(wait);
            else src.Play();
            Object.Destroy(go, wait + (end - start) / Mathf.Max(0.1f, src.pitch) + 0.05f);
        }

        /// <summary>Play the slot's SoundSO, scaled by the slot's volume and staggered by its
        /// delay. The SoundSO owns everything else - that is the point of authoring one.</summary>
        static void PlaySound(SfxSlot slot, Vector3 position, float wait)
        {
            var src = slot.sound.PlayAtPoint(position, wait);
            if (src != null) src.volume *= Mathf.Clamp01(slot.volume);
        }
    }
}
