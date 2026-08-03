using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Volume-preserving directional squash: X widens while Y shortens, then settles back.
    /// Always lerps from the base scale captured at Awake, so hammering the button can't
    /// drift the object's size.
    /// </summary>
    public class SquashFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        Vector3 _baseScale;
        Tween _tween;
        Flynn.Feel.Breather _breather;

        void Awake()
        {
            _baseScale = transform.localScale;
            _breather = GetComponent<Flynn.Feel.Breather>();
        }

        public void Play()
        {
            if (tuning != null) Play(tuning.squash);
        }

        /// <summary>Adopt the object's current scale as the base to squash from. Needed when
        /// something else resizes the object after Awake (the lab's preview art), otherwise
        /// the first squash snaps it back to the size it had at startup.</summary>
        public void Rebase()
        {
            if (_tween.isAlive) _tween.Stop();
            _baseScale = transform.localScale;
        }

        /// <summary>Squash with an explicit settings block (composed moments pass their own).</summary>
        public void Play(SquashSettings s)
        {
            if (s == null) return;

            if (_tween.isAlive) _tween.Stop();
            // Breather also writes localScale every frame — pause it for the punch
            if (_breather != null) _breather.enabled = false;
            FXAudio.Play(s.sfx, transform.position);

            // Scaling moves the sprite about its PIVOT. Art pivoted at the centre therefore
            // squashes about its middle and its base lifts/sinks; measuring the pivot-to-bottom
            // gap lets us push the transform back down so the thing stays standing on the
            // ground. Sprites already pivoted at the base measure 0 and are unaffected.
            var basePos = transform.position;
            float footGap = 0f;
            if (s.anchorBottom)
            {
                var sr = GetComponentInChildren<SpriteRenderer>();
                if (sr != null) footGap = sr.bounds.min.y - basePos.y;
            }
            _popBase = s.anchorBottom ? (Vector3?)basePos : null;

            _tween = Tween.Custom(0f, 1f, s.duration, onValueChange: t =>
            {
                if (this == null) return;
                float k = s.curve.Evaluate(Mathf.Sin(t * Mathf.PI)) * s.punch * Flynn.Feel.VibeTokens.MotionScale;
                transform.localScale = new Vector3(
                    _baseScale.x * (1f + k),
                    _baseScale.y * (1f - k * 0.7f),
                    _baseScale.z);
                // the squash shortens Y by k*0.7, so the bottom edge rises by that fraction
                if (footGap != 0f)
                    transform.position = basePos + Vector3.up * (footGap * (k * 0.7f));
            }, ease: Ease.Linear).OnComplete(() =>
            {
                if (this == null) return;
                if (footGap != 0f) transform.position = basePos;
                _popBase = null;
                if (_breather != null) _breather.enabled = true;
            });
        }

        /// <summary>
        /// Gentle uniform swell (receive ack) - the whole sprite breathes up and settles,
        /// no directional squish. Anchored at the sprite's bottom edge so the feet stay
        /// planted while the body swells upward.
        /// </summary>
        public void Pop(float scale, float duration)
        {
            if (_tween.isAlive) _tween.Stop();
            if (_breather != null) _breather.enabled = false;

            var basePos = transform.position;
            _popBase = basePos;
            // pivot-to-feet distance: growing by f lifts the bottom by off*(f-1), so
            // shift the transform up by the same amount to keep the feet on the ground
            float off = 0f;
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) off = sr.bounds.min.y - basePos.y;

            float amp = (scale - 1f) * Flynn.Feel.VibeTokens.MotionScale;
            _tween = Tween.Custom(0f, 1f, Mathf.Max(0.05f, duration), onValueChange: t =>
            {
                if (this == null) return;
                float f = 1f + Mathf.Sin(t * Mathf.PI) * amp;
                transform.localScale = _baseScale * f;
                transform.position = basePos + Vector3.up * (off * (1f - f));
            }, ease: Ease.Linear).OnComplete(() =>
            {
                if (this == null) return;
                transform.position = basePos;
                _popBase = null;
                if (_breather != null) _breather.enabled = true;
            });
        }

        Vector3? _popBase;

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            transform.localScale = _baseScale == Vector3.zero ? transform.localScale : _baseScale;
            if (_popBase.HasValue) { transform.position = _popBase.Value; _popBase = null; }
            if (_breather != null) _breather.enabled = true;
        }
    }
}
