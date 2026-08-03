using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Flynn.Feel
{
    /// <summary>One spoken line. No rich text in prototype.</summary>
    [System.Serializable]
    public struct DialogueLine
    {
        public string speaker;
        [TextArea] public string text;
    }

    /// <summary>
    /// Cozy bottom-panel dialogue presenter (AC/Stardew pattern): typewriter reveal with
    /// punctuation pauses, soft per-character speech blips with pitch wobble, skip-then-advance
    /// input, bobbing continue arrow. Slides up OutBack, exits InQuad; Esc cancels (quicker,
    /// humbler exit — never resembles completion).
    ///
    /// Builds its own UI at runtime from <see cref="DialogueStyle"/> — no scene anchor wiring,
    /// all layout/timing tuned live in the SO. Colors/sorting come from VibeTokens in code.
    /// Presenter only: feed it lines via Show(); the NPC LLM system can drive it later.
    /// </summary>
    public class DialogueBox : MonoBehaviour
    {
        [Tooltip("All layout/timing/voice tuning.")]
        public DialogueStyle style;
        [Tooltip("Raised when the box opens / closes (completed or cancelled).")]
        public UnityEvent onOpened;
        public UnityEvent onClosed;

        public bool IsOpen => _open;

        Canvas _canvas;
        RectTransform _panel;
        TextMeshProUGUI _name, _body, _arrow;
        Vector2 _arrowBase;
        AudioSource _voice;

        List<DialogueLine> _lines;
        int _index;
        bool _open, _revealing;
        Coroutine _reveal;
        float _shownY, _hiddenY;

        void Awake()
        {
            if (style == null) { Debug.LogWarning("[DialogueBox] no style assigned"); enabled = false; return; }
            BuildUi();
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>Open the box (if closed) and play these lines.</summary>
        public void Show(DialogueLine[] lines)
        {
            if (lines == null || lines.Length == 0 || _canvas == null) return;
            _lines = new List<DialogueLine>(lines);
            _index = 0;

            if (!_open)
            {
                _open = true;
                _canvas.gameObject.SetActive(true);
                _panel.localScale = Vector3.one;
                Tween.Custom(0f, 1f, style.appearDuration, ease: Ease.OutBack, onValueChange: t =>
                    _panel.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(_hiddenY, _shownY, t)));
                onOpened?.Invoke();
            }
            StartLine();
        }

        /// <summary>Cancel mid-conversation (Esc). Distinct from completing (Contract §13).</summary>
        public void Cancel() { if (_open) Close(completed: false); }

        // ── Flow ────────────────────────────────────────────────────────────

        void Update()
        {
            if (!_open) return;

            if (_arrow.gameObject.activeSelf)
                _arrow.rectTransform.anchoredPosition = _arrowBase + Vector2.up *
                    (Mathf.Sin(Time.time / Mathf.Max(0.1f, style.arrowBobPeriod) * Mathf.PI * 2f)
                     * style.arrowBobAmplitude * VibeTokens.MotionScale);

            bool press = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)
                         || Input.GetMouseButtonDown(0);
            if (press)
            {
                PressDip();                       // input ack, every press (Contract §6)
                if (_revealing) SkipReveal();     // press during reveal = show all
                else AdvanceLine();               // press after = next / close
            }
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        void StartLine()
        {
            var line = _lines[_index];
            _name.text = line.speaker;
            _arrow.gameObject.SetActive(false);
            if (_reveal != null) StopCoroutine(_reveal);
            _reveal = StartCoroutine(Reveal(line.text));
        }

        IEnumerator Reveal(string text)
        {
            _revealing = true;
            _body.text = text;
            _body.maxVisibleCharacters = 0;
            _body.ForceMeshUpdate();
            int total = _body.textInfo.characterCount;
            float delay = 1f / Mathf.Max(1f, style.charsPerSecond);

            for (int shown = 1; shown <= total; shown++)
            {
                _body.maxVisibleCharacters = shown;
                char c = text[Mathf.Min(shown - 1, text.Length - 1)];

                if (!char.IsWhiteSpace(c) && style.blip != null
                    && shown % Mathf.Max(1, style.blipEveryN) == 0)
                {
                    _voice.pitch = 1f + Random.Range(-style.blipPitchJitter, style.blipPitchJitter);
                    _voice.PlayOneShot(style.blip, style.blipVolume);
                }

                // Punctuation breathes like speech — the core cozy trick.
                float mult = (c == '.' || c == '!' || c == '?') ? style.sentencePauseMult
                           : (c == ',' || c == ';' || c == ':') ? style.commaPauseMult : 1f;
                yield return new WaitForSeconds(delay * mult);
            }
            _revealing = false;
            _arrow.gameObject.SetActive(true);
        }

        void SkipReveal()
        {
            if (_reveal != null) StopCoroutine(_reveal);
            _revealing = false;
            _body.maxVisibleCharacters = int.MaxValue;
            _arrow.gameObject.SetActive(true);
        }

        void AdvanceLine()
        {
            _index++;
            if (_index < _lines.Count) StartLine();
            else Close(completed: true);
        }

        void Close(bool completed)
        {
            _open = false;
            if (_reveal != null) StopCoroutine(_reveal);
            // Cancel exits quicker + humbler than completion — must never read as "done".
            float dur = completed ? style.exitDuration : style.exitDuration * 0.6f;
            Tween.Custom(1f, 0f, dur, ease: Ease.InQuad, onValueChange: t =>
                    _panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(_hiddenY, _shownY, t)))
                .OnComplete(() => { if (_canvas != null) _canvas.gameObject.SetActive(false); });
            onClosed?.Invoke();
        }

        void PressDip()
        {
            _panel.localScale = Vector3.one * style.pressDip;
            Tween.Custom(style.pressDip, 1f, 0.12f, ease: Ease.OutQuad,
                onValueChange: s => _panel.localScale = Vector3.one * s);
        }

        // ── Runtime UI construction (no scene wiring to get wrong) ─────────

        void BuildUi()
        {
            var canvasGo = new GameObject("DialogueCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;   // overlay stack: dialogue above HUD/world prompts
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _shownY = style.bottomMargin;
            _hiddenY = -(style.panelHeight + 60f);

            // Panel — rounded, ui-surface.
            _panel = NewRect("Panel", canvasGo.transform);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(style.panelWidth, style.panelHeight);
            _panel.anchoredPosition = new Vector2(0f, _hiddenY);
            var panelImg = _panel.gameObject.AddComponent<Image>();
            panelImg.sprite = style.panelSprite;
            panelImg.type = Image.Type.Sliced;
            panelImg.color = VibeTokens.UiSurface;

            // Name plate — raised surface, accent text, perched on the top-left edge.
            var plate = NewRect("NamePlate", _panel);
            plate.anchorMin = plate.anchorMax = new Vector2(0f, 1f);
            plate.pivot = new Vector2(0f, 0.5f);
            plate.sizeDelta = new Vector2(260f, 46f);
            plate.anchoredPosition = new Vector2(style.padding, 0f);
            var plateImg = plate.gameObject.AddComponent<Image>();
            plateImg.sprite = style.panelSprite;
            plateImg.type = Image.Type.Sliced;
            plateImg.color = VibeTokens.UiSurfaceRaised;
            _name = NewText("Name", plate, style.nameSize, VibeTokens.UiAccent,
                TextAlignmentOptions.Center);
            Stretch(_name.rectTransform, 8f);

            // Body text.
            _body = NewText("Body", _panel, style.bodySize, VibeTokens.UiTextPrimary,
                TextAlignmentOptions.TopLeft);
            var bodyRt = _body.rectTransform;
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = new Vector2(style.padding, style.padding);
            bodyRt.offsetMax = new Vector2(-style.padding, -(style.padding + 18f));

            // Continue arrow.
            _arrow = NewText("Arrow", _panel, style.bodySize, VibeTokens.UiAccent,
                TextAlignmentOptions.Center);
            _arrow.text = ">";
            var arrowRt = _arrow.rectTransform;
            arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(1f, 0f);
            arrowRt.pivot = new Vector2(1f, 0f);
            arrowRt.sizeDelta = new Vector2(40f, 40f);
            arrowRt.anchoredPosition = new Vector2(-style.padding, style.padding * 0.5f);
            _arrowBase = arrowRt.anchoredPosition;
            _arrow.gameObject.SetActive(false);

            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;

            _canvas.gameObject.SetActive(false);
        }

        static RectTransform NewRect(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        TextMeshProUGUI NewText(string n, Transform parent, float size, Color color,
            TextAlignmentOptions align)
        {
            var rt = NewRect(n, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }
    }
}
