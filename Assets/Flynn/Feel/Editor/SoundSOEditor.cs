using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Flynn.Feel.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="SoundSO"/>: standard audio fields plus a waveform with
    /// draggable green (start) / red (end) edges to sub-sample the clip, and preview buttons.
    /// </summary>
    [CustomEditor(typeof(SoundSO))]
    public class SoundSOEditor : UnityEditor.Editor
    {
        const int WaveH = 80;

        Texture2D _wave;
        int _waveClipId;

        enum Handle { None, Start, End }
        Handle _drag;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var sound = (SoundSO)target;

            foreach (var name in new[] { "clip", "volume", "volumeJitter", "pitch", "pitchJitter",
                                         "loop", "spatialBlend", "output" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Region", EditorStyles.boldLabel);

            if (sound.clip == null)
                EditorGUILayout.HelpBox("Assign a clip to pick a region.", MessageType.Info);
            else
                DrawRegion(sound, sound.clip);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawRegion(SoundSO sound, AudioClip clip)
        {
            float len = clip.length;
            EnsureWave(clip);

            Rect r = GUILayoutUtility.GetRect(10, WaveH, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.14f));
            if (_wave != null) GUI.DrawTexture(r, _wave, ScaleMode.StretchToFill);

            var startP = serializedObject.FindProperty("startTime");
            var endP = serializedObject.FindProperty("endTime");
            float start = Mathf.Clamp(startP.floatValue, 0f, len);
            float end = (endP.floatValue <= 0f || endP.floatValue > len) ? len : endP.floatValue;

            float sx = r.x + r.width * (len > 0 ? start / len : 0f);
            float ex = r.x + r.width * (len > 0 ? end / len : 1f);

            // dim the excluded head/tail
            EditorGUI.DrawRect(new Rect(r.x, r.y, sx - r.x, r.height), new Color(0, 0, 0, 0.55f));
            EditorGUI.DrawRect(new Rect(ex, r.y, r.xMax - ex, r.height), new Color(0, 0, 0, 0.55f));
            // edges
            EditorGUI.DrawRect(new Rect(sx - 1, r.y, 2, r.height), new Color(0.4f, 0.9f, 0.5f));
            EditorGUI.DrawRect(new Rect(ex - 1, r.y, 2, r.height), new Color(0.95f, 0.5f, 0.4f));

            var hs = new Rect(sx - 4, r.y, 8, r.height);
            var he = new Rect(ex - 4, r.y, 8, r.height);
            EditorGUIUtility.AddCursorRect(hs, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(he, MouseCursor.ResizeHorizontal);

            HandleDrag(r, len, startP, endP, hs, he);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(startP);
            EditorGUILayout.PropertyField(endP);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                $"Region: {start:0.000}s → {end:0.000}s   ({Mathf.Max(0f, end - start):0.000}s)");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("▶ Play region"))
            {
                serializedObject.ApplyModifiedProperties();   // hear the edit you just made
                PlayRegion(sound);
            }
            if (GUILayout.Button("▶ Full"))
            {
                serializedObject.ApplyModifiedProperties();
                PlayFull(sound);
            }
            if (GUILayout.Button("■ Stop")) StopPreview();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Drag the green (start) / red (end) edges over the waveform to sub-sample. " +
                "Preview runs the same code the game does, so pitch, volume, jitter and the " +
                "end edge all apply - and jitter makes repeats differ slightly, as intended.",
                MessageType.None);
        }

        void HandleDrag(Rect r, float len, SerializedProperty startP, SerializedProperty endP,
                        Rect hs, Rect he)
        {
            var e = Event.current;
            float TimeAt(float mx) => Mathf.Clamp01((mx - r.x) / r.width) * len;
            float End() => endP.floatValue <= 0f || endP.floatValue > len ? len : endP.floatValue;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (hs.Contains(e.mousePosition)) { _drag = Handle.Start; e.Use(); }
                    else if (he.Contains(e.mousePosition)) { _drag = Handle.End; e.Use(); }
                    else if (r.Contains(e.mousePosition))
                    {
                        float t = TimeAt(e.mousePosition.x);   // grab the nearer edge
                        if (Mathf.Abs(t - startP.floatValue) <= Mathf.Abs(t - End()))
                        { startP.floatValue = t; _drag = Handle.Start; }
                        else { endP.floatValue = t; _drag = Handle.End; }
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_drag != Handle.None)
                    {
                        float t = TimeAt(e.mousePosition.x);
                        if (_drag == Handle.Start) startP.floatValue = Mathf.Clamp(t, 0f, End());
                        else endP.floatValue = Mathf.Clamp(t, startP.floatValue, len);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_drag != Handle.None) { _drag = Handle.None; e.Use(); }
                    break;
            }
        }

        void EnsureWave(AudioClip clip)
        {
            if (_wave != null && _waveClipId == clip.GetInstanceID()) return;
            _waveClipId = clip.GetInstanceID();
            _wave = BuildWave(clip, 512, WaveH);
        }

        static Texture2D BuildWave(AudioClip clip, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            var col = new Color(0.45f, 0.75f, 0.95f, 0.9f);

            float[] data = null;
            try
            {
                data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) data = null;
            }
            catch { data = null; }   // compressed/streaming clips can't GetData

            if (data != null && data.Length > 0)
            {
                int ch = Mathf.Max(1, clip.channels);
                int frames = data.Length / ch;
                int per = Mathf.Max(1, frames / w);
                int half = h / 2;
                for (int x = 0; x < w; x++)
                {
                    float mx = 0f;
                    int s = x * per;
                    for (int j = 0; j < per; j++)
                    {
                        int fi = s + j;
                        if (fi >= frames) break;
                        float v = Mathf.Abs(data[fi * ch]);
                        if (v > mx) mx = v;
                    }
                    int amp = Mathf.Clamp((int)(mx * half), 0, half);
                    for (int y = half - amp; y <= half + amp; y++)
                        if (y >= 0 && y < h) px[y * w + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // --- Preview on a real AudioSource ---
        //
        // The old path was UnityEditor.AudioUtil.PlayPreviewClip(clip, startSample, loop). That
        // takes a clip and a START sample and NOTHING else - no pitch, no volume, no end - so
        // every knob on the asset except the start edge was silently ignored while auditioning.
        // Driving an AudioSource through SoundSO.Play instead means the preview IS the runtime
        // path: whatever you hear here is what the game plays.

        static GameObject _previewGo;
        static AudioSource _previewSrc;

        static AudioSource Source()
        {
            if (_previewSrc != null) return _previewSrc;
            _previewGo = new GameObject("SoundSO_Preview") { hideFlags = HideFlags.HideAndDontSave };
            _previewSrc = _previewGo.AddComponent<AudioSource>();
            _previewSrc.playOnAwake = false;
            return _previewSrc;
        }

        static void PlayRegion(SoundSO sound)
        {
            if (sound == null || sound.clip == null) return;
            var src = Source();
            src.Stop();
            sound.Play(src);   // region start + scheduled end + level + pitch, exactly as in game
        }

        /// <summary>Whole clip, but still at the asset's level and pitch - for hearing what was
        /// trimmed away without losing the sound's character.</summary>
        static void PlayFull(SoundSO sound)
        {
            if (sound == null || sound.clip == null) return;
            var src = Source();
            src.Stop();
            src.clip = sound.clip;
            src.loop = false;
            src.spatialBlend = sound.spatialBlend;
            src.outputAudioMixerGroup = sound.output;
            src.volume = Mathf.Clamp01(sound.volume);
            src.pitch = Mathf.Max(0.01f, sound.pitch);
            src.timeSamples = 0;
            src.Play();
        }

        static void StopPreview()
        {
            if (_previewSrc != null) _previewSrc.Stop();
            LegacyStop();   // also kill anything the old AudioUtil path left ringing
        }

        void OnDisable()
        {
            StopPreview();
            if (_previewGo != null) DestroyImmediate(_previewGo);
            _previewGo = null;
            _previewSrc = null;
        }

        static MethodInfo _stop;
        static void LegacyStop()
        {
            var t = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (t == null) return;
            if (_stop == null)
                _stop = t.GetMethod("StopAllPreviewClips") ?? t.GetMethod("StopAllClips");
            _stop?.Invoke(null, null);
        }
    }
}
