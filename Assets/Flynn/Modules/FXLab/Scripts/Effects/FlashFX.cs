using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Shader-driven hit flash on this object's SpriteRenderer, via MaterialPropertyBlock —
    /// no material instancing, works on final art (not just tinted primitives). Renderer
    /// must use the Flynn/FXLab/SpriteFlash material (the scene builder assigns it).
    /// </summary>
    public class FlashFX : MonoBehaviour
    {
        static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
        static readonly int FlashColor = Shader.PropertyToID("_FlashColor");

        public FXLabTuning tuning;

        SpriteRenderer _sr;
        MaterialPropertyBlock _mpb;
        Tween _tween;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <param name="impactFrame">True = snap to peak instantly and hold-decay — the classic
        /// frozen impact frame when combined with hitstop.</param>
        public void Play(bool impactFrame = false)
        {
            if (tuning != null) Play(tuning.flash, impactFrame);
        }

        /// <summary>Flash with an explicit settings block (composed moments pass their own).</summary>
        public void Play(FlashSettings s, bool impactFrame = false)
        {
            if (_sr == null || s == null) return;

            if (_tween.isAlive) _tween.Stop();
            FXAudio.Play(s.sfx, transform.position);

            float attack = impactFrame ? 0f : Mathf.Max(0f, s.attack);
            float total = attack + Mathf.Max(0.01f, s.decay);

            _tween = Tween.Custom(0f, 1f, total, onValueChange: t =>
            {
                if (_sr == null) return;
                float sec = t * total;
                float amt = sec < attack
                    ? s.peak * (sec / attack)
                    : s.peak * (1f - (sec - attack) / Mathf.Max(0.01f, s.decay));
                Apply(Mathf.Clamp01(amt), s.color);
            }, ease: Ease.Linear);
        }

        void Apply(float amount, Color color)
        {
            _sr.GetPropertyBlock(_mpb);   // preserves the per-renderer _MainTex
            _mpb.SetFloat(FlashAmount, amount);
            _mpb.SetColor(FlashColor, color);
            _sr.SetPropertyBlock(_mpb);
        }

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            if (_sr != null) Apply(0f, Color.white);
        }
    }
}
