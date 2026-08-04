using UnityEngine;
using Flynn.Npc;
using Flynn.Feel;   // KnowledgeBase

namespace Flynn.Modules.DialogueLLM
{
    /// On-screen driver for the community demo: pick an NPC, see what you have learned.
    ///
    /// Demo scaffolding, not shipping UI. In a real scene you would drive
    /// <see cref="NpcTalkTrigger.StartTalk"/> from your interaction system and drop this.
    public class CommunityDemoDriver : MonoBehaviour
    {
        [SerializeField] private NpcTalkTrigger[] _npcs;
        [SerializeField] private KnowledgeBase _knowledge;

        private string _keyState = "checking...";
        private GUIStyle _style;

        private void Start()
        {
            string key = OpenRouterApiKey.Resolve("OPENROUTER_API_KEY");
            _keyState = string.IsNullOrWhiteSpace(key)
                ? "NO API KEY - the NPCs will fall back to their scripted lines"
                : "API key found";
        }

        private void Update()
        {
            if (DialogueManager.IsDialogueOpen || _npcs == null) return;
            for (int i = 0; i < _npcs.Length && i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                if (_npcs[i] != null) _npcs[i].StartTalk();
            }
        }

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            GUILayout.BeginArea(new Rect(14, 14, 620, 190));
            GUILayout.Label("<b>Mosslight Clearing - LLM community demo</b>", _style);

            if (_npcs != null)
                for (int i = 0; i < _npcs.Length; i++)
                    if (_npcs[i] != null)
                        GUILayout.Label($"<b>{i + 1}</b>  talk to {_npcs[i].gameObject.name}", _style);

            GUILayout.Label("<b>Y</b>  open the Field Guide. Type in the box, Enter to send, Esc to leave.", _style);
            GUILayout.Label("Ask about the spring, the mast, the bees, the kiln - or about each other.", _style);
            if (_knowledge != null)
                GUILayout.Label($"Field guide: <b>{_knowledge.KnownCount}</b> / {_knowledge.TotalCount} entries known", _style);
            GUILayout.Label(_keyState, _style);
            GUILayout.EndArea();
        }
    }
}
