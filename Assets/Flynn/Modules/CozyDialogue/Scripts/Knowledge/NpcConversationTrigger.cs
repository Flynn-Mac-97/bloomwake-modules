using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>Click the NPC → open their world-space conversation. Needs a Collider2D.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class NpcConversationTrigger : MonoBehaviour
    {
        public ConversationSO conversation;
        [Tooltip("Cozy Dialogue UI facade.")]
        public CozyDialogueUI ui;
        [Tooltip("Where the panel floats (defaults to this transform).")]
        public Transform anchor;
        [Tooltip("Player must be within this world radius to click-talk. 0 = no proximity gate.")]
        public float interactRadius = 2.5f;

        // The UI is a SCENE object, so a prefabbed NPC cannot serialise a reference to it without
        // that becoming a per-instance override on every copy. Falling back to a lookup lets an
        // NPC prefab drop into any scene that has the hub and just work.
        void Awake()
        {
            if (ui == null) ui = FindObjectOfType<CozyDialogueUI>();
        }

        /// <summary>
        /// Fires whenever ANY conversation is opened by a click, carrying the trigger that opened it.
        /// Same reasoning as <see cref="Inspectable.AnyInspected"/>: lets one director hear the beat
        /// without a component per NPC. Fires only after the proximity and already-open gates pass,
        /// so it marks a conversation that actually opened.
        /// </summary>
        public static event System.Action<NpcConversationTrigger> AnyOpened;

        void OnMouseDown()
        {
            if (ui == null || ui.DialogueOpen) return;
            if (interactRadius > 0f && !PlayerLocator.InRange(transform.position, interactRadius)) return;
            ui.OpenDialogue(conversation, anchor != null ? anchor : transform);
            AnyOpened?.Invoke(this);
        }
    }
}
