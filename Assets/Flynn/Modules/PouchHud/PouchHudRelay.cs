using System.Collections.Generic;
using UnityEngine;
using Flynn.Core;
using Flynn.Feel;
using Flynn.Player;

namespace Flynn.Modules.PouchHud
{
    /// <summary>
    /// Mirrors gains in the gameplay inventory onto the feel-passed PlayerHUD.
    ///
    /// The two stores are independent by design: <see cref="PlayerInventory"/> owns the truth and
    /// <see cref="PlayerHUD"/> owns its own <c>HUDInventory</c> for layout and juice. PlayerHUD's
    /// only input seam is <c>PickUp</c> — there is no "set state" call — so this walks the delta
    /// instead: <c>OnSlotChanged</c> reports WHICH slot moved but not by how much, so a snapshot of
    /// (item, count) per slot is kept and compared.
    ///
    /// Only INCREASES are relayed. Consuming an item does not pull it back out of the HUD, because
    /// nothing consumes anything yet and a half-built removal path would be worse than none. When
    /// consumption lands, extend this with the matching <c>Drop</c> call.
    /// </summary>
    [DisallowMultipleComponent]
    public class PouchHudRelay : MonoBehaviour
    {
        [Tooltip("The HUD to feed. Auto-found in the scene when empty.")]
        [SerializeField] private PlayerHUD hud;

        [Tooltip("Optional art overrides. Leave empty and every item uses its own icon and name.")]
        [SerializeField] private PouchHudMap map;

        private PlayerInventory _inv;
        private ItemDefinition[] _items;
        private int[] _counts;
        private bool _subscribed;
        private bool _warnedRefused;
        private readonly Dictionary<ItemDefinition, HUDItemDef> _derived = new();

        private void OnEnable() => TrySubscribe();

        // PlayerInventory is [DefaultExecutionOrder(-50)] but may still live on a later scene
        // object, so OnEnable can miss it. Same retry the Pouch adapter uses.
        private void Start() => TrySubscribe();

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (_inv != null) _inv.OnSlotChanged -= HandleSlotChanged;
            _subscribed = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;

            _inv = PlayerInventory.Instance;
            if (_inv == null) return;
            if (hud == null) hud = FindObjectOfType<PlayerHUD>();
            if (hud == null || hud.Inventory == null) return;

            _items = new ItemDefinition[_inv.SlotCount];
            _counts = new int[_inv.SlotCount];

            _inv.OnSlotChanged += HandleSlotChanged;
            _subscribed = true;

            // Anything already in the bag (starting tools) should show without a pickup event.
            for (int i = 0; i < _inv.SlotCount; i++) HandleSlotChanged(i);
        }

        private void HandleSlotChanged(int index)
        {
            if (_items == null || index < 0 || index >= _items.Length) return;

            var slot = _inv.GetSlot(index);
            var item = slot.IsEmpty ? null : slot.item;
            int count = slot.IsEmpty ? 0 : slot.count;

            var prevItem = _items[index];
            int prevCount = _counts[index];
            _items[index] = item;
            _counts[index] = count;

            // Losses first. A fully-spent stack comes back as item==null, so the drop has to be
            // measured against what WAS there, not against the new contents.
            if (prevItem != null)
            {
                int remaining = item == prevItem ? count : 0;
                int lost = prevCount - remaining;
                if (lost > 0) hud.Consume(Resolve(prevItem), lost);
            }

            // A different item in the slot means the whole new stack is a gain; the same item means
            // only the difference is.
            int gained = item == prevItem ? count - prevCount : count;
            if (gained <= 0 || item == null) return;

            var hudItem = Resolve(item);

            for (int i = 0; i < gained; i++)
            {
                if (hud.PickUp(hudItem)) continue;
                if (_warnedRefused) break;
                _warnedRefused = true;
                Debug.LogWarning(
                    $"[PouchHudRelay] HUD refused '{hudItem.displayName}' — its bar is full while " +
                    "PlayerInventory still has room. The two stores have drifted.", this);
                break;
            }
        }

        /// <summary>
        /// HUD view-model for a gameplay item. The ItemDefinition is authoritative for identity and
        /// art — the bar shows the same name and the same sprite the item wears on the ground — so
        /// an item can never appear under another item's label. A map row is an override for the
        /// two things the gameplay definition has no opinion about: dedicated inventory art, and
        /// the placeholder tint role used while an item still has none.
        ///
        /// Cached per definition because HUDInventory compares slots by reference.
        /// </summary>
        private HUDItemDef Resolve(ItemDefinition item)
        {
            if (_derived.TryGetValue(item, out var d)) return d;

            HUDItemDef row = null;
            if (map != null) map.TryGet(item, out row);

            d = ScriptableObject.CreateInstance<HUDItemDef>();
            d.name = item.name;
            d.displayName = string.IsNullOrEmpty(item.displayName) ? item.itemId : item.displayName;
            d.icon = row != null && row.icon != null ? row.icon : item.icon;
            d.role = row != null ? row.role : HUDItemRole.InteractPassive;
            d.maxStack = Mathf.Max(1, item.maxStack);
            d.isTool = item.itemType != ToolType.None;
            d.pinnedToolSlot = 0;

            if (d.icon == null)
                Debug.LogWarning($"[PouchHudRelay] '{d.displayName}' has no icon — the bar will show " +
                                 "a placeholder letter. Assign ItemDefinition.icon.", item);

            _derived[item] = d;
            return d;
        }

        private void OnDestroy()
        {
            foreach (var d in _derived.Values) if (d != null) Destroy(d);
            _derived.Clear();
        }
    }
}
