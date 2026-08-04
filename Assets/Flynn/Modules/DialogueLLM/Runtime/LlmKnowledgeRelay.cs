using UnityEngine;
using Flynn.Npc;    // DialogueTriggerChannel, DialogueTriggerPayload
using Flynn.Feel;   // KnowledgeBase, FieldArchiveHud (Flynn.Modules.Dialogue)

namespace Flynn.Modules.DialogueLLM
{
    /// Turns what an NPC actually teaches you into a Field Guide entry.
    ///
    /// The seam already existed and this just joins the two ends of it. When the model decides a
    /// signal fired, DialogueManager validates that id against the island's signal allowlist and
    /// raises it on the DialogueTriggerChannel. This listens to that channel and calls
    /// KnowledgeBase.Discover with the signal id.
    ///
    /// Signal ids are therefore deliberately identical to TopicLibrary topic ids ("learn.spring"
    /// and so on). That 1:1 naming is the whole mechanism, and it is why a hallucinated topic
    /// cannot leak into the archive: the model can only fire signals the island file declares,
    /// and the allowlist check happens before this ever runs.
    [DisallowMultipleComponent]
    public class LlmKnowledgeRelay : MonoBehaviour
    {
        [Tooltip("The channel DialogueManager raises validated signals on. Same asset assigned " +
                 "to SceneLlmManager.triggerChannel.")]
        [SerializeField] private DialogueTriggerChannel _channel;

        [Tooltip("The archive that records what the player has learned.")]
        [SerializeField] private KnowledgeBase _knowledge;

        [Tooltip("Optional. Shows the 'Entry updated' toast in the top bar.")]
        [SerializeField] private FieldArchiveHud _hud;

        [Tooltip("Only relay signals whose handler is this. Blank relays every signal. The " +
                 "island file marks learning signals with handler 'Discover' so that story " +
                 "beats and one-off effects do not become archive entries.")]
        [SerializeField] private string _handlerFilter = "Discover";

        private void OnEnable()
        {
            if (_channel != null) _channel.OnRaised += OnTrigger;
        }

        private void OnDisable()
        {
            if (_channel != null) _channel.OnRaised -= OnTrigger;
        }

        private void OnTrigger(DialogueTriggerPayload payload)
        {
            if (_knowledge == null) return;

            if (!string.IsNullOrEmpty(_handlerFilter) &&
                !string.Equals(payload.handler, _handlerFilter, System.StringComparison.OrdinalIgnoreCase))
                return;

            // Prefer an explicit topic if one was set; otherwise the signal id IS the topic id.
            string topicId = !string.IsNullOrWhiteSpace(payload.topic) ? payload.topic : payload.triggerKey;
            if (string.IsNullOrWhiteSpace(topicId)) return;

            if (_knowledge.IsKnown(topicId)) return;      // already in the archive; stay quiet

            _knowledge.Discover(topicId);
            if (payload.trustDelta != 0) _knowledge.RaiseTrust(payload.trustDelta);

            if (_hud != null) _hud.Push("Field guide updated");
        }
    }
}
