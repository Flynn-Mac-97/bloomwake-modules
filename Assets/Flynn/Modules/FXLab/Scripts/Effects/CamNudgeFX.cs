using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Tiny camera kick: damped-sine position offset plus a fraction of a degree of roll.
    /// The rotation component is what makes it read as force instead of a glitch; the
    /// amplitude stays cozy-small. Lives on the camera. The kick is applied as a
    /// self-removing offset every LateUpdate (undo last frame's, add this frame's), so
    /// follow scripts that move the camera never fight it - static or moving camera alike.
    /// </summary>
    public class CamNudgeFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        Vector3 _applied;      // offset currently baked into the transform
        float _appliedRoll;
        Vector3 _want;         // offset the tween asks for this frame
        float _wantRoll;
        Tween _tween;

        /// <param name="dir">Push direction of the impact; the camera kicks along it.</param>
        public void Play(Vector2 dir)
        {
            if (tuning == null) return;
            Play(dir, tuning.nudge);
        }

        /// <summary>Nudge with an explicit settings block.</summary>
        public void Play(Vector2 dir, NudgeSettings s)
        {
            if (s == null) return;
            if (_tween.isAlive) _tween.Stop();
            if (dir.sqrMagnitude < 0.001f) dir = Random.insideUnitCircle.normalized;
            Vector2 d = dir.normalized;
            float roll = (Random.value < 0.5f ? -1f : 1f) * s.rotationDeg;

            _tween = Tween.Custom(0f, 1f, s.duration, onValueChange: t =>
            {
                if (this == null) return;
                float damp = 1f - t;
                float wave = Mathf.Sin(t * s.duration * s.frequency * Mathf.PI * 2f) * damp;
                float amp = s.amplitude * Flynn.Feel.VibeTokens.MotionScale;
                _want = (Vector3)(d * (wave * amp));
                _wantRoll = wave * roll;
            }, ease: Ease.Linear).OnComplete(() =>
            {
                if (this == null) return;
                _want = Vector3.zero;
                _wantRoll = 0f;
            });
        }

        void LateUpdate()
        {
            if (_applied == _want && Mathf.Approximately(_appliedRoll, _wantRoll)) return;
            transform.position += _want - _applied;
            transform.localRotation *= Quaternion.Euler(0f, 0f, _wantRoll - _appliedRoll);
            _applied = _want;
            _appliedRoll = _wantRoll;
        }

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            transform.position -= _applied;
            transform.localRotation *= Quaternion.Euler(0f, 0f, -_appliedRoll);
            _applied = _want = Vector3.zero;
            _appliedRoll = _wantRoll = 0f;
        }
    }
}
