using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// Field Archive drawer (handover §6/§7) — a companion, not a menu system: one flat
    /// "Recent &amp; Current" list ordered by interaction recency (talking to an NPC or
    /// re-inspecting a thing bumps it to the top via KnowledgeBase.TopicTouched; discovery
    /// bumps + marks unread). No submenus — tapping a row expands its detail INLINE while
    /// the rest of the list stays visible; tap again to fold. Locked unknown rows below a
    /// divider. Toggle: Y / bottom button / alert VIEW; scrim click or Esc closes.
    /// </summary>
    public class FieldArchive : MonoBehaviour
    {
        public FieldArchiveHud hud;
        public KnowledgeBase knowledge;
        [Tooltip("UI/Drawer.uxml")]
        public VisualTreeAsset drawerUxml;
        [Tooltip("Portrait used for Person diamond tokens (demo: Rowan).")]
        public Texture2D personPortrait;
        public KeyCode toggleKey = KeyCode.Y;

        public bool IsOpen { get; private set; }

        enum Filter { All, People, World, Artifacts }
        Filter _filter = Filter.All;

        VisualElement _root, _drawer, _scrim, _feed, _locked, _btnDot;
        Label _count, _lockedLabel;
        readonly Dictionary<Filter, VisualElement> _chips = new Dictionary<Filter, VisualElement>();

        readonly List<string> _recency = new List<string>();   // topic ids, newest first
        readonly HashSet<string> _unread = new HashSet<string>();
        string _expandedId;
        VisualElement _expandedElement;

        void OnEnable()
        {
            if (knowledge != null)
            {
                knowledge.TopicDiscovered += OnDiscovered;
                knowledge.TopicTouched += OnTouched;
            }
        }

        void OnDisable()
        {
            if (knowledge != null)
            {
                knowledge.TopicDiscovered -= OnDiscovered;
                knowledge.TopicTouched -= OnTouched;
            }
        }

        void Start()
        {
            if (hud == null || hud.Root == null || drawerUxml == null) return;
            _root = hud.Mount(drawerUxml);

            // UXML/USS ship everything VISIBLE for UI Builder editing — runtime hides here.
            _drawer = _root.Q("drawer");
            _drawer.RemoveFromClassList("drawer--in");
            _drawer.style.display = DisplayStyle.None;
            _scrim = _root.Q("archive-scrim");
            _scrim.style.display = DisplayStyle.None;
            _feed = _root.Q("drawer-feed");
            _locked = _root.Q("drawer-locked");
            _lockedLabel = _root.Q<Label>("drawer-locked-label");
            _count = _root.Q<Label>("drawer-count");
            _btnDot = _root.Q("archive-btn-dot");

            _root.Q("archive-btn").RegisterCallback<ClickEvent>(_ => Toggle());
            _root.Q<Label>("drawer-close").RegisterCallback<ClickEvent>(_ => Close());
            _scrim.RegisterCallback<ClickEvent>(_ => Close());

            HookChip("fchip-all", Filter.All);
            HookChip("fchip-people", Filter.People);
            HookChip("fchip-world", Filter.World);
            HookChip("fchip-artifacts", Filter.Artifacts);
            var threadsChip = _root.Q<Label>("fchip-threads");
            if (threadsChip != null) threadsChip.style.display = DisplayStyle.None;

            RefreshBadges();

            // Ambient idle: unread dots (rows + archive button) pulse via one class flip
            // on the layer root — the descendant USS rule dims every dot at once, so
            // rebuilt rows need no per-dot bookkeeping.
            _root.schedule.Execute(() => _root.ToggleInClassList("archive-layer--pulse")).Every(1600);
        }

        void Update()
        {
            if (_root == null) return;
            if (Input.GetKeyDown(toggleKey)) Toggle();
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ── Knowledge events ────────────────────────────────────────────────

        void OnDiscovered(string id)
        {
            Bump(id);
            _unread.Add(id);
            if (IsOpen) Rebuild();
            RefreshBadges();
        }

        // Companion recency: re-encountering a known thing floats it up, quietly.
        void OnTouched(string id)
        {
            Bump(id);
            if (IsOpen) Rebuild();
        }

        void Bump(string id)
        {
            _recency.Remove(id);
            _recency.Insert(0, id);
        }

        // ── Open / close ────────────────────────────────────────────────────

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        /// <summary>UnityEvent/alert-friendly.</summary>
        public void Open()
        {
            if (_root == null || IsOpen) return;
            IsOpen = true;
            _scrim.style.display = DisplayStyle.Flex;
            _drawer.style.display = DisplayStyle.Flex;
            _drawer.schedule.Execute(() => _drawer.AddToClassList("drawer--in")).StartingIn(20);
            Rebuild();
        }

        /// <summary>Quick-jump (E2): open the drawer with this entry expanded + scrolled
        /// into view. Called from the hover cue on known things.</summary>
        public void OpenTo(string topicId)
        {
            if (_root == null || string.IsNullOrEmpty(topicId)) return;
            _filter = Filter.All;
            foreach (var kv in _chips) kv.Value.EnableInClassList("fchip--on", kv.Key == Filter.All);
            _expandedId = topicId;
            _unread.Remove(topicId);
            if (IsOpen) Rebuild(); else Open();

            _drawer.schedule.Execute(() =>
            {
                var scroll = _root.Q<ScrollView>("drawer-scroll");
                if (scroll != null && _expandedElement != null) scroll.ScrollTo(_expandedElement);
            }).StartingIn(90);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _drawer.RemoveFromClassList("drawer--in");
            _scrim.style.display = DisplayStyle.None;
            _drawer.schedule.Execute(() =>
            {
                if (!IsOpen) _drawer.style.display = DisplayStyle.None;
            }).StartingIn(300);
        }

        // ── Feed ────────────────────────────────────────────────────────────

        void HookChip(string name, Filter f)
        {
            var chip = _root.Q<Label>(name);
            _chips[f] = chip;
            chip.RegisterCallback<ClickEvent>(_ =>
            {
                _filter = f;
                foreach (var kv in _chips) kv.Value.EnableInClassList("fchip--on", kv.Key == f);
                Rebuild();
            });
        }

        static bool PassesFilter(Filter f, TopicLibrary.Category c) =>
            f == Filter.All
            || (f == Filter.People && c == TopicLibrary.Category.Person)
            || (f == Filter.World && c == TopicLibrary.Category.World)
            || (f == Filter.Artifacts && c == TopicLibrary.Category.Artifact);

        void Rebuild()
        {
            _feed.Clear();
            _locked.Clear();
            _count.text = $"{knowledge.KnownCount} / {knowledge.TotalCount}";

            foreach (var id in _recency)
            {
                if (!knowledge.library.TryGet(id, out var def)) continue;
                if (!PassesFilter(_filter, def.category)) continue;
                var captured = def;
                _feed.Add(Entry(id, TopicHeader(def), () => TopicSections(captured)));
            }

            // Visible unknowns (handover §7): dimmed locked diamonds below the divider.
            int lockedShown = 0;
            foreach (var def in knowledge.library.topics)
            {
                if (knowledge.IsKnown(def.id)) continue;
                if (!PassesFilter(_filter, def.category)) continue;
                _locked.Add(LockedRow(def));
                lockedShown++;
            }
            bool anyLocked = lockedShown > 0;
            _lockedLabel.style.display = anyLocked ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q("drawer-divider").style.display = anyLocked ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshBadges();
        }

        // One flat entry: header row + inline expansion. No push views.
        VisualElement Entry(string id, VisualElement header, System.Func<List<VisualElement>> sections)
        {
            var entry = new VisualElement();
            entry.AddToClassList("fentry");
            bool open = _expandedId == id;
            entry.EnableInClassList("fentry--open", open);
            if (open) _expandedElement = entry;
            entry.Add(header);

            if (open)
            {
                var expand = new VisualElement();
                expand.AddToClassList("fentry__expand");
                foreach (var s in sections()) expand.Add(s);
                entry.Add(expand);
            }

            header.RegisterCallback<ClickEvent>(_ =>
            {
                _expandedId = open ? null : id;
                _unread.Remove(id);
                Rebuild();
            });
            return entry;
        }

        VisualElement TopicHeader(TopicLibrary.TopicDef def)
        {
            var row = new VisualElement();
            row.AddToClassList("frow");
            row.Add(Token(def.category));

            var body = new VisualElement();
            body.AddToClassList("frow__body");
            var nameRow = new VisualElement();
            nameRow.AddToClassList("frow__name-row");
            var nameLabel = new Label(def.displayName);
            nameLabel.AddToClassList("frow__name");
            nameRow.Add(nameLabel);
            if (_unread.Contains(def.id)) nameRow.Add(UnreadDot());
            body.Add(nameRow);

            var meta = new VisualElement();
            meta.AddToClassList("frow__meta");
            meta.Add(Tag(def.category));
            var status = new Label(StatusOf(def));
            status.AddToClassList("frow__status");
            meta.Add(status);
            body.Add(meta);
            row.Add(body);
            row.Add(Chev());
            return row;
        }

        VisualElement LockedRow(TopicLibrary.TopicDef def)
        {
            var row = new VisualElement();
            row.AddToClassList("frow");
            row.AddToClassList("frow--locked");
            row.Add(LockedToken());

            var body = new VisualElement();
            body.AddToClassList("frow__body");
            var nameLabel = new Label("Unknown");
            nameLabel.AddToClassList("frow__name");
            nameLabel.AddToClassList("frow__name--locked");
            body.Add(nameLabel);
            var hint = new Label(def.lockedHint);
            hint.AddToClassList("frow__hint");
            body.Add(hint);
            row.Add(body);
            return row;
        }

        // ── Inline detail sections ──────────────────────────────────────────

        List<VisualElement> TopicSections(TopicLibrary.TopicDef def)
        {
            var list = new List<VisualElement>();
            if (def.category == TopicLibrary.Category.Person)
            {
                list.Add(Section("KNOWN INFORMATION", def.summary));
                var topics = SectionShell("TOPICS");
                foreach (var t in knowledge.library.topics)
                {
                    if (t.id == def.id) continue;
                    bool known = knowledge.IsKnown(t.id);
                    topics.Add(Step(known ? t.displayName : "Not discussed yet",
                        known, known ? "AVAILABLE" : "UNKNOWN"));
                }
                list.Add(topics);
            }
            else
            {
                list.Add(Section("OBSERVED", def.summary));
            }
            return list;
        }

        // ── Small builders ──────────────────────────────────────────────────

        static VisualElement UnreadDot()
        {
            var dot = new VisualElement();
            dot.AddToClassList("frow__dot");
            return dot;
        }

        static Label Chev()
        {
            var chev = new Label("›");
            chev.AddToClassList("frow__chev");
            return chev;
        }

        string StatusOf(TopicLibrary.TopicDef def) =>
            def.category == TopicLibrary.Category.Person ? "MET" : "OBSERVED";

        VisualElement Token(TopicLibrary.Category cat)
        {
            var token = new VisualElement();
            token.AddToClassList("token");
            var frame = new VisualElement();
            frame.AddToClassList("token__frame");

            if (cat == TopicLibrary.Category.Person && personPortrait != null)
            {
                var img = new VisualElement();
                img.AddToClassList("token__img");
                img.style.backgroundImage = new StyleBackground(personPortrait);
                frame.Add(img);
                frame.AddToClassList("token__frame--green");
            }
            else
            {
                var inner = new VisualElement();
                inner.AddToClassList("token__inner");
                var glyph = new VisualElement();
                if (cat == TopicLibrary.Category.Artifact)
                {
                    glyph.AddToClassList("glyph-diamond");
                    frame.AddToClassList("token__frame--amber");
                }
                else
                {
                    glyph.AddToClassList("glyph-circle");
                    frame.AddToClassList("token__frame--green");
                }
                inner.Add(glyph);
                frame.Add(inner);
            }
            token.Add(frame);
            return token;
        }

        VisualElement LockedToken()
        {
            var token = new VisualElement();
            token.AddToClassList("token");
            var frame = new VisualElement();
            frame.AddToClassList("token__frame");
            frame.AddToClassList("token__frame--locked");
            var inner = new VisualElement();
            inner.AddToClassList("token__inner");
            var g = new VisualElement();
            g.AddToClassList("glyph-lock");
            inner.Add(g);
            frame.Add(inner);
            token.Add(frame);
            return token;
        }

        Label Tag(TopicLibrary.Category cat)
        {
            var tag = new Label(cat == TopicLibrary.Category.Person ? "PERSON"
                : cat == TopicLibrary.Category.Artifact ? "ARTIFACT" : "WORLD");
            tag.AddToClassList("tag");
            tag.AddToClassList(cat == TopicLibrary.Category.Person ? "tag--person"
                : cat == TopicLibrary.Category.Artifact ? "tag--artifact" : "tag--world");
            return tag;
        }

        VisualElement SectionShell(string label)
        {
            var s = new VisualElement();
            s.AddToClassList("dsection");
            var l = new Label(label);
            l.AddToClassList("dsection__label");
            s.Add(l);
            return s;
        }

        VisualElement Section(string label, string body, bool corrupt = false, string hint = null)
        {
            var s = SectionShell(label);
            var b = new Label(body);
            b.AddToClassList(corrupt ? "dsection__body--corrupt" : "dsection__body");
            s.Add(b);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = new Label(hint);
                h.AddToClassList("dsection__hint");
                s.Add(h);
            }
            return s;
        }

        VisualElement Step(string text, bool on, string state)
        {
            var row = new VisualElement();
            row.AddToClassList("dstep");
            var dot = new VisualElement();
            dot.AddToClassList("dstep__dot");
            if (!on) dot.AddToClassList("dstep__dot--off");
            row.Add(dot);
            var t = new Label(text);
            t.AddToClassList("dstep__text");
            if (!on) t.AddToClassList("dstep__text--off");
            row.Add(t);
            if (!string.IsNullOrEmpty(state))
            {
                var st = new Label(state);
                st.AddToClassList("dstep__state");
                if (!on) st.AddToClassList("dstep__state--off");
                row.Add(st);
            }
            return row;
        }

        void RefreshBadges()
        {
            if (_btnDot != null)
                _btnDot.style.display = _unread.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
