namespace Flynn.Player
{
    /// Whoever is currently collecting picked-up items - in this game, the player's pouch.
    ///
    /// The base layer spawns, arcs and magnets world items, so it has to hand them off at the
    /// end. It must NOT know what a PlayerInventory is to do that: the inventory lives up in
    /// Flynn.Runtime, and reaching for it directly is what stopped the base layer from
    /// compiling on its own.
    public interface IItemReceiver
    {
        /// Take up to `count` of `item`. Returns how many were actually accepted - a full
        /// pouch returning less than asked is meaningful, and callers rely on it to leave the
        /// remainder lying in the world rather than destroying it.
        int TryAddItem(ItemDefinition item, int count);
    }

    /// The one receiver in play. Assigned by whatever implements it; null in a scene with no
    /// player, which simply means picked-up items go nowhere.
    public static class ItemReceiver
    {
        public static IItemReceiver Current;
    }
}
