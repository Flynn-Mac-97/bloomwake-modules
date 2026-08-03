# PlayerRig — the player as a module packet (MVP)

The player stack itself (movement, 112-clip 16-dir anim, swing chain, interaction) is the
proven Runtime prefab `Assets/Flynn/Prefabs/Player.prefab` — this module contains the
seams + glue helpers, not a rebuild.

| File | Role |
|---|---|
| `PlayerRelay.cs` | bus `ToolSwingStarted` → `onSwing` UnityEvent (FX/DemoFlow glue) |
| `SimpleCamFollow.cs` | minimal XY lerp follow for the composed scene (no Cinemachine dep) |
| `PlayerRig_Lab.unity` | mockup scene, bridge-built |

## Seams

- **in:** normal input (WASD + LMB swing + E interact via Runtime `Interactable` +
  `PlayerAnchor.asset` — stations/NPCs reference the anchor SO, never the transform)
- **out:** `onSwing`; hits reach Harvest via the Runtime bus chain (`ResourceHit`);
  pickups reach Pouch via `PlayerInventory` directly
- Future: `IHittable` sweep for non-ResourceNode hittables (Contracts) — not needed while
  all hittables wrap ResourceNode.

Battery CUT per MVP scope — RobotBattery may exist on the prefab; leave unwired, no drain
consequences in composed scene.

Cull: delete folder. Player prefab untouched.
