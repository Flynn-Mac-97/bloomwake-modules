using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Marks a collider as hover-cue-able (Stage E1): entering shows the verb circle at the
    /// cursor, exiting hides it. Sits beside NpcConversationTrigger / Inspectable /
    /// TransmitterDecoder on their existing Collider2D.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HoverCueTarget : MonoBehaviour
    {
        public CozyDialogueUI ui;
        public HoverCue.Kind kind = HoverCue.Kind.Inspect;
        [Tooltip("Topic backing this thing — when known, hover shows the recorded badge and " +
                 "right-click jumps to its archive entry ('' = no journal tie-in).")]
        public string topicId;
        [Tooltip("Player must be within this world radius to hover this. 0 = no proximity gate.")]
        public float interactRadius = 2.5f;

        Inspectable _inspectable;

        void Awake()
        {
            _inspectable = GetComponent<Inspectable>();
            // The UI is a SCENE object, so a prefabbed hover target cannot serialise a reference
            // to it without that becoming a per-instance override on every copy.
            if (ui == null) ui = FindObjectOfType<CozyDialogueUI>();
        }

        void OnMouseEnter()
        {
            if (ui == null) return;
            if (interactRadius > 0f && !PlayerLocator.InRange(transform.position, interactRadius)) return;
            bool recorded = _inspectable != null && _inspectable.Inspected;
            ui.ShowHover(kind, topicId, recorded);
        }

        void OnMouseExit()
        {
            if (ui != null) ui.HideHover();
        }

        void OnDisable()
        {
            if (ui != null) ui.HideHover();
        }
    }
}
