using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Ambient bark data (handover §1): one contextual NPC comment with two quick replies and
    /// a Chat handoff. Quick replies are social acknowledgement, not branching — one reaction
    /// line, optional trust nudge, auto-close. Demo content ships as field defaults so creating
    /// the asset IS the demo (module pattern).
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Dialogue/Bark", fileName = "Bark")]
    public class BarkSO : ScriptableObject
    {
        [System.Serializable]
        public struct QuickReply
        {
            public string label;
            [TextArea] public string reaction;
            [Tooltip("Minor relationship signal applied to KnowledgeBase.trust.")]
            public int trustDelta;
        }

        [Tooltip("Badge text; 'Name · Role' supported (bark shows name only).")]
        public string speaker = "Rowan · Grovekeeper";
        [Tooltip("Keep it bar-length — 'Welcome, traveller.' energy, not a paragraph.")]
        [TextArea]
        public string line = "The grove sounds softer today.";
        public QuickReply replyA = new QuickReply
        {
            label = "Softer?",
            reaction = "Like it stopped holding its breath.",
            trustDelta = 1,
        };
        public QuickReply replyB = new QuickReply
        {
            label = "Just passing through.",
            reaction = "Mm. Moss hums either way.",
            trustDelta = 0,
        };
        [Tooltip("False = info-only bark: just the line over the head, no chips, timeout dismiss.")]
        public bool allowResponses = true;
        [Tooltip("Conversation opened by Chat (FieldDialogue). Chip hidden when empty.")]
        public ConversationSO chatConversation;
        [Tooltip("Player distance that raises the bark (world units).")]
        public float triggerRadius = 2.2f;
        [Tooltip("Seconds before an untouched bark fades out.")]
        public float timeout = 8f;
        [Tooltip("Seconds before this NPC barks again after any dismissal.")]
        public float cooldown = 25f;
    }
}
