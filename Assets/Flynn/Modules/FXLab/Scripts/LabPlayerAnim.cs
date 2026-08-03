using System.Collections;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Minimal lab-only player animator: holds the idle pose (or loops it), plays the
    /// swing one-shot when the board fires any swing effect, then falls back to idle.
    /// Facing-aware: each SwingFacing has its own idle + swing frame set, so all three
    /// swing directions can be dialed against the real character art. Exists so FX can
    /// be judged without dragging in the Critter module's animation stack (module
    /// boundary - we borrow its sprite assets only).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class LabPlayerAnim : MonoBehaviour
    {
        [Header("45 (default)")]
        public Sprite[] idleFrames;
        public Sprite[] swingFrames;
        [Header("Front (swing down)")]
        public Sprite[] idleFrontFrames;
        public Sprite[] swingFrontFrames;
        [Header("Back (swing up)")]
        public Sprite[] idleBackFrames;
        public Sprite[] swingBackFrames;

        [Tooltip("Off = hold the first idle frame and let Breather do the sway (current preference).")]
        public bool loopIdle = false;
        public float idleFps = 18f;
        public float swingFps = 24f;

        SpriteRenderer _sr;
        SwingFacing _facing = SwingFacing.Swing45;
        float _idleT;
        bool _swinging;
        float _swingT;
        Coroutine _pending;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        Sprite[] IdleSet =>
            _facing == SwingFacing.Front && idleFrontFrames != null && idleFrontFrames.Length > 0 ? idleFrontFrames :
            _facing == SwingFacing.Back && idleBackFrames != null && idleBackFrames.Length > 0 ? idleBackFrames :
            idleFrames;

        Sprite[] SwingSet =>
            _facing == SwingFacing.Front && swingFrontFrames != null && swingFrontFrames.Length > 0 ? swingFrontFrames :
            _facing == SwingFacing.Back && swingBackFrames != null && swingBackFrames.Length > 0 ? swingBackFrames :
            swingFrames;

        /// <summary>Turn the idle pose without swinging (facing selector in the panel).</summary>
        public void SetFacing(SwingFacing facing) => _facing = facing;

        /// <param name="delay">Seconds to hold the idle pose first — lets the lunge
        /// wind-up read before the swing frames fire.</param>
        public void PlaySwing(float delay = 0f, SwingFacing facing = SwingFacing.Swing45)
        {
            _facing = facing;
            var set = SwingSet;
            if (set == null || set.Length == 0) return;
            if (_pending != null) StopCoroutine(_pending);
            if (delay <= 0f) { StartSwing(); return; }
            _pending = StartCoroutine(DelayedSwing(delay));
        }

        IEnumerator DelayedSwing(float delay)
        {
            yield return new WaitForSeconds(delay);
            _pending = null;
            StartSwing();
        }

        void StartSwing()
        {
            _swinging = true;
            _swingT = 0f;
        }

        void Update()
        {
            if (_sr == null) return;

            if (_swinging)
            {
                var swing = SwingSet;
                _swingT += Time.deltaTime;
                int i = (int)(_swingT * Mathf.Max(1f, swingFps));
                if (swing != null && i < swing.Length)
                {
                    if (swing[i] != null) _sr.sprite = swing[i];
                    return;
                }
                _swinging = false;   // swing finished - resume idle
                _idleT = 0f;
            }

            var idle = IdleSet;
            if (idle == null || idle.Length == 0) return;
            if (!loopIdle)
            {
                if (idle[0] != null && _sr.sprite != idle[0]) _sr.sprite = idle[0];
                return;
            }
            _idleT += Time.deltaTime;
            int idx = (int)(_idleT * Mathf.Max(1f, idleFps)) % idle.Length;
            if (idle[idx] != null) _sr.sprite = idle[idx];
        }
    }
}
