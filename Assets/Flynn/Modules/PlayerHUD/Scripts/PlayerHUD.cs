using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Flynn.Feel
{
    /// <summary>
    /// The player HUD coordinator: sun-battery meter (top-left) + tool bar (Q/W/E, pinned)
    /// + main action bar (1–6) with drag/drop/swap/stack and ghost-icon flights.
    ///
    /// Builds its own UI at runtime from <see cref="HUDStyle"/> — no scene anchor wiring,
    /// all layout/feel tuned live in the SO. Colors/sorting from VibeTokens in code
    /// (canvas order 200 = HUD layer, under dialogue at 300).
    ///
    /// Tool law: a tool always lands on its pinned tool-bar hotkey and never moves between
    /// slots — drag it out to drop it, pick it up again and it returns home.
    ///
    /// Composition surface: public methods (PickUp/Drop/battery) + UnityEvents below.
    /// No bus, no singletons — other modules wire via inspector events.
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [System.Serializable] public class ItemEvent : UnityEvent<HUDItemDef> { }
        [System.Serializable] public class ItemCountEvent : UnityEvent<HUDItemDef, int> { }

        [Tooltip("All layout/feel/audio tuning.")]
        public HUDStyle style;

        [Header("Flight-anchor experiment (toggle, default off)")]
        [Tooltip("Off = ghost icons fly to/from the HUD slot (current). On = pickups absorb " +
                 "into the player sprite and drops spew from it — world-anchored transfer.")]
        public bool anchorFlightsToPlayer;
        [Tooltip("World transform items fly to (pickup) / from (drop) in player mode.")]
        public Transform playerAnchor;

        [Header("Composition events (inspector-wired, module-local)")]
        [Tooltip("An item left the HUD (drag-out or drop key): def + count.")]
        public ItemCountEvent onItemDropped;
        [Tooltip("A tool-bar slot with a tool was selected.")]
        public ItemEvent onToolSelected;
        public UnityEvent onBatteryEmpty;
        public UnityEvent onBatteryFull;
        [Tooltip("Pickup refused — main bar full.")]
        public UnityEvent onPocketsFull;

        public HUDInventory Inventory { get; private set; }
        public float Battery => _battery;
        public RectTransform CanvasRoot => _canvasRt;

        static readonly KeyCode[] MainKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };
        // Z/X/C, not Q/W/E: W is walk, so the old row fired a tool select (and its click) on
        // every step, and E is the interact key everywhere else in the game.
        static readonly KeyCode[] ToolKeys = { KeyCode.Z, KeyCode.X, KeyCode.C };

        RectTransform _canvasRt, _barsRt;
        HUDSlotView[] _mainViews, _toolViews;
        BatteryMeterView _meter;
        AudioSource _audio;

        float _battery;
        bool _charging;
        bool _selTool;
        int _selIndex;

        HUDSlotView _dragOrigin;
        RectTransform _dragGhost;

        float _lastSfxTime = -10f;

        // ── Lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            if (style == null) { Debug.LogWarning("[PlayerHUD] no style assigned"); enabled = false; return; }
            Inventory = new HUDInventory(
                Mathf.Clamp(style.mainSlots, 1, MainKeys.Length),
                Mathf.Clamp(style.toolSlots, 1, ToolKeys.Length));
            BuildUi();
            _battery = style.batteryStart;
            _meter.Set(_battery, instant: true);
            Select(false, 0, quiet: true);
        }

        void Update()
        {
            for (int i = 0; i < _mainViews.Length; i++)
                if (Input.GetKeyDown(MainKeys[i])) Select(false, i);
            for (int i = 0; i < _toolViews.Length; i++)
                if (Input.GetKeyDown(ToolKeys[i])) Select(true, i);

            if (_charging && _battery < 1f)
            {
                float prev = _battery;
                _battery = Mathf.Clamp01(_battery + style.chargeRate * Time.deltaTime);
                _meter.SetImmediate(_battery);
                CheckBatteryEdges(prev);
            }
        }

        // ── Public API: items ───────────────────────────────────────────────

        /// <summary>
        /// Remove units the gameplay inventory has already spent. Without this the bar keeps showing
        /// a stack the player no longer owns — and once anything in the game consumes resources, a
        /// pouch that lies is worse than no pouch. Returns how many it actually removed.
        /// </summary>
        public int Consume(HUDItemDef def, int count)
        {
            if (def == null || Inventory == null || count <= 0) return 0;

            int removed = 0;
            for (int i = 0; i < Inventory.Main.Length && removed < count; i++)
            {
                var slot = Inventory.Main[i];
                if (slot.IsEmpty || slot.item != def) continue;

                int take = Mathf.Min(slot.count, count - removed);
                slot.count -= take;
                if (slot.count <= 0) slot.Clear();
                removed += take;

                if (i < _mainViews.Length && _mainViews[i] != null)
                {
                    _mainViews[i].Refresh();
                    _mainViews[i].PopCount();
                }
            }
            return removed;
        }

        /// <summary>
        /// The pickup flight, reversed: a ghost icon leaves the pouch slot and arcs out to a screen
        /// point. Purely presentational — it does NOT touch the model, because whatever spent the
        /// item has already told the bar via <see cref="Consume"/>. Use it so giving something away
        /// reads as the mirror of gaining it.
        /// </summary>
        public void FlyOut(HUDItemDef def, Vector2 screenTo)
        {
            if (def == null || _mainViews == null) return;

            HUDSlotView from = null;
            for (int i = 0; i < _mainViews.Length; i++)
            {
                var s = Inventory.Main[i];
                if (!s.IsEmpty && s.item == def) { from = _mainViews[i]; break; }
            }

            var ghost = MakeGhost(def, 1, 1f);
            Vector2 start = from != null ? SlotLocal(from) : PlayerLocal();
            HUDFly.Arc(ghost, start, LocalPoint(screenTo),
                style.flyArcHeight, style.flyDuration, shrinkOut: true,
                // NO sound here. Whoever asked for the flight owns the landing note, because two
                // systems each playing "their" cue per item reads as an echo, not as feedback.
                onArrive: () => { if (ghost != null) Destroy(ghost.gameObject); });
        }

        /// <summary>Pick up an item; ghost icon flies from a screen point into its slot.</summary>
        public bool PickUp(HUDItemDef def) =>
            PickUp(def, new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));

        public bool PickUp(HUDItemDef def, Vector2 screenFrom)
        {
            if (def == null || Inventory == null) return false;
            var result = Inventory.TryAdd(def, out bool toTool, out int index);
            if (result == HUDInventory.AddResult.Refused)
            {
                if (def.isTool)
                {
                    // Already carrying it — its home slot answers.
                    _toolViews[Mathf.Clamp(def.pinnedToolSlot, 0, _toolViews.Length - 1)].RefuseShake();
                }
                else
                {
                    ShakeRect(_barsRt);
                    onPocketsFull?.Invoke();
                }
                PlaySfx(style.refuseSfx, style.infoVolume);
                return false;
            }

            var target = toTool ? _toolViews[index] : _mainViews[index];
            bool stacked = result == HUDInventory.AddResult.Stacked;
            // Player mode: the item is absorbed by the player in the world; the slot still
            // punches on arrival so the HUD acks where it went (causality chain intact).
            bool toPlayer = UsePlayerAnchor;
            HUDFly.Arc(MakeGhost(def, 1, 1f), LocalPoint(screenFrom),
                toPlayer ? PlayerLocal() : SlotLocal(target),
                style.flyArcHeight, style.flyDuration, shrinkOut: toPlayer, onArrive: () =>
                {
                    if (target == null) return;
                    target.Refresh();
                    target.PunchLand();
                    if (stacked) target.PopCount();
                });
            PlaySfx(style.pickupSfx, style.actionVolume);
            return true;
        }

        /// <summary>Drop from the selected slot toward a screen point (one item; tools whole).</summary>
        public void DropSelected(Vector2 screenTo) => DropFromSlot(_selTool, _selIndex, screenTo, wholeStack: false);

        public void DropFromSlot(bool tool, int index, Vector2 screenTo, bool wholeStack)
        {
            var slot = Inventory.Get(tool, index);
            if (slot.IsEmpty) return;

            HUDItemDef def;
            int count;
            if (wholeStack || slot.item.isTool) Inventory.RemoveAll(tool, index, out def, out count);
            else { Inventory.RemoveOne(tool, index, out def); count = 1; }

            var view = tool ? _toolViews[index] : _mainViews[index];
            HUDFly.Arc(MakeGhost(def, count, 1f),
                UsePlayerAnchor ? PlayerLocal() : SlotLocal(view), LocalPoint(screenTo),
                style.flyArcHeight * 0.6f, style.flyDuration, shrinkOut: true, onArrive: null);
            view.Refresh();
            PlaySfx(style.dropSfx, style.actionVolume);
            onItemDropped?.Invoke(def, count);
        }

        // ── Public API: battery ─────────────────────────────────────────────

        public void SetBattery(float value)
        {
            _battery = Mathf.Clamp01(value);
            _meter.Set(_battery, instant: true);
        }

        public void AddBattery(float delta)
        {
            float prev = _battery;
            _battery = Mathf.Clamp01(_battery + Mathf.Abs(delta));
            _meter.Set(_battery);
            PlaySfx(style.gainTickSfx, style.infoVolume);
            CheckBatteryEdges(prev);
        }

        public void DrainBattery(float delta)
        {
            float prev = _battery;
            _battery = Mathf.Clamp01(_battery - Mathf.Abs(delta));
            _meter.Set(_battery);
            CheckBatteryEdges(prev);
        }

        public void SetCharging(bool on)
        {
            _charging = on && _battery < 1f;
            _meter.SetCharging(_charging);
        }

        void CheckBatteryEdges(float prev)
        {
            if (_battery <= 0f && prev > 0f) onBatteryEmpty?.Invoke();
            if (_battery >= 1f && prev < 1f)
            {
                _meter.PlayFull();
                // Success tier — the chime always plays (no spam-guard skip).
                if (style.fullChimeSfx != null)
                    _audio.PlayOneShot(style.fullChimeSfx, style.actionVolume);
                SetCharging(false);   // clear completion: the breath stops, the bar rests full
                onBatteryFull?.Invoke();
            }
        }

        // ── Selection (readiness channel) ───────────────────────────────────

        public void SelectSlot(HUDSlotView view) => Select(view.isToolBar, view.index);

        void Select(bool tool, int index, bool quiet = false)
        {
            View(_selTool, _selIndex).SetSelected(false);
            _selTool = tool;
            _selIndex = index;
            var v = View(tool, index);
            v.SetSelected(true);
            if (quiet) return;
            PlaySfx(style.selectSfx, style.infoVolume);
            var slot = Inventory.Get(tool, index);
            if (tool && !slot.IsEmpty) onToolSelected?.Invoke(slot.item);
        }

        HUDSlotView View(bool tool, int index) => tool ? _toolViews[index] : _mainViews[index];

        // ── Drag orchestration (called by HUDSlotView) ──────────────────────

        public void BeginDrag(HUDSlotView origin, PointerEventData e)
        {
            var slot = Inventory.Get(origin.isToolBar, origin.index);
            if (slot.IsEmpty) { _dragOrigin = null; return; }

            _dragOrigin = origin;
            _dragGhost = MakeGhost(slot.item, slot.count, style.dragLiftScale);
            _dragGhost.localRotation = Quaternion.Euler(0f, 0f, 6f);
            _dragGhost.anchoredPosition = LocalPoint(e.position);
            origin.SetHollow(true);
            PlaySfx(style.grabSfx, style.infoVolume);
        }

        public void UpdateDrag(PointerEventData e)
        {
            if (_dragGhost != null) _dragGhost.anchoredPosition = LocalPoint(e.position);
        }

        public void EndDrag(HUDSlotView _, PointerEventData e)
        {
            if (_dragOrigin == null) return;
            var origin = _dragOrigin;
            _dragOrigin = null;

            HUDSlotView target = null;
            var hit = e.pointerCurrentRaycast.gameObject;
            if (hit != null) target = hit.GetComponentInParent<HUDSlotView>();

            if (target == null)
            {
                // Released off the bars = drop into the world (whole stack).
                DestroyGhost();
                DropFromSlot(origin.isToolBar, origin.index, e.position, wholeStack: true);
                return;
            }

            if (target == origin)
            {
                DestroyGhost();
                origin.Refresh();
                return;
            }

            var result = Inventory.TryMove(origin.isToolBar, origin.index,
                                           target.isToolBar, target.index);
            if (result == HUDInventory.MoveResult.Refused)
            {
                // Gentle no: ghost flies home, target wiggles. Tools never leave their hotkey.
                FlyGhostTo(origin, () => origin.Refresh());
                target.RefuseShake();
                PlaySfx(style.refuseSfx, style.infoVolume);
                return;
            }

            if (result == HUDInventory.MoveResult.Swapped)
            {
                // The displaced item flies back the other way.
                var back = Inventory.Get(origin.isToolBar, origin.index);
                HUDFly.Arc(MakeGhost(back.item, back.count, 1f),
                    SlotLocal(target), SlotLocal(origin),
                    style.flyArcHeight * 0.5f, style.flyDuration * 0.8f, shrinkOut: false,
                    onArrive: () => { origin.Refresh(); origin.PunchLand(); });
            }

            FlyGhostTo(target, () =>
            {
                origin.Refresh();
                target.Refresh();
                target.PunchLand();
                if (result == HUDInventory.MoveResult.Merged) target.PopCount();
            });
            PlaySfx(style.placeSfx, style.actionVolume);
        }

        void FlyGhostTo(HUDSlotView slot, System.Action onArrive)
        {
            if (_dragGhost == null) { onArrive?.Invoke(); return; }
            var ghost = _dragGhost;
            _dragGhost = null;
            ghost.localRotation = Quaternion.identity;
            HUDFly.Arc(ghost, ghost.anchoredPosition, SlotLocal(slot),
                style.flyArcHeight * 0.3f, style.flyDuration * 0.6f, shrinkOut: false, onArrive);
        }

        void DestroyGhost()
        {
            if (_dragGhost != null) Destroy(_dragGhost.gameObject);
            _dragGhost = null;
        }

        // ── Runtime UI construction (no scene wiring to get wrong) ─────────

        void BuildUi()
        {
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;   // HUD layer — under dialogue (300)
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRt = (RectTransform)canvasGo.transform;

            _meter = BatteryMeterView.Create(style, _canvasRt);

            int nTool = Inventory.Tools.Length;
            int nMain = Inventory.Main.Length;
            float s = style.slotSize;
            float step = s + style.slotGap;
            float total = nTool * step - style.slotGap + style.barGapBetween
                        + nMain * step - style.slotGap;

            _barsRt = new GameObject("Bars", typeof(RectTransform)).GetComponent<RectTransform>();
            _barsRt.SetParent(_canvasRt, false);
            _barsRt.anchorMin = _barsRt.anchorMax = new Vector2(0.5f, 0f);
            _barsRt.pivot = new Vector2(0.5f, 0f);
            _barsRt.sizeDelta = new Vector2(total, s);
            _barsRt.anchoredPosition = new Vector2(0f, style.barBottomMargin + s * 0.5f);

            _toolViews = new HUDSlotView[nTool];
            _mainViews = new HUDSlotView[nMain];
            float x = s * 0.5f;
            for (int i = 0; i < nTool; i++, x += step)
                _toolViews[i] = HUDSlotView.Create(this, style, _barsRt, true, i,
                    ToolKeys[i].ToString(), new Vector2(x, 0f));
            x += style.barGapBetween - style.slotGap;
            for (int i = 0; i < nMain; i++, x += step)
                _mainViews[i] = HUDSlotView.Create(this, style, _barsRt, false, i,
                    (i + 1).ToString(), new Vector2(x, 0f));

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        /// <summary>Placeholder ghost icon — same look as a slot icon, raycast-transparent.</summary>
        RectTransform MakeGhost(HUDItemDef def, int count, float scale)
        {
            var go = new GameObject("Ghost", typeof(RectTransform));
            go.transform.SetParent(_canvasRt, false);
            var rt = (RectTransform)go.transform;
            float size = style.slotSize - style.iconInset * 2f;
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = Vector3.one * scale;

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (def.icon != null)
            {
                img.sprite = def.icon;
                img.color = Color.white;
            }
            else
            {
                img.sprite = style.panelSprite;
                img.type = Image.Type.Sliced;
                img.color = def.Tint;

                var letterGo = new GameObject("Letter", typeof(RectTransform));
                letterGo.transform.SetParent(rt, false);
                var letter = letterGo.AddComponent<TextMeshProUGUI>();
                if (style.font != null) letter.font = style.font;
                letter.text = def.Initial;
                letter.fontSize = style.slotSize * 0.4f;
                letter.fontStyle = FontStyles.Bold;
                letter.color = VibeTokens.UiInk;
                letter.alignment = TextAlignmentOptions.Center;
                letter.raycastTarget = false;
                var lrt = (RectTransform)letterGo.transform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            }

            if (count > 1)
            {
                var countGo = new GameObject("Count", typeof(RectTransform));
                countGo.transform.SetParent(rt, false);
                var tmp = countGo.AddComponent<TextMeshProUGUI>();
                if (style.font != null) tmp.font = style.font;
                tmp.text = count.ToString();
                tmp.fontSize = style.countSize;
                tmp.color = VibeTokens.UiInk;
                tmp.alignment = TextAlignmentOptions.BottomRight;
                tmp.raycastTarget = false;
                var crt = (RectTransform)countGo.transform;
                crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
                crt.offsetMin = crt.offsetMax = Vector2.zero;
            }
            return rt;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        Vector2 LocalPoint(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, screen, null, out var lp);
            return lp;
        }

        Vector2 SlotLocal(HUDSlotView slot) =>
            LocalPoint(RectTransformUtility.WorldToScreenPoint(null, slot.Rect.position));

        bool UsePlayerAnchor =>
            anchorFlightsToPlayer && playerAnchor != null && Camera.main != null;

        Vector2 PlayerLocal() =>
            LocalPoint(Camera.main.WorldToScreenPoint(playerAnchor.position));

        void ShakeRect(RectTransform rt)
        {
            Vector2 basePos = rt.anchoredPosition;
            float amp = style.refuseShakeAmplitude * VibeTokens.MotionScale;
            Tween.Custom(0f, 1f, style.refuseShakeDuration, onValueChange: t =>
            {
                if (rt == null) return;
                rt.anchoredPosition = basePos + Vector2.right * (Mathf.Sin(t * 24f) * amp * (1f - t));
            }).OnComplete(() => { if (rt != null) rt.anchoredPosition = basePos; });
        }

        void PlaySfx(AudioClip clip, float volume)
        {
            if (clip == null || _audio == null) return;
            if (Time.unscaledTime - _lastSfxTime < style.sfxMinInterval) return;   // spam guard
            _lastSfxTime = Time.unscaledTime;
            _audio.pitch = 1f + Random.Range(-style.pitchJitter, style.pitchJitter);
            _audio.PlayOneShot(clip, volume);
        }
    }
}
