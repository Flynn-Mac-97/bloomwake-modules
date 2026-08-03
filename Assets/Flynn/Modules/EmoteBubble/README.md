# Module: Emote Bubble

World-attached expression channel — a cute bubble above any actor's head: short line, glyph, or
vocalization ("eek!!", "<3", "zZz"), with quiet per-tone sound. This is the standard **Actor/Target
feedback channel** (FEEDBACK_CONTRACT §channels) other mechanics use for state, not-yet reasons,
and character voice.

## Contents
- `Scripts/EmoteProfile.cs` — SO, all tuning: timings (VibeSpec §12 bands), tone styles
  (token colors + sfx), stock vocalizations, spam interval. Tier: **Informational** (vol ≤0.4).
- `Scripts/EmoteBubble.cs` — the bubble: OutBack pop-in, hold, InQuad shrink-out; replace never
  stack; colors + sorting (6000/6001, above the Y-sort band) set in code from VibeTokens.
- `Scripts/EmoteSpeaker.cs` — put on any actor. UnityEvent-friendly API: `Say(string)`,
  `SayHappy(string)`, `Eek()`, `Heart()`, `Sleep()`, `Curious()`.
- `Flynn.Modules.Emote.asmdef` — refs Flynn.Feel + PrimeTween.Runtime + Unity.TextMeshPro.
- Namespace is `Flynn.Feel` = graduation candidate (FeedbackSO pattern).

## Own scene first, composition second
- **Standalone proof:** `Scenes/Emote_Test.unity` — this module's own scene: dummy speakers +
  `EmoteTester` (Tab switches speaker, 1–6 fire tones, hold H hammers the spam rule). Builds,
  tests and tunes with zero other modules present.
- **Composition usage:** `CritterBefriend/Scenes/Critter_Meadow.unity` wires Critter events to
  emotes via **inspector UnityEvents**, never asmdef refs — `onStartled → Eek`,
  `onTrustUp → Heart`, `onBonded → SayHappy("friend!")`. Deleting either module leaves only an
  inert missing-listener entry.
- `EmoteTester` is module scaffolding — stays behind if EmoteBubble graduates.

## VibeSpec/Contract conformance
§14: attached to speaker, ≤5 words, one bubble at a time. §12: appear OutBack / exit InQuad.
§15: system text sentence case; "eek!!" allowed as character vocalization. Contract §16:
replace-don't-stack + minInterval. Glyphs are text for prototype ("<3" not ♥ — default TMP font
coverage); final art rebinds to icon sprites.

**Cull:** delete `Assets/Flynn/Modules/EmoteBubble/` (+ .meta). UnityEvent slots elsewhere go
inert, nothing breaks.
