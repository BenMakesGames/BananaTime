# Overworld Graph Data Model

## Context
**Current behavior**: A single `LevelData` represents a single level. No concept of multiple levels, progression, or unlock state.

**New behavior**: A new `OverworldGraph` data structure models a Mario-style level map. Each node carries screen coordinates and a `Direction`-keyed dictionary of connections. Nodes have one of three states: `Locked`, `Available`, `Completed`. The graph's start node begins `Available`; every other node begins `Locked`. `CompletedLevel(node)` marks the node `Completed` and promotes any adjacent `Locked` nodes to `Available`. **Pure data layer** — no UI, persistence, or game-flow integration in this ticket.

## Prerequisites
None.

## Scope
### In scope
- New types representing the graph, the node, and the node-state enum.
- Construction: build a graph from a set of nodes + a designated start node. On construction, the start node is `Available`; all others are `Locked`.
- `CompletedLevel(OverworldNode)` — the sole API for advancing state. Sets the node to `Completed` and promotes adjacent `Locked` nodes to `Available`.
- Per-node screen coordinates (`Vector2`, in pixels — matches the rest of the codebase's pixel-as-default convention).
- Per-node `Dictionary<Direction, OverworldNode>` connections.

### Out of scope
- Persistence / save format / serialization to JSON.
- UI / `GameState` to render the overworld.
- Game-flow integration: no `TitleScreen` change, no `Playing` change, no hookup of `CompletedLevel` to actual gameplay events.
- A real game-shipping graph definition. Build a small synthetic graph only for sanity-testing the API.
- Win-condition mechanic. `CompletedLevel` is callable but unwired — that's a follow-up ticket.

## Relevant Docs & Anchors
- `BananaTime/Levels/LevelData.cs` — existing level data model. New graph types should live alongside in `BananaTime/Levels/` (or a new `BananaTime/Overworld/` folder — implementer's call; see Open Decisions).
- `BananaTime/Input/Direction.cs` — the existing `Direction` enum (`Up`, `Left`, `Down`, `Right`) that connection dictionaries are keyed by. **Reuse this** — do not declare a new direction enum in `Levels/`.

## Constraints & Gotchas
- `Direction` lives in `BananaTime.Input`. Importing it from `BananaTime.Levels` (or wherever the graph lives) is fine — namespaces here are organizational, not a layered-architecture rule. Just `using BananaTime.Input;` and use it.
- Cross-references between nodes mean construction has a chicken-and-egg shape: you generally need all node instances to exist before you can wire their connections. Build nodes first, then wire connections, then hand the set to the graph constructor.
- "Adjacent" for the purposes of `CompletedLevel` means **any** node in the completed node's `Connections` dictionary, regardless of which `Direction` key it's stored under. Do not filter by direction.
- This is pure data — no PPM service registration (`[AutoRegister]`), no MonoGame `GraphicsDevice`, no Aether. The graph type's only framework dependency is `Microsoft.Xna.Framework.Vector2` for screen coords (same as `LevelData`).

## Open Decisions
1. **Type names** — `OverworldGraph` / `OverworldNode` vs. `LevelGraph` / `LevelNode` vs. `LevelMap` / `MapNode`. Default: **`OverworldGraph` + `OverworldNode`** — matches the brief's "overworld" framing and keeps clear separation from `LevelData` (a single level's contents).
2. **State enum name** — `OverworldNodeState`, `NodeState`, `LevelStatus`. Default: **`OverworldNodeState`** — namespaced by purpose so a future per-level gameplay-state enum doesn't collide.
3. **Folder** — `BananaTime/Levels/` (alongside `LevelData.cs`) vs. new `BananaTime/Overworld/` folder. Default: **`BananaTime/Levels/`** — only three files; a new folder feels heavyweight for the current scope. Implementer can split if it grows.
4. **Node identity field** — add a `string Name` (or similar) to nodes for debugging / logs / future JSON-keying, vs. rely on object reference alone. Default: **add `string Name`** as a node field. Reference-equality is what `CompletedLevel` uses internally; the name is for human-readable diagnostics.
5. **Bidirectional connection wiring** — should the API offer a "connect A↔B in direction D (auto-mirror reverse on B)" helper, or require the caller to wire both sides explicitly? Default: **explicit both-sides wiring** — the data structure should remain honest about asymmetric connections being representable. A bidirectional helper can be added later if authoring becomes painful.
6. **`CompletedLevel` on a non-`Available` node** — what happens if the caller passes a `Locked` or already-`Completed` node? Default: **no-op for already-`Completed`; throw `InvalidOperationException` for `Locked`** (signals a bug — the caller marked complete a level the player couldn't reach). Implementer can soften to a no-op + log if test-fixturing turns out to be painful.
7. **Connection mutability after construction** — should `OverworldNode.Connections` be wire-only-during-build, or mutable forever? Default: **wire-only-during-build** — once handed to `OverworldGraph`'s constructor, treat the topology as frozen. Expose `Connections` as `IReadOnlyDictionary` externally; keep mutation internal/private.

## Acceptance Criteria
- [ ] A public `OverworldNodeState` enum exists with exactly three members: `Locked`, `Available`, `Completed`.
- [ ] A public `OverworldNode` class exists with:
  - A `Vector2 ScreenPosition` (read-only at the public surface).
  - An `IReadOnlyDictionary<Direction, OverworldNode> Connections` exposing the per-direction adjacency map.
  - An `OverworldNodeState State` property whose setter is **not** publicly callable (mutation routes through `OverworldGraph`).
  - A `string Name` (per Open Decision #4 default).
- [ ] A public `OverworldGraph` class exists. Its constructor accepts the full set of nodes plus a designated start node; on construction, the start node's state is `Available` and every other node's state is `Locked`.
- [ ] `OverworldGraph` constructor validates that the designated start node is among the supplied nodes (throws if not).
- [ ] `OverworldGraph.CompletedLevel(OverworldNode node)` exists and:
  - Sets `node.State = Completed`.
  - For each `OverworldNode` in `node.Connections.Values`, if its current state is `Locked`, transitions it to `Available`. Adjacents that are already `Available` or `Completed` are left untouched.
  - Direction is irrelevant to which adjacents get promoted — every entry in `Connections` is considered.
  - Behavior on a non-`Available` input node matches Open Decision #6 (default: no-op for `Completed`, throw for `Locked`).
- [ ] The graph types compile against the existing project with no new dependencies beyond `Microsoft.Xna.Framework` (already referenced) and `BananaTime.Input` (for `Direction`). No `[AutoRegister]`, no PPM-service interfaces.
- [ ] Existing code (`LevelData`, `Playing`, `TitleScreen`, the editor, etc.) is unchanged — this ticket is purely additive.

## Implementation

### 1. Add the state enum
New file (per Open Decision #3 default: `BananaTime/Levels/OverworldNodeState.cs`). Public enum with `Locked`, `Available`, `Completed`. Order is reader's-eye; the source of truth is the three names.

### 2. Add `OverworldNode`
File: `BananaTime/Levels/OverworldNode.cs` (or alongside the graph). Carries:
- `Vector2 ScreenPosition` — set in the constructor, no public setter.
- `string Name` — set in the constructor, no public setter (per Open Decision #4).
- A private/backing `Dictionary<Direction, OverworldNode>` plus a public `IReadOnlyDictionary<Direction, OverworldNode> Connections` view of it.
- An `OverworldNodeState State` property. Public getter; mutation visible only to `OverworldGraph` (e.g., `internal set` if both types are in the same assembly, or a `SetState` method with `internal` access — implementer's idiom call).

A `Connect(Direction, OverworldNode)` method to wire connections during graph build. Whether this is public, internal, or only callable from a builder is the implementer's call — the constraint is that once the graph is constructed (step 3), connections should be effectively frozen (Open Decision #7).

Don't auto-mirror — caller wires both sides if they want a bidirectional path (Open Decision #5).

### 3. Add `OverworldGraph`
File: `BananaTime/Levels/OverworldGraph.cs`. Constructor signature is the implementer's call — `IEnumerable<OverworldNode>` + start node, or `IReadOnlyList`, or a builder pattern. Whatever shape, it must satisfy:
- The start node is among the supplied nodes (throw if not — `ArgumentException` is fine).
- Initial states: start = `Available`, all others = `Locked`.

`CompletedLevel(OverworldNode node)` implements the state transitions in Acceptance Criteria + Open Decision #6. Iterate `node.Connections.Values` (a HashSet to dedupe is unnecessary — a node connected to the same neighbor in two directions is a degenerate but harmless case; promoting it twice is idempotent).

Optionally expose `IReadOnlyCollection<OverworldNode> Nodes` if useful for future consumers (UI rendering will want to iterate all nodes). Implementer's call whether to add it now.

### 4. Sanity-check the API
There's no test project in this repo. For verification, either (a) write a throwaway scratch entry-point or `Main`-stage block that constructs a small graph and asserts the transitions, run it once, then delete it, or (b) walk through the cases mentally with the implementation open. The Test Plan below codifies the cases either approach should cover.

## Test Plan
- [ ] `dotnet build` from `BananaTime/` succeeds with no new warnings.
- [ ] Manual or scratch verification of the canonical scenarios:
  - Build a 3-node line graph A — B — C (A↔B via Right/Left, B↔C via Right/Left), with A as start. Confirm A is `Available`, B and C are `Locked`.
  - Call `CompletedLevel(A)`. Confirm A is `Completed`, B is `Available`, C is `Locked`.
  - Call `CompletedLevel(B)`. Confirm B is `Completed`, A is **still** `Completed` (not regressed to `Available`), C is `Available`.
  - Build a graph where A connects to B via `Up` *and* `Right` (degenerate double-edge). Call `CompletedLevel(A)`. Confirm B is `Available` and the operation does not throw.
  - Call `CompletedLevel` on a node that is currently `Completed` — confirms no-op (per Open Decision #6 default).
  - Call `CompletedLevel` on a node that is currently `Locked` — confirms throw (per Open Decision #6 default).
- [ ] No existing game state visibly changes — title screen, playing, editor all behave as before.
