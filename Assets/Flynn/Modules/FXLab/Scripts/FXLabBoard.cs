using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// What the lab can fire. Moments first — they're the game-facing deliverables; primitives
    /// after — the raw ingredients.
    ///
    /// Values are PINNED. kindPreviews serialises this enum as ints, so renumbering would move
    /// every preview slot onto the wrong effect; 9 is a hole where ArcSweep was culled
    /// (2026-07-27). The number-key bindings come from the panel's fire-list order, not from
    /// these values, so the two can drift apart safely.
    /// </summary>
    public enum FXKind
    {
        // composed moments (the game vocabulary). Material hits left the fire-list 2026-07-27
        // - they are recipes now (SwingTool + ContactWood/Stone); the typed HitMomentSettings
        // path survives only for FXRouter/FXTag consumers.
        Repair = 0, ItemDrop = 1, ItemIdle = 2, ItemPickup = 3,
        // primitives (ingredients)
        Flash = 4, Squash = 5, Puff = 6, SheetSlash = 7, SheetBurst = 8,
        ArcWipe = 10, HitStop = 11, Nudge = 12, Lunge = 13,
        OverlayFade = 14, PuffSpriteSheet = 15, Drifter = 16, TintState = 17, Glint = 18,
        Emote = 19, Droplets = 20, ProgressGlow = 21, FloatAura = 22, Converge = 23,
        ArcRibbon = 24
    }

    /// <summary>
    /// The lab conductor. The FXTunePanel's scrollable fire-list (and keys 1-9, 0) route
    /// here; clicking a prop changes focus. Composed moments choreograph the primitives:
    /// material hits share the swing (tool) and differ at contact (material); Repair is
    /// deliberately impact-free; Drop/Pickup are the item life-cycle pair.
    /// </summary>
    public class FXLabBoard : MonoBehaviour
    {
        public FXLabTuning tuning;
        public Camera cam;
        public Transform player;
        public ArcWipeFX arcWipe;
        public ArcRibbonFX arcRibbon;
        public SheetAnimFX sheetSlash;
        public SheetAnimFX sheetBurst;
        public PuffBurstFX puffer;
        public RingFX ringer;
        public SparkleFX sparkler;
        public ItemDropFX dropFX;
        public ItemIdleFX idleFX;
        public ItemPickupFX pickupFX;
        public SquashFX playerSquash;
        public LabPlayerAnim playerAnim;
        public SwingLungeFX playerLunge;
        public CamNudgeFX camNudge;
        public HitStopFX hitStop;
        public FXTarget[] targets;
        public FXTarget focus;
        public Transform focusMarker;

        // the packaged conductor other scenes reuse; the lab consumes the same component
        FXMomentPlayer _moments;

        void Awake()
        {
            _moments = GetComponent<FXMomentPlayer>();
            if (_moments == null) _moments = gameObject.AddComponent<FXMomentPlayer>();
            _moments.tuning = tuning;
            _moments.sheetSlash = sheetSlash;
            _moments.sheetBurst = sheetBurst;
            _moments.arcWipe = arcWipe;
            _moments.arcRibbon = arcRibbon;
            _moments.puffer = puffer;
            _moments.sparkler = sparkler;
            _moments.ringer = ringer;
            _moments.camNudge = camNudge;
            _moments.hitStop = hitStop;

            // block-path swings route through FXServices - hand it the lab's own arc spawners
            // and the character, so a SwingArcBlock aims the dialed slash, not a fresh one
            var svc = FXServices.Get();
            if (svc != null)
            {
                if (sheetSlash != null) svc.sheetSlash = sheetSlash;
                if (arcWipe != null) svc.arcWipe = arcWipe;
                if (arcRibbon != null) svc.arcRibbon = arcRibbon;
                if (playerLunge != null) svc.lunge = playerLunge;
                if (player != null) svc.player = player;
                // the lunge block raises this; the lab answers with the character's swing clip
                svc.onSwing = _ =>
                {
                    if (playerAnim != null)
                        playerAnim.PlaySwing(tuning.swingLunge.swingAnimDelay, tuning.swingFacing);
                };
            }
        }

        void Start()
        {
            if (focus == null && targets != null && targets.Length > 0)
                focus = targets[0];
            SnapMarker();
        }

        void OnDisable()
        {
            Time.timeScale = 1f;   // never leave the editor slowed
        }

        void Update()
        {
            // master speed: hold the clock at globalSpeed except while a hitstop owns it
            if (tuning != null && (hitStop == null || !hitStop.IsStopping))
                Time.timeScale = Mathf.Clamp(tuning.globalSpeed, 0.1f, 2f);

            // keep the idle pose matched to the selected facing (panel edits it live)
            if (tuning != null && playerAnim != null)
                playerAnim.SetFacing(tuning.swingFacing);

            if (Input.GetMouseButtonDown(0))
                HandleClick();
            // number keys are handled by FXTunePanel: they follow the FIRE-LIST order, which is
            // not the enum's order once an entry is culled
        }

        void HandleClick()
        {
            if (cam == null) return;
            Vector2 p = cam.ScreenToWorldPoint(Input.mousePosition);
            var hit = Physics2D.OverlapPoint(p);
            if (hit == null) return;

            var target = hit.GetComponent<FXTarget>();
            if (target != null)
                SetFocus(target);
        }

        void SetFocus(FXTarget t)
        {
            focus = t;
            RefreshPreview();   // the stand-in art follows the prop you are looking at
            if (t.Squash != null) t.Squash.Play();   // ack the selection
        }

        // ── preview art: dial an effect on the sprite it will really play on ──
        PreviewSlot _preview;
        FXTarget _previewedProp;

        /// <summary>Dress the focus prop in this effect's stand-in art. A slot with no
        /// sprite (or null) strips the props back to their own art.</summary>
        public void ApplyPreview(PreviewSlot slot)
        {
            _preview = slot;
            RefreshPreview();
        }

        public void ClearPreview() => ApplyPreview(null);

        void RefreshPreview()
        {
            bool wanted = _preview != null && _preview.sprite != null;

            // undress the prop that is no longer the one wearing it
            if (_previewedProp != null && (_previewedProp != focus || !wanted))
            {
                var worn = _previewedProp.GetComponent<FXPropPreview>();
                if (worn != null) worn.Clear();
                _previewedProp = null;
            }

            if (wanted && focus != null)
            {
                NeedOnFocus<FXPropPreview>().Apply(_preview);
                _previewedProp = focus;
            }
            SnapMarker();   // silhouette changed - the focus marker sits under the new bounds
        }

        void SnapMarker()
        {
            if (focusMarker == null || focus == null) return;
            var b = focus.GetComponent<SpriteRenderer>().bounds;
            focusMarker.position = new Vector3(b.center.x, b.min.y - 0.08f, 0f);
        }

        // active facing block; Swing45 aims at the focus target, front/back use their own aim
        SwingFacingSettings Facing => tuning.ActiveFacing();

        // behind-player facings sort the slash just under the player sprite
        int SlashOrder()
        {
            var f = Facing;
            if (!f.slashBehindPlayer || player == null) return 50;
            var psr = player.GetComponent<SpriteRenderer>();
            return (psr != null ? psr.sortingOrder : 0) - 1;
        }

        // Both swings now ride the character on ONE 0-360 facing angle (tuning.swingAngleDeg,
        // driven by the panel dial). The old three-direction picker still drives the lab
        // character's idle/swing CLIP - that is animation, not effect placement.
        void PlaySheetSlash() =>
            sheetSlash.PlayAtAngle(tuning.swingAngleDeg, player, tuning.sheetSlash, tuning.swingRig);

        void PlayArcWipe() =>
            arcWipe.PlayAtAngle(tuning.swingAngleDeg, player, tuning.swingRig);

        void PlayArcRibbon() =>
            arcRibbon.PlayAtAngle(tuning.swingAngleDeg, player, tuning.swingRig);

        /// <summary>The swing angle as a direction, for the things that still take a vector
        /// (the character lunge).</summary>
        Vector2 SwingDir()
        {
            float rad = tuning.swingAngleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        public void Fire(FXKind kind)
        {
            if (focus == null) return;
            Vector2 dir = player != null
                ? ((Vector2)(focus.transform.position - player.position)).normalized
                : Vector2.right;
            // swing-flavored kinds follow the swing angle dial instead of the focus
            if (kind == FXKind.SheetSlash || kind == FXKind.ArcWipe
                || kind == FXKind.ArcRibbon || kind == FXKind.Lunge)
                dir = SwingDir();

            switch (kind)
            {
                // moments
                case FXKind.Repair: PlayRepair(); break;
                case FXKind.ItemDrop: dropFX.Play(focus.transform.position, dir); break;
                case FXKind.ItemIdle:
                    idleFX.Toggle(focus.transform.position + new Vector3(0.35f, -0.2f, 0f)
                        + (Vector3)(Random.insideUnitCircle * 0.15f));
                    break;
                case FXKind.ItemPickup: pickupFX.Play(focus.transform.position, player, playerSquash); break;

                // primitives
                case FXKind.Flash: focus.Flash.Play(); break;
                case FXKind.Squash: focus.Squash.Play(); break;
                case FXKind.Puff: puffer.PlayAt(focus.transform.position, tuning.puff); break;
                case FXKind.SheetSlash:
                    PlaySwingAnim(dir);
                    PlaySheetSlash();
                    break;
                case FXKind.SheetBurst: sheetBurst.PlayAt(focus.transform.position, Vector2.zero); break;
                case FXKind.ArcWipe: PlaySwingAnim(dir); PlayArcWipe(); break;
                case FXKind.ArcRibbon: PlaySwingAnim(dir); PlayArcRibbon(); break;
                case FXKind.HitStop: hitStop.Play(); break;
                case FXKind.Nudge: camNudge.Play(dir); break;
                case FXKind.Lunge: if (playerLunge != null) playerLunge.Play(dir); break;
                case FXKind.OverlayFade: NeedOnFocus<OverlayFadeFX>().Toggle(); break;
                case FXKind.PuffSpriteSheet:
                    puffer.PlayAt(focus.transform.position, tuning.puffSheet);
                    break;
                case FXKind.Drifter:
                    NeedOnSelf<AmbientDrifterFX>().ToggleAt(focus.transform.position, DrifterBlock());
                    break;
                case FXKind.TintState: NeedOnFocus<TintStateFX>().Toggle(); break;
                case FXKind.Glint: NeedOnFocus<GlintFX>().ToggleLoop(); break;
                case FXKind.Emote:
                {
                    var b = focus.GetComponent<SpriteRenderer>().bounds;
                    NeedOnSelf<EmoteBubbleFX>().PlayAt(new Vector3(b.center.x, b.max.y, 0f), tuning.emoteIcon);
                    break;
                }
                case FXKind.FloatAura:
                {
                    // rises off the top of the prop, like the thing is giving something off
                    var b = focus.GetComponent<SpriteRenderer>().bounds;
                    NeedOnSelf<FloatAuraFX>().PlayAt(
                        new Vector3(b.center.x, b.center.y, 0f), tuning.floatAura);
                    break;
                }
                case FXKind.Converge:
                    NeedOnSelf<ConvergeFX>().PlayAt(focus.transform.position, tuning.converge);
                    break;
                case FXKind.Droplets:
                    NeedOnSelf<DropletSprayFX>().PlayAt(
                        focus.transform.position + Vector3.up * 0.15f, dir, tuning.droplets);
                    break;
                case FXKind.ProgressGlow:
                {
                    // step 0 -> .25 -> .5 -> .75 -> 1 -> clear, so the ramp is feelable
                    var pg = NeedOnFocus<ProgressGlowFX>();
                    float next = pg.Progress >= 1f ? 0f : Mathf.Min(1f, pg.Progress + 0.25f);
                    pg.SetProgress(next, tuning.progressGlow);
                    break;
                }
            }
        }

        // stateful FX self-provision on whatever they run on - zero scene rewiring
        T NeedOnFocus<T>() where T : Component
        {
            var c = focus.GetComponent<T>();
            if (c == null) c = focus.gameObject.AddComponent<T>();
            var f = c.GetType().GetField("tuning");
            if (f != null && f.GetValue(c) == null) f.SetValue(c, tuning);
            return c;
        }

        T NeedOnSelf<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c == null) c = gameObject.AddComponent<T>();
            var f = c.GetType().GetField("tuning");
            if (f != null && f.GetValue(c) == null) f.SetValue(c, tuning);
            return c;
        }

        // active ambience block for the Drifter entry (panel edits tuning.drifterVariant)
        AmbientDrifterSettings DrifterBlock() =>
            tuning.drifterVariant == DrifterVariant.Fireflies ? tuning.driftFireflies :
            tuning.drifterVariant == DrifterVariant.Pollen ? tuning.driftPollen :
            tuning.drifterVariant == DrifterVariant.Steam ? tuning.driftSteam : tuning.driftLeaves;

        void PlaySwingAnim(Vector2 dir)
        {
            if (playerAnim != null) playerAnim.PlaySwing(tuning.swingLunge.swingAnimDelay, tuning.swingFacing);
            if (playerLunge != null) playerLunge.Play(dir);
        }

        // moments delegate to the packaged FXMomentPlayer - the lab fires the exact
        // component another scene would slot in
        void PlayRepair()
        {
            _moments.PlayRepair(tuning.repair, focus.transform);
        }

        /// <summary>Fire a block recipe at the focus - the panel's recipe list routes here.
        /// swingOpens recipes follow the selected facing like typed hits.</summary>
        public void FireRecipe(FXRecipe recipe)
        {
            if (recipe == null || focus == null) return;
            Vector2 dir = player != null
                ? ((Vector2)(focus.transform.position - player.position)).normalized
                : Vector2.right;
            if (!recipe.swingOpens)
            {
                _moments.PlayRecipeMoment(recipe, focus.transform, dir);
                return;
            }
            // legacy swingOpens recipes still route their arc through FXMomentPlayer; the block
            // path (SwingArcBlock) uses the angle rig directly
            dir = SwingDir();
            PlaySwingAnim(dir);
            var f = Facing;
            _moments.PlayRecipeMoment(recipe, focus.transform, dir,
                f.slashAngleDeg, f.slashFlipY, f.slashOffset, SlashOrder());
        }

        /// <summary>Fire ONE block at the focus, delay ignored - the panel's solo
        /// audition for tweaking an ingredient without the rest of the stack.</summary>
        public void FireBlock(FXBlock block)
        {
            if (block == null || focus == null) return;
            Vector2 dir = player != null
                ? ((Vector2)(focus.transform.position - player.position)).normalized
                : Vector2.right;
            block.Play(new FXContext
            {
                target = focus.transform,
                pos = focus.transform.position,
                dir = dir,
                host = _moments,
                services = FXServices.Get(),
                tuning = tuning
            });
        }
    }
}
