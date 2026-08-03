using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Stateful color tint toward a target color: wet soil after watering, wilt desat,
    /// scan highlight, low-power dim - one component, different settings blocks.
    /// SetTinted(true/false) tweens the SpriteRenderer color (RGB only - alpha stays
    /// with OverlayFadeFX); autoRevert &gt; 0 unwinds by itself (wet soil drying).
    /// </summary>
    public class TintStateFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the parameterless SetTinted/Toggle.")]
        public FXLabTuning tuning;

        SpriteRenderer _sr;
        Tween _tween;
        Tween _revert;
        Color _resting;
        bool _cachedResting;
        bool _tinted;

        public bool IsTinted => _tinted;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void Toggle() => SetTinted(!_tinted);

        /// <summary>UnityEvent-friendly: reads the tuning asset's tintState block.</summary>
        public void SetTinted(bool tinted)
            => SetTinted(tinted, tuning != null ? tuning.tintState : null);

        public void SetTinted(bool tinted, TintStateSettings s)
        {
            if (s == null || tinted == _tinted) return;
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;
            _tinted = tinted;

            if (!_cachedResting)
            {
                _resting = _sr.color;
                _cachedResting = true;
            }

            if (tinted) FXAudio.Play(s.sfx, transform.position);

            // tint RGB toward the target, keep the resting alpha channel
            Color from = _sr.color;
            Color rgb = Color.Lerp(_resting, s.tint, s.blend);
            Color to = tinted ? new Color(rgb.r, rgb.g, rgb.b, _resting.a) : _resting;
            float dur = tinted ? s.inDuration : s.outDuration;

            if (_tween.isAlive) _tween.Stop();
            if (_revert.isAlive) _revert.Stop();
            _tween = Tween.Custom(0f, 1f, Mathf.Max(0.01f, dur), onValueChange: t =>
            {
                if (_sr != null) _sr.color = Color.Lerp(from, to, t);
            });

            if (tinted && s.autoRevert > 0f)
                _revert = Tween.Delay(s.autoRevert, () => SetTinted(false, s));
        }
    }
}
