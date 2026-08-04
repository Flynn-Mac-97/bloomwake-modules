using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Flynn.Feel
{
    /// <summary>
    /// One action-bar slot: frame + selection ring + placeholder icon (token-tinted rounded
    /// square + initial letter) + stack count + hotkey label. Owns its own micro-feel
    /// (hover lift, land punch, refuse wiggle); drag gestures are forwarded to PlayerHUD.
    /// Built entirely at runtime by <see cref="PlayerHUD"/>.
    /// </summary>
    public class HUDSlotView : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public bool isToolBar;
        public int index;

        PlayerHUD _hud;
        HUDStyle _style;
        RectTransform _rt, _body;
        Image _ring, _icon, _fill, _border;
        TextMeshProUGUI _letter, _count;
        Vector2 _basePos;
        Vector3 _baseScale;

        // Parchment roles: a pocket is page-coloured, the tool loop is the deepest paper with the
        // heavy rule — it is the one slot that has to read as "something belongs here" when empty.
        Color FillColor   => isToolBar ? VibeTokens.UiParchmentDeep : VibeTokens.UiParchment;
        Color RuleColor   => isToolBar ? VibeTokens.UiRuleDeep      : VibeTokens.UiRule;
        static Color InkMuted => new Color(VibeTokens.UiInk.r, VibeTokens.UiInk.g, VibeTokens.UiInk.b, 0.55f);

        public RectTransform Rect => _rt;

        // ── Construction ────────────────────────────────────────────────────

        public static HUDSlotView Create(PlayerHUD hud, HUDStyle style, Transform parent,
            bool tool, int index, string hotkey, Vector2 anchoredPos)
        {
            var go = new GameObject((tool ? "ToolSlot" : "Slot") + index, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<HUDSlotView>();
            view._hud = hud;
            view._style = style;
            view.isToolBar = tool;
            view.index = index;
            view.Build(hotkey, anchoredPos);
            return view;
        }

        void Build(string hotkey, Vector2 anchoredPos)
        {
            float s = _style.slotSize;
            _rt = (RectTransform)transform;
            _rt.sizeDelta = new Vector2(s, s);
            _rt.anchorMin = _rt.anchorMax = new Vector2(0f, 0.5f);
            _rt.anchoredPosition = anchoredPos;
            _basePos = anchoredPos;
            _baseScale = Vector3.one;

            // Selection ring — border-only sliced frame, slightly larger, sprout accent.
            var ringRt = NewChild("Ring", _rt, s + 12f);
            _ring = ringRt.gameObject.AddComponent<Image>();
            _ring.sprite = _style.panelSprite;
            _ring.type = Image.Type.Sliced;
            _ring.fillCenter = false;
            _ring.color = VibeTokens.UiSprout;
            _ring.raycastTarget = false;
            ringRt.gameObject.SetActive(false);

            // Body — hover lift scales this, land punch scales the root.
            // Two stacked images: parchment fill, then a border-only pass for the tan rule.
            // uGUI has no border-color, so the rule is its own sliced Image with fillCenter off.
            _body = NewChild("Body", _rt, s);
            _fill = _body.gameObject.AddComponent<Image>();
            _fill.sprite = _style.panelSprite;
            _fill.type = Image.Type.Sliced;
            _fill.color = FillColor;

            var borderRt = NewChild("Rule", _body, s);
            _border = borderRt.gameObject.AddComponent<Image>();
            _border.sprite = _style.panelSprite;
            _border.type = Image.Type.Sliced;
            _border.fillCenter = false;
            _border.pixelsPerUnitMultiplier = _style.ruleWeight;
            _border.color = RuleColor;
            _border.raycastTarget = false;

            var iconRt = NewChild("Icon", _body, s - _style.iconInset * 2f);
            _icon = iconRt.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;

            _letter = NewText("Letter", iconRt, _style.slotSize * 0.4f,
                VibeTokens.UiInk, TextAlignmentOptions.Center);
            _letter.fontStyle = FontStyles.Bold;
            Fill(_letter.rectTransform);

            _count = NewText("Count", _body, _style.countSize,
                VibeTokens.UiInk, TextAlignmentOptions.BottomRight);
            _count.fontStyle = FontStyles.Bold;
            Fill(_count.rectTransform, 4f);

            var hotkeyText = NewText("Hotkey", _rt, _style.hotkeySize,
                InkMuted, TextAlignmentOptions.Center);
            hotkeyText.text = hotkey;
            var hkRt = hotkeyText.rectTransform;
            hkRt.sizeDelta = new Vector2(s, _style.hotkeySize + 6f);
            hkRt.anchoredPosition = new Vector2(0f, s * 0.5f + _style.hotkeySize * 0.7f);

            Refresh();
        }

        // ── View state ──────────────────────────────────────────────────────

        /// <summary>Re-read the model slot and redraw (also clears the hollow drag look).</summary>
        public void Refresh()
        {
            var slot = _hud.Inventory.Get(isToolBar, index);
            ApplyEmptyLook(slot.IsEmpty);
            if (slot.IsEmpty)
            {
                _icon.enabled = false;
                _letter.text = "";
                _count.text = "";
            }
            else
            {
                _icon.enabled = true;
                if (slot.item.icon != null)
                {
                    _icon.sprite = slot.item.icon;
                    _icon.type = Image.Type.Simple;
                    _icon.color = Color.white;
                    _letter.text = "";
                }
                else
                {
                    _icon.sprite = _style.panelSprite;
                    _icon.type = Image.Type.Sliced;
                    _icon.color = slot.item.Tint;
                    _letter.text = slot.item.Initial;
                }
                _count.text = slot.count > 1 ? slot.count.ToString() : "";
            }
            SetHollow(false);
        }

        /// <summary>
        /// An empty pocket recedes into the page. The tool loop does the opposite: it keeps its
        /// heavy rule at full strength so "no tool yet" stays a readable state rather than a gap —
        /// the opening sequence starts with this slot empty and that has to be a question.
        /// </summary>
        void ApplyEmptyLook(bool empty)
        {
            float a = empty ? _style.emptySlotAlpha : 1f;
            var f = FillColor; f.a *= a;
            _fill.color = f;

            var r = RuleColor;
            if (empty && !isToolBar) r.a *= a;
            _border.color = r;
        }

        public void SetSelected(bool on)
        {
            _ring.gameObject.SetActive(on);
            if (!on) return;
            // Accent ring pops on (OutBack) — readiness ack, Contract §14.
            Tween.Custom(0.8f, 1f, VibeTokens.DurQuick, ease: Ease.OutBack, onValueChange: t =>
            { if (_ring != null) _ring.transform.localScale = Vector3.one * t; });
        }

        /// <summary>Origin-slot look while its item is being dragged.</summary>
        public void SetHollow(bool on)
        {
            float a = on ? 0.3f : 1f;
            SetAlpha(_icon, a);
            SetAlpha(_letter, a);
            SetAlpha(_count, a);
        }

        // ── Micro-feel ──────────────────────────────────────────────────────

        /// <summary>Squash-pop when an icon lands here (punch always from base scale — spam-safe).</summary>
        public void PunchLand()
        {
            Tween.Custom(0f, 1f, _style.punchDuration, onValueChange: t =>
            {
                if (this == null) return;
                float k = Mathf.Sin(t * Mathf.PI);
                transform.localScale = _baseScale *
                    (1f + _style.landPunch * VibeTokens.MotionScale * k);
            });
        }

        /// <summary>Stack-count tick pop (repetition channel, Contract §16).</summary>
        public void PopCount()
        {
            Tween.Custom(0f, 1f, VibeTokens.DurQuick, onValueChange: t =>
            {
                if (_count == null) return;
                float k = Mathf.Sin(t * Mathf.PI);
                _count.transform.localScale = Vector3.one * (1f + 0.5f * VibeTokens.MotionScale * k);
            });
        }

        /// <summary>Gentle horizontal no-wiggle — the not-yet answer, never harsh (Contract §12).</summary>
        public void RefuseShake()
        {
            float amp = _style.refuseShakeAmplitude * VibeTokens.MotionScale;
            Tween.Custom(0f, 1f, _style.refuseShakeDuration, onValueChange: t =>
            {
                if (this == null) return;
                _rt.anchoredPosition = _basePos +
                    Vector2.right * (Mathf.Sin(t * 24f) * amp * (1f - t));
            }).OnComplete(() => { if (this != null) _rt.anchoredPosition = _basePos; });
        }

        // ── Pointer / drag (forwarded to the HUD coordinator) ───────────────

        public void OnPointerClick(PointerEventData e) => _hud.SelectSlot(this);
        public void OnPointerEnter(PointerEventData e) => HoverTo(VibeTokens.HoverScale);
        public void OnPointerExit(PointerEventData e) => HoverTo(1f);
        public void OnBeginDrag(PointerEventData e) => _hud.BeginDrag(this, e);
        public void OnDrag(PointerEventData e) => _hud.UpdateDrag(e);
        public void OnEndDrag(PointerEventData e) => _hud.EndDrag(this, e);

        void HoverTo(float scale)
        {
            Tween.Custom(_body.localScale.x, scale, VibeTokens.DurInstant, onValueChange: v =>
            { if (_body != null) _body.localScale = Vector3.one * v; });
        }

        // ── Small builders ──────────────────────────────────────────────────

        static RectTransform NewChild(string n, Transform parent, float size)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        TextMeshProUGUI NewText(string n, Transform parent, float size, Color color,
            TextAlignmentOptions align)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_style.font != null) tmp.font = _style.font;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void Fill(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color; c.a = a; g.color = c;
        }
    }
}
