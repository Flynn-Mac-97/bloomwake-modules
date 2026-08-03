# Pouch — items/slots/counts (MVP domain module)

Thin adapter over Runtime `Flynn.Player.PlayerInventory` (4-slot singleton, stays in
`Assets/Flynn/Scripts/Player/Inventory/`). Zero module→module refs.

| File | Role |
|---|---|
| `Pouch.cs` | seam: `Add/Consume/CountOf/Has` in, `onChanged` UnityEvent out |
| `PouchLabHarness.cs` | **lab only** keyboard add/consume + OnGUI readout |
| `Pouch_Lab.unity` | mockup scene, Codely-wired |

## Seams (glue in composed scene only)

- **in:** `Pouch.Add(item, n)` (Harvest drops via scene glue), `Pouch.Consume(item, n)` (Stations feed)
- **out:** `onChanged` UnityEvent (HUD refresh)
- **needs in scene:** GameObject with `PlayerInventory` + `PlayerInventory_Default.asset` config

## Deliberate cuts

- Currency (Echo Shards) not exposed — cut from MVP scope.
- Auto-collect pickup stays Runtime-side (`DroppedItemMagnet` calls `TryAddItem` directly;
  known gap: `ItemPickedUp` bus event exists but is never published — Pouch listens to
  `PlayerInventory.OnSlotChanged` instead, which does fire on every change).

Cull: delete this folder. Runtime inventory untouched.
