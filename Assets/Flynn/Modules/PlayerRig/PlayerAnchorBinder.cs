using UnityEngine;
using Flynn.Common;

namespace Flynn.Modules.PlayerRig
{
    /// Binds this transform into the shared PlayerAnchor SO — everything that finds
    /// "the player" through the anchor (Interactable, transmitter feed, drop magnet)
    /// works regardless of which player rig is in the scene.
    public class PlayerAnchorBinder : MonoBehaviour
    {
        [SerializeField] private PlayerAnchor _anchor;

        private void OnEnable()
        {
            if (_anchor != null) _anchor.Set(transform);
        }

        private void OnDisable()
        {
            if (_anchor != null) _anchor.Clear(transform);
        }
    }
}
