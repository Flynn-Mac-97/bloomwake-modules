# ResourceNodes — the island's harvestable layer

Grows the `layers.resources` block of the map-painter JSON into real, strikeable nodes, and
grows them back after they're gathered.

**Cull:** delete `Assets/Flynn/Modules/ResourceNodes/` and its `.meta`. Nothing stable
references it. The only outside touch is one persistent listener on the level loader's
`onRegenerated` event, which goes dead-but-harmless.

---

## Design

**Target feeling:** the quiet satisfaction of tending a place. Care, not conquest.

**Verb:** gather — one swing, reusing the existing `IHittable` → `ResourceNode` chain.

**The pillar that shapes everything: renewal.** Every kind carries `regrowSeconds`. A node that
is gone forever turns a cozy island into something to be strip-mined, and quietly teaches the
player to hoard and to feel bad about gathering. Because everything returns, the question stops
being *"have I ruined it?"* and becomes *"what do I feel like tending today?"* Different tempos
per kind (Overgrowth 25s → Tree 90s) give the island a rhythm the player learns and returns to.

**Solarpunk framing is in the verbs, not the theme:** Tree = *Prune*, Overgrowth/Tree stub =
*Clear*, Stone/Rock/Bush = *Gather*. Nothing is destroyed; the island is tended.

**No failure state.** No tool gating, no timer, no quota, no XP. `requiredTool`,
`dropScatterRadius` and `_onWrongTool` exist on `ResourceNodeConfig` but are dead code project-wide
— do not design around them.

### Feedback Contract — micro scorecard (single-tap mechanic)

| criterion | score | how |
|---|---|---|
| Discoverable | 2 | props are the island's visual texture; you walk into them |
| Legible | 2 | six distinct silhouettes, VibeSpec size grammar |
| Targetable | 2 | trigger collider + TileHoverCursor lock marker |
| Predictable | 2 | same sprite ⇒ same kind ⇒ same yield, every time |
| Responsive | 2 | squash + flash on the frame of contact |
| Traceable | 2 | drop arcs from the node to the player |
| Progressive | 1 | health drops but is not shown — *stage sprites are the gap* |
| Confirmed | 2 | node clears, item banks |
| Explained | 2 | hover outline + prompt verb |
| Recoverable | 2 | nothing to recover from; no fail state |
| Persistent | 2 | rebuilt deterministically from the map every load |
| Repeatable | 2 | regrowth is the whole point |
| Accessible | 2 | no timing or precision demand |
| Hierarchical | 2 | Action-band hit, bigger moment on depletion |
| Tunable | 2 | every value is on a `ResourceKindSO` / `ResourceNodeConfig` |
| Cullable | 2 | own asmdef, own folder |

**31/32, no zeros — passes the gate.** The one soft spot is *Progressive*: a node gives no
mid-harvest read of how close it is to done. Fix by filling `healthStages` on the kinds that
deserve it (see the sprite/stage trade-off below).

---

## Pieces

| file | role |
|---|---|
| `ResourceKindSO` | one asset per JSON resource type — which prefab, placement jitter, regrow. Routing only |
| `ResourceCatalogSO` | JSON type id → kind. One reference on the spawner |
| `ResourceNodeSpawner` | reads the loader's layer items, places prefab instances, owns regrowth |
| `HarvestableNode` | the `IHittable` adapter, and the "I was gathered" signal |
| `Configs/` | 10 kinds + 10 `ResourceNodeConfig` + the catalog |
| `Maps/` | `resource-gallery.json` — hand-authored lab map, one of every kind in a row |
| `Prefabs/` | one **prefab variant of `Assets/Flynn/Prefabs/Resources/Tree.prefab`** per kind |

### The prefabs are variants of Tree.prefab — on purpose

The project already has a node rig and it does real work. Rolling a thinner one loses all of it:

```
root [ResourceNode, Animation, SortableSprite]
  └ ResourceCollider  [Rigidbody2D (kinematic), CircleCollider2D]   ← the SOLID footprint
      └ VisualRoot    [SpriteRenderer, Rigidbody2D (dynamic), RelativeJoint2D,
                       SpriteFlash, PolygonCollider2D (trigger), Interactable, Hoverable]
  └ Shadow            [SpriteRenderer]                              ← added by this module
```

`ResourceNode._visualRigidbody` points at VisualRoot's body — that joint pair **is** the wobble.
Without it `AddVisualTorque` is a no-op and struck nodes just sit there. Being variants also means
later fixes to `Tree.prefab` flow into all six kinds.

### The prefab is the design surface — the spawner edits nothing

Every per-kind decision is **authored by hand in Prefab Mode**: sprite, scale, the body collider's
radius / offset / `isTrigger`, the VisualRoot polygon (re-fit it after changing the sprite), the
joint's rest pose, sorting order, and `ResourceNode._config`.

The spawner only ever sets **position** (map placement + `positionJitter`), the object's name, and
the regrow timer. It instantiates through `PrefabUtility.InstantiatePrefab` in the editor, so the
placed nodes stay **live prefab instances** — fix the prefab and the island updates with it.

Earlier this class solved scale from `targetHeight`, swapped the sprite, rebuilt the polygon and
moved the body for ground-sink at spawn time. That is why hit boxes were tree-shaped on a rock and
why nothing could be fixed in the prefab: the values you saw were not the values that ran. Do not
put per-kind authoring back into the spawner.

Kinds that want visual variety get their own prefab (and their own kind asset), not a random
sprite pick at spawn.

**Shadows** come from the scene's shadow system, not from the node. Prefabs carry no `Shadow` child.

### Things that will bite

- **`HarvestableNode` lives on `VisualRoot`, not the node root.** `SwingHarvestHitter` reports the
  struck object as `(hittable as MonoBehaviour).transform`, and the FXLab contact path resolves
  `FlashFX`/`SquashFX` with a plain `GetComponent` on exactly that object. IHittable on a root with
  no SpriteRenderer is why target-side FX silently do nothing elsewhere in the project.
- **`ResourceNode._config` is private with no runtime setter.** That is why there is a prefab per
  kind rather than one prefab configured at spawn — a node regrowing mid-play would otherwise come
  back with the base prefab's stats.
- **Damage normally arrives via `GameEventBus`,** and GameCore has no bus. `HarvestableNode`
  publishes when a bus exists and otherwise reproduces the wobble + flash locally, so the strike is
  always felt either way.
- **The map painter exports duplicate ids** — every Overgrowth on the starting map ships as
  `resource_NaN`. Placement hashing keys on world position, never on `item.id`.
- **New art must import with a BOTTOM-CENTRE pivot.** Every `env_*` sprite is one, and the rig's
  wobble joint rest pose assumes it. The `rock_new` set arrived centre-pivot: nodes looked correct
  in the editor and then sank by half their height on the first physics step, and *only* those 17
  did. Compensating in code loses the fight — the `RelativeJoint2D` owns the sprite's pose relative
  to the body, so it just drags it back. Fix it in the importer
  (`TextureImporterSettings.spriteAlignment = BottomCenter`) and the whole class of problem is gone.
- Sizing: `sizeMode` is `Height` for kinds where height is the read (trees), `LongestSide` for
  scatter sets of mixed aspect (the rocks run 1.16-2.17, so height-sizing made the flat ones twice
  as wide as their round siblings and they stopped reading as one family).
- Nodes are parented to a scene-root `ResourceNodes_Generated`, never under the loader prefab.
  Objects parented into a prefab instance serialise as prefab additions and go stale.

## Wiring

1. `ResourceNodeSpawner` on a scene-root object; assign `loader`, `catalog`, `nodePrefab`.
2. Level loader `onRegenerated` → `ResourceNodeSpawner.Rebuild` (already hooked in GameCore).
3. Context menus: *Rebuild Resource Nodes* / *Clear Resource Nodes*.

## Next

- `healthStages` on Tree and Stone to close the *Progressive* gap.
- A `FeedbackSO` per kind for the gather moment (currently code-driven squash + flash).
- NPCs and largeSprites reuse `JsonMapLoader.BuildLayerItems()` — the seam is kind-agnostic.
