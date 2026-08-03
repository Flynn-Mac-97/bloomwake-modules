using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Single front-end facade for the Cozy Dialogue UI prefab (the FieldUI hub).
    /// World triggers and other systems hold ONE reference to this and call its
    /// methods instead of wiring to five separate presenters — so the prefab drops
    /// into any scene and everything is driven through one uniform seam.
    ///
    /// Pure display: it only forwards to the presenters sitting on this same
    /// GameObject (FieldDialogue / HoverCue / SelfTalkBubble / FieldArchive /
    /// FieldArchiveHud). It holds no game state. Edit the prefab here, callers keep
    /// working — the API is the contract, not the internals.
    /// </summary>
    [DisallowMultipleComponent]
    public class CozyDialogueUI : MonoBehaviour
    {
        [SerializeField] private FieldDialogue _dialogue;
        [SerializeField] private HoverCue _hover;
        [SerializeField] private SelfTalkBubble _tag;
        [SerializeField] private FieldArchive _archive;
        [SerializeField] private FieldArchiveHud _hud;

        /// <summary>Player typed/submitted a line in the dialogue panel.</summary>
        public event Action<string> PlayerSubmitted;
        /// <summary>Dialogue panel closed.</summary>
        public event Action DialogueClosed;

        // ── Lifecycle ────────────────────────────────────

        // The five presenters all live on this GameObject (the FieldUI hub), so the
        // facade needs no inspector wiring — it resolves them itself.
        private void CollectRefs()
        {
            if (_dialogue == null) _dialogue = GetComponent<FieldDialogue>();
            if (_hover == null) _hover = GetComponent<HoverCue>();
            if (_tag == null) _tag = GetComponent<SelfTalkBubble>();
            if (_archive == null) _archive = GetComponent<FieldArchive>();
            if (_hud == null) _hud = GetComponent<FieldArchiveHud>();
        }

        private void Reset() => CollectRefs();   // fill refs when added in the editor
        private void Awake() => CollectRefs();

        private void OnEnable()
        {
            if (_dialogue == null) return;
            _dialogue.PlayerSubmitted += OnSubmitted;
            _dialogue.Closed += OnClosed;
        }

        private void OnDisable()
        {
            if (_dialogue == null) return;
            _dialogue.PlayerSubmitted -= OnSubmitted;
            _dialogue.Closed -= OnClosed;
        }

        private void OnSubmitted(string line) => PlayerSubmitted?.Invoke(line);
        private void OnClosed() => DialogueClosed?.Invoke();

        // ── Hover cue (cursor verb circle) ───────────────
        public void ShowHover(HoverCue.Kind kind, string topicId = null) => _hover?.Show(kind, topicId);
        public void ShowHover(HoverCue.Kind kind, string topicId, bool recorded) => _hover?.Show(kind, topicId, recorded);
        public void HideHover() => _hover?.Hide();

        // ── Self-talk tag (the little popup line) ────────
        public void ShowTag(Transform anchor, string line) => _tag?.ObserveFrom(anchor, line);

        // ── NPC dialogue panel ───────────────────────────
        public bool DialogueOpen => _dialogue != null && _dialogue.IsOpen;

        public void OpenDialogue(ConversationSO conversation, Transform anchor)
        {
            if (_dialogue == null || _dialogue.IsOpen) return;
            _dialogue.Begin(conversation, anchor != null ? anchor : transform);
        }

        public void OpenFreeText(string speaker, string greeting) => _dialogue?.OpenFreeText(speaker, greeting);
        public void RevealLine(string line) => _dialogue?.RevealLine(line);
        public void LeaveDialogue() => _dialogue?.Leave();

        // ── Field archive (the guide) ────────────────────
        public bool ArchiveOpen => _archive != null && _archive.IsOpen;
        public void OpenArchive() => _archive?.Open();
        public void OpenArchiveTo(string topicId) => _archive?.OpenTo(topicId);
        public void CloseArchive() => _archive?.Close();
        public void ToggleArchive() => _archive?.Toggle();

        // ── HUD (toasts + overlay mount for barks) ───────
        public void Push(string message) => _hud?.Push(message);
        public VisualElement HudRoot => _hud != null ? _hud.Root : null;
        public VisualElement MountOverlay(VisualTreeAsset vta) => _hud != null ? _hud.Mount(vta) : null;
    }
}
