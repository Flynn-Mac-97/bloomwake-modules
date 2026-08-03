using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flynn.Feel
{
    /// <summary>
    /// UI Toolkit host for the Field Archive HUD (HANDOVER.md): mounts TopBar.uxml (location
    /// chip · painter2D understanding gauge · n/m chip) and hosts the side-alert stack
    /// instantiated from Alert.uxml. Skeletons live in UXML so they open in UI Builder;
    /// theme.uss carries the design tokens. Wire KnowledgeBase.onNotify → Push(string).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class FieldArchiveHud : MonoBehaviour
    {
        [Tooltip("theme.uss — kept as a runtime override on the panel root (UXML also imports it).")]
        public StyleSheet theme;
        [Tooltip("UI/TopBar.uxml")]
        public VisualTreeAsset topBarUxml;
        [Tooltip("UI/Alert.uxml — one toast, instantiated per Push().")]
        public VisualTreeAsset alertUxml;
        public KnowledgeBase knowledge;
        [Tooltip("Alert VIEW opens this drawer (Stage B).")]
        public FieldArchive archive;
        [Tooltip("Location chip text.")]
        public string locationName = "Mosslight Clearing";
        [Tooltip("Seconds an alert stays (handover: ~8).")]
        public float alertHold = 8f;

        /// <summary>Root other Field UI components attach into (dialogue, drawer...).</summary>
        public VisualElement Root { get; private set; }

        // Grove palette (handoff-grove/STYLE-NOTES.md): chrome-green track, bright-green fill
        static readonly Color GaugeTrack = new Color32(0x2A, 0x37, 0x20, 255);
        static readonly Color GaugeFill = new Color32(0x8F, 0xBF, 0x5A, 255);

        Label _gaugeNum, _countLabel;
        VisualElement _gauge, _alerts;
        float _fraction;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            Root = doc.rootVisualElement;
            if (theme != null) Root.styleSheets.Add(theme);

            if (topBarUxml == null)
            {
                Debug.LogWarning("[FieldArchiveHud] topBarUxml not assigned — top bar skipped.");
            }
            else
            {
                var bar = Mount(topBarUxml);
                bar.Q<Label>("location-label").text = locationName;
                _gauge = bar.Q("gauge");
                _gaugeNum = bar.Q<Label>("gauge-num");
                _countLabel = bar.Q<Label>("understand-count");
                _gauge.generateVisualContent += DrawGauge;
            }

            _alerts = new VisualElement();
            _alerts.AddToClassList("alerts");
            _alerts.pickingMode = PickingMode.Ignore;
            Root.Add(_alerts);
        }

        /// <summary>Instantiate a UXML skeleton as a full-screen, click-through layer.</summary>
        public VisualElement Mount(VisualTreeAsset vta)
        {
            var tc = vta.Instantiate();
            tc.pickingMode = PickingMode.Ignore;
            tc.style.position = Position.Absolute;
            tc.style.left = 0; tc.style.right = 0; tc.style.top = 0; tc.style.bottom = 0;
            Root.Add(tc);
            return tc;
        }

        void Update()
        {
            if (knowledge == null || _gaugeNum == null) return;
            int n = knowledge.KnownCount, m = knowledge.TotalCount;
            _gaugeNum.text = n.ToString();
            _countLabel.text = $"{n} / {m}";
            float f = m > 0 ? (float)n / m : 0f;
            if (!Mathf.Approximately(f, _fraction))
            {
                _fraction = f;
                _gauge.MarkDirtyRepaint();
            }
        }

        // Conic progress ring (handover §3.2) — filled wedge under the cream disc.
        void DrawGauge(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var rect = _gauge.contentRect;
            var c = rect.center;
            float r = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (r <= 0f) return;

            p.fillColor = GaugeTrack;
            p.BeginPath();
            p.Arc(c, r, 0, 360);
            p.Fill();

            if (_fraction <= 0f) return;
            p.fillColor = GaugeFill;
            p.BeginPath();
            p.MoveTo(c);
            p.Arc(c, r, -90, -90 + 360f * Mathf.Clamp01(_fraction));
            p.ClosePath();
            p.Fill();
        }

        // ── Alerts (side toasts) ───────────────────────────────────────────

        /// <summary>UnityEvent-friendly. Category tile derived from the message prefix.</summary>
        public void Push(string message)
        {
            if (_alerts == null || alertUxml == null || string.IsNullOrEmpty(message)) return;

            string code = "KN", tileClass = "alert__tile--green", cat = "KNOWLEDGE UPDATED";
            if (message.StartsWith("Entry")) { code = "EN"; tileClass = "alert__tile--copper"; cat = "ENTRY UPDATED"; }
            else if (message.StartsWith("Artifact")) { code = "AD"; tileClass = "alert__tile--copper"; cat = "ARTIFACT DECODED"; }
            else if (message.StartsWith("Question") || message.StartsWith("Task"))
            { code = "TH"; tileClass = "alert__tile--amber"; cat = "THREAD"; }

            var tc = alertUxml.Instantiate();
            var alert = tc.Q(className: "alert");
            alert.RemoveFromClassList("alert--in");   // UXML ships open for UI Builder preview

            var tile = tc.Q<Label>("tile");
            tile.text = code;
            tile.RemoveFromClassList("alert__tile--green");
            tile.AddToClassList(tileClass);
            tc.Q<Label>("cat").text = cat;
            tc.Q<Label>("name").text = message;

            var dot = tc.Q(className: "alert__dot");
            dot.schedule.Execute(() => dot.ToggleInClassList("alert__dot--dim")).Every(900);

            tc.Q<Label>("view").RegisterCallback<ClickEvent>(_ =>
            {
                tc.RemoveFromHierarchy();
                if (archive != null) archive.Open();   // newest entry sits at the top
            });
            tc.Q<Label>("dismiss").RegisterCallback<ClickEvent>(_ => tc.RemoveFromHierarchy());

            _alerts.Add(tc);
            alert.schedule.Execute(() => alert.AddToClassList("alert--in")).StartingIn(20);
            StartCoroutine(Dismiss(tc));
        }

        IEnumerator Dismiss(VisualElement alert)
        {
            yield return new WaitForSeconds(alertHold);
            alert.RemoveFromHierarchy();
        }
    }
}
