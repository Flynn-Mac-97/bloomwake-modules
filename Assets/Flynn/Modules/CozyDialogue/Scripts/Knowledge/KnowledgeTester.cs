using UnityEngine;
using Flynn.Feel;

namespace Flynn.Modules.Dialogue
{
    /// <summary>
    /// Rig keys for the knowledge grove demo: Y = mock trust up (unlocks the trust-gated
    /// choice). Module scaffolding.
    /// </summary>
    public class KnowledgeTester : MonoBehaviour
    {
        public KnowledgeBase knowledge;

        void Update()
        {
            if (knowledge != null && Input.GetKeyDown(KeyCode.Y))
                knowledge.RaiseTrust(1);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 900, 44),
                "[KnowledgeGrove] click NPC = talk | click object = inspect | Y = +trust | Esc = leave talk");
        }
    }
}
