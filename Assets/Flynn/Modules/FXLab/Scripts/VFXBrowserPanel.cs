using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// In-game asset browser for downloaded VFX packs (IMGUI dev tool): scans rootFolder on
    /// play (editor-only API behind #if UNITY_EDITOR - the lab only runs in the editor),
    /// auto-detects animations (numbered file sequences, importer-sliced sheets, horizontal
    /// strips, uniform grids), skips junk (gif previews, jpg promos, cover art), groups the
    /// list under per-pack subheadings (click a heading to fold), previews the selected one
    /// animated, and assigns it onto a tuning block. B toggles.
    /// </summary>
    public class VFXBrowserPanel : MonoBehaviour
    {
        public FXLabTuning tuning;
        [Tooltip("Where the 'Selected block' target reads the open recipe block from. Auto-found.")]
        public FXTunePanel tunePanel;
        public string rootFolder = "Assets/Flynn/Modules/FXLab/VFX";
        public KeyCode toggleKey = KeyCode.B;
        [Range(1f, 3f)] public float uiScale = 2f;
        public float previewFps = 18f;

        class Entry
        {
            public string pack;            // top-level folder under rootFolder (subheading group)
            public string label;
            public Sprite[] frames;        // preview frames (may be runtime slices)
            public Sprite[] assetFrames;   // persistable sprite assets (null for strip/grid slices)
            public Texture2D gridSheet;    // set for strip/grid - assigned as sheet+cellSize instead
            public int gridCell;
            public int gridCellH;          // 0 = square cells
            public int gridCols;
            public Rect[] gridRects;       // segmentation result - assigned as sheet+cellRects
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, int> _packCounts = new Dictionary<string, int>();
        readonly HashSet<string> _folded = new HashSet<string>();
        static readonly int[] CellCandidates = { 128, 100, 96, 64, 48, 32 };
        static readonly string[] TargetNames =
            { "Slash", "Burst", "Wood", "Metal", "Stone", "Puff chips", "Puff sheet", "Selected block" };
        // VFX sheets need alpha, so only alpha-capable formats; kills gif previews + jpg promo shots
        static readonly string[] SheetExts = { ".png", ".tga", ".psd", ".tif", ".tiff" };
        static readonly string[] JunkNames = { "preview", "cover", "icon", "banner", "screenshot", "thumbnail" };

        bool _visible;
        Vector2 _scroll;
        int _sel = -1;
        int _target;
        string _note = "";

        float Sw => Screen.width / uiScale;
        float Sh => Screen.height / uiScale;

        void Awake() => Scan();

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) _visible = !_visible;
        }

        // ── scan ─────────────────────────────────────────────────────────────
        void Scan()
        {
            _entries.Clear();
            _sel = -1;
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });
            var sequences = new Dictionary<string, List<(int n, string path)>>();
            var singles = new List<string>();

            foreach (var g in guids)
            {
                var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                if (!Wanted(p)) continue;
                string file = System.IO.Path.GetFileNameWithoutExtension(p);
                var m = Regex.Match(file, @"^(.+?)[ _\-\(]*0*(\d+)\)?$");
                if (m.Success)
                {
                    string key = System.IO.Path.GetDirectoryName(p) + "|" +
                                 m.Groups[1].Value.TrimEnd(' ', '_', '-', '(');
                    if (!sequences.TryGetValue(key, out var list)) sequences[key] = list = new List<(int, string)>();
                    list.Add((int.Parse(m.Groups[2].Value), p));
                }
                else singles.Add(p);
            }

            foreach (var kv in sequences)
            {
                if (kv.Value.Count == 1) { singles.Add(kv.Value[0].path); continue; }
                kv.Value.Sort((a, b) => a.n.CompareTo(b.n));
                var frames = new List<Sprite>();
                foreach (var item in kv.Value)
                {
                    var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(item.path);
                    if (sp != null) frames.Add(sp);
                }
                if (frames.Count == 0) continue;
                var parts = kv.Key.Split('|');
                _entries.Add(new Entry
                {
                    pack = PackOf(parts[0]),
                    label = Label(parts[0], parts[1]) + "  [" + frames.Count + "f files]",
                    frames = frames.ToArray(),
                    assetFrames = frames.ToArray()
                });
            }

            foreach (var p in singles)
                AddSingle(p);

            _entries.Sort((a, b) =>
            {
                int byPack = string.Compare(a.pack, b.pack, System.StringComparison.OrdinalIgnoreCase);
                return byPack != 0 ? byPack
                    : string.Compare(a.label, b.label, System.StringComparison.OrdinalIgnoreCase);
            });
            _packCounts.Clear();
            foreach (var e in _entries)
                _packCounts[e.pack] = _packCounts.TryGetValue(e.pack, out var n) ? n + 1 : 1;
#endif
        }

        /// <summary>Skip non-sheet junk: gif previews, jpg promo shots, cover/icon images.</summary>
        static bool Wanted(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            bool ok = false;
            foreach (var e in SheetExts) if (ext == e) { ok = true; break; }
            if (!ok) return false;
            string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            foreach (var j in JunkNames) if (file.Contains(j)) return false;
            return true;
        }

        /// <summary>Top-level folder under rootFolder = the pack this asset belongs to.</summary>
        string PackOf(string folder)
        {
            string f = folder.Replace('\\', '/');
            string root = rootFolder.Replace('\\', '/').TrimEnd('/');
            if (!f.StartsWith(root)) return System.IO.Path.GetFileName(f);
            string rel = f.Substring(root.Length).TrimStart('/');
            int cut = rel.IndexOf('/');
            string top = cut > 0 ? rel.Substring(0, cut) : rel;
            return string.IsNullOrEmpty(top) ? "(loose files)" : top;
        }

#if UNITY_EDITOR
        void AddSingle(string path)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            string folder = System.IO.Path.GetDirectoryName(path);

            // importer-sliced sub-sprites win
            var subs = new List<Sprite>();
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (o is Sprite sp) subs.Add(sp);
            if (subs.Count > 1)
            {
                subs.Sort((a, b) => TrailingNum(a.name).CompareTo(TrailingNum(b.name)));
                _entries.Add(new Entry
                {
                    pack = PackOf(folder),
                    label = Label(folder, file) + "  [" + subs.Count + "f sliced]",
                    frames = subs.ToArray(),
                    assetFrames = subs.ToArray()
                });
                return;
            }

            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return;

            // wide strips: content-aware first - alpha-island segmentation handles sparse
            // sheets with uneven spacing and several variants per row (divisor math can't)
            if (tex.width > tex.height * 3 && TrySegmentStrip(folder, file, tex))
                return;

            // single-row strip: infer the frame count by divisor search, so non-square
            // frames work too. Prefer the split whose frame aspect is closest to square.
            if (tex.width > tex.height)
            {
                int bestN = 0;
                float bestScore = float.MaxValue;
                // square-cell split first (the common case), then general divisors
                if (tex.width % tex.height == 0 && tex.width / tex.height >= 2)
                { bestN = tex.width / tex.height; bestScore = 0f; }
                for (int n = 4; n <= 64; n++)
                {
                    if (tex.width % n != 0) continue;
                    float aspect = (tex.width / (float)n) / tex.height;
                    if (aspect < 0.6f || aspect > 1.5f) continue;
                    float score = Mathf.Abs(1f - aspect);
                    if (score < bestScore) { bestScore = score; bestN = n; }
                }
                if (bestN >= 2)
                {
                    AddGrid(folder, file, tex, tex.width / bestN, tex.height, "strip");
                    return;
                }
            }

            // uniform grid at a common cell size (multi-row sheets)
            foreach (int c in CellCandidates)
            {
                if (tex.width % c == 0 && tex.height % c == 0 && tex.width / c >= 2 && tex.height / c >= 1
                    && (tex.width / c) * (tex.height / c) >= 4)
                {
                    AddGrid(folder, file, tex, c, c, "grid" + c);
                    return;
                }
            }

            // plain single frame
            var main = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var one = main != null
                ? main
                : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), Mathf.Max(tex.width, tex.height));
            _entries.Add(new Entry
            {
                pack = PackOf(folder),
                label = Label(folder, file) + "  [1f]",
                frames = new[] { one },
                assetFrames = main != null ? new[] { main } : null
            });
        }

        void AddGrid(string folder, string file, Texture2D tex, int cellW, int cellH, string tag)
        {
            int cols = tex.width / cellW, rows = tex.height / cellH;
            var frames = new Sprite[cols * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    frames[r * cols + c] = Sprite.Create(tex,
                        new Rect(c * cellW, tex.height - (r + 1) * cellH, cellW, cellH),
                        new Vector2(0.5f, 0.5f), Mathf.Max(cellW, cellH));
            _entries.Add(new Entry
            {
                pack = PackOf(folder),
                label = Label(folder, file) + "  [" + cols * rows + "f " + tag + "]",
                frames = frames,
                gridSheet = tex,
                gridCell = cellW,
                gridCellH = cellH == cellW ? 0 : cellH,
                gridCols = cols
            });
        }

        /// <summary>
        /// Content-aware strip detection: find columns with visible alpha, merge fragments
        /// into frame islands, split islands into separate animations wherever the gap is
        /// much bigger than usual (packs often lay several color variants on one row).
        /// Emits one entry per animation with explicit pixel rects.
        /// </summary>
        bool TrySegmentStrip(string folder, string file, Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            if ((long)w * h > 16_000_000) return false;   // skip giant atlases
            var px = ReadPixelsBlit(tex);
            if (px == null) return false;

            // columns that contain any visible pixel
            var solid = new bool[w];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (px[y * w + x].a > 12) { solid[x] = true; break; }

            // contiguous runs of solid columns
            var runs = new List<Vector2Int>();   // x = first col, y = last col
            int start = -1;
            for (int x = 0; x < w; x++)
            {
                if (solid[x] && start < 0) start = x;
                if (!solid[x] && start >= 0) { runs.Add(new Vector2Int(start, x - 1)); start = -1; }
            }
            if (start >= 0) runs.Add(new Vector2Int(start, w - 1));
            if (runs.Count < 2) return false;

            // merge detached specks into their neighbour frame: gap much smaller than a frame
            int medWidth = MedianRunWidth(runs);
            int mergeGap = Mathf.Max(2, medWidth / 4);
            var frames = new List<Vector2Int> { runs[0] };
            for (int i = 1; i < runs.Count; i++)
            {
                var last = frames[frames.Count - 1];
                if (runs[i].x - last.y - 1 <= mergeGap)
                    frames[frames.Count - 1] = new Vector2Int(last.x, runs[i].y);
                else frames.Add(runs[i]);
            }
            if (frames.Count < 2) return false;
            medWidth = MedianRunWidth(frames);

            // split into animation groups on unusually large gaps
            var gapList = new List<int>();
            for (int i = 1; i < frames.Count; i++) gapList.Add(frames[i].x - frames[i - 1].y - 1);
            var sortedGaps = new List<int>(gapList);
            sortedGaps.Sort();
            int medGap = sortedGaps[sortedGaps.Count / 2];
            int splitGap = Mathf.Max(medGap * 3, medWidth);
            var groups = new List<List<Vector2Int>> { new List<Vector2Int> { frames[0] } };
            for (int i = 1; i < frames.Count; i++)
            {
                if (gapList[i - 1] > splitGap) groups.Add(new List<Vector2Int>());
                groups[groups.Count - 1].Add(frames[i]);
            }

            bool any = false;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var grp = groups[gi];
                if (grp.Count < 2) continue;   // lone island = probably cover art, skip

                // uniform cell per group (max island width), full sheet height, centered
                int cellW = 0;
                foreach (var r in grp) cellW = Mathf.Max(cellW, r.y - r.x + 1);
                cellW = Mathf.Min(cellW + 2, w);
                var rects = new Rect[grp.Count];
                var sprites = new Sprite[grp.Count];
                float ppu = Mathf.Max(cellW, h);
                for (int i = 0; i < grp.Count; i++)
                {
                    float cx = (grp[i].x + grp[i].y + 1) * 0.5f;
                    float rx = Mathf.Round(Mathf.Clamp(cx - cellW * 0.5f, 0, w - cellW));
                    rects[i] = new Rect(rx, 0, cellW, h);
                    sprites[i] = Sprite.Create(tex, rects[i], new Vector2(0.5f, 0.5f), ppu);
                }
                string suffix = groups.Count > 1 ? " (part " + (gi + 1) + ")" : "";
                _entries.Add(new Entry
                {
                    pack = PackOf(folder),
                    label = Label(folder, file) + suffix + "  [" + grp.Count + "f seg]",
                    frames = sprites,
                    gridSheet = tex,
                    gridRects = rects
                });
                any = true;
            }
            return any;
        }

        static int MedianRunWidth(List<Vector2Int> runs)
        {
            var ws = runs.ConvertAll(r => r.y - r.x + 1);
            ws.Sort();
            return ws[ws.Count / 2];
        }

        /// <summary>CPU copy of any texture (compressed / non-readable welcome) via RT blit.</summary>
        static Color32[] ReadPixelsBlit(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Destroy(copy);
            return px;
        }
#endif

        static int TrailingNum(string s)
        {
            var m = Regex.Match(s, @"(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        // relative-to-pack: assets in the pack root show bare names; deeper subfolders keep a prefix
        string Label(string folder, string baseName)
        {
            string sub = System.IO.Path.GetFileName(folder.Replace('\\', '/').TrimEnd('/'));
            return sub == PackOf(folder) ? baseName : sub + " / " + baseName;
        }

        // ── UI ───────────────────────────────────────────────────────────────
        void OnGUI()
        {
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));
            if (!_visible)
            {
                GUI.Label(new Rect(10, Sh - 26, 200, 22), "[B] vfx browser");
                return;
            }

            GUILayout.BeginArea(new Rect(190, 10, 240, Sh - 20), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("VFX BROWSER  [B] hide");
            if (GUILayout.Button("rescan", GUILayout.Width(60))) Scan();
            GUILayout.EndHorizontal();

            if (_sel >= 0 && _sel < _entries.Count)
                DrawPreview(_entries[_sel]);
            else
                GUILayout.Label(_entries.Count == 0
                    ? "nothing found under\n" + rootFolder
                    : "select an entry below");

            if (_note != "") GUILayout.Label(_note);
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);
            string curPack = null;
            bool curFolded = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.pack != curPack)
                {
                    curPack = e.pack;
                    curFolded = _folded.Contains(curPack);
                    _packCounts.TryGetValue(curPack, out int n);
                    if (GUILayout.Button((curFolded ? "> " : "v ") + curPack + "  (" + n + ")", PackHeader))
                    {
                        if (curFolded) _folded.Remove(curPack); else _folded.Add(curPack);
                        curFolded = !curFolded;
                    }
                }
                if (curFolded) continue;
                bool on = i == _sel;
                if (GUILayout.Toggle(on, e.label, GUI.skin.button) && !on)
                {
                    _sel = i;
                    _note = "";
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        GUIStyle _packHeader;
        GUIStyle PackHeader => _packHeader ?? (_packHeader = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            stretchWidth = true
        });

        void DrawPreview(Entry e)
        {
            // animated preview - unscaled time so the global speed slider doesn't drag it
            int idx = e.frames.Length > 1
                ? (int)(Time.unscaledTime * Mathf.Max(1f, previewFps)) % e.frames.Length
                : 0;
            var sp = e.frames[idx];

            Rect r = GUILayoutUtility.GetRect(220, 120);
            var prev = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;

            if (sp != null && sp.texture != null)
            {
                Rect tr = sp.textureRect;
                var uv = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height,
                    tr.width / sp.texture.width, tr.height / sp.texture.height);
                float aspect = tr.height > 0 ? tr.width / tr.height : 1f;
                float h = r.height * 0.9f, w = h * aspect;
                if (w > r.width * 0.9f) { w = r.width * 0.9f; h = w / Mathf.Max(0.01f, aspect); }
                GUI.DrawTextureWithTexCoords(
                    new Rect(r.x + (r.width - w) * 0.5f, r.y + (r.height - h) * 0.5f, w, h),
                    sp.texture, uv, true);
            }

            GUILayout.Label(e.pack + " / " + e.label + "   frame " + (idx + 1) + "/" + e.frames.Length);
            previewFps = Row("fps", previewFps, 4f, 60f, previewFps.ToString("0"));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(24)))
                _target = (_target + TargetNames.Length - 1) % TargetNames.Length;
            GUILayout.Label(TargetNames[_target], GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(24)))
                _target = (_target + 1) % TargetNames.Length;
            if (GUILayout.Button("assign", GUILayout.Width(56)))
                Assign(e);
            GUILayout.EndHorizontal();
        }

        void Assign(Entry e)
        {
            if (tuning == null) return;

            if (_target == 5)   // puff chips want individual sprite assets
            {
                if (e.assetFrames == null)
                {
                    _note = "strip/grid slices can't persist as chips -\nuse a file-sequence or sliced entry";
                    return;
                }
                tuning.puff.sprites = e.assetFrames;
                _note = "assigned " + e.assetFrames.Length + " chip sprite(s) to Puff";
                MarkDirty();
                return;
            }

            if (_target == 7 && tunePanel == null) tunePanel = FindObjectOfType<FXTunePanel>();

            SheetFXSettings block =
                _target == 0 ? tuning.sheetSlash :
                _target == 1 ? tuning.sheetBurst :
                _target == 2 ? tuning.hitWood.burst :
                _target == 3 ? tuning.hitMetal.burst :
                _target == 6 ? tuning.puffSheet.anim :
                _target == 7 ? (tunePanel != null ? tunePanel.SelectedBlockSheet : null)
                             : tuning.hitStone.burst;

            if (block == null)
            {
                _note = "no target block - open a recipe in the tuner\nand select a Puff or Burst block first";
                return;
            }

            if (e.assetFrames != null)
            {
                block.frames = e.assetFrames;
                block.cellRects = new Rect[0];
                _note = "assigned " + e.assetFrames.Length + " frame(s) to " + TargetNames[_target];
            }
            else if (e.gridRects != null)
            {
                // segmentation result - explicit rects persist on the SO
                block.frames = new Sprite[0];
                block.sheet = e.gridSheet;
                block.cellRects = e.gridRects;
                block.frameCount = e.gridRects.Length;
                _note = "assigned " + e.gridRects.Length + " segmented frame(s) to " + TargetNames[_target];
            }
            else
            {
                // runtime slices can't serialize - hand the block the sheet recipe instead
                block.frames = new Sprite[0];
                block.cellRects = new Rect[0];
                block.sheet = e.gridSheet;
                block.cellSize = e.gridCell;
                block.cellHeight = e.gridCellH;
                block.row = 0;
                block.frameCount = e.gridCols;
                _note = "assigned as sheet (cell " + e.gridCell +
                        (e.gridCellH > 0 ? "x" + e.gridCellH : "") + ") to " + TargetNames[_target] +
                        "\ngrid: hunt rows with the row stepper";
            }
            MarkDirty();
        }

        static float Row(string label, float value, float min, float max, string readout)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(40));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(readout, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return value;
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(tuning);
#endif
        }
    }
}
