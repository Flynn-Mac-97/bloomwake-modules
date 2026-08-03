using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// VibeSpec motion floor: "nothing statue-still." Tiny idle scale-sway so sprites read alive.
    /// Random phase per instance so a scene never pulses in sync. Defaults come from VibeTokens;
    /// tune per-object only when a thing should feel notably calmer/livelier.
    /// </summary>
    public class Breather : MonoBehaviour
    {
        [Tooltip("Scale sway as a fraction of base scale. VibeSpec default 0.02 (2%).")]
        public float amplitude = VibeTokens.BreatheAmplitude;
        [Tooltip("Seconds per breath. VibeSpec default 3.")]
        public float period = VibeTokens.BreathePeriod;
        [Tooltip("Sway Y more than X (squash-like breathing) when true; uniform when false.")]
        public bool squashy = true;

        Vector3 _base;
        float _phase;

        void Awake()
        {
            _base = transform.localScale;
            _phase = Random.value * Mathf.PI * 2f;
        }

        void OnDisable() => transform.localScale = _base;

        /// <summary>Adopt the object's current scale as the new resting size. Anything that
        /// legitimately resizes the object has to call this - the breather writes localScale
        /// every frame from the size captured at Awake, so otherwise it overwrites the change
        /// on the next Update and the resize looks like it never happened.</summary>
        public void Rebase() => _base = transform.localScale;

        void Update()
        {
            float s = Mathf.Sin((Time.time / Mathf.Max(0.01f, period)) * Mathf.PI * 2f + _phase)
                      * amplitude * VibeTokens.MotionScale;
            transform.localScale = squashy
                ? new Vector3(_base.x * (1f - s * 0.5f), _base.y * (1f + s), _base.z)
                : _base * (1f + s);
        }
    }
}
