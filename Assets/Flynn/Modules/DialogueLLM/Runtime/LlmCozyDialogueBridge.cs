using UnityEngine;
using Flynn.Npc;
using Flynn.Feel;   // FieldDialogue lives in Flynn.Modules.Dialogue, namespace Flynn.Feel

namespace Flynn.Modules.DialogueLLM
{
    /// Renders the LLM brain (<see cref="DialogueManager"/>) through CozyDialogue's
    /// <see cref="FieldDialogue"/> box, so the conversation looks like the rest of the game
    /// instead of like a debug panel.
    ///
    /// The brain runs with its own panel suppressed (`DialogueManager._hideOwnPanel = true`).
    /// It still builds real VisualElements to write into - it just never shows them. Its turn
    /// moments surface as four events which drive the cozy box, and the box's say-row feeds the
    /// player's text back in. Four events out, two in; that is the entire contract.
    ///
    /// Ordering matters and is why this retries: DialogueManager is a singleton that may wake up
    /// after this component. OnEnable and Start both attempt the hook.
    [DisallowMultipleComponent]
    public class LlmCozyDialogueBridge : MonoBehaviour
    {
        [Tooltip("The cozy dialogue box that becomes the visible surface.")]
        [SerializeField] private FieldDialogue field;

        [Tooltip("Shown in the cozy box while the model is generating. Keep it short - it is " +
                 "replaced by the real line a moment later.")]
        [SerializeField] private string thinkingLine = "...";

        private DialogueManager _brain;
        private bool _hooked;

        public bool IsHooked => _hooked;

        private void OnEnable() => TryHook();
        private void Start() => TryHook();

        private void TryHook()
        {
            if (_hooked) return;
            _brain = DialogueManager.Instance;
            if (_brain == null || field == null) return;

            _brain.SessionOpened   += OnSessionOpened;
            _brain.SessionClosed   += OnSessionClosed;
            _brain.ThinkingStarted += OnThinking;
            _brain.ReplyProduced   += OnReply;
            field.PlayerSubmitted  += OnPlayerSubmitted;
            field.Closed           += OnCozyClosed;
            _hooked = true;
        }

        private void OnDisable()
        {
            if (!_hooked) return;
            if (_brain != null)
            {
                _brain.SessionOpened   -= OnSessionOpened;
                _brain.SessionClosed   -= OnSessionClosed;
                _brain.ThinkingStarted -= OnThinking;
                _brain.ReplyProduced   -= OnReply;
            }
            if (field != null)
            {
                field.PlayerSubmitted -= OnPlayerSubmitted;
                field.Closed          -= OnCozyClosed;
            }
            _hooked = false;
        }

        // brain -> cozy box
        private void OnSessionOpened(string npcName) => field.OpenFreeText(npcName, npcName + " is listening.");
        private void OnSessionClosed() { if (field.IsOpen) field.Leave(); }
        private void OnThinking() { if (field.IsOpen) field.RevealLine(thinkingLine); }
        private void OnReply(string line) { if (field.IsOpen) field.RevealLine(line); }

        // cozy box -> brain
        private void OnPlayerSubmitted(string text) { if (_brain != null) _brain.SubmitPlayerText(text); }
        private void OnCozyClosed() { if (DialogueManager.IsDialogueOpen && _brain != null) _brain.Close(); }
    }
}
