namespace Flynn.Contracts
{
    /// Publishes how far a character's visual is lifted off the ground (hop / jump), in world
    /// units. Ground FX (blob shadow, dust) read this without knowing the concrete mover.
    public interface ILiftProvider
    {
        /// Current vertical lift of the visual above its grounded rest position, world units (0 = grounded).
        float Lift { get; }
    }
}
