using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Thin adapter: plays a <see cref="SoundSO"/> on a local AudioSource. Wire the no-code
    /// way — a UnityEvent (e.g. PlayerMover.onFootstep / onJump) calls <see cref="Play"/> and you
    /// drag the sound .asset into the event's object slot. Reusable on anything that needs audio.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundPlayer : MonoBehaviour
    {
        [Tooltip("Source the sounds play through. Auto-binds to the AudioSource on this object.")]
        public AudioSource source;

        [Tooltip("Sounds to warm up at start so the FIRST play doesn't hitch. List the ones fired here.")]
        public SoundSO[] warmup;

        void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
        }

        void Start()
        {
            if (source == null || warmup == null) return;

            foreach (var sound in warmup)
                if (sound != null) sound.Prime();       // decode clip data up front

            // Allocate the DSP voice once, silently, so the first audible play is instant.
            float v = source.volume;
            source.volume = 0f;
            foreach (var sound in warmup)
            {
                if (sound == null || sound.clip == null) continue;
                source.clip = sound.clip;
                source.Play();
                source.Stop();
            }
            source.volume = v;
        }

        /// <summary>UnityEvent target: fire this sound. Assign the sound asset in the event slot.</summary>
        public void Play(SoundSO sound)
        {
            if (sound != null && source != null) sound.Play(source);
        }
    }
}
