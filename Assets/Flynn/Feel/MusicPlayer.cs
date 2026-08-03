using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Loops a <see cref="SoundSO"/>'s trimmed REGION as background music.
    ///
    /// WHY THIS EXISTS: SoundSO.Play hands the AudioSource its own loop flag, and an AudioSource
    /// loops the WHOLE clip - it knows nothing about the trim. Slot a 60-second slice of a
    /// six-minute track into that and it plays your slice once, then repeats the entire track from
    /// the top. So the region has to be re-armed by hand each time round.
    ///
    /// Two sources, alternating: the next pass is scheduled on the idle one BEFORE the current
    /// pass ends, so the wrap is sample-accurate instead of the click you get from seeking a
    /// single source at the loop point.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [Tooltip("The trimmed sound asset. Its startTime/endTime region is what loops.")]
        public SoundSO music;
        [Tooltip("Start as soon as the scene runs.")]
        public bool playOnEnable = true;
        [Tooltip("Scales the asset's own level, so one track can sit lower in one scene than another.")]
        [Range(0f, 1f)] public float volume = 0.5f;
        [Tooltip("Seconds to ease up from silence. Music snapping in at full level reads as a glitch.")]
        public float fadeInSeconds = 1.5f;

        AudioSource _a, _b;
        bool _useA = true;
        double _nextStart;
        bool _running;
        float _fade = 1f;

        void Awake()
        {
            _a = NewSource();
            _b = NewSource();
        }

        AudioSource NewSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;          // the region loop is ours, not the source's
            src.spatialBlend = 0f;     // music is never positional
            return src;
        }

        void OnEnable() { if (playOnEnable) Play(); }
        void OnDisable() { Stop(); }

        public void Play()
        {
            if (music == null || music.clip == null) return;
            music.Prime();             // decode up front so the first bar doesn't hitch
            _running = true;
            _fade = fadeInSeconds > 0.01f ? 0f : 1f;
            _nextStart = AudioSettings.dspTime + 0.15;   // a beat of headroom to arm the first pass
            Schedule();
            Schedule();                // and keep one queued behind it
        }

        public void Stop()
        {
            _running = false;
            if (_a != null) _a.Stop();
            if (_b != null) _b.Stop();
        }

        /// <summary>Arm the idle source to play one pass of the region at <see cref="_nextStart"/>.</summary>
        void Schedule()
        {
            var src = _useA ? _a : _b;
            _useA = !_useA;

            src.clip = music.clip;
            src.pitch = Mathf.Max(0.01f, music.pitch);
            if (music.output != null) src.outputAudioMixerGroup = music.output;
            src.volume = Level();
            src.timeSamples = StartSample;

            double dur = RegionSeconds;
            src.PlayScheduled(_nextStart);
            src.SetScheduledEndTime(_nextStart + dur);
            _nextStart += dur;
        }

        void Update()
        {
            if (!_running || music == null) return;

            // unscaled: a hitstop must not stall the music fade
            if (_fade < 1f && fadeInSeconds > 0.01f)
                _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / fadeInSeconds);

            float v = Level();
            if (_a != null) _a.volume = v;
            if (_b != null) _b.volume = v;

            // queue the following pass a second before the current one runs out
            if (AudioSettings.dspTime > _nextStart - 1.0) Schedule();
        }

        float Level() => Mathf.Clamp01(music.volume * volume * _fade);

        double RegionSeconds =>
            Mathf.Max(0.05f, music.Duration) / Mathf.Max(0.01f, music.pitch);

        int StartSample
        {
            get
            {
                float start = Mathf.Clamp(music.startTime, 0f, music.ClipLength);
                return Mathf.Clamp(Mathf.RoundToInt(start * music.clip.frequency),
                    0, Mathf.Max(0, music.clip.samples - 1));
            }
        }
    }
}
