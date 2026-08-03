using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Swing body language: anticipation (pull back + gather-squash), lunge (push toward
    /// the swing direction + stretch along it), settle (ease home). Runs on the character
    /// root so the arc pivots ride the lunge with it. Pauses a co-located Breather while
    /// active (both write scale).
    /// </summary>
    public class SwingLungeFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        Vector3 _baseScale;
        Quaternion _baseRot;
        Vector3 _posOffset;   // what this lunge has currently added to localPosition
        Flynn.Feel.Breather _breather;
        Tween _tween;

        void Awake()
        {
            _breather = GetComponent<Flynn.Feel.Breather>();
        }

        public void Play(Vector2 dir)
        {
            if (tuning == null) return;
            Play(dir, tuning.swingLunge);
        }

        /// <summary>Lunge with an explicit settings block.</summary>
        public void Play(Vector2 dir, LungeSettings s)
        {
            if (s == null) return;
            if (_tween.isAlive) _tween.Stop();
            ClearOffset();
            // captured per SWING, not once at Awake: the character walks, and a base cached at
            // startup drags them back to their spawn point every time they swing
            _baseScale = transform.localScale;
            _baseRot = transform.localRotation;
            if (_breather != null) _breather.enabled = false;

            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            dir = dir.normalized;
            float side = dir.x >= 0f ? 1f : -1f;   // which way "into the swing" leans
            // stretch splits across axes by how much of the swing points along each
            float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y);
            float sum = Mathf.Max(0.01f, ax + ay);
            ax /= sum; ay /= sum;

            float antic = Mathf.Max(0.01f, s.anticipation);
            float lunge = Mathf.Max(0.01f, s.lungeDuration);
            float settle = Mathf.Max(0.01f, s.settleDuration);
            float total = antic + lunge + settle;

            _tween = Tween.Custom(0f, 1f, total, onValueChange: t =>
            {
                if (this == null) return;
                float sec = t * total;
                float off, k;   // k: -1 = gathered wind-up ... +1 = full stretch

                if (sec < antic)
                {
                    float p = sec / antic;
                    off = -s.backOffset * Mathf.Sin(p * Mathf.PI * 0.5f);
                    k = -p;
                }
                else if (sec < antic + lunge)
                {
                    float p = (sec - antic) / lunge;
                    float e = 1f - (1f - p) * (1f - p);   // ease-out: the push is front-loaded
                    off = Mathf.Lerp(-s.backOffset, s.lungeOffset, e);
                    k = Mathf.Sin(p * Mathf.PI * 0.5f);
                }
                else
                {
                    float p = (sec - antic - lunge) / settle;
                    float e = p * p * (3f - 2f * p);      // smoothstep home
                    off = Mathf.Lerp(s.lungeOffset, 0f, e);
                    k = 1f - e;
                }

                ApplyOffset((Vector3)(dir * off));
                // torque: wind-up leans away (k negative), lunge leans in
                transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, -side * s.torqueDegrees * k);
                float st = s.stretch * k * Flynn.Feel.VibeTokens.MotionScale;
                transform.localScale = new Vector3(
                    _baseScale.x * (1f + st * ax - st * 0.7f * ay),
                    _baseScale.y * (1f + st * ay - st * 0.7f * ax),
                    _baseScale.z);
            }, ease: Ease.Linear).OnComplete(Restore);
        }

        /// <summary>Move by the CHANGE since the last frame, never to an absolute point. An
        /// absolute write owns the transform outright, which freezes a walking character for the
        /// length of the lunge and snaps them back to wherever the base was captured.</summary>
        void ApplyOffset(Vector3 want)
        {
            transform.localPosition += want - _posOffset;
            _posOffset = want;
        }

        /// <summary>Hand the transform back exactly as we found it, wherever it has since moved to.</summary>
        void ClearOffset()
        {
            if (_posOffset == Vector3.zero) return;
            transform.localPosition -= _posOffset;
            _posOffset = Vector3.zero;
        }

        void Restore()
        {
            if (this == null) return;
            ClearOffset();
            transform.localScale = _baseScale;
            transform.localRotation = _baseRot;
            if (_breather != null) _breather.enabled = true;
        }

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            if (_baseScale != Vector3.zero) Restore();
        }
    }
}
