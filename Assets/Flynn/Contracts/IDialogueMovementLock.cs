using UnityEngine;

namespace Flynn.Contracts
{
    /// Lets dialogue freeze the player and turn them to face whoever is talking, without the
    /// NPC assembly knowing what a player IS.
    ///
    /// Flynn.Npc used to reach for the concrete PlayerController2D here, which is the single
    /// reason the LLM stack could not compile on its own. The capability dialogue actually
    /// needs is this small, so it lives in Contracts and the player implements it.
    public interface IDialogueMovementLock
    {
        /// True while a conversation owns the player's input.
        bool IsMovementLocked { get; set; }

        /// Turn to look at a world position — the speaker.
        void FacePoint(Vector3 worldPoint);
    }
}
