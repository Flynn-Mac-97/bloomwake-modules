using UnityEngine;
using UnityEngine.Events;
using Flynn.Npc;

namespace Flynn.Modules.DialogueLLM
{
    /// Seam-in for talking to one LLM NPC: put this on the NPC, wire any interaction
    /// (Interactable, lab button) to StartTalk(). Wraps DialogueManager.OpenAgent.
    public class NpcTalkTrigger : MonoBehaviour
    {
        [SerializeField] private DialogueManager _dialogue;
        [SerializeField] private string _npcId = "transmitter";
        [SerializeField] private Sprite _portraitOverride;
        [SerializeField] private NpcRelationshipState _relationship;

        public UnityEvent onTalkStarted = new UnityEvent();

        public void StartTalk()
        {
            if (_dialogue == null || string.IsNullOrWhiteSpace(_npcId)) return;
            _dialogue.OpenAgent(_npcId, _portraitOverride, _relationship, transform.position);
            onTalkStarted.Invoke();
        }
    }
}
