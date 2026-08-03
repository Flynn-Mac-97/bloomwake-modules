# LevelLoad — ground generation (cullable module)

The island ground-generation stack, ported out of the stable tree (2026-07-17) so level
loading can grow around it as one slot-in module. **Generates the walkable map from a
painted Tilemap**: paint cells → the stack builds the visible island (top fill, cliff
skirt, fringe lip, grass, foliage scatter) at runtime, profile-driven.

**Status (2026-07-18):** port verified (compile clean, `2D_Lighting_Demo` intact, zero
missing scripts) + lab scene wired and feel-confirmed working. Load orchestration not
started.

## What's inside (namespace unchanged: `Flynn.Environment`)

| Piece | Role |
|---|---|
| `TilemapToSpriteShape` | traces painted tilemap cells into contour SpriteShapes — the geometric base |
| `IslandSkirt` | the island look: cliff skirt (shingle planes / tiling shader), top fill (4 fbm paint layers), fringe overhang lip, underside edge — all driven by the profile |
| `IslandVisualProfile` (SO) | ALL look knobs + material values. Runtime materials only — swap the asset = restyle live. `Configs/IslandProfile_Default` + `_Autumn` |
| `BillboardGrass` | one-mesh grass cards over painted cells, 3-color pick + wind sway (`Flynn/GrassBlades` shader, MPB-driven) |
| `IslandFoliage` | per-cell prop scatter over the painted surface (spawns into stable `FloraContactHandler` / `SortableSprite`) |
| `ShinglePlanePrototype` | standalone cliff-plane experiment scene component |

Shaders (`Shaders/`): IslandSkirt, IslandTopFill, IslandFringe, IslandSkirtSprite,
IslandUndersideEdge, ShinglePlane, GrassBlades + `IslandShading.hlsl` (island-only
include, lives here). Materials, profiles, and the `brushes1/2` paint-layer sheets are
in `Materials/` / `Configs/` / `Textures/`.

## Boundary notes

- Deps point INWARD only: module → `Flynn.Runtime` (`SortableSprite`,
  `FloraContactHandler`) + SpriteShape. Nothing stable references this module —
  verified at port time (only `ResourceNode` → `WindManager`, which **stayed** in
  `Scripts/World/`; grass reads its `_Wind*` shader globals loosely, no code ref).
- `GrassBlades.shader` includes the SHARED `Assets/Flynn/Shaders/Wind.hlsl` by absolute
  path (FlynnSprite/SpriteOutline also use it — it stays stable infra).
- `2D_Lighting_Demo` keeps working untouched: files moved WITH their .meta files, so
  every scene/asset GUID reference survived. Slot-back later = the scene already
  points at these components; a future composed scene just adds the same set.
- Water*, GrassDecal*, Parallax, PushableCrate stayed in `Scripts/Environment/` —
  not part of ground gen, not scene-active (checked).

## Lab scene — `Scenes/LevelLoad_Lab.unity` (wired 2026-07-18)

The module's confined tuning environment. One Grid/Tilemap (demo-matched: cellSize
`(1, 0.5, 0)`, tilemap scale `(0.5, 0.5, 1)`) + one "Island" GO carrying the whole
stack + `LabIslandPainter`.

**Tuning loop:** open the scene → right-click `LabIslandPainter` header → "Paint
Island" / "Clear Island" (ellipse blob, noise coastline — dial radii / wobble / seed,
repaint; the `[ExecuteAlways]` stack regenerates live, no play mode needed; auto-paints
on Play if the map is empty) → tweak `Configs/IslandProfile_Default.asset` (or a
duplicate) and watch skirt/fringe/fill respond.

Tune HERE, not in `2D_Lighting_Demo` — the demo consumes the same components + profile
asset, so dialed values carry over for free. Wiring reference assets (stable-tree, by
design): SpriteShape profile `Assets/Flynn/SpriteShape/Grass.asset`, ground tile
`Assets/Flynn/Tiles/Ground/020-floating-set-variations-result-7_2.asset`, grass cards
`Assets/Flynn/Sprites/World/Terrain/Grass/GrassBlades_Sheet.png` (5 slices), foliage
`Assets/Flynn/Prefabs/Resources/Flora.prefab`.

## Next (the actual level-load layer)

This port is step 1. The load module proper comes next: pick a tilemap/level asset →
build ground through this stack → place spawn points → hand off to gameplay modules.
