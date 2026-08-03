using UnityEngine;

namespace Flynn.Contracts
{
    /// <summary>
    /// Queryable terrain height: returns the discrete elevation level of the surface at a world
    /// position. Implemented by the map loader; read by the player's elevation probe so the two
    /// stay decoupled (PlayerRig → Contracts ← LevelLoad — dependency points inward).
    /// </summary>
    public interface IElevationSource
    {
        /// <summary>Elevation level (0 = ground) of the top surface at this world position.</summary>
        int LevelAt(Vector3 worldPos);
    }
}
