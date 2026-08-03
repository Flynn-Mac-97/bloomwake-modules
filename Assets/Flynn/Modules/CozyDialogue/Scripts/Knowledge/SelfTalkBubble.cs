using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Repairbot self-talk (design §5) — the SAME system as the ambient bark: the small dark
    /// speech bubble springs up from the inspected object and the line typewrites while the
    /// auto-width box grows. No reply chips, no name — the bot muttering to itself as it
    /// wanders and looks at things. Mounts the shared Bark.uxml. Inspectables call
    /// ObserveFrom(transform, line).
    /// </summary>
    public class SelfTalkBubble : MonoBehaviour
    {
        public FieldArchiveHud hud;
        [Tooltip("UI/Bark.uxml — the shared bark bubble skeleton.")]
        public VisualTreeAsset bubbleUxml;
        [Tooltip("Typewriter pacing + blips (same asset as the bark).")]
        public DialogueStyle style;
        [Tooltip("Seconds the bubble stays after the line lands.")]
        public float hold = 3.2f;
        [Tooltip("World units above the inspected object's visual TOP (sprite bounds, not pivot).")]
        public float heightOffset = 0.2f;
        [Tooltip("Blip pitch base — a touch higher than NPC barks (smaller voice).")]
        public float voicePitch = 1.2f;

        VisualElement _root, _bubble;
        Label _line;
        AudioSource _voice;
        Transform _anchor;
        Coroutine _typeCo;
        float _hideAt;
        bool _shown, _typing;

        void Awake()
        {
            _voice = GetComponent<AudioSource>();
            if (_voice == null) _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
        }

        public void ObserveFrom(Transform anchor, string line)
        {
            if (hud == null || hud.Root == null || bubbleUxml == null || string.IsNullOrEmpty(line)) return;
            if (_root == null)
            {
                _root = hud.Mount(bubbleUxml);
                _bubble = _root.Q(className: "bark");
                _line = _root.Q<Label>("bark-line");
                _root.Q("bark-replies").style.display = DisplayStyle.None;   // self-talk = info-only
            }

            _anchor = anchor;
            _shown = true;
            _line.text = "";
            _root.style.display = DisplayStyle.Flex;
            _bubble.RemoveFromClassList("bark--in");
            Track();
            _bubble.schedule.Execute(() => _bubble.AddToClassList("bark--in")).StartingIn(20);

            _hideAt = float.MaxValue;   // clock starts when the line lands
            if (_typeCo != null) StopCoroutine(_typeCo);
            _typeCo = StartCoroutine(TypeLine(line));
        }

        void Update()
        {
            if (!_shown) return;
            Track();
            if (_typing) return;
            if (Time.time >= _hideAt)
            {
                _shown = false;
                _bubble.RemoveFromClassList("bark--in");
                _root.schedule.Execute(() =>
                {
                    if (!_shown) _root.style.display = DisplayStyle.None;
                }).StartingIn(240);
            }
        }

        // Same growing-box typewriter as AmbientBark.
        IEnumerator TypeLine(string line)
        {
            _typing = true;
            float cps = style != null ? style.charsPerSecond * 1.4f : 45f;
            float delay = 1f / Mathf.Max(1f, cps);

            for (int i = 1; i <= line.Length; i++)
            {
                _line.text = line.Substring(0, i);
                char c = line[i - 1];
                if (style != null && style.blip != null && !char.IsWhiteSpace(c)
                    && i % Mathf.Max(1, style.blipEveryN) == 0)
                {
                    _voice.pitch = voicePitch + Random.Range(-style.blipPitchJitter, style.blipPitchJitter);
                    _voice.PlayOneShot(style.blip, style.blipVolume * 0.7f);
                }
                float mult = (c == '.' || c == '!' || c == '?') ? 4f : (c == ',') ? 2f : 1f;
                yield return new WaitForSeconds(delay * mult);
            }

            _typing = false;
            _hideAt = Time.time + hold;
        }

        void Track()
        {
            var cam = Camera.main;
            if (cam == null || _anchor == null || _bubble == null || _bubble.panel == null) return;
            // Anchor to the sprite's visual TOP (pivots vary; a fixed offset off the
            // pivot floats the bubble too high on small/centre-pivot sprites).
            var rend = _anchor.GetComponentInChildren<Renderer>();
            Vector3 top = rend != null
                ? new Vector3(rend.bounds.center.x, rend.bounds.max.y, _anchor.position.z)
                : _anchor.position;
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(
                _bubble.panel, top + Vector3.up * heightOffset, cam);
            float w = _bubble.resolvedStyle.width;
            _bubble.style.left = float.IsNaN(w) ? p.x : p.x - w * 0.5f;
            _bubble.style.top = p.y;
        }
    }
}
