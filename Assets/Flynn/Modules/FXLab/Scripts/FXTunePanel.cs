using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// In-game lab UI (IMGUI - dev tool, not player UI). Left: scrollable fire-list of
    /// every moment + primitive (click = fire at focus AND select it in the tuner).
    /// Right: the tuner - sound picker with waveform trim editor, per-effect shape
    /// controls. Everything writes straight into FXLabTuning.asset, so dialled-in values
    /// are permanent. Tab toggles both panels.
    /// </summary>
    public class FXTunePanel : MonoBehaviour
    {
        public FXLabTuning tuning;
        public FXLabBoard board;
        public KeyCode toggleKey = KeyCode.Tab;
        [Tooltip("IMGUI scale multiplier - default text is tiny on modern screens.")]
        [Range(1f, 3f)] public float uiScale = 2f;

        static readonly (string label, FXKind kind)[] Entries =
        {
            ("Repair", FXKind.Repair),
            ("Item Drop", FXKind.ItemDrop),
            ("Item Idle", FXKind.ItemIdle),
            ("Item Pickup", FXKind.ItemPickup),
            ("Flash", FXKind.Flash),
            ("Squash", FXKind.Squash),
            ("Puff", FXKind.Puff),
            ("Sheet Slash", FXKind.SheetSlash),
            ("Sheet Burst", FXKind.SheetBurst),
            ("Arc Wipe", FXKind.ArcWipe),
            ("Arc Ribbon", FXKind.ArcRibbon),
            ("Hit Stop", FXKind.HitStop),
            ("Cam Nudge", FXKind.Nudge),
            ("Body Lunge", FXKind.Lunge),
            ("Overlay Fade", FXKind.OverlayFade),
            ("PuffSpriteSheet", FXKind.PuffSpriteSheet),
            ("Drifter", FXKind.Drifter),
            ("Tint State", FXKind.TintState),
            ("Glint", FXKind.Glint),
            ("Emote", FXKind.Emote),
            ("Droplets", FXKind.Droplets),
            ("Progress Glow", FXKind.ProgressGlow),
            ("Float Aura", FXKind.FloatAura),
            ("Converge", FXKind.Converge)
        };
        const int MomentCount = 4;   // Entries[0..3] are composed moments

        // add-block menu for the recipe editor - one entry per concrete FXBlock type
        static readonly (string label, System.Func<FXBlock> make)[] BlockTypes =
        {
            ("Sfx", () => new SfxBlock()),
            ("Flash", () => new FlashBlock()),
            ("Squash", () => new SquashBlock()),
            ("Puff", () => new PuffBlock()),
            ("Ring", () => new RingBlock()),
            ("Sparkle", () => new SparkleBlock()),
            ("Burst", () => new BurstBlock()),
            ("Nudge", () => new NudgeBlock()),
            ("HitStop", () => new HitStopBlock()),
            ("FadeOut", () => new FadeOutBlock()),
            ("Droplet", () => new DropletBlock()),
            ("Tint", () => new TintBlock()),
            ("Emote", () => new EmoteBlock()),
            ("Aura", () => new FloatAuraBlock()),
            ("Converge", () => new ConvergeBlock()),
            ("Recipe", () => new RecipeBlock()),
            ("Arc", () => new SwingArcBlock()),
            ("Lunge", () => new LungeBlock())
        };

        static readonly (string label, EmoteIcon icon)[] Emotes =
        {
            ("love", EmoteIcon.Affection), ("!", EmoteIcon.Alarm), ("?", EmoteIcon.Curious),
            ("note", EmoteIcon.Content), ("zZz", EmoteIcon.Sleep), ("mad", EmoteIcon.Anger),
            ("ha", EmoteIcon.Laugh)
        };

        const int WaveW = 320, WaveH = 56;

        bool _visible = true;
        int _effect;
        int _recipeSel = -1;   // >= 0 = recipe mode, overrides the kind tuner
        int _blockSel;
        bool _addMenu;
        bool _addLinked = true;   // new layers reuse the dialed primitives by default
        bool _confirmDelete;
        Vector2 _fireScroll;
        Vector2 _tuneScroll;
        Texture2D _waveTex;
        AudioClip _waveClip;
        PreviewSlot _appliedSlot;   // what the focus prop is currently wearing
        Sprite _appliedSprite;
        float _appliedScale = 1f;
        int _dragHandle;   // 0 = none, 1 = trim in, 2 = trim out
        bool _dragged;

        static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
        };

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) _visible = !_visible;

            // 1-9,0 fire the first ten FIRE-LIST entries. The panel owns this (not the board)
            // because the list order and the FXKind values drift apart whenever an entry is
            // culled - binding to the enum would silently mis-map the keys.
            // Keys stay quiet while an IMGUI text field (recipe rename) has focus.
            if (board == null || GUIUtility.keyboardControl != 0) return;
            for (int i = 0; i < Keys.Length && i < Entries.Length; i++)
                if (Input.GetKeyDown(Keys[i]))
                {
                    _effect = i;
                    _recipeSel = -1;
                    board.Fire(Entries[i].kind);
                }
        }

        SfxSlot SlotFor(FXKind kind)
        {
            switch (kind)
            {
                case FXKind.Repair: return tuning.repair.sfx;
                case FXKind.ItemDrop: return tuning.drop.sfx;
                case FXKind.ItemPickup: return tuning.pickup.sfx;
                case FXKind.Flash: return tuning.flash.sfx;
                case FXKind.Squash: return tuning.squash.sfx;
                case FXKind.Puff: return tuning.puff.sfx;
                case FXKind.SheetSlash: return tuning.sheetSlash.sfx;
                case FXKind.SheetBurst: return tuning.sheetBurst.sfx;
                case FXKind.ArcWipe: return tuning.arcWipe.sfx;
                case FXKind.ArcRibbon: return tuning.arcRibbon.sfx;
                case FXKind.OverlayFade: return tuning.overlayFade.sfx;
                case FXKind.PuffSpriteSheet: return tuning.puffSheet.sfx;
                case FXKind.TintState: return tuning.tintState.sfx;
                case FXKind.Glint: return tuning.glint.sfx;
                case FXKind.Emote: return tuning.emote.sfx;
                case FXKind.Droplets: return tuning.droplets.sfx;
                case FXKind.FloatAura: return tuning.floatAura.sfx;
                case FXKind.Converge: return tuning.converge.sfx;
                default: return null;   // HitStop / Nudge / Drifter / ProgressGlow are silent
            }
        }

        // virtual screen size after the scale matrix — all Rects use these
        float Sw => Screen.width / uiScale;
        float Sh => Screen.height / uiScale;

        void OnGUI()
        {
            // scale the whole IMGUI pass (rendering + input follow the matrix)
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));

            if (tuning == null) return;
            if (!_visible)
            {
                GUI.Label(new Rect(Sw - 150, 10, 140, 22), "[Tab] lab panels");
                return;
            }

            DrawFireList();
            DrawTuner();
            SyncPreview();   // after the tuner: the selection for this pass is settled

            if (GUI.changed) MarkDirty();
        }

        // ── left: scrollable fire-list ───────────────────────────────────────
        void DrawFireList()
        {
            GUILayout.BeginArea(new Rect(10, 10, 170, Sh - 20), GUI.skin.box);
            GUILayout.Label("FIRE  [Tab] hide");
            _fireScroll = GUILayout.BeginScrollView(_fireScroll);

            GUILayout.Label("- MOMENTS -");
            for (int i = 0; i < Entries.Length; i++)
            {
                if (i == MomentCount) GUILayout.Label("- PRIMITIVES -");
                string label = i < 10 ? Entries[i].label + "  [" + (i + 1) % 10 + "]" : Entries[i].label;
                if (GUILayout.Button(label))
                {
                    _effect = i;   // firing also selects it in the tuner
                    _recipeSel = -1;
                    _confirmDelete = false;
                    if (board != null) board.Fire(Entries[i].kind);
                }
            }

            GUILayout.Label("- RECIPES -");
            var recipes = tuning.recipes;
            if (recipes != null)
                for (int i = 0; i < recipes.Count; i++)
                {
                    if (recipes[i] == null) continue;
                    if (GUILayout.Button(recipes[i].name))
                    {
                        SelectRecipe(i);   // firing also selects it in the tuner
                        if (board != null) board.FireRecipe(recipes[i]);
                    }
                }
            if (GUILayout.Button("+ new recipe"))
            {
                if (tuning.recipes == null) tuning.recipes = new List<FXRecipe>();
                tuning.recipes.Add(new FXRecipe { name = "Recipe " + (tuning.recipes.Count + 1) });
                SelectRecipe(tuning.recipes.Count - 1);
                MarkDirty();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void SelectRecipe(int index)
        {
            _recipeSel = index;
            _blockSel = 0;
            _addMenu = false;
            _confirmDelete = false;
        }

        // ── right: tuner ─────────────────────────────────────────────────────
        void DrawTuner()
        {
            GUILayout.BeginArea(new Rect(Sw - 360, 10, 350, Sh - 20), GUI.skin.box);
            GUILayout.Label("FX LAB TUNER");
            uiScale = Row("ui scale", uiScale, 1f, 3f, uiScale.ToString("0.0") + "x");
            tuning.globalSpeed = Row("speed", tuning.globalSpeed, 0.3f, 1.5f, tuning.globalSpeed.ToString("0.00") + "x");
            GUILayout.Space(4);

            if (_recipeSel >= 0 && tuning.recipes != null && _recipeSel < tuning.recipes.Count)
                DrawRecipeTuner();
            else
                DrawKindTuner();

            GUILayout.EndArea();
        }

        void DrawKindTuner()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30)))
                _effect = (_effect + Entries.Length - 1) % Entries.Length;
            GUILayout.Label(Entries[_effect].label, CenteredLabel(), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30)))
                _effect = (_effect + 1) % Entries.Length;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _tuneScroll = GUILayout.BeginScrollView(_tuneScroll);

            var kind = Entries[_effect].kind;
            var slot = SlotFor(kind);
            if (slot != null)
                DrawSlot(slot);

            if (kind == FXKind.SheetSlash) { DrawSwingRig(); DrawSheetControls(tuning.sheetSlash); }
            else if (kind == FXKind.ArcWipe) DrawSwingRig();
            else if (kind == FXKind.ArcRibbon) { DrawSwingRig(); DrawRibbonControls(); }
            else if (kind == FXKind.SheetBurst) DrawSheetControls(tuning.sheetBurst);
            else if (kind == FXKind.ItemDrop) DrawDropControls();
            else if (kind == FXKind.ItemIdle) DrawIdleControls();
            else if (kind == FXKind.ItemPickup) DrawPickupControls();
            else if (kind == FXKind.Lunge) DrawLungeControls();
            else if (kind == FXKind.OverlayFade) DrawOverlayFadeControls();
            else if (kind == FXKind.PuffSpriteSheet) DrawPuffSheetControls(tuning.puffSheet);
            else if (kind == FXKind.Drifter) DrawDrifterControls();
            else if (kind == FXKind.TintState) DrawTintControls(tuning.tintState);
            else if (kind == FXKind.Glint) DrawGlintControls();
            else if (kind == FXKind.Emote) DrawEmoteControls();
            else if (kind == FXKind.Droplets) DrawDropletControls(tuning.droplets);
            else if (kind == FXKind.ProgressGlow) DrawProgressGlowControls();
            else if (kind == FXKind.FloatAura) DrawFloatAuraControls(tuning.floatAura);
            else if (kind == FXKind.Converge) DrawConvergeControls(tuning.converge);

            DrawPreviewRow(tuning.PreviewFor(kind), () => tuning.PreviewFor(kind, create: true));

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (slot != null && GUILayout.Button("Play SFX"))
                Audition(slot);
            if (board != null && GUILayout.Button("Fire FX"))
                board.Fire(kind);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Colors + curves: inspector on FXLabTuning.asset\n(play-mode edits persist there too)");
            GUILayout.EndScrollView();
        }

        // ── recipe tuner: flat block editor, one block's controls at a time ──
        void DrawRecipeTuner()
        {
            var recipes = tuning.recipes;
            var r = recipes[_recipeSel];

            // header: cycle recipes, rename inline
            GUILayout.BeginHorizontal();
            int cycle = 0;
            if (GUILayout.Button("<", GUILayout.Width(30))) cycle = -1;
            r.name = GUILayout.TextField(r.name, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30))) cycle = +1;
            GUILayout.EndHorizontal();
            if (cycle != 0)
                SelectRecipe((_recipeSel + recipes.Count + cycle) % recipes.Count);
            GUILayout.Space(6);

            _tuneScroll = GUILayout.BeginScrollView(_tuneScroll);

            // moment-level: does the swing tool open this recipe?
            r.swingOpens = GUILayout.Toggle(r.swingOpens, " swing opens (tool arc, then blocks land)");
            if (r.swingOpens)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("swing", GUILayout.Width(60));
                foreach (ArcVariant v in System.Enum.GetValues(typeof(ArcVariant)))
                {
                    bool on = r.arc == v;
                    if (GUILayout.Toggle(on, v.ToString(), GUI.skin.button) && !on)
                        r.arc = v;
                }
                GUILayout.EndHorizontal();
                r.contactDelay = Row("contact", r.contactDelay, 0f, 0.4f, r.contactDelay.ToString("0.00") + "s");
                DrawFacingControls();
            }
            r.tintFromTarget = GUILayout.Toggle(r.tintFromTarget,
                " material tint from target (one recipe, every material)");
            DrawPreviewRow(r.preview, () => r.preview);
            GUILayout.Space(4);

            // flat block list: enable | select | reorder | remove
            GUILayout.Label("- BLOCKS -");
            var blocks = r.blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                GUILayout.BeginHorizontal();
                if (b != null) b.enabled = GUILayout.Toggle(b.enabled, "", GUILayout.Width(18));
                bool sel = i == _blockSel;
                string lbl = BlockLabel(b) + (b != null && b.delay > 0f
                    ? "  +" + b.delay.ToString("0.00") + "s" : "");
                if (GUILayout.Toggle(sel, lbl, GUI.skin.button, GUILayout.ExpandWidth(true)) && !sel)
                    _blockSel = i;
                if (GUILayout.Button("^", GUILayout.Width(24)) && i > 0)
                {
                    (blocks[i - 1], blocks[i]) = (blocks[i], blocks[i - 1]);
                    _blockSel = i - 1;
                }
                if (GUILayout.Button("v", GUILayout.Width(24)) && i < blocks.Count - 1)
                {
                    (blocks[i + 1], blocks[i]) = (blocks[i], blocks[i + 1]);
                    _blockSel = i + 1;
                }
                if (GUILayout.Button("x", GUILayout.Width(24)))
                {
                    blocks.RemoveAt(i);
                    if (_blockSel >= blocks.Count) _blockSel = blocks.Count - 1;
                    MarkDirty();
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button(_addMenu ? "- add block" : "+ add block"))
                _addMenu = !_addMenu;
            if (_addMenu) DrawAddMenu(blocks);
            GUILayout.Space(6);

            // selected block: its full controls (sound = same waveform trim editor)
            if (_blockSel >= 0 && _blockSel < blocks.Count && blocks[_blockSel] != null)
            {
                var b = blocks[_blockSel];
                GUILayout.Label("- " + BlockLabel(b).ToUpper() + " -");
                DrawBlockControls(b);
                if (board != null && GUILayout.Button("Play block solo"))
                    board.FireBlock(b);
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (board != null && GUILayout.Button("Fire recipe"))
                board.FireRecipe(r);
            // start a new recipe from this one - a real copy, not a second name for the same
            // blocks (which is what duplicating the list entry in the inspector gives you)
            if (GUILayout.Button("dup", GUILayout.Width(50)))
            {
                recipes.Insert(_recipeSel + 1, r.DeepCopy());
                SelectRecipe(_recipeSel + 1);
                MarkDirty();
            }
            if (GUILayout.Button(_confirmDelete ? "sure?" : "del", GUILayout.Width(50)))
            {
                if (_confirmDelete)
                {
                    recipes.RemoveAt(_recipeSel);
                    _recipeSel = Mathf.Min(_recipeSel, recipes.Count - 1);
                    _confirmDelete = false;
                    MarkDirty();
                }
                else
                    _confirmDelete = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Colors + curves + sprites: inspector on FXLabTuning.asset\n(play-mode edits persist there too)");
            GUILayout.EndScrollView();
        }

        void DrawAddMenu(List<FXBlock> blocks)
        {
            // A new layer reuses what you already dialed - that is the point of the primitives
            // list. Untick only for a one-off that has to differ from everything else.
            _addLinked = GUILayout.Toggle(_addLinked, " use my dialed primitive settings");
            GUILayout.Label(_addLinked
                ? "adds a layer that PLAYS the primitive (re-dial it, every recipe follows)"
                : "adds a blank layer with default settings to customise");

            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var (label, make) in BlockTypes)
            {
                if (GUILayout.Button(label))
                {
                    var made = make();
                    if (made is IPrimitiveLinked linked) linked.UsePrimitive = _addLinked;
                    blocks.Add(made);
                    _blockSel = blocks.Count - 1;
                    _addMenu = false;
                    MarkDirty();
                }
                if (++col % 3 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>The link row for a block that mirrors a primitive. Returns TRUE when the
        /// block's own settings sliders should be drawn (i.e. it has been made custom) -
        /// while linked there is nothing to tune here, the primitive owns the values.</summary>
        bool DrawPrimitiveLink(FXBlock b)
        {
            if (!(b is IPrimitiveLinked pl)) return true;

            if (pl.UsePrimitive)
            {
                GUILayout.Label("plays the dialed '" + pl.PrimitiveName + "' primitive.\n" +
                                "Tune it in its own fire-list entry - every\nrecipe using it follows.");
                if (GUILayout.Button("make custom (copy values here)"))
                {
                    pl.CopyFromPrimitive(tuning);
                    pl.UsePrimitive = false;
                    MarkDirty();
                }
                return false;
            }

            GUILayout.Label("custom settings, this layer only");
            if (GUILayout.Button("re-link to '" + pl.PrimitiveName + "'"))
            {
                pl.UsePrimitive = true;
                MarkDirty();
            }
            return true;
        }

        static string BlockLabel(FXBlock b) =>
            b == null ? "(null)" : b.GetType().Name.Replace("Block", "");

        // Per-block controls, dispatched by type. Wrapper blocks reuse the same rows the
        // typed tuner shows; every SfxSlot goes through DrawSlot = full waveform trim.
        void DrawBlockControls(FXBlock b)
        {
            b.delay = Row("delay", b.delay, 0f, 1.5f, b.delay.ToString("0.00") + "s");
            // While a layer is linked to its primitive there is nothing to tune here - the
            // primitive owns the values. Composition params (offsets, tint, which emotion)
            // stay editable either way: those belong to the layer, not the effect.
            bool custom = DrawPrimitiveLink(b);
            switch (b)
            {
                case SfxBlock sb:
                    DrawSlot(sb.sfx);
                    break;
                case FlashBlock fb:
                    if (custom)
                    {
                        fb.flash.peak = Row("peak", fb.flash.peak, 0f, 1f, fb.flash.peak.ToString("0.00"));
                        fb.flash.attack = Row("attack", fb.flash.attack, 0f, 0.2f, fb.flash.attack.ToString("0.00") + "s");
                        fb.flash.decay = Row("decay", fb.flash.decay, 0f, 0.6f, fb.flash.decay.ToString("0.00") + "s");
                    }
                    fb.impactFrame = GUILayout.Toggle(fb.impactFrame, " impact frame (crisp first frame)");
                    fb.useMomentTint = GUILayout.Toggle(fb.useMomentTint, " use material tint");
                    if (custom) DrawSlot(fb.flash.sfx);
                    break;
                case SquashBlock qb:
                    if (custom)
                    {
                        qb.squash.punch = Row("punch", qb.squash.punch, 0f, 0.6f, qb.squash.punch.ToString("0.00"));
                        qb.squash.duration = Row("time", qb.squash.duration, 0.05f, 0.8f, qb.squash.duration.ToString("0.00") + "s");
                        DrawSlot(qb.squash.sfx);
                    }
                    break;
                case PuffBlock pb:
                    // two puff primitives exist - pick which one this layer mirrors
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("from", GUILayout.Width(60));
                    foreach (PuffBlock.Source src in System.Enum.GetValues(typeof(PuffBlock.Source)))
                    {
                        bool on = pb.source == src;
                        if (GUILayout.Toggle(on, src.ToString(), GUI.skin.button) && !on)
                        {
                            pb.source = src;
                            MarkDirty();
                        }
                    }
                    GUILayout.EndHorizontal();
                    if (custom)
                    {
                        pb.puff.count = Mathf.RoundToInt(Row("count", pb.puff.count, 1, 24, pb.puff.count.ToString()));
                        pb.puff.speed = Row("speed", pb.puff.speed, 0.2f, 3f, pb.puff.speed.ToString("0.00"));
                        pb.puff.life = Row("life", pb.puff.life, 0.1f, 1.5f, pb.puff.life.ToString("0.00") + "s");
                        pb.puff.size = Row("size", pb.puff.size, 0.01f, 0.15f, pb.puff.size.ToString("0.00"));
                        pb.puff.upBias = Row("up bias", pb.puff.upBias, 0f, 1f, pb.puff.upBias.ToString("0.00"));
                    }
                    pb.useMomentTint = GUILayout.Toggle(pb.useMomentTint, " chips from material tint");
                    if (custom)
                    {
                        DrawSlot(pb.puff.sfx);
                        // a custom Puff block owns its OWN PuffSettings, so it needs its own
                        // mote anim - the primitive's sheet does not reach in here
                        DrawMoteAnim(pb.puff, "Selected block");
                    }
                    break;
                case RingBlock rb:
                    if (custom)
                    {
                        rb.ring.startRadius = Row("start r", rb.ring.startRadius, 0f, 0.5f, rb.ring.startRadius.ToString("0.00"));
                        rb.ring.endRadius = Row("end r", rb.ring.endRadius, 0.1f, 1.2f, rb.ring.endRadius.ToString("0.00"));
                        rb.ring.duration = Row("time", rb.ring.duration, 0.1f, 1f, rb.ring.duration.ToString("0.00") + "s");
                    }
                    rb.useMomentTint = GUILayout.Toggle(rb.useMomentTint, " use material tint");
                    if (custom) DrawSlot(rb.ring.sfx);
                    break;
                case SparkleBlock kb:
                    if (custom)
                    {
                        kb.sparkle.count = Mathf.RoundToInt(Row("count", kb.sparkle.count, 1, 16, kb.sparkle.count.ToString()));
                        kb.sparkle.riseSpeed = Row("rise", kb.sparkle.riseSpeed, 0f, 1.2f, kb.sparkle.riseSpeed.ToString("0.00"));
                        kb.sparkle.life = Row("life", kb.sparkle.life, 0.2f, 2f, kb.sparkle.life.ToString("0.00") + "s");
                        kb.sparkle.twinkleHz = Row("twinkle", kb.sparkle.twinkleHz, 1f, 12f, kb.sparkle.twinkleHz.ToString("0") + "hz");
                    }
                    kb.useMomentTint = GUILayout.Toggle(kb.useMomentTint, " use material tint");
                    if (custom) DrawSlot(kb.sparkle.sfx);
                    break;
                case BurstBlock bb:
                    bb.aimAlongDir = GUILayout.Toggle(bb.aimAlongDir, " aim along direction");
                    if (custom)
                    {
                        DrawSlot(bb.anim.sfx);
                        DrawSheetControls(bb.anim);
                    }
                    break;
                case NudgeBlock nb:
                    if (custom)
                    {
                        nb.nudge.amplitude = Row("kick", nb.nudge.amplitude, 0f, 0.15f, nb.nudge.amplitude.ToString("0.000"));
                        nb.nudge.rotationDeg = Row("rot", nb.nudge.rotationDeg, 0f, 2f, nb.nudge.rotationDeg.ToString("0.0") + "deg");
                        nb.nudge.duration = Row("time", nb.nudge.duration, 0.05f, 0.6f, nb.nudge.duration.ToString("0.00") + "s");
                        nb.nudge.frequency = Row("freq", nb.nudge.frequency, 4f, 30f, nb.nudge.frequency.ToString("0") + "hz");
                    }
                    break;
                case HitStopBlock hb:
                    if (custom)
                    {
                        hb.hitStop.duration = Row("freeze", hb.hitStop.duration, 0f, 0.2f, hb.hitStop.duration.ToString("0.000") + "s");
                        hb.hitStop.timeScale = Row("scale", hb.hitStop.timeScale, 0f, 0.5f, hb.hitStop.timeScale.ToString("0.00"));
                    }
                    break;
                case FadeOutBlock db:
                    db.endAlpha = Row("to alpha", db.endAlpha, 0f, 1f, db.endAlpha.ToString("0.00"));
                    db.duration = Row("fade", db.duration, 0.05f, 1.5f, db.duration.ToString("0.00") + "s");
                    db.restoreDelay = Row("hold", db.restoreDelay, 0f, 3f, db.restoreDelay.ToString("0.00") + "s");
                    db.restoreDuration = Row("restore", db.restoreDuration, 0.05f, 1f, db.restoreDuration.ToString("0.00") + "s");
                    GUILayout.Label("hold 0 = stay faded (no restore)");
                    DrawSlot(db.sfx);
                    break;
                case DropletBlock wb:
                    if (custom)
                    {
                        DrawDropletControls(wb.droplets);
                        DrawSlot(wb.droplets.sfx);
                    }
                    break;
                case TintBlock tb:
                    if (custom)
                    {
                        DrawTintControls(tb.tint);
                        DrawSlot(tb.tint.sfx);
                    }
                    break;
                case EmoteBlock eb:
                    // WHICH emotion is this layer's business even when the look is shared
                    eb.icon = DrawEmoteRow(eb.icon);
                    if (custom)
                    {
                        DrawEmoteShape(eb.emote);
                        DrawSlot(eb.emote.sfx);
                    }
                    break;
                case FloatAuraBlock fa:
                    if (custom)
                    {
                        DrawFloatAuraControls(fa.aura);
                        DrawSlot(fa.aura.sfx);
                    }
                    fa.useMomentTint = GUILayout.Toggle(fa.useMomentTint, " use material tint");
                    break;
                case ConvergeBlock cv:
                    if (custom)
                    {
                        DrawConvergeControls(cv.converge);
                        DrawSlot(cv.converge.sfx);
                    }
                    cv.useMomentTint = GUILayout.Toggle(cv.useMomentTint, " use material tint");
                    break;
                case RecipeBlock cb:
                    DrawRecipePicker(cb);
                    break;
                case SwingArcBlock ab:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("arc", GUILayout.Width(60));
                    foreach (ArcVariant v in System.Enum.GetValues(typeof(ArcVariant)))
                    {
                        bool on = ab.arc == v;
                        if (GUILayout.Toggle(on, v.ToString(), GUI.skin.button) && !on) ab.arc = v;
                    }
                    GUILayout.EndHorizontal();
                    // facing is one angle now: this layer can pin its own, or ride whatever the
                    // caller/dial says (the normal case - a character supplies it later)
                    ab.overrideAngle = GUILayout.Toggle(ab.overrideAngle, " pin this layer's angle");
                    if (ab.overrideAngle)
                        ab.angleDeg = Row("angle", ab.angleDeg, 0f, 360f, ab.angleDeg.ToString("0") + "deg");
                    else
                        DrawSwingRig();
                    break;
                case LungeBlock _:
                    GUILayout.Label("character lunge - plays on the scene's SwingLungeFX.\n" +
                                    "Knobs live on the Lunge entry in the fire-list.");
                    break;
            }
        }

        /// <summary>Pick which recipe a RecipeBlock plays: cycle the names on the asset, and
        /// jump the tuner into the chosen one so a sub-recipe is one click away.</summary>
        void DrawRecipePicker(RecipeBlock block)
        {
            var recipes = tuning.recipes;
            if (recipes == null || recipes.Count == 0)
            {
                GUILayout.Label("no recipes on this asset yet");
                return;
            }

            // never let a recipe reference the one being edited - that is the loop guard's job,
            // but offering it in the picker at all is just a trap
            var pickable = new List<FXRecipe>();
            for (int i = 0; i < recipes.Count; i++)
                if (recipes[i] != null && i != _recipeSel) pickable.Add(recipes[i]);
            if (pickable.Count == 0)
            {
                GUILayout.Label("no other recipe to nest");
                return;
            }

            int at = pickable.FindIndex(r => string.Equals(r.name, block.recipe,
                System.StringComparison.OrdinalIgnoreCase));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30)))
                block.recipe = pickable[(at - 1 + pickable.Count) % pickable.Count].name;
            GUILayout.Label(at >= 0 ? block.recipe : "(none - pick one)",
                CenteredLabel(), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30)))
                block.recipe = pickable[(at + 1) % pickable.Count].name;
            GUILayout.EndHorizontal();

            if (at >= 0 && GUILayout.Button("edit '" + block.recipe + "'"))
                SelectRecipe(recipes.IndexOf(pickable[at]));
            GUILayout.Label("delay above = when this sub-recipe lands\n(a hit's contact delay is just this)");
        }

        /// <summary>The emotion picker - one button per feeling, the selected one stays down.</summary>
        EmoteIcon DrawEmoteRow(EmoteIcon current)
        {
            var picked = current;
            GUILayout.BeginHorizontal();
            foreach (var (label, icon) in Emotes)
            {
                bool on = current == icon;
                if (GUILayout.Toggle(on, label, GUI.skin.button) && !on) picked = icon;
            }
            GUILayout.EndHorizontal();
            return picked;
        }

        void DrawEmoteShape(EmoteSettings s)
        {
            s.size = Row("size", s.size, 0.06f, 0.4f, s.size.ToString("0.00"));
            s.riseOffset = Row("height", s.riseOffset, 0f, 0.5f, s.riseOffset.ToString("0.00"));
            s.hold = Row("hold", s.hold, 0.2f, 2.5f, s.hold.ToString("0.00") + "s");
            s.overshoot = Row("pop", s.overshoot, 1f, 1.5f, "x" + s.overshoot.ToString("0.00"));
        }

        void Audition(SfxSlot slot)
        {
            FXAudio.Play(slot, board != null && board.focus != null
                ? board.focus.transform.position : Vector3.zero);
        }

        void DrawSlot(SfxSlot slot)
        {
            // An authored SoundSO owns its own level/pitch/region, so the lab's clip picker and
            // trim editor have nothing to say about it - show what is slotted and get out of
            // the way. (Assign the asset in the inspector; runtime IMGUI has no object picker.)
            if (slot.UsesSound)
            {
                GUILayout.Label("SoundSO: " + slot.sound.name, CenteredLabel());
                GUILayout.Label("level / pitch / region live on that asset");
                slot.volume = Row("mix", slot.volume, 0f, 1f, slot.volume.ToString("0.00"));
                slot.delay = Row("stagger", slot.delay, 0f, 0.5f, slot.delay.ToString("0.00") + "s");
                if (GUILayout.Button("unslot (back to clip picker)"))
                {
                    slot.sound = null;
                    MarkDirty();
                }
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) CycleClip(slot, -1);
            GUILayout.Label(slot.clip != null ? slot.clip.name : "(no sound)",
                CenteredLabel(), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30))) CycleClip(slot, +1);
            GUILayout.EndHorizontal();

            float len = slot.clip != null ? slot.clip.length : 0f;

            if (slot.clip != null)
                DrawWaveform(slot);

            slot.volume = Row("volume", slot.volume, 0f, 1f, slot.volume.ToString("0.00"));
            slot.pitchJitter = Row("jitter", slot.pitchJitter, 0f, 0.3f, slot.pitchJitter.ToString("0.00"));
            slot.delay = Row("stagger", slot.delay, 0f, 0.5f, slot.delay.ToString("0.00") + "s");
            slot.trimStart = Row("trim in", slot.trimStart, 0f, 1f, (slot.trimStart * len).ToString("0.00") + "s");
            slot.trimEnd = Row("trim out", slot.trimEnd, 0f, 1f, (slot.trimEnd * len).ToString("0.00") + "s");
            if (slot.trimEnd < slot.trimStart) slot.trimEnd = slot.trimStart;

            if (slot.variants != null && slot.variants.Length > 0)
                GUILayout.Label("+" + slot.variants.Length + " variant clip(s), random per play (edit in inspector)");
            GUILayout.Label("slot a SoundSO on this block in the\ninspector for pitch/region control");
        }

        void DrawDropControls()
        {
            var s = tuning.drop;
            GUILayout.Space(6);
            s.hopDistance = Row("hop dist", s.hopDistance, 0.1f, 1.2f, s.hopDistance.ToString("0.00"));
            s.hopHeight = Row("hop high", s.hopHeight, 0.05f, 1f, s.hopHeight.ToString("0.00"));
            s.hopDuration = Row("hop time", s.hopDuration, 0.1f, 1f, s.hopDuration.ToString("0.00") + "s");
            s.bounces = Mathf.RoundToInt(Row("bounces", s.bounces, 0, 3, s.bounces.ToString()));
            s.itemSize = Row("item size", s.itemSize, 0.04f, 0.3f, s.itemSize.ToString("0.00"));
        }

        void DrawIdleControls()
        {
            var s = tuning.idle;
            GUILayout.Space(6);
            s.hoverHeight = Row("hover", s.hoverHeight, 0f, 0.25f, s.hoverHeight.ToString("0.00"));
            s.bobAmplitude = Row("bob amp", s.bobAmplitude, 0f, 0.15f, s.bobAmplitude.ToString("0.00"));
            s.bobPeriod = Row("bob time", s.bobPeriod, 0.5f, 4f, s.bobPeriod.ToString("0.0") + "s");
            s.shadowAlpha = Row("shadow", s.shadowAlpha, 0f, 0.6f, s.shadowAlpha.ToString("0.00"));

            GUILayout.BeginHorizontal();
            s.glint = GUILayout.Toggle(s.glint, " glint");
            s.glow = GUILayout.Toggle(s.glow, " glow (white)");
            GUILayout.EndHorizontal();
            if (s.glint)
                s.glintInterval = Row("wink every", s.glintInterval, 0.5f, 6f, s.glintInterval.ToString("0.0") + "s");
            if (s.glow)
                s.glowAlpha = Row("glow amt", s.glowAlpha, 0f, 0.6f, s.glowAlpha.ToString("0.00"));

            GUILayout.Label("fire = spawn / clear; Item Drop settles into this loop");
        }

        void DrawLungeControls()
        {
            var s = tuning.swingLunge;
            GUILayout.Space(6);
            s.anticipation = Row("wind-up", s.anticipation, 0.01f, 0.25f, s.anticipation.ToString("0.00") + "s");
            s.backOffset = Row("pull back", s.backOffset, 0f, 0.15f, s.backOffset.ToString("0.00"));
            s.lungeOffset = Row("lunge", s.lungeOffset, 0f, 0.3f, s.lungeOffset.ToString("0.00"));
            s.lungeDuration = Row("push time", s.lungeDuration, 0.02f, 0.4f, s.lungeDuration.ToString("0.00") + "s");
            s.settleDuration = Row("settle", s.settleDuration, 0.05f, 0.6f, s.settleDuration.ToString("0.00") + "s");
            s.stretch = Row("stretch", s.stretch, 0f, 0.5f, s.stretch.ToString("0.00"));
            s.torqueDegrees = Row("torque", s.torqueDegrees, 0f, 25f, s.torqueDegrees.ToString("0") + "deg");
            s.swingAnimDelay = Row("anim delay", s.swingAnimDelay, 0f, 0.3f, s.swingAnimDelay.ToString("0.00") + "s");
            GUILayout.Label("auto-plays with every swing; this button fires it alone.\nanim delay holds the idle pose through the wind-up");
        }

        void DrawOverlayFadeControls()
        {
            var s = tuning.overlayFade;
            GUILayout.Space(6);
            s.fadedAlpha = Row("alpha", s.fadedAlpha, 0f, 1f, s.fadedAlpha.ToString("0.00"));
            s.fadeOutDuration = Row("fade", s.fadeOutDuration, 0.02f, 0.6f, s.fadeOutDuration.ToString("0.00") + "s");
            s.fadeInDuration = Row("restore", s.fadeInDuration, 0.02f, 0.8f, s.fadeInDuration.ToString("0.00") + "s");
            GUILayout.Label("fire = toggle faded state on the focus prop\n(game: trigger/sorting calls SetFaded, no player math here)");
        }

        /// <summary>The sprite-sheet puff: the same debris physics, but every mote plays a
        /// dust animation instead of being a static dot.</summary>
        void DrawPuffSheetControls(PuffSettings s)
        {
            DrawPuffControls(s);
            DrawMoteAnim(s, "Puff sheet");
        }

        /// <summary>The per-mote frame animation, shown wherever a PuffSettings is dialed -
        /// the primitive AND a recipe's Puff block, which carries its own copy.</summary>
        void DrawMoteAnim(PuffSettings s, string browserTarget)
        {
            GUILayout.Space(6);
            GUILayout.Label("- MOTE ANIMATION -");
            bool hasArt = s.anim != null
                && (s.anim.sheet != null || (s.anim.frames != null && s.anim.frames.Length > 0));
            if (!hasArt)
                GUILayout.Label("no sheet yet - open the VFX browser [B],\npick a dust anim, assign to '"
                                + browserTarget + "'");
            s.animOverLife = GUILayout.Toggle(s.animOverLife, " fit anim to mote life");
            DrawSheetControls(s.anim);
        }

        /// <summary>The sheet block the VFX browser's "Selected block" target writes into -
        /// whichever recipe block is open in the tuner, when it animates from a sheet.
        /// Null when the selection has no sheet to assign to.</summary>
        public SheetFXSettings SelectedBlockSheet
        {
            get
            {
                if (tuning == null || _recipeSel < 0 || tuning.recipes == null
                    || _recipeSel >= tuning.recipes.Count) return null;
                var r = tuning.recipes[_recipeSel];
                if (r?.blocks == null || _blockSel < 0 || _blockSel >= r.blocks.Count) return null;
                switch (r.blocks[_blockSel])
                {
                    case PuffBlock pb: return pb.puff.anim;
                    case BurstBlock bb: return bb.anim;
                    default: return null;
                }
            }
        }

        void DrawPuffControls(PuffSettings s)
        {
            GUILayout.Space(6);
            s.count = Mathf.RoundToInt(Row("count", s.count, 1, 24, s.count.ToString()));
            s.speed = Row("speed", s.speed, 0.2f, 3f, s.speed.ToString("0.00"));
            s.life = Row("life", s.life, 0.1f, 1.5f, s.life.ToString("0.00") + "s");
            s.size = Row("size", s.size, 0.01f, 0.15f, s.size.ToString("0.00"));
            s.upBias = Row("up bias", s.upBias, 0f, 1f, s.upBias.ToString("0.00"));
            s.gravity = Row("gravity", s.gravity, -8f, 3f, s.gravity.ToString("0.0"));
        }

        void DrawDrifterControls()
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("kind", GUILayout.Width(60));
            string[] names = { "leaves", "flies", "pollen", "steam" };
            for (int i = 0; i < names.Length; i++)
            {
                bool on = (int)tuning.drifterVariant == i;
                if (GUILayout.Toggle(on, names[i], GUI.skin.button) && !on)
                    tuning.drifterVariant = (DrifterVariant)i;
            }
            GUILayout.EndHorizontal();

            var s = tuning.drifterVariant == DrifterVariant.Fireflies ? tuning.driftFireflies
                : tuning.drifterVariant == DrifterVariant.Pollen ? tuning.driftPollen
                : tuning.drifterVariant == DrifterVariant.Steam ? tuning.driftSteam
                : tuning.driftLeaves;
            s.count = Mathf.RoundToInt(Row("count", s.count, 1, 24, s.count.ToString()));
            s.drift.x = Row("drift X", s.drift.x, -0.4f, 0.4f, s.drift.x.ToString("0.00"));
            s.drift.y = Row("drift Y", s.drift.y, -0.4f, 0.4f, s.drift.y.ToString("0.00"));
            s.wobbleAmp = Row("wobble", s.wobbleAmp, 0f, 0.3f, s.wobbleAmp.ToString("0.00"));
            s.flickerAmp = Row("flicker", s.flickerAmp, 0f, 1f, s.flickerAmp.ToString("0.00"));
            s.size = Row("size", s.size, 0.005f, 0.12f, s.size.ToString("0.000"));
            s.life = Row("life", s.life, 0.5f, 15f, s.life.ToString("0.0") + "s");
            GUILayout.Label("fire = toggle the loop at the focus prop\n(restart after edits to respawn motes)");
        }

        void DrawTintControls(TintStateSettings s)
        {
            GUILayout.Space(6);
            s.blend = Row("blend", s.blend, 0f, 1f, s.blend.ToString("0.00"));
            s.inDuration = Row("in", s.inDuration, 0.02f, 1f, s.inDuration.ToString("0.00") + "s");
            s.outDuration = Row("out", s.outDuration, 0.02f, 2f, s.outDuration.ToString("0.00") + "s");
            s.autoRevert = Row("revert", s.autoRevert, 0f, 20f, s.autoRevert.ToString("0.0") + "s");
            GUILayout.Label("fire = toggle tint on focus prop; revert 0 = stays\n(wet soil / wilt / scan highlight - color in inspector)");
        }

        void DrawGlintControls()
        {
            var s = tuning.glint;
            GUILayout.Space(6);
            s.interval = Row("every", s.interval, 0.3f, 8f, s.interval.ToString("0.0") + "s");
            s.intervalJitter = Row("jitter", s.intervalJitter, 0f, 1f, s.intervalJitter.ToString("0.00"));
            s.size = Row("size", s.size, 0.01f, 0.2f, s.size.ToString("0.00"));
            s.duration = Row("wink", s.duration, 0.1f, 1f, s.duration.ToString("0.00") + "s");
            GUILayout.Label("fire = toggle shimmer loop on focus prop\n(solar panels, tool-ready, hover ack)");
        }

        void DrawEmoteControls()
        {
            var s = tuning.emote;
            GUILayout.Space(6);
            var picked = DrawEmoteRow(tuning.emoteIcon);
            if (picked != tuning.emoteIcon)
            {
                tuning.emoteIcon = picked;
                if (board != null) board.Fire(FXKind.Emote);   // hear/see the feeling you clicked
            }
            DrawEmoteShape(s);
            GUILayout.Label("fire = pop the selected emotion above the focus prop.\n" +
                            "Art per emotion: emote.iconSet on FXLabTuning.asset\n" +
                            "(no sprite = a dot in that emotion's colour)");
        }

        /// <summary>Stand-in art for the selected effect: the prop wears a real sprite so the
        /// effect is dialed against the silhouette it will play on, not a coloured square.
        /// Sprites are assigned on the asset (runtime IMGUI has no object picker).</summary>
        void DrawPreviewRow(PreviewSlot slot, System.Func<PreviewSlot> create)
        {
            GUILayout.Space(6);
            GUILayout.Label("- PREVIEW ART -");
            if (slot == null)
            {
                if (GUILayout.Button("+ preview art slot"))
                {
                    create();
                    MarkDirty();
                }
                GUILayout.Label("adds a sprite field on the asset for this effect");
                return;
            }

            GUILayout.Label(slot.sprite != null
                ? "wearing: " + slot.sprite.name
                : "(drop a sprite into this effect's preview slot on the asset)");
            if (slot.sprite != null)
            {
                slot.scale = Row("art scale", slot.scale, 0.1f, 6f, "x" + slot.scale.ToString("0.00"));
                GUILayout.BeginHorizontal();
                if (board != null && GUILayout.Button("wear")) board.ApplyPreview(slot);
                if (board != null && GUILayout.Button("strip")) board.ClearPreview();
                GUILayout.EndHorizontal();
            }
            tuning.autoApplyPreview = GUILayout.Toggle(tuning.autoApplyPreview, " auto-wear on select");
        }

        // The slot the current selection owns: a recipe carries its own, a primitive gets one
        // from the asset's per-kind list (created on demand from the panel).
        PreviewSlot CurrentPreview(bool create = false)
        {
            if (_recipeSel >= 0 && tuning.recipes != null && _recipeSel < tuning.recipes.Count)
            {
                var r = tuning.recipes[_recipeSel];
                return r != null ? r.preview : null;
            }
            return tuning.PreviewFor(Entries[_effect].kind, create);
        }

        // Re-dress the prop only when the SLOT, its sprite or its scale actually changed -
        // otherwise every OnGUI pass would re-apply, and a live scale drag would never land.
        void SyncPreview()
        {
            if (board == null || !tuning.autoApplyPreview) return;
            var slot = CurrentPreview();
            bool same = ReferenceEquals(slot, _appliedSlot)
                && (slot == null || (slot.sprite == _appliedSprite && Mathf.Approximately(slot.scale, _appliedScale)));
            if (same) return;

            _appliedSlot = slot;
            _appliedSprite = slot != null ? slot.sprite : null;
            _appliedScale = slot != null ? slot.scale : 1f;
            board.ApplyPreview(slot);
        }

        void DrawDropletControls(DropletSettings s)
        {
            GUILayout.Space(6);
            s.count = Mathf.RoundToInt(Row("count", s.count, 1, 24, s.count.ToString()));
            s.speed = Row("speed", s.speed, 0.3f, 3f, s.speed.ToString("0.00"));
            s.spreadDegrees = Row("spread", s.spreadDegrees, 5f, 90f, s.spreadDegrees.ToString("0") + "deg");
            s.life = Row("life", s.life, 0.2f, 1.5f, s.life.ToString("0.00") + "s");
            s.stretch = Row("stretch", s.stretch, 1f, 3f, "x" + s.stretch.ToString("0.0"));
        }

        /// <summary>The swing's one continuous facing + how it sits on the character. Stands in
        /// for the character's own facing until one drives it.</summary>
        void DrawSwingRig()
        {
            var rig = tuning.swingRig;
            GUILayout.Space(6);
            GUILayout.Label("- SWING FACING -");
            tuning.swingAngleDeg = Row("angle", tuning.swingAngleDeg, 0f, 360f,
                tuning.swingAngleDeg.ToString("0") + "deg");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("right")) tuning.swingAngleDeg = 0f;
            if (GUILayout.Button("up")) tuning.swingAngleDeg = 90f;
            if (GUILayout.Button("left")) tuning.swingAngleDeg = 180f;
            if (GUILayout.Button("down")) tuning.swingAngleDeg = 270f;
            GUILayout.EndHorizontal();
            rig.angleOffsetDeg = Row("art offset", rig.angleOffsetDeg, -180f, 180f,
                rig.angleOffsetDeg.ToString("0") + "deg");
            rig.behindPlayer = GUILayout.Toggle(rig.behindPlayer, " draw behind the character");
            GUILayout.Label("centred on the character's origin and\nparented to them - the angle orbits it");
        }

        /// <summary>Only the knobs you cannot judge without dragging them live - the crescent's
        /// shape and its timing. Colours + curves stay in the inspector, like every other block.</summary>
        void DrawRibbonControls()
        {
            var s = tuning.arcRibbon;
            GUILayout.Space(6);
            GUILayout.Label("- RIBBON -");
            s.groundSquash = Row("iso squash", s.groundSquash, 0.05f, 1f, s.groundSquash.ToString("0.00"));
            s.sweepDeg = Row("sweep", s.sweepDeg, -220f, 220f, s.sweepDeg.ToString("0") + "deg");
            s.contactAt = Row("contact", s.contactAt, 0f, 1f, s.contactAt.ToString("0.00"));
            s.radius = Row("reach", s.radius, 0.15f, 2f, s.radius.ToString("0.00"));
            s.width = Row("width", s.width, 0.02f, 0.6f, s.width.ToString("0.00"));
            s.feather = Row("feather", s.feather, 0f, 0.9f, s.feather.ToString("0.00"));
            s.windup = Row("windup", s.windup, 0f, 0.3f, s.windup.ToString("0.00") + "s");
            s.duration = Row("sweep time", s.duration, 0.05f, 0.6f, s.duration.ToString("0.00") + "s");
            s.tailFrac = Row("tail", s.tailFrac, 0.05f, 1f, s.tailFrac.ToString("0.00"));
            s.headBoost = Row("head", s.headBoost, 0f, 2f, s.headBoost.ToString("0.00"));
            s.aimIsScreenSpace = GUILayout.Toggle(s.aimIsScreenSpace, " aim is a screen angle");
            GUILayout.Label("check the aim at 45/135deg, not the cardinals -\nthe squash deflects only the diagonals");
        }

        void DrawFloatAuraControls(FloatAuraSettings s)
        {
            GUILayout.Space(6);
            s.count = Mathf.RoundToInt(Row("icons", s.count, 1, 16, s.count.ToString()));
            s.emitOver = Row("emit over", s.emitOver, 0f, 1.5f, s.emitOver.ToString("0.00") + "s");
            s.riseHeight = Row("rise", s.riseHeight, 0.05f, 1.5f, s.riseHeight.ToString("0.00"));
            s.riseDuration = Row("climb", s.riseDuration, 0.2f, 2.5f, s.riseDuration.ToString("0.00") + "s");
            s.spread = Row("spread", s.spread, 0f, 0.8f, s.spread.ToString("0.00"));
            s.swayAmp = Row("sway", s.swayAmp, 0f, 0.2f, s.swayAmp.ToString("0.00"));
            s.swayHz = Row("sway hz", s.swayHz, 0.2f, 4f, s.swayHz.ToString("0.0"));
            s.size = Row("size", s.size, 0.02f, 0.4f, s.size.ToString("0.00"));
            s.fadeStart = Row("fade at", s.fadeStart, 0f, 1f, s.fadeStart.ToString("0.00"));
            GUILayout.Label("icons/tints: inspector. emit over 0 = burst\ninstead of a stream");
        }

        void DrawConvergeControls(ConvergeSettings s)
        {
            GUILayout.Space(6);
            s.count = Mathf.RoundToInt(Row("pieces", s.count, 1, 24, s.count.ToString()));
            s.startRadius = Row("from r", s.startRadius, 0.05f, 2f, s.startRadius.ToString("0.00"));
            s.radiusJitter = Row("r jitter", s.radiusJitter, 0f, 0.4f, s.radiusJitter.ToString("0.00"));
            s.angleJitter = Row("ang jitter", s.angleJitter, 0f, 45f, s.angleJitter.ToString("0") + "deg");
            s.duration = Row("travel", s.duration, 0.1f, 2f, s.duration.ToString("0.00") + "s");
            s.stagger = Row("stagger", s.stagger, 0f, 0.25f, s.stagger.ToString("0.00") + "s");
            s.swirlDegrees = Row("swirl", s.swirlDegrees, -180f, 180f, s.swirlDegrees.ToString("0") + "deg");
            s.spinDegrees = Row("spin", s.spinDegrees, 0f, 720f, s.spinDegrees.ToString("0") + "deg");
            s.size = Row("size", s.size, 0.02f, 0.3f, s.size.ToString("0.00"));
            s.endScale = Row("end size", s.endScale, 0f, 1.5f, "x" + s.endScale.ToString("0.00"));
            s.fadeStart = Row("fade at", s.fadeStart, 0f, 1f, s.fadeStart.ToString("0.00"));
            GUILayout.Label("sprites/tints + the pull curve: inspector.\nEase-IN pull = the satisfying snap home");
        }

        void DrawProgressGlowControls()
        {
            var s = tuning.progressGlow;
            GUILayout.Space(6);
            s.maxAlpha = Row("glow", s.maxAlpha, 0f, 1f, s.maxAlpha.ToString("0.00"));
            s.pulseAmp = Row("pulse", s.pulseAmp, 0f, 0.5f, s.pulseAmp.ToString("0.00"));
            s.pulseHzStart = Row("hz @0%", s.pulseHzStart, 0.2f, 4f, s.pulseHzStart.ToString("0.0"));
            s.pulseHzEnd = Row("hz @100%", s.pulseHzEnd, 0.2f, 4f, s.pulseHzEnd.ToString("0.0"));
            GUILayout.Label("fire = step progress +25% on focus prop (wraps to clear)");
        }

        void DrawPickupControls()
        {
            var s = tuning.pickup;
            GUILayout.Space(6);
            s.anticipation = Row("wind-up", s.anticipation, 0f, 0.3f, s.anticipation.ToString("0.00"));
            s.flyDuration = Row("fly time", s.flyDuration, 0.1f, 1f, s.flyDuration.ToString("0.00") + "s");
            s.arcHeight = Row("arc high", s.arcHeight, 0f, 0.6f, s.arcHeight.ToString("0.00"));
            s.itemSize = Row("item size", s.itemSize, 0.04f, 0.3f, s.itemSize.ToString("0.00"));
            s.receiverPopScale = Row("recv pop", s.receiverPopScale, 1f, 1.3f,
                "x" + s.receiverPopScale.ToString("0.00"));
            s.receiverPopDuration = Row("pop time", s.receiverPopDuration, 0.1f, 0.6f,
                s.receiverPopDuration.ToString("0.00") + "s");
        }

        // Swing facing: pick which player anim fires + dial the slash re-aim per facing.
        // Values live per facing on the SO, so each direction keeps its own rotation/flip.
        void DrawFacingControls()
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("facing", GUILayout.Width(60));
            string[] names = { "45", "front", "back" };
            for (int i = 0; i < names.Length; i++)
            {
                bool on = (int)tuning.swingFacing == i;
                if (GUILayout.Toggle(on, names[i], GUI.skin.button) && !on)
                    tuning.swingFacing = (SwingFacing)i;
            }
            GUILayout.EndHorizontal();

            var f = tuning.swingFacing == SwingFacing.Front ? tuning.facingFront
                : tuning.swingFacing == SwingFacing.Back ? tuning.facingBack : tuning.facing45;
            f.slashAngleDeg = Row("slash rot", f.slashAngleDeg, -180f, 180f,
                f.slashAngleDeg.ToString("0") + "deg");
            f.slashOffset.x = Row("slash X", f.slashOffset.x, -0.6f, 0.6f,
                f.slashOffset.x.ToString("0.00"));
            f.slashOffset.y = Row("slash Y", f.slashOffset.y, -0.6f, 0.6f,
                f.slashOffset.y.ToString("0.00"));
            f.slashFlipY = GUILayout.Toggle(f.slashFlipY, " slash flip Y (this facing)");
            f.slashBehindPlayer = GUILayout.Toggle(f.slashBehindPlayer, " slash behind player");
            GUILayout.Space(4);
        }

        // Sheet-anim shape controls. Pack sheets keep anims on ODD rows (0, 2, 4... are
        // empty spacers), so the row stepper is the fast way to hunt animations.
        void DrawSheetControls(SheetFXSettings s)
        {
            GUILayout.Space(6);
            if (s.frames != null && s.frames.Length > 0)
            {
                GUILayout.Label("using " + s.frames.Length + " individual frame sprite(s) - edit in inspector");
                s.fps = Row("fps", s.fps, 6f, 60f, s.fps.ToString("0"));
                s.worldSize = Row("size", s.worldSize, 0.2f, 3f, s.worldSize.ToString("0.00"));
                s.angleOffsetDeg = Row("art angle", s.angleOffsetDeg, -180f, 180f, s.angleOffsetDeg.ToString("0") + "deg");
                s.offsetY = Row("offset Y", s.offsetY, -0.5f, 0.5f, s.offsetY.ToString("0.00"));
                s.flipY = GUILayout.Toggle(s.flipY, " flip Y (arc downward)");
                DrawSheetLayers(s);
                return;
            }
            if (s.cellRects != null && s.cellRects.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.cellRects.Length + " segmented frames (browser rects)",
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("clear", GUILayout.Width(50)))
                {
                    s.cellRects = new Rect[0];
                    MarkDirty();
                }
                GUILayout.EndHorizontal();
                s.fps = Row("fps", s.fps, 6f, 60f, s.fps.ToString("0"));
                s.worldSize = Row("size", s.worldSize, 0.2f, 3f, s.worldSize.ToString("0.00"));
                s.angleOffsetDeg = Row("art angle", s.angleOffsetDeg, -180f, 180f, s.angleOffsetDeg.ToString("0") + "deg");
                s.offsetY = Row("offset Y", s.offsetY, -0.5f, 0.5f, s.offsetY.ToString("0.00"));
                s.flipY = GUILayout.Toggle(s.flipY, " flip Y (arc downward)");
                DrawSheetLayers(s);
                return;
            }
            int cellH = s.cellHeight > 0 ? s.cellHeight : s.cellSize;
            int maxRow = s.sheet != null ? Mathf.Max(0, s.sheet.height / Mathf.Max(1, cellH) - 1) : 19;
            int maxCols = s.sheet != null ? s.sheet.width / Mathf.Max(1, s.cellSize) : 12;

            GUILayout.BeginHorizontal();
            GUILayout.Label("row", GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(30))) s.row = Mathf.Max(0, s.row - 1);
            GUILayout.Label(s.row + " / " + maxRow + "  (anims sit on odd rows)",
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("+", GUILayout.Width(30))) s.row = Mathf.Min(maxRow, s.row + 1);
            GUILayout.EndHorizontal();

            s.frameCount = Mathf.RoundToInt(Row("frames", s.frameCount, 1, maxCols, s.frameCount.ToString()));
            s.fps = Row("fps", s.fps, 6f, 60f, s.fps.ToString("0"));
            s.worldSize = Row("size", s.worldSize, 0.2f, 3f, s.worldSize.ToString("0.00"));
            s.angleOffsetDeg = Row("art angle", s.angleOffsetDeg, -180f, 180f, s.angleOffsetDeg.ToString("0") + "deg");
            s.offsetY = Row("offset Y", s.offsetY, -0.5f, 0.5f, s.offsetY.ToString("0.00"));
            s.flipY = GUILayout.Toggle(s.flipY, " flip Y (arc downward)");
            DrawSheetLayers(s);
        }

        void DrawSheetLayers(SheetFXSettings s)
        {
            GUILayout.BeginHorizontal();
            s.glow = GUILayout.Toggle(s.glow, " glow");
            GUILayout.EndHorizontal();
            if (s.glow)
            {
                s.glowAlpha = Row("glow amt", s.glowAlpha, 0f, 1f, s.glowAlpha.ToString("0.00"));
                s.glowScale = Row("glow size", s.glowScale, 1f, 2f, s.glowScale.ToString("0.00"));
            }
            s.ghostCount = Mathf.RoundToInt(Row("ghosts", s.ghostCount, 0, 4, s.ghostCount.ToString()));
            if (s.ghostCount > 0)
            {
                s.ghostFrameLag = Mathf.RoundToInt(Row("ghost lag", s.ghostFrameLag, 1, 4, s.ghostFrameLag + " frm"));
                s.ghostAlphaFalloff = Row("ghost fade", s.ghostAlphaFalloff, 0f, 1f, s.ghostAlphaFalloff.ToString("0.00"));
            }
        }

        // ── waveform trim editor ─────────────────────────────────────────────
        // Peak-rendered waveform with the trim window highlighted; drag the nearer
        // handle to move an in/out point, release to audition the trimmed window.
        void DrawWaveform(SfxSlot slot)
        {
            if (_waveClip != slot.clip)
                BuildWaveTexture(slot.clip);

            Rect r = GUILayoutUtility.GetRect(WaveW, WaveH, GUILayout.ExpandWidth(true));
            if (_waveTex == null)
            {
                GUI.Label(r, "waveform unavailable (clip Load Type must be Decompress On Load)");
                return;
            }

            GUI.DrawTexture(r, _waveTex, ScaleMode.StretchToFill);

            float inX = r.x + slot.trimStart * r.width;
            float outX = r.x + slot.trimEnd * r.width;

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            if (inX > r.x) GUI.DrawTexture(new Rect(r.x, r.y, inX - r.x, r.height), Texture2D.whiteTexture);
            if (outX < r.xMax) GUI.DrawTexture(new Rect(outX, r.y, r.xMax - outX, r.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.53f, 0.95f);
            GUI.DrawTexture(new Rect(inX - 1f, r.y, 2f, r.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.37f, 0.31f, 0.95f);
            GUI.DrawTexture(new Rect(outX - 1f, r.y, 2f, r.height), Texture2D.whiteTexture);
            GUI.color = prev;

            HandleWaveDrag(slot, r, inX, outX);
        }

        void HandleWaveDrag(SfxSlot slot, Rect r, float inX, float outX)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when r.Contains(e.mousePosition):
                    _dragHandle = Mathf.Abs(e.mousePosition.x - inX) <= Mathf.Abs(e.mousePosition.x - outX) ? 1 : 2;
                    _dragged = false;
                    e.Use();
                    break;

                case EventType.MouseDrag when _dragHandle != 0:
                    float t = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width);
                    if (_dragHandle == 1) slot.trimStart = Mathf.Min(t, slot.trimEnd);
                    else slot.trimEnd = Mathf.Max(t, slot.trimStart);
                    _dragged = true;
                    MarkDirty();
                    e.Use();
                    break;

                case EventType.MouseUp when _dragHandle != 0:
                    if (_dragged)   // audition the freshly trimmed window
                        Audition(slot);
                    _dragHandle = 0;
                    _dragged = false;
                    e.Use();
                    break;
            }
        }

        void BuildWaveTexture(AudioClip clip)
        {
            _waveClip = clip;
            if (_waveTex != null) Destroy(_waveTex);
            _waveTex = null;
            if (clip == null) return;

            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return;   // streaming/compressed clips can't be read

            var bg = new Color32(28, 28, 40, 255);
            var wave = new Color32(136, 190, 160, 255);
            var pixels = new Color32[WaveW * WaveH];
            int samplesPerCol = Mathf.Max(1, data.Length / WaveW);

            for (int x = 0; x < WaveW; x++)
            {
                float peak = 0f;
                int start = x * samplesPerCol;
                int end = Mathf.Min(start + samplesPerCol, data.Length);
                for (int i = start; i < end; i++)
                {
                    float a = data[i] < 0f ? -data[i] : data[i];
                    if (a > peak) peak = a;
                }
                int half = Mathf.Clamp(Mathf.RoundToInt(peak * (WaveH * 0.5f)), 1, WaveH / 2);
                for (int y = 0; y < WaveH; y++)
                {
                    bool on = Mathf.Abs(y - WaveH / 2) <= half;
                    pixels[y * WaveW + x] = on ? wave : bg;
                }
            }

            _waveTex = new Texture2D(WaveW, WaveH, TextureFormat.RGBA32, false);
            _waveTex.SetPixels32(pixels);
            _waveTex.Apply();
        }

        static float Row(string label, float value, float min, float max, string readout)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(readout, GUILayout.Width(55));
            GUILayout.EndHorizontal();
            return value;
        }

        void CycleClip(SfxSlot slot, int dir)
        {
            var lib = tuning.clipLibrary;
            if (lib == null || lib.Length == 0) return;
            // index -1 = "no sound"; cycling wraps through it
            int i = System.Array.IndexOf(lib, slot.clip) + dir;
            if (i < -1) i = lib.Length - 1;
            if (i >= lib.Length) i = -1;
            slot.clip = i < 0 ? null : lib[i];
        }

        static GUIStyle CenteredLabel()
        {
            var s = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            return s;
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            // play-mode edits to a ScriptableObject survive exiting play, but dirty-marking
            // makes sure they hit disk on the next project save
            UnityEditor.EditorUtility.SetDirty(tuning);
#endif
        }
    }
}
