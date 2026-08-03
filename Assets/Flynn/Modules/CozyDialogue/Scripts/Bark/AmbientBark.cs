using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Ambient bark presenter (handover §1) — game speech bubble, Stardew/Oxenfree energy:
    /// a tiny bar pops UP from the NPC (scale bounce from the tail point), the line
    /// typewrites while the auto-width bubble grows to fit, then reply chips (if any) fade
    /// in underneath. No name tag — the bubble's position IS the identity. Two modes:
    /// info-only (line, auto-hide) and responsive (chips + Chat handoff to FieldDialogue).
    /// Say(line) = UnityEvent-friendly one-liner for other systems. Debug: B forces the bark.
    /// </summary>
    public class AmbientBark : MonoBehaviour
    {
        public BarkSO bark;
        public CozyDialogueUI ui;
        public KnowledgeBase knowledge;
        [Tooltip("Optional: typewriter pacing + blips (barks type ~1.4x faster).")]
        public DialogueStyle style;
        [Tooltip("UI/Bark.uxml")]
        public VisualTreeAsset barkUxml;
        [Tooltip("Found by 'Player' tag when empty.")]
        public Transform player;
        [Tooltip("World units above this transform the bubble anchors to.")]
        public float heightOffset = 1.1f;

        VisualElement _root, _bubble, _replies;
        Label _line;
        AudioSource _voice;
        Coroutine _typeCo;
        float _cooldownUntil, _timeoutAt, _closeAt;
        bool _shown, _reacting, _typing;

        void Awake()
        {
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
        }

        void Start()
        {
            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }
        }

        void Update()
        {
            if (bark == null || ui == null || ui.HudRoot == null) return;

            bool inDialogue = ui != null && ui.DialogueOpen;
            if (inDialogue) { if (_shown) Hide(); return; }

            if (Input.GetKeyDown(KeyCode.B) && !_shown) { Show(); return; }

            if (!_shown)
            {
                if (player != null
                    && Vector2.Distance(player.position, transform.position) <= bark.triggerRadius
                    && Time.time >= _cooldownUntil)
                    Show();
                return;
            }

            TrackAnchor();
            if (_typing) return;

            if (_reacting)
            {
                if (Time.time >= _closeAt) Hide();
                return;
            }

            bool walkedAway = player != null
                && Vector2.Distance(player.position, transform.position) > bark.triggerRadius * 1.35f;
            if (walkedAway || Time.time >= _timeoutAt) Hide();
        }

        void Show()
        {
            if (!EnsureBuilt()) return;

            var a = _root.Q<Label>("bark-reply-a");
            var b = _root.Q<Label>("bark-reply-b");
            var chat = _root.Q<Label>("bark-chat");
            a.text = bark.replyA.label;
            b.text = bark.replyB.label;
            a.style.display = string.IsNullOrEmpty(bark.replyA.label) ? DisplayStyle.None : DisplayStyle.Flex;
            b.style.display = string.IsNullOrEmpty(bark.replyB.label) ? DisplayStyle.None : DisplayStyle.Flex;
            chat.style.display = bark.chatConversation != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool anyChip = bark.allowResponses
                && (!string.IsNullOrEmpty(bark.replyA.label) || !string.IsNullOrEmpty(bark.replyB.label)
                    || bark.chatConversation != null);

            Raise();
            StartType(bark.line, chipsAfter: anyChip, closeAfter: false);
        }

        /// <summary>
        /// UnityEvent-friendly info line (mode 1): tiny bar over the head, no chips,
        /// auto-hides. Lets inspection/world events reuse the same bubble.
        /// </summary>
        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line) || ui == null || ui.HudRoot == null) return;
            if (!EnsureBuilt()) return;
            Raise();
            _reacting = true;
            _closeAt = float.MaxValue;
            StartType(line, chipsAfter: false, closeAfter: true);
        }

        bool EnsureBuilt()
        {
            if (_root != null) return true;
            if (barkUxml == null) return false;
            _root = ui.MountOverlay(barkUxml);
            _bubble = _root.Q(className: "bark");
            _line = _root.Q<Label>("bark-line");
            _replies = _root.Q("bark-replies");
            _root.Q<Label>("bark-reply-a").RegisterCallback<ClickEvent>(_ => Reply(bark.replyA));
            _root.Q<Label>("bark-reply-b").RegisterCallback<ClickEvent>(_ => Reply(bark.replyB));
            _root.Q<Label>("bark-chat").RegisterCallback<ClickEvent>(_ => Chat());
            return true;
        }

        void Raise()
        {
            _shown = true;
            _reacting = false;
            _line.text = "";
            _replies.style.display = DisplayStyle.None;
            _root.style.display = DisplayStyle.Flex;
            _bubble.RemoveFromClassList("bark--in");
            TrackAnchor();
            _bubble.schedule.Execute(() => _bubble.AddToClassList("bark--in")).StartingIn(20);
        }

        void Hide()
        {
            _shown = false;
            _reacting = false;
            _typing = false;
            if (_typeCo != null) StopCoroutine(_typeCo);
            _bubble.RemoveFromClassList("bark--in");
            _root.schedule.Execute(() =>
            {
                if (!_shown) _root.style.display = DisplayStyle.None;
            }).StartingIn(240);
            _cooldownUntil = Time.time + (bark != null ? bark.cooldown : 20f);
        }

        void Reply(BarkSO.QuickReply reply)
        {
            if (_reacting || _typing) return;
            _reacting = true;
            _closeAt = float.MaxValue;
            _replies.style.display = DisplayStyle.None;
            if (knowledge != null && reply.trustDelta != 0) knowledge.trust += reply.trustDelta;
            StartType(reply.reaction, chipsAfter: false, closeAfter: true);
        }

        void Chat()
        {
            Hide();
            if (ui != null && bark.chatConversation != null)
                ui.OpenDialogue(bark.chatConversation, transform);
        }

        void StartType(string line, bool chipsAfter, bool closeAfter)
        {
            if (_typeCo != null) StopCoroutine(_typeCo);
            _timeoutAt = float.MaxValue;   // clock starts when the line lands
            _typeCo = StartCoroutine(TypeLine(line, chipsAfter, closeAfter));
        }

        // Bubble is auto-width, so the box visibly grows with the text — that's the juice.
        IEnumerator TypeLine(string line, bool chipsAfter, bool closeAfter)
        {
            _typing = true;
            _line.text = "";
            float cps = style != null ? style.charsPerSecond * 1.4f : 45f;
            float delay = 1f / Mathf.Max(1f, cps);

            for (int i = 1; i <= line.Length; i++)
            {
                _line.text = line.Substring(0, i);
                char c = line[i - 1];
                if (style != null && style.blip != null && !char.IsWhiteSpace(c)
                    && i % Mathf.Max(1, style.blipEveryN) == 0)
                {
                    _voice.pitch = 1.1f + Random.Range(-style.blipPitchJitter, style.blipPitchJitter);
                    _voice.PlayOneShot(style.blip, style.blipVolume * 0.8f);
                }
                float mult = (c == '.' || c == '!' || c == '?') ? 4f : (c == ',') ? 2f : 1f;
                yield return new WaitForSeconds(delay * mult);
            }

            _typing = false;
            if (chipsAfter)
            {
                yield return new WaitForSeconds(0.25f);
                if (_shown && !_reacting) _replies.style.display = DisplayStyle.Flex;
            }
            _timeoutAt = Time.time + (bark != null ? bark.timeout : 6f);
            if (closeAfter) _closeAt = Time.time + 2.4f;
        }

        void TrackAnchor()
        {
            var cam = Camera.main;
            if (cam == null || _bubble == null || _bubble.panel == null) return;
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(
                _bubble.panel, transform.position + Vector3.up * heightOffset, cam);
            // Manual X centering so the typewriter growth stays symmetric over the NPC
            // (percent translate re-resolves lazily while the auto-width box grows).
            float w = _bubble.resolvedStyle.width;
            _bubble.style.left = float.IsNaN(w) ? p.x : p.x - w * 0.5f;
            _bubble.style.top = p.y;
        }
    }
}
