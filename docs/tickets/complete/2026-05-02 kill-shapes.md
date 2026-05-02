# Kill Shapes

## Context
**Current behavior**: Every shape in a level is a normal terrain collider. Banana physics-collides with all of them indistinguishably. There is no fail state — banana cannot die.

**New behavior**: Each `LevelShape` carries an `IsKill` flag (default false). In the level editor, pressing **K** toggles the currently-edited shape between normal and kill, with a clear visual difference. At play time, banana touching a kill shape teleports it back to `LevelData.StartPosition` (with velocities and rewind buffer cleared).

## Prerequisites
None.

## Scope
### In scope
- Add `IsKill` flag to `LevelShape` data model + JSON round-trip.
- **Backwards-compatible loading**: existing level JSON files (e.g., `StoneHenge.json`) that pre-date this feature have no `isKill` field on their shapes. Loading such files MUST succeed, and every shape MUST default to `IsKill = false`. `System.Text.Json` already returns the property's default for a missing field, but verify in the test plan and do not regress this with attribute changes that would error on missing properties.
- Editor: K-key toggle on the currently-edited shape; distinct rendering for kill shapes.
- Editor load/save preserves the flag (constructor populates `EditorShape`, `ExportToConsole` writes it back).
- Playing: kill fixtures tagged at construction; banana-touch detection + respawn at level start; respawn clears velocities and the rewind buffer.

### Out of scope
- Death VFX / sound / camera shake / flash.
- Hold-K-to-paint or any multi-shape selection — single-shape toggle only.
- Per-kill-shape custom respawn points (always returns to `LevelData.StartPosition`).

## Relevant Docs & Anchors
- `docs/REFERENCE.md §Physics` — coordinate scale, fixed-step, Aether quirks.
- **Code anchors**:
  - `LevelShape` in `BananaTime/Levels/LevelData.cs` — model + JSON options.
  - `EditorShape` private class inside `LevelEditor` — editor-side shape state.
  - `LevelEditor.Input` (the `Keys.X` / `Keys.Space` handlers) — pattern for adding the K key.
  - `LevelEditor.DrawShapes` — color logic to extend.
  - `LevelEditor` constructor + `ExportToConsole` — load/save round-trip points.
  - `LevelTerrain` constructor — only place that builds physics fixtures from shapes.
  - `Banana.IsGrounded` — established pattern for walking `Body.ContactList` and checking each contact.
  - `Playing.FixedUpdate`, `Playing.CaptureSnapshot`, `Playing.ClearRewindBuffer` — where the kill check + respawn fit.

## Constraints & Gotchas
- Aether `Vector2` vs. XNA `Vector2`: follow the existing `XnaVector2` / `AetherVector2` aliasing already used in `LevelTerrain.cs` and `Playing.cs`. Body positions are in **meters**; `LevelData.StartPosition` is in **pixels** — convert with `PhysicsConstants.MetersPerPixel` (see how `Playing` constructs the banana).
- During rewind, `World.Step` is skipped, so contacts don't update — the kill check should run after `World.Step` / `CaptureSnapshot` and only on forward frames. Don't run it inside `StepRewind`.
- The editor's red coloring already means "non-convex / wrong winding" — pick a different visual (color or other) for kill shapes so the two states aren't conflated. A kill shape that *also* has a winding problem should still surface that error somehow; implementer's call how (e.g., separate outline + fill colors, dashed line, or just prioritize one).
- Be mindful of identifying kill fixtures cheaply at contact-walk time. `Fixture.IsSensor` and `Body.Tag` / `Fixture.Tag` are both viable; pick one and apply consistently in `LevelTerrain` and the contact-walk in `Playing`.

## Open Decisions
1. **Field/property name** — `IsKill`, `IsKillShape`, `Kill`. Default: `IsKill` (matches the brief: "kill shape"). Same name on `LevelShape` and `EditorShape`.
2. **Sensor vs. solid kill fixture** — sensor (`fixture.IsSensor = true`) means banana phases through and dies on overlap; solid means banana physically bounces off and dies on touch. Default: **sensor** — instant-death feel matches "kill shape" better than a bouncy hazard, and avoids weird interactions where banana wedges against a kill surface mid-frame. Implementer can flip if it feels wrong.
3. **Kill-fixture identification at contact time** — options: `Fixture.IsSensor` (works only if also chosen for #2), tag the `Body` via `Body.Tag` with a sentinel/enum, or tag each `Fixture.Tag`. Default: tag `Body.Tag` with a small marker (sentinel object or enum). Robust regardless of sensor choice and grep-able.
4. **Kill-shape editor color** — needs to be visually distinct from white (normal) and red (winding error). Default: magenta or orange. Implementer's eye.
5. **Indicate `EditingShape` is a kill shape in the HUD** — nice-to-have, not required. Default: append " [KILL]" to the existing `mode:` text when applicable.

## Acceptance Criteria
- [ ] `LevelShape` has a public `IsKill` property (bool, defaults false), serialized via the existing camelCase `JsonSerializerOptions`.
- [ ] Loading any existing level JSON that has no `isKill` field on its shapes (e.g., `StoneHenge.json`) succeeds without exception, and every shape ends up with `IsKill == false`. This must be a non-negotiable load-path guarantee, not a happy-path side effect.
- [ ] In the level editor, pressing `K` while a shape is being edited toggles that shape's `IsKill`; pressing `K` with no shape selected is a no-op.
- [ ] Kill shapes render visually distinctly from non-kill shapes in the editor (color or other obvious cue), and the difference is visible whether or not the shape is currently being edited.
- [ ] `ExportToConsole` JSON output includes the `isKill` field for shapes where it's true; round-tripping a level through save → load → save produces the same flag values.
- [ ] In `LevelTerrain`, kill-flagged shapes produce fixtures/bodies that are identifiable at contact-walk time (matches Open Decision #3).
- [ ] In `Playing`, after a forward `World.Step`, if banana is touching any kill fixture, banana respawns: position = `LevelData.StartPosition` (in meters), linear velocity = 0, angular velocity = 0, rotation = 0 (or whatever start pose the level implies — match `new Banana(...)`'s spawn state).
- [ ] On respawn, the rewind buffer is fully cleared (matches `ClearRewindBuffer` semantics) and `IsRewinding` remains false.
- [ ] Kill detection does not run during rewind playback.

## Implementation

### 1. Add `IsKill` to the data model
In `BananaTime/Levels/LevelData.cs`, add a bool `IsKill` property to `LevelShape` (default false). The existing `JsonSerializerOptions` (camelCase, indented) handles serialization automatically; verify by inspecting a serialized output. No converter changes needed.

### 2. Mirror the flag on `EditorShape`
In `LevelEditor.EditorShape`, add a public mutable `IsKill` field (default false). Update the `LevelEditor` constructor's foreach over `config.Level.Shapes` to copy `shape.IsKill` onto the new `EditorShape`, and update `ExportToConsole` to copy it back when building the `LevelShape` list.

### 3. Add the K-key toggle in editor input
In `LevelEditor.Input`, add a `Keys.K` branch — when pressed and `EditingShape.HasValue`, flip `Shapes[EditingShape.Value].IsKill`. Place it next to the other single-key handlers (`Keys.X` export, `Keys.Space` start). No-op if no shape is selected.

### 4. Render kill shapes distinctly
In `LevelEditor.DrawShapes`, branch the color/style choice on the shape's `IsKill` flag. The existing logic picks white-vs-red based on `IsConvexClockwise`; extend so kill shapes use a different palette (see Open Decision #4). Apply the choice to both the edge lines (including the implied closing edge at half-alpha) and the point dots.

### 5. Surface kill state in the editor HUD
Optional but cheap: in `DrawHud`, when `EditingShape.HasValue`, append a " [KILL]" suffix (or similar) to the existing `mode:` line if that shape is flagged. Helps when toggling.

### 6. Tag kill fixtures in `LevelTerrain`
In `LevelTerrain`'s constructor loop, after creating the chain/loop shape, branch on `shape.IsKill`. For kill shapes, apply the chosen identification strategy (Open Decision #3 — default: set `body.Tag` to a marker; if going with sensor, also `chain.IsSensor = true`). For non-kill shapes, behavior is unchanged (friction/restitution as today).

### 7. Hold the start position on `Playing`
`Playing` discards `config.Level.StartPosition` after passing it to the `Banana` constructor. Capture it (e.g., in a `Vector2 StartPositionPixels` field, or keep a reference to the `LevelData`) so respawn can use it. Convert to meters at respawn time the same way the constructor does.

### 8. Kill-detection + respawn in `Playing.FixedUpdate`
After `CaptureSnapshot()` on forward frames (i.e., not inside the `StepRewind` branch), walk `Banana.Body.ContactList` similarly to `Banana.IsGrounded` — for each touching contact, check whether the *other* body/fixture is kill-marked. If any, call a new private `Respawn()` method.

`Respawn()`: set banana body position to start (meters), linear velocity zero, angular velocity zero, rotation zero (match the `new Banana(...)` starting pose). Then call `ClearRewindBuffer()`. Do not change `IsRewinding` (should already be false on this path) and do not call `World.Step` again.

Edge case: do not run kill detection inside `StepRewind` — only on the forward branch, after the snapshot is captured.

### 9. Verify the snapshot capture order is sane
Quick check: capturing the *pre-respawn* snapshot is fine (the snapshot was the banana's last live position before death; respawn fires after capture). No buffer-clearing race because `Respawn` calls `ClearRewindBuffer` which resets `RewindHead` and `RewindCount`. Confirm this matches behavior expected by the Acceptance Criteria.

## Test Plan
- [ ] `dotnet build` (from repo root or `BananaTime/`) succeeds with no new warnings.
- [ ] Open `StoneHenge.json` (which has no `isKill` fields anywhere) via the Edit-Saved-Level menu — loads cleanly with no exception, and every shape renders as non-kill.
- [ ] In the editor, click an existing shape to select it; press K — its rendering changes to the kill style. Press K again — reverts.
- [ ] Press K with no shape selected — nothing happens (no crash, no visual change).
- [ ] With at least one shape flagged kill, press X to export. Confirm the printed JSON includes `"isKill": true` on the right shape and omits or shows `false` on others.
- [ ] Re-load the exported JSON (paste into a level file or re-import) — kill flags persist.
- [ ] Author a small test level: a flat floor + a ceiling kill shape near spawn. Play it. Jump up into the kill shape — banana teleports back to start. Velocity is zero on respawn.
- [ ] Same test level: roll along the floor for >6 seconds to fill the rewind buffer (HUD shows cyan `6.00/6s`). Kill yourself — confirm HUD reads `0.00/6s` after respawn.
- [ ] Verify rewind still works in a level *without* killing yourself: nothing about the rewind mechanic should regress.
- [ ] Press R to start a rewind; while rewinding, do not interact — confirm no respawn fires from a stale contact (the kill check should only run on forward frames).

## Learnings

### Open Decisions resolved
1. **Field name** — `IsKill` (matches default).
2. **Sensor vs. solid** — sensor (`chain.IsSensor = true`). Banana phases through and respawns rather than physically bouncing; feels closer to "instant death" than a bumpable hazard.
3. **Identification** — `body.Tag = LevelTerrain.KillTag` (a `static readonly object` sentinel exposed as `LevelTerrain.KillTag`). Body-level tag chosen over fixture-level so a single reference-equality check identifies kill bodies; decoupled from the sensor decision so flipping #2 wouldn't break detection.
4. **Editor color** — `Color.Magenta`. Kill state takes visual priority over the convex-clockwise red warning; acceptable since kill shapes are sensors and don't need strict winding for "rolling on top of" behavior.
5. **HUD `[KILL]` suffix** — added; ` [KILL]` appended to the `mode:` line. Also added `K kill` to the help line at the bottom of the editor.

### Architectural decisions
- **`StartPositionMeters` cached on `Playing`** — the `PlayingConfig.Level` reference is otherwise only used at construction. Storing the converted meters value once (rather than holding the `LevelData` and re-converting per respawn) keeps respawn allocation-free and matches the existing pattern of `PictureName` being captured separately.
- **Kill check placement** — runs after `CaptureSnapshot()` on the forward branch only. Capturing the pre-respawn snapshot is harmless because `Respawn()` immediately calls `ClearRewindBuffer()`, wiping the just-captured frame. This sidesteps any kill-during-rewind concerns since `World.Step` doesn't run during rewind so contacts don't update.

### Interesting tidbits
- Aether's `Contact.IsTouching` works for sensor fixtures — sensors generate contact events without solving manifolds, so the `Banana.IsGrounded`-style `ContactList` walk handles both solid and sensor cases uniformly.
- `System.Text.Json` returns the property's default for missing JSON fields by default — no migration needed for pre-existing levels (`StoneHenge.json` loads with `IsKill = false` on every shape automatically). Adding `[JsonRequired]` would have broken this; not done intentionally.

### Rejected alternatives
- **`Fixture.IsSensor` as the kill marker** — would have coupled detection to the sensor choice. If a future ticket wants solid kill shapes (e.g., spike walls that physically block), `IsSensor`-as-marker silently breaks. `Body.Tag` keeps the two decisions independent.
- **Per-shape custom respawn points** — out of scope and not requested. Always respawns to `LevelData.StartPosition`.

### Related areas
- `Banana.IsGrounded` was the model for the kill-contact walk; both iterate `Body.ContactList` and inspect the *other* fixture/body. Future contact-driven mechanics (collectibles, level-end triggers, checkpoints) should follow the same pattern + `Body.Tag` sentinel approach for cheap identification.
