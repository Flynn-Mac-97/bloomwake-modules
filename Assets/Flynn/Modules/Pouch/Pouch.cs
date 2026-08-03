using UnityEngine;
using UnityEngine.Events;
using Flynn.Player;

namespace Flynn.Modules.Pouch
{
    /// Thin adapter over the Runtime PlayerInventory singleton. Surfaces the seam
    /// other modules glue to in the composed scene: Add/Consume/CountOf in,
    /// onChanged out. Currency routing deliberately not exposed (cut from MVP).
    public class Pouch : MonoBehaviour
    {
        [Tooltip("Fired whenever any slot changes (add, remove, consume).")]
        public UnityEvent onChanged = new UnityEvent();

        private bool _subscribed;

        private PlayerInventory Inv => PlayerInventory.Instance;

        private void OnEnable()
        {
            TrySubscribe();
        }

        // Inventory singleton has DefaultExecutionOrder(-50) but may live on a later
        // scene object — retry once Start runs.
        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (Inv != null) Inv.OnSlotChanged -= HandleSlotChanged;
            _subscribed = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed || Inv == null) return;
            Inv.OnSlotChanged += HandleSlotChanged;
            _subscribed = true;
        }

        private void HandleSlotChanged(int slotIndex)
        {
            onChanged.Invoke();
        }

        /// Returns how many were actually added (0 when full or inventory absent).
        public int Add(ItemDefinition item, int count = 1)
        {
            return Inv != null && item != null ? Inv.TryAddItem(item, count) : 0;
        }

        /// Returns how many were actually consumed.
        public int Consume(ItemDefinition item, int count = 1)
        {
            return Inv != null && item != null ? Inv.TryConsume(item, count) : 0;
        }

        /// Total of this item across all slots.
        public int CountOf(ItemDefinition item)
        {
            if (Inv == null || item == null) return 0;
            int total = 0;
            for (int i = 0; i < 4; i++)
            {
                var slot = Inv.GetSlot(i);
                if (!slot.IsEmpty && slot.item == item) total += slot.count;
            }
            return total;
        }

        public bool Has(ItemDefinition item, int count = 1)
        {
            return CountOf(item) >= count;
        }
    }
}
