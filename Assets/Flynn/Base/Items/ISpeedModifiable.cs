namespace Flynn.Player
{
    /// Something whose movement speed can be temporarily scaled by the world around it.
    ///
    /// Long grass slowing you as you wade through it is a base-layer effect, but the thing
    /// being slowed is the player, who lives above. This is the seam between them.
    public interface ISpeedModifiable
    {
        /// Apply a multiplier. Returns a handle to hand back to RemoveSpeedModifier.
        int AddSpeedModifier(float multiplier);

        /// Drop a previously applied multiplier.
        void RemoveSpeedModifier(int handle);
    }
}
