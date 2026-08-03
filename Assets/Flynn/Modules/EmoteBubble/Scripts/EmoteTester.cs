using UnityEngine;
using Flynn.Feel;

namespace Flynn.Modules.Emote
{
    /// <summary>
    /// Standalone test rig for the module's own scene — proves EmoteBubble with ZERO other
    /// modules present. Keyboard fires every tone on the selected speaker; hold H to hammer
    /// the spam rule. Module scaffolding: stays behind when EmoteBubble graduates.
    /// </summary>
    public class EmoteTester : MonoBehaviour
    {
        [Tooltip("Dummy speakers in the test scene. Tab cycles which one talks.")]
        public EmoteSpeaker[] speakers;

        int _index;

        EmoteSpeaker Current =>
            (speakers != null && speakers.Length > 0) ? speakers[_index % speakers.Length] : null;

        void Update()
        {
            var s = Current;
            if (s == null) return;

            if (Input.GetKeyDown(KeyCode.Tab)) _index++;
            if (Input.GetKeyDown(KeyCode.Alpha1)) s.Say("hello there");
            if (Input.GetKeyDown(KeyCode.Alpha2)) s.SayHappy("lovely day");
            if (Input.GetKeyDown(KeyCode.Alpha3)) s.Eek();
            if (Input.GetKeyDown(KeyCode.Alpha4)) s.Heart();
            if (Input.GetKeyDown(KeyCode.Alpha5)) s.Sleep();
            if (Input.GetKeyDown(KeyCode.Alpha6)) s.Curious();
            if (Input.GetKey(KeyCode.H)) s.Eek();   // spam hammer — minInterval must hold
        }

        // Dev-tool UI (brutalist scope is fine here — not player-facing).
        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 640, 44),
                $"[EmoteTester] speaker: {(Current != null ? Current.name : "none")}  |  " +
                "Tab=switch  1=say 2=happy 3=eek 4=heart 5=zZz 6=?  hold H=spam test");
        }
    }
}
