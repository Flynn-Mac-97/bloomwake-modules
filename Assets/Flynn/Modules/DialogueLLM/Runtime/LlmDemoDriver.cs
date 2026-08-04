using UnityEngine;
using Flynn.Npc;

namespace Flynn.Modules.DialogueLLM
{
    /// On-screen driver for the demo scene: tells you what to press, and - more usefully - tells
    /// you whether the LLM is actually configured before you waste time wondering why the NPC
    /// answers with a fallback line.
    ///
    /// Demo scaffolding, not shipping UI. Delete it in a real scene and drive
    /// <see cref="NpcTalkTrigger.StartTalk"/> from whatever your interaction system is.
    public class LlmDemoDriver : MonoBehaviour
    {
        [SerializeField] private NpcTalkTrigger _npc;
        [SerializeField] private KeyCode _talkKey = KeyCode.T;
        [SerializeField] private LlmCozyDialogueBridge _bridge;

        private string _keyState = "checking...";
        private GUIStyle _style;

        private void Start()
        {
            // Editor-only key lookup; in a player build this reports the env var only.
            string key = OpenRouterApiKey.Resolve("OPENROUTER_API_KEY");
            _keyState = string.IsNullOrWhiteSpace(key)
                ? "NO API KEY - replies will use the offline fallback"
                : "API key found";
        }

        private void Update()
        {
            if (!Input.GetKeyDown(_talkKey)) return;
            if (DialogueManager.IsDialogueOpen) return;
            if (_npc != null) _npc.StartTalk();
        }

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            GUILayout.BeginArea(new Rect(14, 14, 560, 150));
            GUILayout.Label("<b>Bloomwake - LLM dialogue demo</b>", _style);
            GUILayout.Label("Press <b>" + _talkKey + "</b> to talk to the NPC. Type in the box, Enter to send, Esc to leave.", _style);
            GUILayout.Label("The reply is rendered by CozyDialogue's FieldDialogue, not by the LLM module's own panel.", _style);
            GUILayout.Label(_keyState, _style);
            if (_bridge != null && !_bridge.IsHooked)
                GUILayout.Label("<color=#c86>bridge not hooked - check the FieldDialogue reference</color>", _style);
            GUILayout.EndArea();
        }
    }
}
