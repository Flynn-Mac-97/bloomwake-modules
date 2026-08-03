using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Hover action feedback (Stage E1): over an interactable the OS cursor hides and this
    /// small circle BECOMES the cursor — popping in at the pointer and cutely lerping after
    /// it (soft chase, not rigid lock). Verb glyphs: cream chat bubble (green) for NPCs,
    /// magnifier (amber) for inspectables, signal (copper) for the transmitter. Never blocks
    /// clicks. One instance on FieldUI; HoverCueTarget components call Show/Hide.
    /// </summary>
    public class HoverCue : MonoBehaviour
    {
        public enum Kind { Chat, Inspect, Decode }

        public FieldArchiveHud hud;
        [Tooltip("UI/HoverCue.uxml")]
        public VisualTreeAsset cueUxml;
        [Tooltip("Cue hides while the dialogue box is up.")]
        public FieldDialogue dialogue;
        [Tooltip("E2: known targets get the recorded badge; right-click jumps to the entry.")]
        public KnowledgeBase knowledge;
        public FieldArchive archive;
        [Tooltip("Chase easing — higher snaps harder, lower drifts dreamier.")]
        public float followSpeed = 14f;

        VisualElement _root, _cue;
        Vector2 _pos;
        string _knownTopic;
        bool _shown;

        public void Show(Kind kind) => Show(kind, null, false);
        public void Show(Kind kind, string topicId) => Show(kind, topicId, false);

        /// <summary>
        /// topicId enables the E2 journal tie-in when that topic is known.
        /// recorded forces the "seen" badge for objects tracked locally (already
        /// inspected) rather than through the KnowledgeBase.
        /// </summary>
        public void Show(Kind kind, string topicId, bool recorded)
        {
            if (hud == null || hud.Root == null || cueUxml == null) return;
            if (dialogue != null && dialogue.IsOpen) return;
            if (archive != null && archive.IsOpen) return;
            if (_root == null)
            {
                _root = hud.Mount(cueUxml);
                _cue = _root.Q(className: "hovercue");
            }

            bool known = recorded
                || (!string.IsNullOrEmpty(topicId) && knowledge != null && knowledge.IsKnown(topicId));
            _knownTopic = known ? topicId : null;

            _cue.EnableInClassList("hovercue--chat", kind == Kind.Chat);
            _cue.EnableInClassList("hovercue--inspect", kind == Kind.Inspect);
            _cue.EnableInClassList("hovercue--decode", kind == Kind.Decode);
            _cue.EnableInClassList("hovercue--known", known);

            _shown = true;
            UnityEngine.Cursor.visible = false;            // the cue IS the cursor now
            _root.style.display = DisplayStyle.Flex;
            _pos = PointerPos();               // pop in AT the pointer, then chase
            Place(_pos);
            if (!_cue.ClassListContains("hovercue--in"))
            {
                _cue.RemoveFromClassList("hovercue--in");
                _cue.schedule.Execute(() => { if (_shown) _cue.AddToClassList("hovercue--in"); }).StartingIn(15);
            }
        }

        public void Hide()
        {
            if (_cue == null || !_shown) return;
            _shown = false;
            UnityEngine.Cursor.visible = true;
            _cue.RemoveFromClassList("hovercue--in");
            _root.schedule.Execute(() =>
            {
                if (!_shown) _root.style.display = DisplayStyle.None;
            }).StartingIn(200);
        }

        void OnDisable()
        {
            UnityEngine.Cursor.visible = true;             // never leave the player cursorless
        }

        void Update()
        {
            if (!_shown) return;
            if (dialogue != null && dialogue.IsOpen) { Hide(); return; }
            if (archive != null && archive.IsOpen) { Hide(); return; }
            // E2 quick-jump: right-click a known thing → drawer opens straight to its entry.
            if (_knownTopic != null && Input.GetMouseButtonDown(1))
            {
                var topic = _knownTopic;
                Hide();
                if (archive != null) archive.OpenTo(topic);
                return;
            }
            // Editor/game-view re-shows the OS cursor on focus changes — enforce per frame.
            if (UnityEngine.Cursor.visible) UnityEngine.Cursor.visible = false;
            // Cute chase: ease toward the pointer instead of locking to it.
            _pos = Vector2.Lerp(_pos, PointerPos(), 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            Place(_pos);
        }

        Vector2 PointerPos()
        {
            if (_cue == null || _cue.panel == null) return _pos;
            var screen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return RuntimePanelUtils.ScreenToPanel(_cue.panel, screen);
        }

        void Place(Vector2 p)
        {
            if (_cue == null) return;
            _cue.style.left = p.x - 17f;       // centered on the pointer (34px circle)
            _cue.style.top = p.y - 17f;
        }
    }
}
