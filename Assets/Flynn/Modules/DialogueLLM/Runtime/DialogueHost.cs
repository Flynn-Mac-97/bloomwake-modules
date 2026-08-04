using Flynn.Player;

namespace Flynn.Npc
{
    /// What the dialogue panel needs from the player's bag, and nothing else.
    ///
    /// Dialogue lets you show an item off or gift it, so it has to list what you are carrying.
    /// It must NOT know what a PlayerInventory is to do that - the inventory lives up in
    /// Flynn.Runtime, and naming it directly is what kept the LLM stack welded to the game.
    public interface IDialogueInventory
    {
        int SlotCount { get; }

        /// Contents of a slot. False when the slot is empty or out of range.
        bool TryGetSlot(int index, out ItemDefinition item, out int count);

        /// Whether this slot may be given away. The wrench is not giftable, but that is the
        /// inventory's rule to know - dialogue used to hard-code "slot 0" and would have
        /// silently started gifting the wrench the day that changed.
        bool IsGiftableSlot(int index);

        /// Take items out of a slot. Returns the item removed, and how many, via `removed`.
        ItemDefinition RemoveFromSlot(int index, int count, out int removed);
    }

    /// The codex panel, as dialogue uses it: a milestone toast and a toggle.
    public interface ICodexPanel
    {
        void ShowToast(string message);
        void Toggle();
    }

    /// Whatever is currently filling those roles. Assigned by the implementors; null is a
    /// legitimate state meaning "this scene has no bag / no codex", which is exactly what a
    /// bare dialogue test scene should be.
    public static class DialogueHost
    {
        public static IDialogueInventory Inventory;
        public static ICodexPanel Codex;
    }
}
