using UnityEngine;
using UnityEngine.Audio;

namespace Flynn.Feel
{
    /// <summary>
    /// Data-driven sound asset — the game's general audio container: sfx, hit-variant sheets, and
    /// music all live in a SoundSO. Holds a clip, the standard AudioSource knobs, optional random
    /// jitter, and a sub-sample REGION (start/end) so a clip with dead air, several hits, or a
    /// music intro/loop baked in can be trimmed to just the part you want — no file editing. Pick
    /// the region on the waveform in the custom inspector (Flynn.Feel.Editor.SoundSOEditor).
    ///
    /// Shared infra in Flynn.Feel (like FeedbackSO) — reuse anywhere: fire it from a UnityEvent
    /// via <see cref="SoundPlayer"/>, or one-shot at a point with <see cref="PlayAtPoint"/>.
    /// (Multi-section support — many named regions per clip, random/named pick — is a later packet.)
    /// </summary>
    [CreateAssetMenu(fileName = "Sound", menuName = "Flynn/Feel/Sound")]
    public class SoundSO : ScriptableObject
    {
        [Tooltip("Source clip.")]
        public AudioClip clip;

        [Header("Level")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random ± added to volume each play (0 = none).")]
        [Range(0f, 0.5f)] public float volumeJitter = 0f;

        [Header("Pitch")]
        [Range(0.1f, 3f)] public float pitch = 1f;
        [Tooltip("Random ± added to pitch each play — kills the machine-gun sameness on repeats.")]
        [Range(0f, 0.5f)] public float pitchJitter = 0f;

        [Header("Routing")]
        public bool loop = false;
        [Tooltip("0 = 2D (UI/character sfx), 1 = fully 3D positional.")]
        [Range(0f, 1f)] public float spatialBlend = 0f;
        [Tooltip("Optional mixer group.")]
        public AudioMixerGroup output;

        [Header("Region (sub-sample) — seconds")]
        [Tooltip("Playback starts here. Set on the waveform in the inspector.")]
        public float startTime = 0f;
        [Tooltip("Playback ends here. <= 0 or beyond the clip = play to the end.")]
        public float endTime = -1f;

        /// <summary>Clip length in seconds (0 if none).</summary>
        public float ClipLength => clip != null ? clip.length : 0f;

        /// <summary>End time clamped to the clip; treats <=0 / overshoot as "to the end".</summary>
        public float EndTimeSafe =>
            (endTime <= 0f || endTime > ClipLength) ? ClipLength : endTime;

        /// <summary>Region duration in seconds (unscaled by pitch).</summary>
        public float Duration => Mathf.Max(0f, EndTimeSafe - Mathf.Clamp(startTime, 0f, ClipLength));

        /// <summary>Configure the given source for this sound and play the region on it (mono /
        /// non-overlapping). <paramref name="delay"/> holds the start back by N seconds - used to
        /// stagger layered sounds so a stack reads as a blend instead of one louder hit.</summary>
        public void Play(AudioSource src, float delay = 0f)
        {
            if (clip == null || src == null) return;

            src.clip = clip;
            src.loop = loop;
            src.spatialBlend = spatialBlend;
            if (output != null) src.outputAudioMixerGroup = output;
            src.volume = Mathf.Clamp01(volume + Random.Range(-volumeJitter, volumeJitter));
            src.pitch = Mathf.Max(0.01f, pitch + Random.Range(-pitchJitter, pitchJitter));

            int startSample = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(startTime, 0f, ClipLength) * clip.frequency),
                0, Mathf.Max(0, clip.samples - 1));
            src.timeSamples = startSample;

            // Play() is lower-latency than PlayScheduled(now) — no dropped first buffer.
            // PlayDelayed keeps the timeSamples set above, so the region start survives.
            if (delay > 0f) src.PlayDelayed(delay);
            else src.Play();
            if (!loop && Duration > 0.0001f)   // stop at region end, pushed out by the delay
                src.SetScheduledEndTime(AudioSettings.dspTime + delay + Duration / src.pitch);
        }

        /// <summary>Decode the clip's audio data now so the first real play doesn't hitch.</summary>
        public void Prime()
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
        }

        /// <summary>One-shot the sound at a world position on a throwaway AudioSource
        /// (auto-destroyed). Each call gets its own source, so overlapping plays layer.</summary>
        public AudioSource PlayAtPoint(Vector3 pos, float delay = 0f)
        {
            if (clip == null) return null;
            var go = new GameObject($"Sound_{name}");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            Play(src, delay);
            float life = delay
                + (loop ? clip.length : Mathf.Max(0.05f, Duration)) / Mathf.Max(0.01f, src.pitch) + 0.1f;
            Destroy(go, life);
            return src;
        }
    }
}
