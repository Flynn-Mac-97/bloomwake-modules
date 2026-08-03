using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Progress-on-the-object: a warm additive-feel overlay that brightens and pulses
    /// faster as work completes (repair %, growth %). The Feedback Contract "progress"
    /// answer for sustained verbs. SetProgress(0..1) drives it; 0 clears. Overlay is a
    /// child copying the target sprite, so flip/scale/sort follow automatically.
    /// </summary>
    public class ProgressGlowFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for SetProgress.")]
        public FXLabTuning tuning;

        SpriteRenderer _sr;
        SpriteRenderer _overlay;
        float _progress;
        float _t;

        public float Progress => _progress;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void Clear() => SetProgress(0f);

        /// <summary>UnityEvent-friendly: reads the tuning asset's progressGlow block.</summary>
        public void SetProgress(float p)
            => SetProgress(p, tuning != null ? tuning.progressGlow : null);

        public void SetProgress(float p, ProgressGlowSettings s)
        {
            _progress = Mathf.Clamp01(p);
            if (s == null) return;
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;

            if (_progress <= 0f)
            {
                if (_overlay != null) _overlay.enabled = false;
                return;
            }

            if (_overlay == null)
            {
                var go = new GameObject("ProgressGlow");
                go.transform.SetParent(transform, false);
                _overlay = go.AddComponent<SpriteRenderer>();
            }
            _overlay.enabled = true;
        }

        void Update()
        {
            if (_overlay == null || !_overlay.enabled || _sr == null) return;
            var s = tuning != null ? tuning.progressGlow : null;
            if (s == null) return;

            _t += Time.deltaTime;
            _overlay.sprite = _sr.sprite;                       // follow anims/flips
            _overlay.flipX = _sr.flipX;
            _overlay.flipY = _sr.flipY;
            _overlay.sortingLayerID = _sr.sortingLayerID;
            _overlay.sortingOrder = _sr.sortingOrder + 1;

            float hz = Mathf.Lerp(s.pulseHzStart, s.pulseHzEnd, _progress);
            float pulse = 1f + s.pulseAmp * Mathf.Sin(_t * hz * Mathf.PI * 2f);
            var c = s.color;
            c.a = s.maxAlpha * _progress * pulse;
            _overlay.color = c;
        }
    }
}
