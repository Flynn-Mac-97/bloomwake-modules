using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Periodic white wink on a sprite - solar panel shimmer, tool-ready glint,
    /// hover ack. Wink() = one flash at a random point on the sprite's upper half;
    /// SetLooping(true) repeats on an interval with jitter. Generalized from the
    /// ItemIdle glint.
    /// </summary>
    public class GlintFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the parameterless Wink/Toggle.")]
        public FXLabTuning tuning;

        SpriteRenderer _sr;
        bool _looping;
        float _next;

        public bool IsLooping => _looping;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void ToggleLoop()
        {
            SetLooping(!_looping);
            if (_looping) Wink();   // immediate ack, then the interval takes over
        }

        public void SetLooping(bool on)
        {
            _looping = on;
            _next = NextDelay(tuning != null ? tuning.glint : null);
        }

        /// <summary>UnityEvent-friendly: reads the tuning asset's glint block.</summary>
        public void Wink() => Wink(tuning != null ? tuning.glint : null);

        public void Wink(GlintSettings s)
        {
            if (s == null) return;
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();

            // random point on the sprite, biased to the upper half (light catches tops)
            Vector3 pos = transform.position;
            if (_sr != null)
            {
                var b = _sr.bounds;
                pos = new Vector3(
                    Random.Range(b.min.x, b.max.x),
                    Random.Range(b.center.y, b.max.y), b.center.z);
            }

            FXAudio.Play(s.sfx, pos);

            var go = new GameObject("Glint");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FXSprites.SoftCircle;
            sr.color = s.color;
            sr.sortingOrder = (_sr != null ? _sr.sortingOrder : 0) + 1;

            float size = s.size;
            Tween.Custom(0f, 1f, Mathf.Max(0.05f, s.duration), onValueChange: t =>
            {
                if (go == null) return;
                // swell then vanish - a wink, not a lamp
                float pulse = Mathf.Sin(t * Mathf.PI);
                go.transform.localScale = Vector3.one * (size * pulse);
                var c = s.color;
                c.a *= pulse;
                sr.color = c;
            });
            Destroy(go, s.duration + 0.1f);
        }

        float NextDelay(GlintSettings s)
        {
            if (s == null) return 2.5f;
            return s.interval * (1f + Random.Range(-s.intervalJitter, s.intervalJitter));
        }

        void Update()
        {
            if (!_looping) return;
            _next -= Time.deltaTime;
            if (_next <= 0f)
            {
                Wink();
                _next = NextDelay(tuning != null ? tuning.glint : null);
            }
        }
    }
}
