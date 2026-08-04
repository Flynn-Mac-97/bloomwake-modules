# Module: Player HUD

**Status: LOCKED 2026-07-16** — feel-passed by user, no additional edits wanted. Revisit later
for: real-world composition (world pickup prefab → `PickUp`, wrench swing → `DrainBattery`),
final art rebind, possible hybrid flight (player absorb → secondary hop to slot).

Player-facing game GUI: **sun-battery meter** (top-left) + **pinned tool bar** (Q/W/E) +
**main action bar** (1–6), with the full UI-feedback vocabulary tuned in one lab scene before
any gameplay wiring. DialogueBox pattern: the HUD **builds its own UI at runtime** from
`HUDStyle` — no scene anchor wiring, colors/sorting from VibeTokens in code (canvas order 200 =
HUD layer, under dialogue at 300). Placeholder look = token-tinted rounded squares + initial
letters; final art assigns `HUDItemDef.icon` / sprites and the look rebinds, roles stay.

**Design (MDA / pure-cozy):** target feeling = *calm competence* — the HUD is a gentle
companion, never a nag. Battery is sun-energy: empty means "tools rest", not danger. Low state
goes **sleepy** (droop + slow breathe), never an alarm. Refusals are a soft no-wiggle. The
pinned wrench = muscle-memory comfort: one hotkey, forever.

## Contents
- `Scripts/HUDStyle.cs` — SO, ALL tuning: layout, battery behaviour, motion, audio slots.
- `Scripts/HUDItemDef.cs` — SO per item: token role tint, optional final art, `isTool` +
  `pinnedToolSlot`, `maxStack`.
- `Scripts/HUDInventory.cs` — pure model, the rules: tools pin to their slot; **any move
  touching the tool bar refuses** (hotkeys never shuffle); items stack/merge/swap.
- `Scripts/PlayerHUD.cs` — coordinator: builds canvas/bars/meter, drag orchestration, battery
  state + edge events. Composition surface = public methods + UnityEvents
  (`onItemDropped(def,count)`, `onToolSelected(def)`, `onBatteryEmpty/Full`, `onPocketsFull`).
- `Scripts/HUDSlotView.cs` — slot view + micro-feel (hover lift, land punch, refuse wiggle,
  count pop); forwards drag gestures.
- `Scripts/BatteryMeterView.cs` — fill + lagging white chip-ghost (spend stays readable),
  cool→warm fill color, charging breath + sun spin, sleepy low state, full-charge bounce+flash.
- `Scripts/HUDFly.cs` — arced ghost-icon flights (pickup in, drop out, slot-to-slot).
- **Flight-anchor experiment** (kept as toggle): `PlayerHUD.anchorFlightsToPlayer` +
  `playerAnchor` — off (default) = ghosts fly to/from the HUD slot; on = pickups absorb into
  the player sprite in the world (slot still punches = HUD ack) and drops spew from it.
  V toggles it in the lab; arrow keys move the Player stand-in (W is a tool hotkey, so arrows).
- `Scripts/HUDTester.cs` — HUD_Lab rig: every moment on a key (see class docs). Stays behind
  on graduation.
- `Flynn.Modules.HUD.asmdef` — refs `Flynn.Feel`, `PrimeTween.Runtime`, `Unity.TextMeshPro`,
  `UnityEngine.UI`.
- `Configs/`, `Scenes/` — created by Codely wiring.

## Feedback moments (Contract tiers)

| Moment | Treatment | Tier |
|---|---|---|
| Pickup | ghost flies mouse→slot, land squash-pop, count pop if stacked, pluck sfx | Action |
| Drop (key or drag-out) | ghost flies out + shrinks, slot clears, thump sfx | Action |
| Drag start | ghost lifts (1.12×, 6° tilt), origin hollows, grab blip | Action |
| Move / swap / merge | ghosts fly both ways, land punches, place sfx | Action |
| Refuse (tool move, pockets full) | soft no-wiggle + muted blip — never harsh | Informational |
| Select slot | accent ring OutBack pop, tick blip; tool select fires `onToolSelected` | Informational |
| Battery drain | fill eases down, white chip lags behind (spend readable) | Informational |
| Battery gain | fill eases up warm, chip snaps, gain tick | Informational |
| Charging | sun spins + warm breath on icon and fill | Informational |
| Battery full | overshoot bounce + flash + chime (always plays) | Success |
| Battery low | sleepy droop + slow breathe + dim — no alarm (pure cozy) | Ambient |
| Battery empty | `onBatteryEmpty` — compose as "tools rest", not failure | Informational |

Spam guards (Contract §16): punches always from stored base scale; one sfx per
`sfxMinInterval`; H-key spam test in the lab.

## Parchment pass (2026-07-29)

The bar used to be dark-purple `ui-surface` under a cream UI Toolkit dialogue box — two identities
on one screen. It now wears the **parchment set** (VibeSpec §11b), lifted verbatim from
`CozyDialogue/UI/theme.uss`. Matched by *token*, not by tech: the HUD stays uGUI so the feel-passed
PrimeTween juice survives, and the shared palette keeps it from drifting from the dialogue.

- Each slot is a parchment fill plus a **border-only sliced Image** for the tan rule — uGUI has no
  border-color, so the rule is its own pass (`ruleWeight` → `pixelsPerUnitMultiplier`).
- Numerals and hotkeys are **Alegreya**, same face as the dialogue.
- Selection ring moved from amber `ui-accent` to sprout `ui-sprout`.
- **The tool loop is the signature.** Deepest paper, heaviest rule, and it is the one slot that
  keeps a full-strength border while empty (`emptySlotAlpha` skips it) — the opening sequence starts
  with no wrench, and that absence has to read as a question rather than a hole.

**Icons are now real.** `ItemDefinition.icon` was a dangling GUID on every item (four of them
pointed at the *same* missing sprite). Each now carries the sprite from its own `*_Drop.prefab`, so
the inventory icon is literally the log you saw on the ground. `PouchHudMap` is demoted from gate to
override: every item reaches the bar carrying its own name and icon, and a row only supplies
dedicated inventory art or a placeholder tint. Identity always comes from the ItemDefinition, so a
row can no longer make one item wear another's label — it used to show Wood as "Scrap".

## Composition usage
Other modules call `PickUp(def, screenPos)` / `DropFromSlot(...)` / `DrainBattery(...)` directly
or wire the UnityEvents in the inspector — no asmdef ref back, no bus. E.g. a world pickup
calls `hud.PickUp(berryDef, screenPos)` on trigger; `onItemDropped` spawns the world item.

**Cull:** delete `Assets/Flynn/Modules/PlayerHUD/` (+ .meta). Inspector event slots elsewhere
go inert, nothing breaks.
