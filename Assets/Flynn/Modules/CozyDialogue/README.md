# Module: Cozy Dialogue

Bottom-panel dialogue presenter, AC/Stardew-shaped: typewriter reveal with **punctuation pauses**
(text breathes like speech), soft per-character **speech blips** with pitch wobble (character
"voice" without VO), skip-then-advance input with a press-dip ack, bobbing continue arrow.
Panel slides up OutBack, exits InQuad; Esc cancels with a quicker, humbler exit that never
resembles completion (Contract §13).

## Contents
- `Scripts/DialogueStyle.cs` — SO, ALL tuning: panel layout numbers, type sizes, chars/sec,
  sentence/comma pause multipliers, blip clip + every-N + pitch jitter + volume (≤0.4,
  Informational band), appear/exit durations, press dip, arrow bob.
- `Scripts/DialogueBox.cs` — the presenter. **Builds its own UI at runtime** from the style SO —
  no scene anchor wiring (Codely's biggest error surface, deliberately removed). Colors/sorting
  from VibeTokens in code (ui-surface panel, raised name plate, accent text; canvas order 300 =
  dialogue layer in the §13 overlay stack). API: `Show(DialogueLine[])`, `Cancel()`,
  `onOpened/onClosed`.
- `Scripts/DialogueTester.cs` — own-scene rig: T starts a scripted conversation.
- Namespace `Flynn.Feel` = graduation candidate (FeedbackSO pattern); tester stays behind.

## Knowledge Grove expansion (Scripts/Knowledge/)

Foundations for the knowledge/conversation UX, one demo scene (`Scenes/Knowledge_Grove.unity`):
world-space NPC panel (portrait/name/typewriter line) with **choice buttons** (+ leave) and
**clickable `<link>` topics** → `DefinitionCard`; **knowledge limits** (choices dimmed with a
gentle reason until topic/trust unmet → met); `Inspectable` self-talk via EmoteSpeaker
(UnityEvent bridge); `KnowledgeBase` = single Discover() write path → instant
"Entry updated" toasts (`NotificationFeed`); `CodexPanel` (C) with **visible unknowns** (dimmed
??? rows + hints) and **tasks as open questions** (clue + n/m progress, auto-resolve);
`TransmitterDecoder` unscrambles corrupted artifact topics on the text itself. Demo content
ships as SO field-initializer defaults (`TopicLibrary`, `ConversationSO`, `TaskSO`) — creating
the assets = full demo, no inspector list wiring. Session-only state; persistence is a later slice.

## Field Archive UI (UI Toolkit — Track A) — IN PROGRESS

Visual source of truth: **`handoff-grove/`** (STYLE-NOTES.md + Grove mockup html + screenshots)
— the 2026-07-13 "Mosslight Clearing" parchment redesign. It **supersedes** the older
`Solarpunk RPG dialogue prototype/` dark theme. Design system: `UI/theme.uss`. **Skeletons live in UXML** (`UI/TopBar.uxml`, `UI/Dialogue.uxml`,
`UI/Alert.uxml`) — double-click to open in UI Builder; they ship in the "open" state for
preview and C# resets classes at runtime. Dynamic rows (choice pills, alert instances) are
C#-instantiated. `UI/FieldPanelSettings.asset` + `UI/FieldRuntimeTheme.tss` (1280×720,
match 0.5). Ref wiring job: `unity_jobs/wire_field_uxml.py`.

- **Stage 1 DONE:** `Scripts/FieldUI/FieldArchiveHud.cs` (top status bar + gauge + side alerts,
  `Push(string)`) and `Scripts/FieldUI/FieldDialogue.cs` (bottom box, name/leave badges, choice
  pills + locked hints + copper key bullet, portrait rise, typewriter cadence, 0.5s choice
  auto-reveal). Data layer unchanged (ConversationSO/KnowledgeBase). Old UGUI `NotificationFeed`
  + `WorldDialoguePanel` disabled in Knowledge_Grove; `CodexPanel` (C key) is the interim codex.
- **Stage A DONE (2026-07-13):** Ambient bark (design handover §1, Oxenfree/Firewatch):
  `Scripts/Bark/BarkSO.cs` + `AmbientBark.cs` + `UI/Bark.uxml` — non-modal world-anchored
  bubble over the NPC, 2 quick replies (reaction + trust nudge, auto-close) + Chat handoff to
  FieldDialogue. Proximity-triggered (no Player object in Knowledge_Grove yet → **B key forces
  it**). Asset: `Configs/Bark_Rowan.asset`. Wiring: `unity_jobs/wire_ambient_bark.py`.
- **Stage B DONE (2026-07-13):** Field Archive drawer — `Scripts/FieldUI/FieldArchive.cs` +
  `UI/Drawer.uxml`: right 388px panel (world visible, scrim click closes), filter chips,
  unified Recent & Current feed (recency-ordered, threads bump on new clues), diamond tokens
  (portrait for People, shape glyphs for World/Artifact/Thread — default font lacks ◎◆),
  locked unknown rows, entry detail push-view per category, per-entry unread dots + button
  dot, alert VIEW → opens drawer. Toggle: **Y** / bottom button / Esc. TopicLibrary gained
  `category` + `rowan` Person topic (start node discovers it). CodexPanel + KnowledgeTester
  disabled (Y freed). Wiring: `unity_jobs/wire_field_archive.py`.
- **Stage C DONE (2026-07-13):** highlighted topics — FieldDialogue parses `<link="id">`
  spans; after the typewriter lands, the line rebuilds as a word-flow with clickable copper
  mono terms (+ diamond glyph; 2022.3 UITK has no link-tag events). Term click =
  Discover/touch + cream term card (tab badge, FIELD RECORD, dark FIELD NOTE inset;
  corrupted topics scrambled until decoded).
- **Stage D DONE (2026-07-13):** `TransmitterDecoder` rewritten to a themed UITK modal
  (`UI/Decode.uxml`: status light, 10-seg integrity meter amber→green, scan-sweep readout,
  3 stage rows, Begin/Cancel — one action, not a minigame) → `KnowledgeBase.MarkDecoded`
  (new `IsDecoded` state: archive shows DECODED RECORD, AD alert tile). `SelfTalkBubble`
  reuses the BARK system (mounts `UI/Bark.uxml`, no chips): same dark bubble, spring-up pop,
  growing-box typewriter, slightly higher blip pitch — the bot muttering over the inspected
  object. `Inspectable` prefers it over the legacy emote path. Wiring:
  `unity_jobs/wire_stage_cd.py` + `unity_jobs/selftalk_use_bark.py`.
- **Stage E1 DONE (2026-07-13):** hover action cue — `Scripts/FieldUI/HoverCue.cs` (+
  `HoverCueTarget.cs`, `UI/HoverCue.uxml`): small circle rides the cursor over interactables
  showing the verb — shape-built cream chat bubble on green (NPC), Kenney zoom on amber
  (inspect), signal on copper (decode). Spring-pop in/out, picking-ignored, hides during
  dialogue. Wiring: `unity_jobs/wire_hover_cue.py` (Rowan/lantern/shard/transmitter).
- **Stage E2 DONE (2026-07-13):** hover ↔ journal — known targets show a cream corner badge
  (green mini-diamond = "recorded") on the cursor cue; **right-click** quick-jumps via
  `FieldArchive.OpenTo(id)` (drawer opens, entry expanded + scrolled into view). Target
  `topicId` wired per host (rowan / discoverTopicId / decoder topicId):
  `unity_jobs/wire_hover_journal.py`. Feature-complete — tweaks only from here.
- **Grove re-theme (2026-07-13, evening):** full restyle to the `handoff-grove/` parchment
  design — warm parchment panels (2px #9C8552 border + `.bevel` inner-line element), dark
  chrome-green HUD pills, new fonts (Alegreya body / IM Fell English SC display /
  Silkscreen pixel chrome / AlegreyaItalic true italics — OFL, in `Art/Placeholder_UI/Fonts/`).
  ~All in `UI/theme.uss` + small UXML additions (bevel, dialogue speech tail, decode head
  diamond); C# untouched except gauge colors in `FieldArchiveHud`. Class/element names
  unchanged — no rewiring. **Swap points documented in theme.uss header:** 9-slice panel
  sprites (per-class background-image + `-unity-slice-*`, retire `.bevel` with one rule) and
  the ICON MAP section at file end (all background-image icons in one place). The old
  push-view detail styles remain restyled for API compat; drawer still uses inline expand.
- **Dialogue sprite kit (2026-07-14):** user-imported `UI/Dialogue_UI.psd` (9-slice, 2x,
  sprite borders authored) now skins the dialogue box/tabs/tail/choices/bullets/say-row via
  `theme.uss` classes (consolidated out of UI Builder inline styles — runtime-built choices
  need class-level sprites). Rule: sprite elements keep `background-color` TRANSPARENT — any
  fill bleeds through the baked alpha shadow as a white haze (the "white shadow" bug).
  Hover/locked states now use `-unity-background-image-tint-color`, not fills. New `.proceed`
  continue-arrow class. Drawer/decode/bark still flat-styled awaiting their own sprites.
- **Dialogue pager + soft reveal (2026-07-14):** long NPC lines split Pokémon-style —
  `FieldDialogue.Paginate` packs words/link-spans (atomic) into pages by visible chars
  (`pageCharBudget`, default 130; sentence ends past 60% budget break early). Mid-pages arm
  the kit continue-arrow (`.proceed--in` + timer-toggled `--alt` bob); click/Space/E steps.
  Last page eases choices in: `.choices--open`/`.say-row--open` animate max-height+opacity
  (panel grows smoothly), then pills stagger via `.choice--in`. Demo: Rowan start line
  lengthened to ~3 pages in `Configs/RowanConversation.asset`.
- **Transcript scroll (2026-07-14):** text area is a FIXED 112px `ScrollView`
  (`dialogue-scroll`) — pages/nodes APPEND as paragraphs (word-flow swaps in place for
  term pages), older paragraphs dim (`.para--old` 0.5), auto-scroll chases the typewriter,
  player wheels back to reread. Transcript clears per conversation (`Begin`). Fixed height
  = choices/say-row never shift with text volume. Scrollbar clicks blocked from the
  panel's advance handler.
- **Cozy UI Tuner — CULLED same day (2026-07-16):** runtime SO-driven type tuner
  (UIStyleSO/UIStyleApplier/editor window) built then removed at user's call — editing
  `theme.uss` classes directly is the workflow; a runtime override layer on top of USS
  added indirection (and caused a style-flash bug) for no gain. If revived, see git
  history; the flash lesson: inline overrides on runtime-built elements need apply-at-build,
  a slow tick alone snaps visibly.
- **Dialogue one-page display (2026-07-16, evening):** user call — the window shows ONLY the
  segment currently typing; each proceed step / node **replaces** the text (`_scroll.Clear()`
  in `ShowPage`). Supersedes the 2026-07-14 transcript-append behavior; reread/history cut
  "for now" (scroll machinery + fades kept in place for easy revival). Also that evening:
  choices single-column full-width 15px bold, reading window auto-height (fits text, cap 260,
  settles to 112), box truly centered, name badge 24px bold + role subtext (speaker strings
  now "Rowan · Gardener"), say-input 16px with kit enter keycap `_11` + arrow `_9` (flex-basis
  0 fix — TextField shoved the button out of the clipped row), choice-click bubbling fix
  (pill click was skip-revealing the next node instantly).
- **Dialogue phases + trust chip (2026-07-16, from `updates/dialogue updates.docx`):**
  reading phase = text owns the whole box (`.dialogue-scroll` 236px tall, no choice space,
  wheel + scroller locked); once ALL pages are read the window morphs — scroll eases to 112px
  (`.dialogue-scroll--settled` height transition) while choices/say-row rise into the freed
  space (one motion, never a snap); back-scroll through the transcript unlocks only then.
  New node/unheard line re-enters reading-tall. Trust now an icon token on the name badge
  (`trust-chip`: Kenney star + count, Kenyatta-plate ref — no bar, no "trust" label), polled
  from `KnowledgeBase.trust`, ease-out-back pulse on rise. `pageCharBudget` should be ~300
  in-scene for the taller window. Docx asks NOT done here: world tile-action diamond
  (deferred by user — separate edit set).
- **Field-guide sprite kit (2026-07-16):** user-imported `UI/field_guide_UI.png` (2x, 14
  sprites, borders authored) skins the Field Archive drawer via `theme.uss` — page panel
  (`_2`, retires the drawer's `.bevel` with one rule), green header bar (`_0`) with cream
  title/count, kit × button (`_5`, Label text transparent = hit area only), filter chips
  cream/green (`_3`/`_4`), entry rows cream card (`_13`), undiscovered rows dark olive card
  (`_12`, text lightened), diamond token mount (`_10`; amber/locked via tint), circle glyph
  (`_9`), amber unread dots (`_6`), divider rule (`_11`), scrollbar track+dragger (`_1`/`_8`).
  Same rules as the dialogue kit: transparent `background-color` under sprites (white-shadow
  bug), state changes via `-unity-background-image-tint-color`, `-unity-slice-scale: 0.5px`.
  Unused sprite: `_7` (small arrow button) — reserved. Alert tiles/detail push-view/bark/decode
  still flat, awaiting their own sprites.
- **Field-guide concept match (2026-07-16, evening):** restyle pass against
  `screenshots/final_field_guilde.png` (the exact-match brief). Header = grip icon
  (`solarpunk_icons.png` #16 — the project icon set) + PixelifySans title/count in warm brown
  rgb(100,86,53), no bar; type scale up across drawer (names 22, hints/tags/status 11 pixel,
  section 13, dsection body 17); tokens 56px; frow__status = grey pill; dstep dots → diamonds;
  kit scrollbar arrows (`_7` + 180° flip); drawer 440px wide, bottom 48. **Pixel chrome face
  for the drawer = PixelifySans (`Assets/Flynn/Fonts/`), user pick — Silkscreen remains only
  on dialogue-box badges.** User's UI Builder inline styles consolidated into classes
  (inline-styles lesson again: runtime rows only see classes). Visual verify loop = bridge
  `manage_screenshot capture_game_view` + crop/zoom compare vs concept.
- **One icon set (2026-07-17):** ALL module icons now come from `Assets/Flynn/Textures/
  solarpunk_icons.png` (64 sprites, 8×8 row-major, white-on-transparent = tintable).
  Swapped: leave arrow (`_0`), send arrow (`_1`, cream on green), proceed (`_3` arrow-down),
  drawer × (`_7`, Label text transparent = hit area), trust handshake (`_15`, user pick),
  hovercue inspect (`_20` magnifier) + decode (`_31` radio tower), leaf-dot (`_32` real leaf).
  Already solarpunk: grip `_16` (book), choice lock `_22` (shield-?), glyph lock `_55` (key),
  glyph-circle `_63`. Kenney + kit-sheet icon refs in the module: zero. Indexed contact sheet
  for picking: `screenshots/icons_indexed.png` (regen via PIL script if the sheet grows).
- **Flat base reset (2026-07-17):** user call — the sprite-kit panel skins (dialogue_UI.png /
  field_guide_UI.png) weren't landing; all PANEL/pill/bar rules flattened back to the
  handoff-grove reference recipe (`handoff-grove/STYLE-NOTES.md`: parch-panel #ECE0C2 +
  2px border-strong #9C8552 + radius 16 + `.bevel` inner line, parch-card rows #F4ECD3,
  chrome-green pills #2A3720, sage nameplate, flat diamond bullets, thin flat scrollbars,
  hover = background-color again not tint). 44 rules rewritten in one pass; verified via
  atlas render. **Kit sprites now used ONLY as icons** (`.proceed` arrow, `.say-send__icon`)
  + the icon set (`solarpunk_icons.png`, Kenney star). The two kit sheets stay on disk for
  a future art pass — when real 9-slice art returns, re-skin class-by-class against the
  atlas, and mind the white-shadow rule (transparent fills under sprites).
- **Component atlas (2026-07-17):** `UI/ComponentGallery.uxml` (+ `UI/gallery.uss`, gallery-only
  layout — no game styling in it) = the design surface. Double-click → UI Builder shows EVERY
  component at once: full layers as 0.4x template-instance tiles (Dialogue/Drawer/TopBar/Decode),
  overlays 1:1 (Alert/Bark/HoverCue), and 1:1 swatches of the whole vocabulary (text roles, tags,
  chips, pills, badges, tokens, rows, scrollbar). All swatches use theme.uss classes ONLY —
  editing a class updates game + atlas together. **Workflow: design in the atlas, style in
  theme.uss classes, never per-scene/inline; build new UI by composing existing classes, add new
  components to the atlas first.** `.gal-static` neutralizes absolute-positioned components
  inside swatch cells; gallery.uss loads after theme.uss so gal-resets win.
  **Coverage is COMPLETE (2026-07-17):** every theme.uss class is represented in the atlas —
  directly as a swatch (incl. STATES + VARIANTS: alert tiles, detail push-view, corrupt text,
  decode stages/ok-states, hovercue variants, hint input, portrait token) or inside a layer
  instance. Only exemptions: `.alerts` (pure C# layout container) + transient state modifiers
  (--open/--dip/…). Audit one-liner: diff classes defined in theme.uss vs classes used across
  UI/*.uxml (python regex; see git for the snippet) — run it when adding classes.
- **Single font (2026-07-17):** one face module-wide — Alegreya-VF, declared ONLY on the 7 layer
  root classes (.dialogue-root/.top-bar/.archive-layer/.alert/.bark/.decode-scrim/.hovercue);
  every label inherits. Face swap = replace those 7 identical lines. All per-class
  font-definitions removed (38); Pixelify/IMFell/IBMPlex/Silkscreen now unused by the module.
  Gotcha bank: `-unity-font` (legacy) always LOSES to `-unity-font-definition` — set fonts via
  font-definition / UI Builder "Font Asset" slot on the CLASS; and duplicate same-specificity
  rules later in the sheet silently win (stale ICON MAP dupes bit us).
- **Design polish pass (2026-07-17, frontend-design skill):** three moves on the flat base.
  (1) **Letterpress borders** — panels/cards get per-side border colors (lit top/left
  `#B9A472`/`#AE9765`, shaded right/bottom `#8A7147`/`#82683C`; cards use the soft pair
  `#DACBA0`/`#B7A06B`) = depth without box-shadow/sprites. Tails match the shaded edge.
  (2) **Diamond glyph system completed (the signature):** every status dot is now a rotated
  square — alert/frow/fchip/archive-btn unread dots, decode light + dstage dots. No circles
  left in status roles. (3) **Micro-type floor:** all 8–9px labels raised to 10–12px with
  1–2px letter-spacing (alert cat/view, termcard labels, dstage state, detail status/back,
  decode title, choice hint). Also fixed dead kit-era hover states (`.choice:hover`,
  `.choice--locked`, `.say-send:hover`, `.say-input:focus` used background-image TINT with no
  image → now background-color); locked choices finally read locked (darker fill, hairline
  border). Off-palette strays killed: trust chip black → chrome-green, drawer × brown block →
  ghost icon button, fchip radius 13 → true pill 999 (STYLE-NOTES compliance). Hover lift
  (translate 0 -1px, idle-motion pick #10) on choice/frow/fchip/archive-btn/decode-btn/bark
  replies — pure USS. Verified via atlas headless renders (`screenshots/atlas_v2/`).
  Follow-up same day: scrollbar recipe made GLOBAL (`.unity-scroller--vertical`, no scope) —
  `detail__scroll` + gallery page were unstyled defaults (21px track + arrows + thin dragger);
  the two per-scroll duplicate blocks (dialogue-scroll / drawer__scroll) deleted, dragger
  hover darkens. Any new ScrollView now themes itself.
  Reading-window inset (sunken parch card on `.dialogue-scroll`) tried + REVERTED same day —
  user call: no inset on dialogue text, prose sits directly on the panel.
- **User inline-edit consolidation (2026-07-17):** user's UI Builder edits moved into theme.uss
  classes, inline `style=` forks stripped (real files + gallery swatch dupes): badge name/role →
  white text (name middle-center, role lower-center), gauge num height 11/nowrap, termcard →
  right 6 / bottom 110 (rides the box edge), drawer width 440→368, choice Label margin 2/2,
  hovercue chat bubble = icon `solarpunk_icons_8` (shape+tail retired, tail display:none);
  user's own USS hovercue tune kept (1px rings, bg alpha 0.88). Gallery keeps layout-only
  inline (gal-cell widths, sample portrait). **Choices crop fix:** `.choices--open` max-height
  116→164 (3 pills @ ~41px clipped at 116 — the "cap must fit or pills CLIP" lesson);
  `.dialogue-scroll` 104→112 to match the C# settled height (mid-line clip gone).

## Final polish stages — UI idle-motion pass (first picks DONE 2026-07-17)

**Shipped (picks 1+2, 6, 10):** panel breathe — `.dialogue-box--breathing` (slow 2.1s scale
transition) + `--breathe` (scale 1.004) flipped by a `schedule.Execute().Every(2100)` in
`FieldDialogue` (starts 650ms after open, stopped on Leave, and paused/resumed around Dip so
the fast press-dip never eases at breathe speed); speech-tail wiggle ±2° in phase
(`.dialogue-tail--breathing/--wiggle`, same scheduler); unread-dot pulse — ONE class flip on
the drawer layer root (`.archive-layer--pulse` descendant rule dims `frow__dot` +
`archive-btn__dot` + `fchip__dot` together, 1.6s ease — rebuilt rows need no bookkeeping,
scheduler in `FieldArchive.Start`); hover lift everywhere (translate -1px, pure USS, shipped
in the design-polish pass). Compile-verified 0 errors. Motion = play-mode only, not visible
in headless renders.

Static UI over a breathing world reads dead. Ambient-tier idle layer (VibeSpec bands: periods
≥3s, amplitudes invisible in screenshots, × MotionScale). UITK 2022.3 has no USS keyframes —
loops = scheduler toggling classes (proceed-bob / alert-dot pattern). Remaining candidates:

1. Dialogue panel breathe (scale 1.000↔1.004, ~4s) — biggest de-deadener.
2. Speech-tail wiggle ±2° synced to breath — sells "voice from NPC".
3. Portrait sway 1–2px, out of phase with panel.
4. Hot-choice bullet pulse (amber diamond) — eye-draw without shouting.
5. Term shimmer (copper words, slow underline-opacity wave) — invites clicks (Contract §2).
6. Unread dot pulse (drawer rows, like alert dot) — unread feels un-read.
7. Drawer-open leaf flourish (2–3 solarpunk-icon leaves drift-fall, one-shot).
8. Dust motes over drawer parchment (3–4 1px specks, slow drift) — biggest cozy payoff.
9. Gauge glint on progress tick — rewarded, silent otherwise.
10. Hover lift everywhere (scale 1.02 + tint, pure USS :hover) — UI answers the cursor.

First-pass picks: 1+2, 6, 10. Event moments (typewriter, arrow bob, pill stagger, trust pulse)
already exist — this is only the idle layer.

- Skipped from the design handover (deliberate): SaveSystem persistence, LLM turn pipeline
  (keyword match stands in; legacy DialogueManager hooks in later), manager/service
  architecture (module-local components + SOs instead), DialogueContextResolver machinery
  (bark trigger = proximity + cooldown only in this mockup).
- ⚠️ Codely failure mode: it skipped creating/assigning the runtime theme — a PanelSettings with
  NULL themeStyleSheet renders broken "everything wrong" UI. Fixed via bridge job
  (`unity_jobs/fix_field_panelsettings.py`). Check `themeStyleSheet` first when Field UI looks wrong.

## Boundaries
- Presenter only. Does NOT touch the legacy `DialogueManager` (NPC LLM system) — that system can
  feed `Show(lines)` later via UnityEvent/adapter, same cross-module pattern as EmoteBubble.
- Movement is not locked while talking (A Short Hike style — cozy, zero player-controller coupling).

**Cull:** delete `Assets/Flynn/Modules/CozyDialogue/`. Nothing else references it.
Codely prompts delivered inline in chat — not saved to disk.
