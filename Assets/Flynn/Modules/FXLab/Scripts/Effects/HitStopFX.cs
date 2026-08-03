using System.Collections;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Micro time-freeze at contact — sells resistance. Scaled PrimeTween tweens freeze
    /// with it (that's the point: a flash held at peak during the stop reads as an anime
    /// impact frame). Coroutine on unscaled time, so the stop always ends.
    /// </summary>
    public class HitStopFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        Coroutine _running;

        /// <summary>True while a stop is holding the clock - the board leaves timeScale alone then.</summary>
        public bool IsStopping { get; private set; }

        public void Play()
        {
            if (tuning == null) return;
            Play(tuning.hitStop);
        }

        /// <summary>Stop with an explicit settings block. Restores to the tuning's
        /// globalSpeed when a tuning asset is assigned, else to 1.</summary>
        public void Play(HitStopSettings s)
        {
            if (s == null) return;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Stop(s));
        }

        IEnumerator Stop(HitStopSettings s)
        {
            IsStopping = true;
            Time.timeScale = s.timeScale;
            yield return new WaitForSecondsRealtime(s.duration);
            // restore to the lab's master speed, not hard 1
            Time.timeScale = tuning != null ? tuning.globalSpeed : 1f;
            IsStopping = false;
            _running = null;
        }

        void OnDisable()
        {
            // never leave the editor stuck in slow motion
            Time.timeScale = 1f;
            IsStopping = false;
        }
    }
}
