# PlatformTiles module

Draws raised platforms as isometric cube tiles, the way a traditional 2D isometric game does it.
The island base keeps its own island/skirt rendering and is untouched.

## Two sorting variants — pick ONE per actor

The module ships both ways of sorting an actor against raised ground. They are mutually exclusive:
the order-space one writes a large per-frame `sortingOrder`, which stops equal-order camera Y
sorting from ever running.

| Component | Use when | Writes |
|---|---|---|
| `PlatformLevelSort` | platforms are drawn as **PlatformTiles cubes**, all on one sorting layer (`PlatformSorting_Lab`) | `sortingOrder` only, from the depth+height formula below |
| `ActorSortLayer` | platform surfaces have **their own sorting layer per level** — the `JsonMapLoader` island-stack path used in `GameCore` | `sortingLayerName` only; ordering inside the layer is left to the camera's transparency sort axis |

`ActorSortLayer` assumes the interleaved layer list (Project Settings ▸ Tags and Layers), back to
front:

```
Level0   ground surfaces at elevation 0 (base island, mud, ground decals)
Actors0  player / props standing on the ground
Level1   platform surfaces at elevation 1
Actors1  player / props standing on that platform
Level2   Actors2   Level3   Actors3
```

An actor is then unconditionally in front of every surface at or below its own level and behind any
platform above it; only actors sharing a level compete, and those resolve by world Y
(`Assets/Settings/Renderer2DData.asset` → Transparency Sort Mode `Custom Axis`, axis `(0,1,0)`).
Ascending a level moves the actor up one **pair** of layers, which is what keeps them over the
platform they just climbed onto.

The known cost of layer-based sorting: it is absolute. A player standing on the GROUND directly in
front of a level-1 rim is still drawn behind it, because `Level1` outranks `Actors0` unconditionally.
Move the platform's skirt renderer down to the lower level's layer if that reads wrong in play.

## The sort — depth and height are separate

Mixing elevation into the depth term is what makes iso sorting go wrong. A tile's depth comes purely
from its **footprint** on the ground, never from how high it is drawn:

```
tile   order = baseOrder + round(-footprintY * depthSteps) * HeightSlots + elevation * 2
player order = baseOrder + round(-groundY    * depthSteps) * HeightSlots + elevation * 2 + 1
```

World-Y on an isometric grid is proportional to `(cellX + cellY)`, so the footprint Y **is** the iso
row. Rows nearer the camera sit lower on screen, get a larger order, and draw last. Elevation only
breaks ties *inside* one row, so a tall stack can never steal depth from its neighbours.

The player projects its feet back to the ground plane (`footY - level * riseWorld`) — otherwise
standing on a platform would read as standing further away. The trailing `+1` wins the tie against
the tile it stands on. Both behaviours then fall out with no special cases:

- **behind a platform** → further-back row → the platform draws over you.
- **running down its near side** → nearer row → you draw over it.
- **stood on top** → same row, +1 → you draw over the tile beneath you.

`baseOrder` / `depthSteps` / `HeightSlots` **must match** `PlatformLevelSort` on the player
(currently 0 / 20 / 16). They're plain fields deliberately duplicated on both sides rather than a
shared SO, so neither module needs an assembly reference to the other and both stay cullable.
`HeightSlots` must exceed `maxElevation * 2 + 1`.

## Tile size

One tile spans a **2×2 block of base cells**, matching how the maps are authored (they paint N base
cells per map tile), so platforms read as chunky blocks rather than a fine mosaic. On the current
map that's an exact fit: 548 footprint cells → 137 blocks, no partial blocks.

## Culling

Only visible surfaces are emitted. Each column draws its **top** tile, plus side tiles down to
whichever of its **two front neighbours** is lower (the front pair being one iso row nearer the
camera). Interior columns of a plateau therefore cost exactly one tile.

On the current map: **976 tiles → 178**.

## Collision

`BuildTiles` also emits a **`PlatformCollision`** Tilemap under the Grid: the footprint at the
**ground plane**, tiles carrying `ColliderType.Grid` — which on an isometric grid is the cell's
diamond (verified 1×0.5, ratio 2.00) — merged by `TilemapCollider2D` + `CompositeCollider2D` into
one outline. Static `Rigidbody2D`, renderer disabled, layer `GroundBorder`.

Ground plane, not the raised Y: the player walks on the ground and must be blocked by where the
platform *stands*, not where its top face is drawn. `CompositeCollider2D` defers its bake and
under-fills in edit mode, so the builder calls `GenerateGeometry()` explicitly.

**`PlatformBorderBuilder` (LevelLoad) is now the island coastline only.**

## What it reads

The `JsonLayer_*` sibling tilemaps under the Grid that the level painter produces, each raised by
`height * heightStep`. Coupled by **name convention only** — no code dependency on the loader.

`_heightStep` must equal the painter's `heightStep` (currently 0.25). If it doesn't, real layers
round down to level 0 and vanish; the builder logs an error rather than failing silently.

## Usage

1. Paint the level (`JsonMapLoader` ▸ Regenerate).
2. `PlatformTileBuilder` ▸ **Build Tiles**. Re-runnable — clears first.

Children are normal saved objects, not `HideFlags.DontSave`, so they survive play and domain reload
with no runtime rebuild.

## Art

`Blockout/IsoCube_Platform.png`, `Blockout/IsoDiamond_Platform.png` — PPU 100, pivot (0.5, 0)
bottom-centre. The builder auto-fits the sprite's width to the block width, so art of any resolution
seats correctly; `_tileOffset` nudges it if the cube body isn't exactly one elevation rise.
