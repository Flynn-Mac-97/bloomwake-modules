using UnityEngine;
using Flynn.Feel;

namespace Flynn.Modules.Dialogue
{
    /// <summary>
    /// Standalone rig for the module's own scene: T starts a scripted cozy conversation.
    /// Module scaffolding — stays behind if DialogueBox graduates.
    /// </summary>
    public class DialogueTester : MonoBehaviour
    {
        [Tooltip("The box under test.")]
        public DialogueBox box;

        [Tooltip("Scripted lines (VibeSpec §15 voice). Edit freely to test pacing.")]
        public DialogueLine[] lines =
        {
            new DialogueLine { speaker = "Rowan", text = "Oh, hello you." },
            new DialogueLine { speaker = "Rowan", text = "The wind has been kind today, hasn't it? The whole grove is humming." },
            new DialogueLine { speaker = "Rowan", text = "Take a berry for the road. The bushes made plenty this week." },
        };

        void Update()
        {
            if (box == null) return;
            if (Input.GetKeyDown(KeyCode.T) && !box.IsOpen)
                box.Show(lines);
        }

        // Dev-tool UI (brutalist scope — not player-facing).
        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 640, 44),
                "[DialogueTester] T = start conversation | Space/E/click = skip -> advance | Esc = cancel");
        }
    }
}
