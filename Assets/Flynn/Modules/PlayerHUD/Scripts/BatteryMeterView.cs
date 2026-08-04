using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace Flynn.Feel
{
    /// <summary>
    /// The sun-battery meter (top-left). Reads as care, not danger: drains show a lagging
    /// white chip-ghost (you can see what you just spent), gains glow warm, charging breathes
    /// and slowly spins the sun, low battery goes sleepy (droop + dim) — never an alarm.
    /// Built at runtime by <see cref="PlayerHUD"/>; PlayerHUD owns the value + events,
    /// this view only expresses it.
    /// </summary>
    public class BatteryMeterView : MonoBehaviour
    {
        HUDStyle _style;
        RectTransform _rt, _iconRt;
        Image _icon, _fill, _chip, _flash;
        Vector3 _baseScale;
        float _shown;          // fill currently displayed
        float _chipShown;
        bool _charging;
        bool _low;
        Tween _fillTween, _chipTween;

        // ── Construction ────────────────────────────────────────────────────

        public static BatteryMeterView Create(HUDStyle style, Transform canvasRoot)
        {
            var go = new GameObject("BatteryMeter", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            var view = go.AddComponent<BatteryMeterView>();
            view._style = style;
            view.Build();
            return view;
        }

        void Build()
        {
            float h = _style.batteryHeight;
            float iconSize = h * 1.9f;

            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0f, 1f);
            _rt.pivot = new Vector2(0f, 1f);
            _rt.sizeDelta = new Vector2(iconSize + 10f + _style.batteryWidth, iconSize);
            _rt.anchoredPosition = new Vector2(_style.batteryMargin, -_style.batteryMargin);
            _baseScale = Vector3.one;

            // Sun icon — warm circle, spins while charging, droops when sleepy.
            _iconRt = NewRect("Sun", _rt, new Vector2(iconSize, iconSize));
            _iconRt.anchorMin = _iconRt.anchorMax = new Vector2(0f, 0.5f);
            _iconRt.anchoredPosition = new Vector2(iconSize * 0.5f, 0f);
            _icon = _iconRt.gameObject.AddComponent<Image>();
            _icon.sprite = _style.circleSprite;
            _icon.color = VibeTokens.LightWarm;

            // Bar background.
            var bgRt = NewRect("BarBg", _rt, new Vector2(_style.batteryWidth, h));
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 0.5f);
            bgRt.pivot = new Vector2(0f, 0.5f);
            bgRt.anchoredPosition = new Vector2(iconSize + 10f, 0f);
            var bg = bgRt.gameObject.AddComponent<Image>();
            bg.sprite = _style.panelSprite;
            bg.type = Image.Type.Sliced;
            bg.color = VibeTokens.UiParchmentDeep;

            // Chip ghost (behind the fill): lags drains so spend is readable (Contract §9).
            _chip = NewFilled("Chip", bgRt, new Color(1f, 1f, 1f, 0.45f));
            // Live fill: cool→warm with charge level.
            _fill = NewFilled("Fill", bgRt, VibeTokens.LightWarm);
            // Full-charge flash overlay.
            _flash = NewFilled("Flash", bgRt, new Color(1f, 1f, 1f, 0f));
            _flash.fillAmount = 1f;

            Set(_style.batteryStart, instant: true);
        }

        Image NewFilled(string n, RectTransform parent, Color color)
        {
            var rt = NewRect(n, parent, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _style.panelSprite;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform NewRect(string n, Transform parent, Vector2 size)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            return rt;
        }

        // ── Value display ───────────────────────────────────────────────────

        /// <summary>Tweened set — drains leave the chip lagging, gains pull it up instantly.</summary>
        public void Set(float value, bool instant = false)
        {
            value = Mathf.Clamp01(value);
            bool gain = value >= _shown;

            _fillTween.Stop();
            _chipTween.Stop();

            if (instant)
            {
                _shown = _chipShown = value;
                Apply();
                return;
            }

            float from = _shown;
            _fillTween = Tween.Custom(from, value, VibeTokens.DurQuick, ease: Ease.OutQuad,
                onValueChange: v => { _shown = v; Apply(); });

            if (gain)
            {
                _chipShown = value;   // chip never trails a gain, only a spend
            }
            else
            {
                float chipFrom = _chipShown;
                _chipTween = Tween.Custom(0f, 1f,
                    _style.chipLagDelay + _style.chipLagDuration, onValueChange: t =>
                    {
                        if (this == null) return;
                        float span = _style.chipLagDelay + _style.chipLagDuration;
                        float k = Mathf.InverseLerp(_style.chipLagDelay, span, t * span);
                        _chipShown = Mathf.Lerp(chipFrom, value, Mathf.SmoothStep(0f, 1f, k));
                        Apply();
                    });
            }
        }

        /// <summary>Per-frame set while charging — no tween churn.</summary>
        public void SetImmediate(float value)
        {
            _shown = _chipShown = Mathf.Clamp01(value);
            Apply();
        }

        void Apply()
        {
            if (_fill == null) return;
            _fill.fillAmount = _shown;
            _fill.color = Color.Lerp(VibeTokens.LightCool, VibeTokens.LightWarm, _shown);
            _chip.fillAmount = Mathf.Max(_chipShown, _shown);
            _low = _shown < _style.lowThreshold;
        }

        public void SetCharging(bool on) => _charging = on;

        /// <summary>Full charge — Success tier: overshoot bounce + warm flash.</summary>
        public void PlayFull()
        {
            Tween.Custom(0f, 1f, VibeTokens.DurStandard, onValueChange: t =>
            {
                if (this == null) return;
                float k = Mathf.Sin(t * Mathf.PI);
                transform.localScale = _baseScale * (1f + 0.12f * VibeTokens.MotionScale * k);
            });
            Tween.Custom(0.85f, 0f, VibeTokens.DurSlow, ease: Ease.OutQuad, onValueChange: a =>
            {
                if (_flash == null) return;
                var c = _flash.color; c.a = a; _flash.color = c;
            });
        }

        // ── Idle expression (charging breath / sleepy droop) ────────────────

        void Update()
        {
            if (_icon == null) return;

            if (_charging)
            {
                // Warm breath + slow sun spin — progress you can feel without reading numbers.
                float breath = Mathf.Sin(Time.time / Mathf.Max(0.1f, _style.chargePulsePeriod)
                                          * Mathf.PI * 2f) * 0.5f + 0.5f;
                _iconRt.localScale = Vector3.one *
                    (1f + 0.1f * VibeTokens.MotionScale * breath);
                _iconRt.Rotate(0f, 0f, 25f * Time.deltaTime);
                _icon.color = Color.Lerp(VibeTokens.LightWarm, Color.white, breath * 0.35f);
                var f = _fill.color; f.a = 0.85f + 0.15f * breath; _fill.color = f;
            }
            else if (_low)
            {
                // Sleepy, not scary: slow breathe, gentle droop, dimmed sun.
                float breath = Mathf.Sin(Time.time / (VibeTokens.BreathePeriod * 1.5f)
                                          * Mathf.PI * 2f) * 0.5f + 0.5f;
                _iconRt.localScale = Vector3.one *
                    (1f - 0.04f * VibeTokens.MotionScale * breath);
                _iconRt.localRotation = Quaternion.Lerp(_iconRt.localRotation,
                    Quaternion.Euler(0f, 0f, -18f), Time.deltaTime * 2f);
                _icon.color = Color.Lerp(_icon.color,
                    VibeTokens.LightWarm * new Color(1f, 1f, 1f, 0.55f), Time.deltaTime * 2f);
            }
            else
            {
                _iconRt.localScale = Vector3.Lerp(_iconRt.localScale, Vector3.one,
                    Time.deltaTime * 4f);
                _iconRt.localRotation = Quaternion.Lerp(_iconRt.localRotation,
                    Quaternion.identity, Time.deltaTime * 4f);
                _icon.color = Color.Lerp(_icon.color, VibeTokens.LightWarm, Time.deltaTime * 4f);
            }
        }
    }
}
