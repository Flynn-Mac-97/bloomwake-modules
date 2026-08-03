# FXLab — visual-effects workbench (cullable module)

One scene to build, compare, and tune every feel primitive (swing arcs, flash, squash,
hitstop, camera nudge, debris, ring, sparkles) **decoupled from any gameplay scene**.
Proven effects graduate to `Flynn.Feel`; gameplay modules then just add the asmdef ref.
Cull a failed experiment by deleting its component file — cull the whole lab by deleting
this folder.

## Build / rebuild the scene

Scene wiring is done **via Codely prompt** (workflow doctrine — Claude writes the prompt,
user runs it in Codely). No editor builder script. If the scene needs regenerating or
extending, ask Claude for a fresh wiring prompt.

All tuning lives in `Configs/FXLabTuning.asset` — play-mode slider edits **persist**
(it's a ScriptableObject): press Play, click, tune, done. The scene is just scaffolding;
the tuning asset is the work product.

## Using the lab

- **Left panel = scrollable fire-list** (keys `1`–`9`, `0` = first ten): MOMENTS (the
  game vocabulary) then PRIMITIVES (raw ingredients). Clicking fires at the focused prop
  and selects the effect in the tuner. Click a **prop** to change focus.
- **Right panel = tuner**: per effect — cycle its sound from `clipLibrary`, trim the
  played window on a **waveform view** (drag gold in / red out handles; release
  auto-auditions), volume, pitch jitter, plus per-effect shape controls (swing variant,
  contact delay, burst row, hop/fly params, per-facing slash re-aim...). Writes directly
  into `FXLabTuning.asset` = permanent. Waveform needs clip Load Type = Decompress On
  Load (Unity default).
- **B** toggles the **VFX browser** (scans `VFX/` packs, auto-detects strips/grids/
  sequences, previews animated, assigns onto tuning blocks). **Tab** toggles the panels.

### Moment catalog (game-facing, modular — apply to character/world later)

| Moment | Recipe |
|---|---|
| Hit Wood / Metal / Stone | shared swing (tool) → per-material contact: sfx set, flash tone, squash stiffness, chip colors, pack burst. Hitstop last = impact-frame freeze. |
| Repair | the anti-hit: soft glow + happy bounce + rising motes + pulse ring. **No hitstop, no shake** — care, not impact. |
| Item Drop | parabola hop away from player → landing dust + shrinking settle bounces → hands off to Item Idle. |
| Item Idle | ground-loot bob + inverse-breathing shadow + white glint winks (optional white glow). |
| Item Pickup | anticipation hop → magnet arc chasing receiver → absorb sparkle + gentle bottom-anchored receiver swell. |

**FX master-list scaffold (2026-07-17):** the full game FX vocabulary is now based out.
New primitives (all self-provision in the lab, fire-list bottom):
- `AmbientDrifterFX` — the ambience floor: leaves / fireflies / pollen / steam as 4
  tuning variants (`driftLeaves`…) of ONE component. Fire = toggle loop at focus.
- `TintStateFX` — stateful color tint: wet soil (autoRevert = drying), wilt desat,
  scan highlight, low-power dim.
- `GlintFX` — periodic wink loop: solar shimmer, tool-ready, hover ack.
- `EmoteBubbleFX` — critter/NPC icon pop (! ? heart note zZz), icons in `tuning.emote`.
- `DropletSprayFX` — watering arc (stretched droplets + gravity).
- `ProgressGlowFX` — progress-on-object glow, pulses faster near done (the Feedback
  Contract "progress" answer). Fire = step +25%.
- `puff2` — second dialable puff block (debris vs footstep dust side by side).

Registry recipes seeded (dial in the panel, wire later): Repair, HarvestPop, SeedPlant,
SproutPop, StageUp, Bloom, WaterSplash, BefriendAck, Startle, JumpLand, PowerOn,
ScanPulse, StashDeposit, CraftDone (+ HitWood). Deferred until mechanic lands: BeamFX
(transmitter), weather rain, day-night light shift.

### PuffSpriteSheet (2026-07-27)

`Puff 2` is now **`PuffSpriteSheet`** (`tuning.puffSheet`, `FormerlySerializedAs("puff2")` so
dialed values survive): same debris physics — launch, gravity, spin, shrink, fade — but every
mote plays its own little dust animation instead of being a static dot. `PuffSettings.anim` is
a standard `SheetFXSettings`, so the **VFX browser [B] assigns onto it** via the new
`Puff sheet` target, and `animOverLife` fits one playthrough to the mote's life (off = the
sheet's own fps, holding the last frame). Empty sheet = the old sprite/dot behaviour, so the
first `Puff` is untouched.

Mote scale still comes from the puff's own `size`, not `anim.worldSize`; the anim's `sfx`
slot is unused (the puff plays its own, once per burst, not once per mote).

**A recipe's `Puff` block owns its own `PuffSettings`, so it has its own mote anim** — dialing
the `PuffSpriteSheet` primitive does not reach into it. The block's controls show the MOTE
ANIMATION section plus a `copy sheet from PuffSpriteSheet` button, and the VFX browser gained
a **`Selected block`** target that assigns onto whichever Puff/Burst block is open in the
recipe tuner (auto-finds the tune panel; warns when nothing suitable is selected).

Frame slicing moved to **`FXSheetFrames.Resolve(SheetFXSettings)`** — shared by `SheetAnimFX`
and the puff so a browser-assigned block is interpreted identically by both (two slicers
drifting apart would show different frames for the same assignment). Cached per settings
block, rebuilt on change; `SheetAnimFX` lost its private copy.

### Swing = one 0-360 angle, rigged on the character (2026-07-27)

The three-direction picker is gone from the swing EFFECTS. Both surviving arcs — **Sheet
Slash** and **Arc Wipe** — take a single `swingAngleDeg` (0 = +X, counter-clockwise):

- **Parented to the character, centred on its ORIGIN.** A pivot object rides the player at
  local zero and is rotated on Z; the art hangs off that pivot at `forwardOffset`/`pivotOffset`.
  So the angle *orbits* the art around the character instead of spinning it in place — which
  is exactly why the old setup needed a different offset and flip per direction. The effect
  also tracks the character while it plays, since it is parented rather than positioned once.
- **`SwingRigSettings`** (`tuning.swingRig`) holds the shared rigging: `pivotOffset`,
  `angleOffsetDeg` (art correction if the neutral pose isn't drawn along +X), `behindPlayer`
  (resolved per play against the character's live sorting order), `sortingOrder`.
- **Angle priority:** the block's own `overrideAngle` → `FXContext.swingAngleDeg` (the seam a
  real character will drive) → the panel dial. The panel shows the dial plus
  right/up/left/down shortcuts on both swing entries.
- **The body lunge reads the same angle**, or the character would lunge one way while the arc
  swept another. The old `SwingFacing` enum still drives the lab character's idle/swing CLIP —
  that's animation, not effect placement, so it was left alone.

**`ArcSweepFX` culled.** `ArcVariant` is now `{ Wipe = 1, Sheet = 2 }` — values pinned so
recipes already holding Wipe/Sheet keep meaning what they meant, and an asset still holding
the old `Sweep` (0) falls through to Sheet. `FXKind` values are likewise pinned with a hole at
9, because `kindPreviews` serialises that enum as ints. Number-key bindings moved from the
board to the panel: they follow the fire-list order, which drifts from the enum whenever an
entry is culled.

### Duplicating a recipe no longer aliases it (2026-07-27)

Duplicating a recipe in the inspector copies the `[SerializeReference]` **reference** — Unity
serialises one managed instance and points both list entries at it, so editing either edited
both. Three parts to the fix:

- **`FXRecipe.DeepCopy()`** + `FXCopy.Deep/DeepBlock` (JSON round-trip — separate objects,
  arrays included; sprite/clip/SoundSO refs survive as instance ids). The tune panel's recipe
  editor gained a **`dup`** button that uses it, which is the supported way to start a recipe
  from an existing one.
- **`FXLabTuning.SplitSharedBlocks()`** runs in `OnEnable`: any block instance held by two
  recipes (or twice in one) is repaired — first holder keeps it, later ones get their own deep
  copy — and logs how many it split. Idempotent, so an already-clean asset is untouched.
- **`CopyFromPrimitive` now deep-copies too.** `Clone()` is `MemberwiseClone`, so a detached
  block still shared the primitive's sprite/colour **arrays** — "make custom" then editing the
  colours would have edited the primitive. The runtime tint path still uses the cheap shallow
  `Clone()` (it replaces the array wholesale and must not allocate per play).

### Two care primitives (2026-07-27)

Both are sprite-driven — art slots in, no logic changes. Fire-list entries + `Aura` /
`Converge` recipe blocks (primitive-linkable and material-tintable like the rest).

- **`FloatAuraFX`** — rising icon aura: plus signs / hearts / leaves drift up off a tended
  thing and fade. The healing-number vocabulary, minus the numbers and the snap. Two details
  carry it: icons **emit over a window** (`emitOver`) rather than bursting, which reads as
  ongoing care instead of an impact; and each sways on a random phase and direction while
  climbing (`swayAmp`/`swayHz`), so a repeat never looks like the same stamp twice. Climb is
  ease-OUT — a released thing, not a thrown one. Set `emitOver: 0` for a burst.
- **`ConvergeFX`** — the inverse of a puff: pieces start on a ring and are pulled INWARD,
  spiralling and shrinking as they arrive. Absorption language — cogs and nuts pulling into a
  machine being repaired. Three things make it satisfying rather than merely inward: the
  radius closes on an **ease-in curve** (`pull`), so pieces hang out there then snap home —
  being pulled, not falling; they sweep a few degrees around the centre on the way
  (`swirlDegrees`), reading as orbit-and-capture; and they arrive **staggered**, so it's a run
  of arrivals instead of one thud. Ring is jittered in both radius and angle so it never looks
  mechanical.

Both block versions spawn at the target's **sprite centre**, not its pivot — the effect
belongs on the thing, not at its feet.

⚠ The new `FXKind` entries are appended, never inserted: `kindPreviews` serialises the enum as
ints, so inserting would silently re-map existing preview slots.

### Sounds extracted to SoundSO assets (2026-07-27)

Every sliced sfx slot on `FXLabTuning.asset` was turned into a real `SoundSO` in
`Configs/Sounds/` and slotted back in — 17 slots, **15 assets** (identical slices share one:
`arcSweep`/`arcWipe`, and the `Puff` primitive with the Puff block that copied it).

Fractional `trimStart`/`trimEnd` became real seconds by reading each WAV header; a slot that
played the whole clip got `endTime: -1` ("to the end") rather than a hard-coded length. The
slot's dialed level moved ONTO the SoundSO and the slot's own `volume` was reset to 1, so
playback level is unchanged (`FXAudio` multiplies the two) while the asset now carries the
authored sound and the slot keeps a neutral per-layer mix. `clip`/`trim` stay on the slots as
dead fallback data — `UsesSound` makes the SoundSO win.

### Authoring-pass fixes (2026-07-27)

- **Squash keeps the base planted.** `SquashSettings.anchorBottom` (default on) measures the
  pivot-to-bottom gap and pushes the transform down as Y shortens. Scaling deforms about the
  PIVOT, so centre-pivoted art (most real sprites) used to sink into the ground mid-squash.
  Art already pivoted at its base measures 0 and is unaffected.
- **Art-scale slider works.** `Breather` rewrites `localScale` every frame from the size it
  captured at Awake, and `SquashFX` lerps from its own Awake-time base — so a preview resize
  was silently stomped on the next Update, and the first squash snapped the prop back to its
  original size. Both gained `Rebase()`, which `FXPropPreview` calls on apply and on clear.
- **Sound layering.** Every play already got its own throwaway `AudioSource`, so nothing was
  ever being cut off — but identical clips starting on the same frame sum into one louder copy
  of themselves rather than a blend. `SfxSlot.delay` staggers a layer deliberately, and
  `FXAudio` adds a sub-frame scatter (≤12 ms) automatically so simultaneous layers never sit
  in perfect phase.
- **`SoundSO` slots into any sfx slot.** `SfxSlot.sound` wins over the raw clip and brings its
  own level, pitch, jitter, region and mixer routing; the slot's `volume` still scales it and
  its `delay` still staggers it. The lab's clip picker + waveform trim stay for quick work —
  the panel shows the slotted asset's name instead, with `unslot` to go back. `SoundSO.Play`
  and `PlayAtPoint` gained an optional `delay` (default 0, so existing callers are unchanged);
  doing the stagger inside them preserves the region start and pushes the scheduled end out,
  which a `Stop()` + `PlayDelayed()` from outside would have destroyed.

### Layers reuse the dialed primitives (2026-07-27)

Adding a layer to a recipe should reuse what you already tuned, not hand you a blank slate.
Every settings-bearing block implements **`IPrimitiveLinked`**: when `usePrimitive` is on it
plays `tuning.flash` / `tuning.squash` / `tuning.puff` / … directly, so **re-dialing a
primitive retunes every recipe that links it** (same idea as `SwingTool`, one level down).

- The add-block menu carries a **`use my dialed primitive settings`** toggle, on by default.
  Untick it for a blank layer to customise from scratch — that's the fallback.
- While linked, the block's sliders are hidden (the primitive owns those values) and
  **`make custom (copy values here)`** deep-copies the primitive's current values into the
  block and detaches. `re-link` goes back.
- **Composition params stay editable either way** — `delay`, `offset`, `useMomentTint`,
  `impactFrame`, `aimAlongDir`, and the Emote block's *which emotion*. Those belong to the
  layer, not to the effect.
- `PuffBlock` picks its source (`Puff` or `PuffSpriteSheet`) since two puff primitives exist.
- Detach needs a real copy, never a reference — aliasing would make a recipe edit mutate the
  primitive — so every settings class now has `Clone()`.

⚠ `usePrimitive` defaults to **false** in C# on purpose: `[SerializeReference]` blocks already
in an asset run their field initializers on load, so a `true` default would silently relink
every dialed recipe and throw away its per-material values. New layers get `true` from the add
menu instead.

### Recipes compose recipes (2026-07-27) — the sub-section model

A moment is no longer a flat pile of primitives. `RecipeBlock` plays another recipe by name,
so the tool swing is authored **once** and every material hit just references it:

```
SwingTool     = [ Sfx(whoosh), Lunge, Arc(Sheet) ]        <- no contact; doesn't know what it hits
ContactWood   = [ Sfx, Flash, Squash, Puff(chips), Nudge, HitStop ]
Hit_Wood      = [ Recipe(SwingTool), Recipe(ContactWood) @0.14s ]
Hit_Stone     = [ Recipe(SwingTool), Recipe(ContactStone) @0.12s ]
```

Re-dial `SwingTool` and every hit in the game changes with it. **The contact delay is just
the nested block's own `delay`** — so `FXRecipe.swingOpens` / `arc` / `contactDelay` are only
needed by the old flat recipes; composed ones leave them off. Guarded at 3 levels of nesting
(a recipe reaching itself warns and stops), and the panel's picker won't offer the recipe
you're editing.

Three blocks make a swing authorable: **`Arc`** (`SwingArcBlock` — sweep/wipe/sheet with the
active facing's angle/flip/offset + behind-player sorting), **`Lunge`** (character body
language; also raises `FXServices.onSwing`, which the lab answers with the swing animation
clip), and **`Recipe`** itself. Arc and Lunge resolve their own aim from the facing, so they
point correctly however they were fired. `FXServices` gained `sheetSlash` / `arcSweep` /
`arcWipe` (created) and `lunge` / `player` (**found, never created** — a swing belongs to a
character); `FXLabBoard` hands it the lab's own dialed spawners at Awake.

### Scope pass (2026-07-27) — the three layers added

- **Preview art (`PreviewSlot` + `FXPropPreview`)** — every effect can carry a stand-in
  sprite: drop a real tree into HitWood's slot and the focus prop *becomes* that tree while
  the effect is selected, so chips/squash/flash are dialed against the real silhouette
  instead of a coloured square. Recipes hold their own `preview`; primitives use
  `tuning.kindPreviews` (panel's `+ preview art slot` creates the field). The prop captures
  its sprite/scale/collider on first swap and restores on strip — props stay disposable.
  Sprites are assigned **on the asset** (runtime IMGUI has no object picker); the panel
  gives scale + wear/strip + `auto-wear on select`.
- **Material tint (one recipe, every material)** — `FXRecipe.tintFromTarget` reads the
  target's own sprite colour into the firing; blocks opt in with `useMomentTint`
  (Flash/Ring/Sparkle take the hue and keep their dialed alpha, Puff builds a 3-shade chip
  ramp). Callers can override with `PlayRecipe(..., tint)`. Blocks always recolour a
  **clone** — the primitives read their settings every tween frame, so mutating the asset
  would recolour effects already in flight.
- **Emotions are named** — `EmoteIcon` (Affection / Alarm / Curious / Content / Sleep /
  Anger / Laugh) replaces index-picked icons: game code says the *feeling*, art is bound in
  `emote.iconSet`. No sprite = a dot in that emotion's colour, so all seven stay
  distinguishable while dialing. New `EmoteBlock` puts a bubble in any recipe (pops at the
  target's sprite top, not its pivot); `FXServices` provisions the spawner.
- **Registry parked** — PowerOn / ScanPulse / StashDeposit / CraftDone are commented out of
  `Builtin` (factories kept). No verb behind them yet; already-seeded assets are untouched.

## USING THE EFFECTS IN ANOTHER SCENE (the slot-in contract)

Everything here is designed to move: **components carry no scene assumptions; all dialed
values live on `FXLabTuning.asset`.** Copy nothing — reference the same asset and the
numbers you dialed in the lab come with it.

There are four kinds of piece, each with its own slot-in recipe:

### 1. Target-side ingredients — live ON the object that reacts
Add to any sprite object (a tree, a rock, a chest):
- **`FlashFX`** — needs the object's renderer material set to `Flynn/FXLab/SpriteFlash`
  (`Shaders/FXFlash.shader`). Keep `SpriteRenderer.color` white — it multiplies in.
- **`SquashFX`** — plain add. Pauses a co-located `Breather` automatically.
- **`OverlayFadeFX`** — occlusion fade (player running behind trees/roofs). Pure
  STATE: plain add + assign tuning, then whatever detects the overlap (trigger
  volume, sorting system) calls `SetFaded(true/false)` — no player math inside.
  Lab fire button toggles it on the focus prop; knobs in `tuning.overlayFade`.
Both take the tuning asset in their `tuning` field for standalone use, and both have
explicit-settings entry points (`Play(FlashSettings)`, `Play(SquashSettings)`,
`Pop(scale, duration)`) that composed moments call with per-material blocks.

### 2. Spawner ingredients — live anywhere, fire at any position
`PuffBurstFX`, `RingFX`, `SparkleFX`, `SheetAnimFX` (slash/burst frame anims),
`ItemDropFX`, `ItemIdleFX`, `ItemPickupFX`. One instance per scene is enough — they
spawn throwaway objects per play. Add to any GameObject (an "FX" child of a manager is
fine), assign `tuning`, call:

```csharp
puffer.PlayAt(worldPos, tuning.hitWood.puff);       // any block, any position
sheetSlash.PlayAt(pos, dir, tuning.sheetSlash);     // + optional angle/flip/offset/order
dropFX.Play(fromPos, dirAway);                       // or pass an explicit DropSettings
```

### 3. Globals — one each
- **`CamNudgeFX`** on the camera (reads `tuning.nudge`, or pass a block). Applies its
  kick as a self-removing offset each LateUpdate — safe on static AND follow cameras.
- **`HitStopFX`** anywhere (owns `Time.timeScale` briefly; restores to
  `tuning.globalSpeed` — in a game scene keep `globalSpeed = 1`).

### 4. `FXMomentPlayer` — the composed-moment conductor (THE slot-in component)
This is what a gameplay scene actually consumes. One component, assign: `tuning` +
the spawner refs (slash, burst, puffer, sparkler, ringer) + optional camNudge/hitStop.
Then from game code:

```csharp
moments.PlayHit(tuning.hitWood, targetTransform, dir);    // full: slash + delayed contact
moments.PlayContact(tuning.hitMetal, target, dir);        // contact only (game drives swing)
moments.PlayRepair(tuning.repair, target);
moments.PlayHitWood(target);                              // UnityEvent-friendly shorthands
```

- The target just needs `FlashFX`/`SquashFX` on it — the moment finds them with
  `GetComponent` and skips whatever's missing. Unassigned spawners are skipped too:
  partial wiring degrades gracefully, never throws.
- `onSwing` (UnityEvent&lt;Vector2&gt;) fires when a hit opens — hook the character rig
  (`SwingLungeFX.Play`, anim triggers) in the inspector, no code coupling.
- `PlayHit`'s optional args carry the per-facing slash re-aim (angle/flip/offset/sorting)
  — in the lab these come from `tuning.facing45/Front/Back`; a game caller passes its
  own facing's values.
- **The lab itself runs on this component** (`FXLabBoard` self-provisions one at Awake),
  so if it looks right in the lab it will look right in your scene.

### 5. `SlashFacingRouter` — UnityEvent-friendly slash glue
For character swings driven by another module: takes any aim `Vector2`, picks the facing
block (up = Back, straight down = Front, else 45 — same thresholds the critter clip pick
uses) and fires `SheetAnimFX` with that facing's dialed rotation/flip/offset +
behind-player sorting (resolved per fire, Y-sort safe). Hook a module's
`UnityEvent<Vector2>` swing event straight to `PlaySlash` in the inspector.

### 6. Block recipes + `VFXPresenter` — moments as pure data (2026-07-17)
The generalization of the composed-moment idea: every sub-effect is an `FXBlock`
(`Scripts/Recipes/`) with `enabled` + `delay` + settings; a moment is an `FXRecipe` — a
named `[SerializeReference]` block list on the tuning asset (`Recipes` section). New
moment = new list entry, **zero conductor code**; blocks stagger via per-block delay.

- Wrapper blocks reuse the existing settings/components 1:1: Sfx, Flash, Squash, Puff,
  Ring, Sparkle, Burst (generic pack anim), Nudge, HitStop (put last in hits).
- Self-executing blocks need no target component: `FadeOutBlock` (alpha fade +
  optional auto-restore for lab repeat).
- **`VFXPresenter`** = the target-side hub: add to any sprite, assign tuning, call
  `Play(recipe)` or hook `PlayByName("HitWood")` to a UnityEvent. World blocks route
  through **`FXServices`** (find-or-create, reuses existing CamNudgeFX/HitStopFX —
  zero scene wiring).
- Conductor path: `FXMomentPlayer.PlayRecipe(recipe, target, dir)` and
  `PlayRecipeMoment(recipe, ...)` — honors the recipe's own swing metadata
  (`swingOpens` / `arc` / `contactDelay` live ON the recipe: hit grammar = swing tool
  opens, blocks land after the delay; off = repair/item grammar, blocks fire at once).
- **Lab recipe editor (tune panel, 2026-07-17):** fire-list has a `- RECIPES -`
  section (dynamic — every recipe on the asset, `+ new recipe` to add). Selecting one
  opens the recipe tuner: inline rename, swing metadata, flat block list
  (enable / reorder / remove / `+ add block` type menu), and ONE selected block's
  controls at a time — including the **full waveform trim editor for every block's
  SfxSlot** — plus "Play block solo" (audition one ingredient without the stack) and
  "Fire recipe". No stacked inspector foldouts; colors/curves/sprites stay inspector.
- New block = one class in `Scripts/Recipes/FXBlocks.cs` + one `BlockTypes` entry +
  one `DrawBlockControls` case in `FXTunePanel` — recipes pick it up as data.
  Adding a whole new EFFECT component still follows the pattern below.
- **Recipe registry (`Scripts/Recipes/FXRecipeLibrary.cs`):** built-in combos shipped
  as code factories. Each name seeds ONCE per asset (`FXLabTuning.seededNames`):
  dialed values never overwritten, deletions stay deleted, new registry entries
  appear in the lab on next load. Claude's loop for a new effect set = (block class
  if new primitive) + factory method + one `Builtin` line — zero scene work.

### Worked example: Critter_Meadow (first consumer, 2026-07-16)
CritterBefriend exposes `CritterAnimator.onSwing` (UnityEvent&lt;Vector2&gt;); the scene holds
`Player/SlashFX` (SheetAnimFX + SlashFacingRouter) and CamNudgeFX on the camera, listeners
wired in the inspector. Old embedded swing FX (SlashArc, fx overlay, SwingWorldFeedback)
disabled. Total cost: one UnityEvent added to critter code, zero cross-module refs.
Details: CritterBefriend README §"Swing FX = FXLab slot-in".

### Adding NEW effects later (the pattern to copy)
1. Settings = a `[Serializable]` block class in `FXLabTuning.cs` + a field on the SO.
2. Component under `Scripts/Effects/` — one file, one class, self-builds its runtime
   children, **two entry points**: `Play()` reading its own tuning block (inspector
   convenience) and `Play(<Block>Settings)` explicit (composition). Never reach into
   another effect's block.
3. Fire button: add an `FXKind`, a case in `FXLabBoard.Fire`, controls in `FXTunePanel`.
4. If it joins a composed moment, add the block to that moment's settings class and the
   call to `FXMomentPlayer` — the board inherits it for free.

Different variants of the same effect (e.g. two slash styles) = duplicate the tuning
asset (`Ctrl+D` on `FXLabTuning.asset`), dial the copy, and hand different assets to
different consumers — components don't care which asset they read.

### Module-boundary note
Other **modules'** *code* must not reference `Flynn.Modules.FXLab` types (deps point
inward only). Composed **game scenes** (in `Assets/Flynn/Scenes/`) may freely hold FXLab
components + the tuning asset — scenes assemble modules. When an effect is needed by
another module's *code*, that's the graduation trigger (below).

## Architecture

- `FXLabTuning` (SO) — every knob, one asset, one inspector. Per-effect `SfxSlot`
  (clip + volume + **pitch jitter** + trim window) links audio into the same tuning
  surface.
- One small component per primitive under `Scripts/Effects/` — each self-builds its
  runtime children (no prefab wiring), reads only its tuning block, and is
  deletable in isolation.
- `FXMomentPlayer` — the packaged composed-moment conductor (see slot-in contract).
- `FXLabBoard` / `FXTunePanel` / `VFXBrowserPanel` / `LabPlayerAnim` — **lab scaffolding
  only**, never reused elsewhere.
- Flash is MaterialPropertyBlock-driven via `Shaders/FXFlash.shader` — works on real art
  later, not just tinted primitives. Renderer color stays white-tinted (URP gotcha:
  SpriteRenderer.color multiplies into the flash).
- Swing-arc variants compared per material hit: **Sweep** (ghost-trail crescent),
  **Wipe** (shader radial wipe), **Sheet** (pack frame-anim; per-facing re-aim via
  `SwingFacingSettings`). Cull losers after feel-test.
- asmdef refs: `Flynn.Feel`, `PrimeTween.Runtime` only. Deps point inward; nothing stable
  references this module.

## Known placeholder edges

- Placeholder sprites (crescent, soft dot, ring) are procedural (`FXSprites`) — final art
  swaps in per effect without touching logic.
- Button labels use legacy `TextMesh` (zero deps). If glyphs render magenta under URP,
  swap the label material for an unlit text shader — cosmetic only.
- Default sfx clips are arbitrary picks from the Dustyroom CC0 library
  (`Assets/Flynn/Audio/Placeholder/DustyRoom/`) — swap by feel in the tuning asset.
- `SquashFX` pauses a co-located `Breather` while punching (both write localScale).
- Spawner effects create throwaway GameObjects per play — pool at graduation if a path
  runs hot.
- `SquashFX.Pop` captures position at start (bottom-anchor compensation) — a receiver
  moving during the ~0.25s swell would snap back; fine in the lab, handle at graduation.

## Graduation path

Effect proves out → move its component (+ its settings block, reshaped into a
`Flynn.Feel` profile or `FeedbackSO` slot) into `Assets/Flynn/Feel/` — namespace move,
then delete the lab copy. `FXMomentPlayer` graduates the same way once the moment set
stabilizes. Bake `globalSpeed` into durations at graduation (game scenes shouldn't ride
`Time.timeScale`). CritterBefriend's embedded swing FX (`SlashArc`, `SwingBodyLanguage`,
`SwingWorldFeedback`) get replaced by the graduated versions.
