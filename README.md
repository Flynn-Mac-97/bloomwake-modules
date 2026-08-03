# Bloomwake — shared modules

A standalone Unity project containing a slice of the Bloomwake codebase: the NPC/LLM dialogue
stack, the cozy dialogue UI, harvestable resource nodes, and the player rig. Clone it, open it,
it compiles.

**Unity 2022.3.62f3** (URP 14.0.12, 2D). Open the folder directly in Unity Hub — everything is
committed, including `ProjectSettings` and `Packages/manifest.json`.

## What's here

| Assembly | Folder | What it is |
|---|---|---|
| `Flynn.Contracts` | `Assets/Flynn/Contracts` | Cross-module interfaces only. No behaviour. |
| `Flynn.Base` | `Assets/Flynn/Base` | Shared floor: event bus, sorting, world items, hover, item data. |
| `Flynn.Npc` | `Assets/Flynn/Scripts/NPC` | The LLM/NPC stack — dialogue brain, memory (LiteDB), prompts, island content. |
| `Flynn.Modules.Dialogue` | `Assets/Flynn/Modules/CozyDialogue` | The cozy dialogue UI: field dialogue box, Field Archive drawer, barks, knowledge base. |
| `Flynn.Modules.ResourceNodes` | `Assets/Flynn/Modules/ResourceNodes` | Harvestable, regrowing nodes spawned from a map JSON. |
| `Flynn.Modules.PlayerRig` | `Assets/Flynn/Modules/PlayerRig` | Player movement, sprite animation, swing, camera. |
| `Flynn.Modules.LevelLoad` | `Assets/Flynn/Modules/LevelLoad` | Island ground generation from painted map JSON. |
| `Flynn.Modules.FXLab` | `Assets/Flynn/Modules/FXLab` | Feel/VFX primitives. |
| `Flynn.Feel` | `Assets/Flynn/Feel` | `FeedbackSO` + design tokens. Uses PrimeTween. |
| `David.Runtime` | `Assets/David` | Iso map loading. A leaf dependency of LevelLoad. |

Dependencies point **inward only**: `Contracts <- Base <- Npc`, and the modules sit on top of
`Base`. Nothing references the parent game, which is why this slice can exist at all.

## Scenes to start from

- `Assets/Flynn/Modules/PlayerRig/PlayerRig_Lab.unity` — walk around, swing.
- `Assets/Flynn/Modules/ResourceNodes/ResourceNodes_Lab.unity` — one of every harvestable kind.
- `Assets/Flynn/Modules/CozyDialogue/Scenes/Dialogue_Test.unity` — the dialogue box on its own.
- `Assets/Flynn/Modules/CozyDialogue/Scenes/Knowledge_Grove.unity` — dialogue + knowledge + the
  Field Archive drawer.

## Running the LLM dialogue

The NPC brain talks to OpenRouter. Without a key the rest of the project still compiles and the
non-LLM scenes still run.

1. Set the key as either an EditorPrefs entry `Flynn.OpenRouter.ApiKey`, or the environment
   variable `OPENROUTER_API_KEY`.
2. Optional: a local Ollama at `localhost:11434` for embeddings. Without it there is a graceful
   keyword fallback.

## Conventions worth knowing before you edit

- **One MonoBehaviour/SO class per file**, named after the class. Unity requires it for
  serialization; breaking it produces silent missing-script components.
- **Config and tuning live in ScriptableObjects**, not in fields on scene objects. Change an
  `.asset`, not code.
- **Sorting is one layer with order bands.** Everything is on `Default`; painted ground renders
  at orders 4/13/22/35 and the actor/prop band is 4850–6001. A new renderer left at Unity's
  default order 0 draws *under the ground* — that is what "my effect disappeared" means here.
- **New sprites import with a bottom-centre pivot.** The node rig's wobble joint assumes it, and
  centre-pivot art sinks by half its height on the first physics step.
- **A `using` in this codebase does not prove a dependency.** Many are vestigial. Check for real
  type usage before assuming coupling.

## Known gaps

These are honest, not oversights:

- `FieldArchive.knowledge` is unassigned on `CozyDialogueUI.prefab`. Opening the Field Archive
  drawer without assigning a `KnowledgeBase` will throw a null reference.
- The LLM stack and the cozy dialogue UI are **not wired to each other**. `Flynn.Npc` renders
  through its own UI Toolkit panel; the cozy UI is driven separately. Connecting them is open work.
- Two `Flynn.Npc` data assets reference missing scripts, and one CozyDialogue UXML has a dangling
  template reference.
- The parent game's composed scene is not here, so some prefabs expect managers that only exist
  in that scene.
