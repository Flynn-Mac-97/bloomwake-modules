using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Field Archive dialogue slice (HANDOVER.md §3.3, §6 + design §4 highlighted topics).
    /// Skeleton in Dialogue.uxml. Typewriter runs on plain text; when the line lands it is
    /// rebuilt as a word-flow where <link="id">terms</link> become clickable copper mono
    /// spans (Pentiment) — 2022.3 UI Toolkit has no link-tag events, so terms are word
    /// Labels. Clicking a term discovers/touches the topic and opens the cream term card
    /// Choices, locked hints, say-row,
    /// portrait rise as before.
    /// </summary>
    public class FieldDialogue : MonoBehaviour
    {
        const string InputHint = "…or say it in your own words";

        [Tooltip("Provides the UI Toolkit root + theme.")]
        public FieldArchiveHud hud;
        [Tooltip("UI/Dialogue.uxml — box + portrait + term card skeleton.")]
        public VisualTreeAsset dialogueUxml;
        [Tooltip("Pacing + blips reused from the dialogue style asset.")]
        public DialogueStyle style;
        public KnowledgeBase knowledge;
        [Tooltip("NPC portrait texture (placeholder: luma-portrait.png).")]
        public Texture2D portrait;
        [Tooltip("Line played when typed text matches no choice (handover §3.3 'unheard').")]
        public string unheardLine = "Hm. Say it another way, maybe?";
        [Tooltip("Visible chars per page — long lines split Pokémon-style, stepped with the arrow.")]
        public int pageCharBudget = 130;
        [Tooltip("Transcript edge-fade band (px): paragraphs fade out near the scroll window's top/bottom and fade back to full when scrolled into the middle.")]
        public float fadeBand = 24f;

        public bool IsOpen { get; private set; }

        VisualElement _root, _box, _choices, _portrait, _sayRow, _proceed, _trustChip, _tail;
        Label _trustCount;
        ScrollView _scroll;              // fixed-height transcript — player can scroll back
        Label _ghost;                    // invisible full-page label: reserves final height during reveal
        bool _settled;                   // all pages read: compact window + choices; unlocks back-scroll
        int _shownTrust = int.MinValue;
        Label _para;                     // paragraph currently being typed
        VisualElement _termCard;
        Label _nameLabel, _roleLabel, _termTab, _termBody, _termNote;
        TextField _input;
        Button _send;
        AudioSource _voice;
        IVisualElementScheduledItem _bob;
        IVisualElementScheduledItem _breathe;

        ConversationSO _convo;
        string _nodeId;
        ConversationSO.Node _node;
        string _rawLine = "";
        bool _revealing;
        bool _awaitAdvance;
        string _fullLine = "";
        List<string> _pages = new List<string>();
        int _pageIx;
        Coroutine _reveal;

        // Free-text / LLM mode: driven by an external brain (composition bridge) instead of a
        // ConversationSO. RevealLine shows model-supplied lines; the say-row raises PlayerSubmitted.
        bool _freeText;
        public event System.Action<string> PlayerSubmitted;
        public event System.Action Closed;

        struct Seg
        {
            public string text;
            public string termId;
        }

        void Awake()
        {
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
        }

        // ── Public API (NpcConversationTrigger-compatible) ──────────────────

        public void Begin(ConversationSO conversation, Transform anchor)
        {
            if (conversation == null || IsOpen || hud == null || hud.Root == null) return;
            if (_root == null)
            {
                if (dialogueUxml == null)
                {
                    Debug.LogWarning("[FieldDialogue] dialogueUxml not assigned.");
                    return;
                }
                Build();
            }

            _convo = conversation;
            _freeText = false;
            IsOpen = true;
            _scroll.Clear();   // fresh transcript per conversation
            _root.style.display = DisplayStyle.Flex;
            _box.RemoveFromClassList("dialogue-box--closing");
            _box.schedule.Execute(() => _box.AddToClassList("dialogue-box--open")).StartingIn(30);
            _portrait.style.backgroundImage = portrait != null ? new StyleBackground(portrait) : null;
            _portrait.schedule.Execute(() => _portrait.AddToClassList("portrait--in")).StartingIn(120);
            _box.schedule.Execute(() => { if (IsOpen) StartBreathe(); }).StartingIn(650);

            _shownTrust = int.MinValue;
            RefreshTrust(pulse: false);
            EnterNode(_convo.startNodeId);
        }

        public void Leave()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _freeText = false;
            Closed?.Invoke();
            CloseTerm();
            if (_reveal != null) StopCoroutine(_reveal);
            _awaitAdvance = false;
            HideArrow();
            _choices.RemoveFromClassList("choices--open");
            _sayRow.RemoveFromClassList("say-row--open");
            _portrait.RemoveFromClassList("portrait--in");
            StopBreathe();
            _box.AddToClassList("dialogue-box--closing");
            _box.RemoveFromClassList("dialogue-box--open");
            _root.schedule.Execute(() => _root.style.display = DisplayStyle.None).StartingIn(240);
        }

        // ── Free-text / LLM mode (composition bridge) ───────────────────────

        /// Open the box with no ConversationSO — an external brain feeds lines via RevealLine
        /// and receives the player's typed turns through the PlayerSubmitted event.
        public void OpenFreeText(string speaker, string greeting)
        {
            if (IsOpen || hud == null || hud.Root == null) return;
            if (_root == null)
            {
                if (dialogueUxml == null) { Debug.LogWarning("[FieldDialogue] dialogueUxml not assigned."); return; }
                Build();
            }

            _convo = null;
            _freeText = true;
            IsOpen = true;
            _scroll.Clear();
            _root.style.display = DisplayStyle.Flex;
            _box.RemoveFromClassList("dialogue-box--closing");
            _box.schedule.Execute(() => _box.AddToClassList("dialogue-box--open")).StartingIn(30);
            _portrait.style.backgroundImage = portrait != null ? new StyleBackground(portrait) : null;
            _portrait.schedule.Execute(() => _portrait.AddToClassList("portrait--in")).StartingIn(120);
            _box.schedule.Execute(() => { if (IsOpen) StartBreathe(); }).StartingIn(650);

            _shownTrust = int.MinValue;
            RefreshTrust(pulse: false);
            SetBadgeName(speaker ?? "");
            RevealLine(greeting ?? "");
        }

        /// Reveal an externally-supplied line in the current session (free-text mode).
        public void RevealLine(string line)
        {
            if (!IsOpen) return;
            _freeText = true;
            StartReveal(line ?? "", default);
        }

        void Update()
        {
            if (!IsOpen) return;
            bool press = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E);
            if (press)
            {
                if (_revealing) { Dip(); SkipReveal(); }
                else if (_awaitAdvance) Advance();
            }
            if (Input.GetKeyDown(KeyCode.Escape)) Leave();

            // Trust is a plain int on KnowledgeBase (no change event) — poll while open.
            if (knowledge != null && knowledge.trust != _shownTrust)
                RefreshTrust(pulse: _shownTrust != int.MinValue);
        }

        // ── Flow ────────────────────────────────────────────────────────────

        void EnterNode(string id)
        {
            if (!_convo.TryGetNode(id, out var node)) { Leave(); return; }
            _nodeId = id;
            SetBadgeName(node.speaker);
            CloseTerm();

            if (!string.IsNullOrEmpty(node.discoverTopicId))
                knowledge?.Discover(node.discoverTopicId);

            StartReveal(node.line, node);
        }

        void StartReveal(string rawLine, ConversationSO.Node node)
        {
            _node = node;
            _pages = Paginate(rawLine, Mathf.Max(40, pageCharBudget));
            _pageIx = 0;
            SetSettled(false);   // reading phase: text owns the whole window again
            _choices.RemoveFromClassList("choices--open");
            _sayRow.RemoveFromClassList("say-row--open");
            _choices.Clear();
            ShowPage();
        }

        // Reading phase ↔ settled phase. Reading: only the current segment visible, wheel
        // locked (scroll affordance hidden — Contract §2). Settled (all pages read): the
        // sequence's earlier pages re-show, back-scroll unlocks with the fade vignette,
        // choices rise below (window height itself is fixed in USS).
        void SetSettled(bool settled)
        {
            _settled = settled;
            _scroll.EnableInClassList("dialogue-scroll--settled", settled);
            _scroll.verticalScrollerVisibility =
                settled ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            if (settled)
            {
                foreach (var child in _scroll.Children())
                    child.style.display = DisplayStyle.Flex;
                ScrollToBottom();
            }
        }

        void ShowPage()
        {
            _rawLine = _pages[_pageIx];
            _awaitAdvance = false;
            HideArrow();

            // While typing, only the current segment shows — earlier pages stay in the
            // tree but hidden; SetSettled(true) re-shows them so the player can scroll
            // back through the finished sequence with the edge-fade vignette.
            foreach (var child in _scroll.Children())
                child.style.display = DisplayStyle.None;
            // A hidden ghost of the FULL page sits in the layout so content height is
            // stable while the typewriter runs (window height is fixed in USS).
            var holder = new VisualElement();
            _ghost = new Label(StripTags(_rawLine));
            _ghost.AddToClassList("dialogue-line");
            _ghost.visible = false;
            holder.Add(_ghost);
            _para = new Label("");
            _para.AddToClassList("dialogue-line");
            _para.AddToClassList("para");
            _para.style.position = Position.Absolute;
            _para.style.left = 0f;
            _para.style.top = 0f;
            _para.style.right = 6f;   // match the ghost's right margin so wrapping is identical
            holder.Add(_para);
            _scroll.Add(holder);
            // Reading pins to the TOP so every page starts on the same line;
            // bottom-pinning is only for the settled history view.
            ScrollToTop();

            if (_reveal != null) StopCoroutine(_reveal);
            _reveal = StartCoroutine(Reveal(StripTags(_rawLine)));
        }

        void ScrollToBottom()
        {
            _scroll.schedule.Execute(() =>
            {
                _scroll.verticalScroller.value = _scroll.verticalScroller.highValue;
                UpdateFades();
            }).StartingIn(10);
        }

        void ScrollToTop()
        {
            _scroll.schedule.Execute(() =>
            {
                _scroll.verticalScroller.value = 0f;
                UpdateFades();
            }).StartingIn(10);
        }

        // Reading vignette: each paragraph's opacity follows its place in the scroll
        // window — full in the middle, fading toward the top/bottom edges. While pinned
        // at the live bottom, older paragraphs also sit dimmed so the newest line reads
        // as current; scrolling back lifts that dim so history is fully readable.
        void UpdateFades()
        {
            if (_scroll == null) return;
            float vpH = _scroll.contentViewport.layout.height;
            if (float.IsNaN(vpH) || vpH <= 0f) return;
            float band = Mathf.Max(1f, fadeBand);
            float y = _scroll.scrollOffset.y;
            bool atBottom = y >= _scroll.verticalScroller.highValue - 8f;

            VisualElement last = null;
            foreach (var child in _scroll.Children()) last = child;

            foreach (var child in _scroll.Children())
            {
                float center = (child.layout.yMin + child.layout.yMax) * 0.5f - y;
                float edge = Mathf.Min(Mathf.Clamp01(center / band),
                                       Mathf.Clamp01((vpH - center) / band));
                float baseOp = (child == last || !atBottom) ? 1f : 0.55f;
                child.style.opacity = edge * baseOp;
            }
        }

        IEnumerator Reveal(string line)
        {
            _revealing = true;
            _fullLine = line;
            _para.text = "";
            float delay = 1f / Mathf.Max(1f, style.charsPerSecond);

            for (int i = 1; i <= _fullLine.Length; i++)
            {
                _para.text = _fullLine.Substring(0, i);
                char c = _fullLine[i - 1];
                if (!char.IsWhiteSpace(c) && style.blip != null
                    && i % Mathf.Max(1, style.blipEveryN) == 0)
                {
                    _voice.pitch = 1f + Random.Range(-style.blipPitchJitter, style.blipPitchJitter);
                    _voice.PlayOneShot(style.blip, style.blipVolume);
                }
                float mult = (c == '.' || c == '!' || c == '?') ? style.sentencePauseMult
                           : (c == ',' || c == ';' || c == ':') ? style.commaPauseMult : 1f;
                yield return new WaitForSeconds(delay * mult);
            }
            LandPage();
        }

        void SkipReveal()
        {
            if (_reveal != null) StopCoroutine(_reveal);
            LandPage();
        }

        // Page finished: mid-pages arm the continue arrow (Pokémon step); the last page
        // eases the choices in (handover §6: auto-reveal, no extra click at the end).
        void LandPage()
        {
            LandLine();
            if (_pageIx < _pages.Count - 1)
            {
                _awaitAdvance = true;
                ShowArrow();
            }
            else
            {
                HideArrow();
                // Beat, then the morph: window shrinks + choices rise together (one motion).
                _box.schedule.Execute(() =>
                {
                    if (!IsOpen || _revealing) return;
                    SetSettled(true);
                    if (_freeText) OpenSayRow();
                    else ShowChoices(_node);
                }).StartingIn(450);
            }
        }

        void Advance()
        {
            if (!_awaitAdvance) return;
            _awaitAdvance = false;
            Dip();
            _pageIx++;
            ShowPage();
        }

        // ── Continue arrow (kit sprite, gentle bob) ─────────────────────────

        void ShowArrow()
        {
            if (_proceed == null) return;
            _proceed.AddToClassList("proceed--in");
            _bob?.Pause();
            _bob = _proceed.schedule.Execute(() => _proceed.ToggleInClassList("proceed--alt"))
                .Every(420);
        }

        void HideArrow()
        {
            if (_proceed == null) return;
            _bob?.Pause();
            _proceed.RemoveFromClassList("proceed--in");
            _proceed.RemoveFromClassList("proceed--alt");
        }

        // Split a raw line (link tags intact) into pages by visible length. Link spans are
        // atomic; a sentence end past ~60% budget breaks early so pages read naturally.
        static List<string> Paginate(string raw, int budget)
        {
            var chunks = new List<(string raw, int len)>();
            var rx = new Regex("<link=\"[^\"]+\">.*?</link>");
            int pos = 0;
            void AddWords(string s)
            {
                foreach (var w in s.Split(' '))
                    if (w.Length > 0) chunks.Add((w, StripTags(w).Length));
            }
            foreach (Match m in rx.Matches(raw))
            {
                if (m.Index > pos) AddWords(raw.Substring(pos, m.Index - pos));
                chunks.Add((m.Value, StripTags(m.Value).Length));
                pos = m.Index + m.Length;
            }
            if (pos < raw.Length) AddWords(raw.Substring(pos));

            var pages = new List<string>();
            var sb = new System.Text.StringBuilder();
            int len = 0;
            foreach (var c in chunks)
            {
                if (len > 0 && len + 1 + c.len > budget)
                {
                    pages.Add(sb.ToString());
                    sb.Clear();
                    len = 0;
                }
                if (len > 0) { sb.Append(' '); len++; }
                sb.Append(c.raw);
                len += c.len;

                string visible = StripTags(c.raw).TrimEnd();
                char last = visible.Length > 0 ? visible[visible.Length - 1] : ' ';
                if ((last == '.' || last == '!' || last == '?') && len >= budget * 6 / 10)
                {
                    pages.Add(sb.ToString());
                    sb.Clear();
                    len = 0;
                }
            }
            if (len > 0) pages.Add(sb.ToString());
            if (pages.Count == 0) pages.Add("");
            return pages;
        }

        // Page finished typing: swap the plain paragraph for a term-aware word flow
        // in place (design §4) — earlier transcript paragraphs are untouched.
        void LandLine()
        {
            _revealing = false;
            if (_para == null) return;
            // page complete — drop the sizing ghost, return the paragraph to normal flow
            if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; }
            _para.style.position = Position.Relative;
            _para.text = _fullLine;
            var segs = Parse(_rawLine);
            bool hasTerm = false;
            foreach (var s in segs) if (s.termId != null) hasTerm = true;
            if (!hasTerm) { _para = null; ScrollToTop(); return; }

            var flow = new VisualElement();
            flow.AddToClassList("line-flow");
            flow.AddToClassList("para");
            foreach (var seg in segs)
            {
                var words = seg.text.Split(' ');
                for (int w = 0; w < words.Length; w++)
                {
                    if (string.IsNullOrEmpty(words[w])) continue;
                    if (seg.termId != null && w == 0)
                    {
                        var d = new VisualElement();
                        d.AddToClassList("term__diamond");
                        flow.Add(d);
                    }
                    var lbl = new Label(words[w]);
                    lbl.AddToClassList("lw");
                    if (seg.termId != null)
                    {
                        lbl.AddToClassList("term");
                        var id = seg.termId;
                        lbl.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); OpenTerm(id); });
                    }
                    flow.Add(lbl);
                }
            }
            var parent = _para.parent;
            parent.Insert(parent.IndexOf(_para), flow);
            parent.Remove(_para);
            _para = null;
            ScrollToTop();
        }

        static List<Seg> Parse(string raw)
        {
            var segs = new List<Seg>();
            var rx = new Regex("<link=\"(?<id>[^\"]+)\">(?<txt>.*?)</link>");
            int pos = 0;
            foreach (Match m in rx.Matches(raw))
            {
                if (m.Index > pos)
                    segs.Add(new Seg { text = StripTags(raw.Substring(pos, m.Index - pos)) });
                segs.Add(new Seg { text = StripTags(m.Groups["txt"].Value), termId = m.Groups["id"].Value });
                pos = m.Index + m.Length;
            }
            if (pos < raw.Length) segs.Add(new Seg { text = StripTags(raw.Substring(pos)) });
            return segs;
        }

        // ── Term card (design §4, mockup §3.6 — cream popover) ─────────────

        void OpenTerm(string id)
        {
            if (knowledge == null || knowledge.library == null) return;
            if (!knowledge.library.TryGet(id, out var def)) return;
            knowledge.Discover(id);   // discovery or quiet touch — same write path

            _termTab.text = def.displayName;
            _termBody.text = def.summary;
            _termBody.EnableInClassList("termcard__body--corrupt", false);
            _termNote.text = "“" + def.lockedHint + "”";

            _termCard.style.display = DisplayStyle.Flex;
            _termCard.RemoveFromClassList("termcard--in");
            _termCard.schedule.Execute(() => _termCard.AddToClassList("termcard--in")).StartingIn(15);
        }

        void CloseTerm()
        {
            if (_termCard == null) return;
            _termCard.RemoveFromClassList("termcard--in");
            _termCard.style.display = DisplayStyle.None;
        }

        // ── Choices / say-row (unchanged behavior) ──────────────────────────

        // Free-text mode: no choice pills — just raise the say-row for the player to type.
        void OpenSayRow()
        {
            _sayRow.schedule.Execute(() => _sayRow.AddToClassList("say-row--open")).StartingIn(30);
        }

        void ShowChoices(ConversationSO.Node node)
        {
            _choices.Clear();
            int i = 0;
            foreach (var choice in node.choices)
            {
                if (choice.isLeave) continue;   // leave lives on the cream badge
                bool available = Available(choice);

                var pill = new VisualElement();
                pill.AddToClassList("choice");

                if (available)
                {
                    var bullet = new VisualElement();
                    bullet.AddToClassList("choice__bullet");
                    if (choice.isKey) bullet.AddToClassList("choice__bullet--hot");
                    pill.Add(bullet);
                    pill.Add(new Label(choice.label));

                    var captured = choice;
                    // StopPropagation is load-bearing: EnterNode clears the choices, detaching
                    // this pill — the box's bubble handler can no longer tell the click came
                    // from a choice and would skip-reveal the fresh node instantly.
                    pill.RegisterCallback<ClickEvent>(e =>
                    {
                        e.StopPropagation();
                        Dip();
                        EnterNode(captured.nextId);
                    });
                }
                else
                {
                    pill.AddToClassList("choice--locked");
                    var lockIcon = new VisualElement();
                    lockIcon.AddToClassList("choice__lock");
                    pill.Add(lockIcon);
                    pill.Add(new Label(choice.label));
                    var hint = new Label((choice.lockedReason ?? "locked").ToUpperInvariant());
                    hint.AddToClassList("choice__hint");
                    pill.Add(hint);
                }

                // container eases open first, then pills fade/rise in sequence
                pill.schedule.Execute(() => pill.AddToClassList("choice--in")).StartingIn(220 + 90 * i);
                _choices.Add(pill);
                i++;
            }
            _choices.schedule.Execute(() => _choices.AddToClassList("choices--open")).StartingIn(30);
            _sayRow.schedule.Execute(() => _sayRow.AddToClassList("say-row--open")).StartingIn(160);
        }

        bool Available(ConversationSO.Choice choice)
        {
            bool topicOk = string.IsNullOrEmpty(choice.requiredTopicId)
                           || (knowledge != null && knowledge.IsKnown(choice.requiredTopicId));
            bool trustOk = knowledge == null || knowledge.trust >= choice.requiredTrust;
            return topicOk && trustOk;
        }

        void Submit()
        {
            if (!IsOpen || _revealing) return;
            string typed = _input.value.Trim();
            if (string.IsNullOrEmpty(typed) || typed == InputHint) return;
            if (_freeText)
            {
                SetInput(InputHint, true);
                _input.Blur();
                PlayerSubmitted?.Invoke(typed);
                return;
            }
            if (!_convo.TryGetNode(_nodeId, out var node)) return;

            var words = Regex.Split(typed.ToLowerInvariant(), "[^a-z']+");
            ConversationSO.Choice best = default;
            int bestScore = 0;
            foreach (var choice in node.choices)
            {
                if (choice.isLeave || !Available(choice)) continue;
                string label = choice.label.ToLowerInvariant();
                int score = 0;
                foreach (var w in words)
                    if (w.Length >= 3 && label.Contains(w)) score++;
                if (score > bestScore) { bestScore = score; best = choice; }
            }

            SetInput(InputHint, true);
            _input.Blur();
            if (bestScore > 0) { Dip(); EnterNode(best.nextId); }
            else StartReveal(unheardLine, node);   // gentle miss — same choices come back
        }

        void Dip()
        {
            StopBreathe();   // dip's fast scale must not ease at the breathe's slow duration
            _box.AddToClassList("dialogue-box--dip");
            _box.schedule.Execute(() => _box.RemoveFromClassList("dialogue-box--dip")).StartingIn(120);
            _box.schedule.Execute(() => { if (IsOpen) StartBreathe(); }).StartingIn(430);
        }

        // Ambient idle: panel breathe (scale 1.000↔1.004, ~4.2s full cycle) + speech-tail
        // wiggle ±2° in phase. UITK 2022.3 has no USS keyframes — a scheduler flips the
        // half-cycle classes and USS transitions ease between them.
        void StartBreathe()
        {
            if (_box == null) return;
            _box.AddToClassList("dialogue-box--breathing");
            _tail?.AddToClassList("dialogue-tail--breathing");
            if (_breathe == null)
                _breathe = _box.schedule.Execute(() =>
                {
                    _box.ToggleInClassList("dialogue-box--breathe");
                    _tail?.ToggleInClassList("dialogue-tail--wiggle");
                }).Every(2100);
            else
                _breathe.Resume();
        }

        void StopBreathe()
        {
            _breathe?.Pause();
            if (_box == null) return;
            _box.RemoveFromClassList("dialogue-box--breathing");
            _box.RemoveFromClassList("dialogue-box--breathe");
            if (_tail == null) return;
            _tail.RemoveFromClassList("dialogue-tail--breathing");
            _tail.RemoveFromClassList("dialogue-tail--wiggle");
        }

        static string StripTags(string s) => Regex.Replace(s, "<.*?>", "");

        // Trust as an icon token + count on the name badge (no bar, no "trust" label);
        // soft scale pulse when it rises mid-conversation.
        void RefreshTrust(bool pulse)
        {
            _shownTrust = knowledge != null ? knowledge.trust : 0;
            if (_trustChip == null) return;
            _trustChip.style.display = knowledge != null ? DisplayStyle.Flex : DisplayStyle.None;
            _trustCount.text = _shownTrust.ToString();
            if (!pulse) return;
            _trustChip.AddToClassList("trust-chip--pulse");
            _trustChip.schedule.Execute(() => _trustChip.RemoveFromClassList("trust-chip--pulse"))
                .StartingIn(260);
        }

        void SetBadgeName(string speaker)
        {
            int sep = speaker.IndexOf('·');
            if (sep > 0)
            {
                _nameLabel.text = speaker.Substring(0, sep).Trim();
                _roleLabel.text = speaker.Substring(sep + 1).Trim();
            }
            else
            {
                _nameLabel.text = speaker;
                _roleLabel.text = "";
            }
            _roleLabel.style.display = string.IsNullOrEmpty(_roleLabel.text)
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ── Construction ───────────────────────────────────────────────────

        void Build()
        {
            _root = hud.Mount(dialogueUxml);
            _root.style.display = DisplayStyle.None;

            _portrait = _root.Q("portrait");
            _box = _root.Q("dialogue-box");
            _tail = _root.Q("dialogue-tail");
            _scroll = _root.Q<ScrollView>("dialogue-scroll");
            _choices = _root.Q("choices");
            _nameLabel = _root.Q<Label>("npc-name");
            _roleLabel = _root.Q<Label>("npc-role");
            _sayRow = _root.Q("say-row");
            _input = _root.Q<TextField>("say-input");
            _send = _root.Q<Button>("say-send");
            _proceed = _root.Q("proceed");

            _trustChip = _root.Q("trust-chip");
            _trustCount = _root.Q<Label>("trust-count");

            // UXML ships in the open state for UI Builder preview — reset for runtime
            _choices.RemoveFromClassList("choices--open");
            _sayRow.RemoveFromClassList("say-row--open");
            _proceed?.RemoveFromClassList("proceed--in");
            _scroll.RemoveFromClassList("dialogue-scroll--settled");   // runtime starts reading-tall

            // Comfortable wheel speed — UITK default is a crawl.
            _scroll.mouseWheelScrollSize = 60f;

            // Reading phase locks back-scroll: eat wheel input before the ScrollView sees it.
            _scroll.RegisterCallback<WheelEvent>(e =>
            {
                if (!_settled) e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            _termCard = _root.Q("termcard");
            _termTab = _root.Q<Label>("termcard-tab");
            _termBody = _root.Q<Label>("termcard-body");
            _termNote = _root.Q<Label>("termcard-note");
            _root.Q<Label>("termcard-close").RegisterCallback<ClickEvent>(_ => CloseTerm());
            _termCard.style.display = DisplayStyle.None;   // UXML ships open for UI Builder

            _box.RemoveFromClassList("dialogue-box--open");
            _portrait.RemoveFromClassList("portrait--in");

            _root.Q("leave-badge").RegisterCallback<ClickEvent>(_ => Leave());
            // scrollbar clicks must not read as "advance page"
            _scroll.verticalScroller.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            // reading vignette tracks scrolling and content growth
            _scroll.verticalScroller.valueChanged += _ => UpdateFades();
            _scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateFades());
            _box.RegisterCallback<ClickEvent>(e =>
            {
                // Clicks on interactive children are their own actions, not advance presses —
                // without this, a choice click bubbles here mid-new-reveal and skip-reveals
                // the next node instantly (no typewriter).
                if (e.target is VisualElement t && (Within(t, _choices) || Within(t, _sayRow)))
                    return;
                if (_revealing) { Dip(); SkipReveal(); }
                else if (_awaitAdvance) Advance();
            });

            _send.clicked += Submit;
            _input.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    e.StopPropagation();
                }
            });
            // No placeholder support in 2022.3 — hint text swapped on focus.
            _input.RegisterCallback<FocusInEvent>(_ => { if (_input.value == InputHint) SetInput("", false); });
            _input.RegisterCallback<FocusOutEvent>(_ => { if (string.IsNullOrEmpty(_input.value)) SetInput(InputHint, true); });
            SetInput(InputHint, true);
        }

        static bool Within(VisualElement v, VisualElement root)
        {
            for (var p = v; p != null; p = p.parent)
                if (p == root) return true;
            return false;
        }

        void SetInput(string text, bool hint)
        {
            _input.SetValueWithoutNotify(text);
            _input.EnableInClassList("say-input--hint", hint);
        }
    }
}
