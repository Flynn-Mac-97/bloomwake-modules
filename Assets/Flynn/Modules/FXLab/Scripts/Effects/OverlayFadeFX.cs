using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Occlusion fade: drops the sprite to a see-through alpha while the player stands
    /// behind it (trees, roofs, tall props). Pure STATE - no player math here: whatever
    /// detects the overlap (trigger volume, sorting system) calls SetFaded(true/false).
    /// Idempotent, tween-safe mid-transition, UnityEvent-friendly.
    /// </summary>
    public class OverlayFadeFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the parameterless SetFaded/Toggle.")]
        public FXLabTuning tuning;

        SpriteRenderer _sr;
        Tween _tween;
        float _fullAlpha = 1f;
        bool _cachedFull;
        bool _faded;

        public bool IsFaded => _faded;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void Toggle() => SetFaded(!_faded);

        /// <summary>UnityEvent-friendly: reads the tuning asset's overlayFade block.</summary>
        public void SetFaded(bool faded)
            => SetFaded(faded, tuning != null ? tuning.overlayFade : null);

        public void SetFaded(bool faded, OverlayFadeSettings s)
        {
            if (s == null || faded == _faded) return;
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;
            _faded = faded;

            // resting alpha remembered from the first un-faded state (props may not be 1)
            if (!_cachedFull)
            {
                _fullAlpha = _sr.color.a;
                _cachedFull = true;
            }

            FXAudio.Play(s.sfx, transform.position);
            float from = _sr.color.a;
            float to = faded ? s.fadedAlpha : _fullAlpha;
            float dur = faded ? s.fadeOutDuration : s.fadeInDuration;

            if (_tween.isAlive) _tween.Stop();
            _tween = Tween.Custom(from, to, Mathf.Max(0.01f, dur), onValueChange: a =>
            {
                if (_sr == null) return;
                var c = _sr.color;
                c.a = a;
                _sr.color = c;
            });
        }
    }
}
