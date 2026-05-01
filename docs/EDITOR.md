# Banana Time — Level Editor

Authoring tool for tracing geometry on top of bitmap level art.

## States

| State | Role |
|---|---|
| `LevelPicker` | Lists all loaded `Pictures` via `Menu`. Selecting one transitions to `LevelEditor` with a `LevelEditorConfig(PictureName)`. `Esc` / `Back` returns to `TitleScreen`. |
| `LevelEditor` | `GameState<LevelEditorConfig>`. Mouse-driven shape editor over the chosen picture. |

`TitleScreen` exposes the editor through a `Level Editor` menu item that transitions to `LevelPicker`.

## Input model

| Action | Effect |
|---|---|
| Left-click empty space, no shape selected | Start new shape with the click as point 0. Select that shape. |
| Left-click empty space, shape selected | Append point to selected shape. |
| Left-click on existing point (any shape) | Select that shape. Hit radius = `PointHitRadius` (5 px screen). |
| Right-click, shape selected | Pop last point. If shape now empty, delete the shape and exit edit mode. |
| Right-click, no shape selected | No-op. |
| `Esc`, shape selected | Exit edit mode (keep shape). |
| `Esc`, no shape selected | Return to `LevelPicker`. |
| `WASD` / arrows / numpad `8 6 2 4` | Pan view at `PanSpeed` px/s (continuous, dt-scaled in `Update`). |

Mouse is the editor's primary input. The rest of the game uses no cursor; `LevelEditor.Enter()` calls `Mouse.UseSystemCursor()` and `Leave()` reverts to `Mouse.UseNoCursor()`.

## Rendering

- Picture drawn at `(-PanOffset.X, -PanOffset.Y)` (integer-truncated).
- For each shape:
  - `DrawFilledCircle` per point at `PointRadius` (3 px), white.
  - White line between consecutive points.
  - Implied closing edge from last point back to first drawn at 50% alpha white (`Color.White * 0.5f`).
- Lines drawn via the same `WhitePixel` rotated-rect trick as `Playing`/`TestTerrain`.
- HUD: picture name, current edit-mode (shape index + point count), pan offset, control hints.

## Constants (in `LevelEditor.cs`)

| Const | Value | Why |
|---|---|---|
| `PointRadius` | `3` | Visual marker size. |
| `PointHitRadius` | `5` | Selection tolerance — slightly larger than visual so points feel clickable. |
| `PanSpeed` | `240f` | Pixels per second; fast enough to traverse a multi-screen image without feeling sluggish. |

## Coordinate spaces

Three live in this codebase. Don't mix them up.

| Space | Units | Origin | Y axis | Where it shows up |
|---|---|---|---|---|
| **Screen** | `int` pixels (pre-zoom internal RT, 640×360) | top-left of viewport | down | `Mouse.X / Mouse.Y`, draw calls |
| **Picture-local** | `int`/`float` pixels | top-left of source bitmap | down | What the editor stores per shape point |
| **Physics world** | `float` meters | matches picture-local origin (no flip) | down (gravity `+Y`) | Aether bodies/shapes |

Editor stores points in **picture-local pixels**. Not meters, not screen-relative.

## Conversions

### Screen ↔ picture (live in `LevelEditor`)
```
picture = screen + PanOffset
screen  = picture - PanOffset
```
`PanOffset` is the picture-pixel coordinate of the screen's top-left corner.

### Picture ↔ physics
```
meters = pixels * PhysicsConstants.MetersPerPixel    // 1/256
pixels = meters * PhysicsConstants.PixelsPerMeter    // 256
```
Or use `PhysicsConstants.ToMeters(...)` / `ToPixels(...)`. Same X and Y both — no flip.

### Xna ↔ Aether `Vector2`
Two `Vector2` types in scope. Convert with extension methods:
```csharp
xna.ToAether();
aether.ToXna();
```
File-level aliases keep the rest readable:
```csharp
using XnaVector2    = Microsoft.Xna.Framework.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;
```

### Picture-pixel point → Aether vertex (export pipeline, not yet implemented)
```csharp
var aetherVert = (picturePoint * PhysicsConstants.MetersPerPixel).ToAether();
```
Feed into `Vertices` and either `CreateChainShape` (open) or `CreatePolygon` (convex closed).

## Winding order — the big gotcha

Aether/Box2D wants polygons CCW **in standard math space (Y-up)**. We use Y-down everywhere. Y-flip inverts winding sense, so:

> **Trace solid polygon shapes clockwise on screen.** Visual CW + Y-down = math CCW = correct.

Trace counter-clockwise and `CreatePolygon` will silently produce inside-out collision (banana falls *into* the floor, contact normals reversed).

Chain shapes (open paths) don't have an "inside" so winding only affects one-sided collision normal direction. Still trace consistently — pick clockwise to match polygon convention.

## Gotchas

- **Polygon convexity & vertex limit.** Aether `CreatePolygon` requires convex shapes with ≤ `Settings.MaxPolygonVertices` (default 8) verts. Arbitrary level traces will violate both. Plan: decompose to convex pieces, or emit a `ChainShape` instead.
- **Mouse coords are zoom-corrected for you.** `MouseManager` divides by `Graphics.Zoom` internally; `Mouse.X / Mouse.Y` are already in the 640×360 internal-resolution space. Don't divide again.
- **`Mouse.LeftClicked` fires on release, not press.** Same for `RightClicked`. `*Down` is hold-state.
- **`PanOffset` is unbounded.** No clamping yet — pan can drift far past picture edges and clicks out there will spawn points in negative or out-of-bounds picture coords.
- **Coordinate truncation.** Picture is drawn at `(int)PanOffset.X / Y`, so sub-pixel pan jitters by 1 px. Acceptable for an editor; revisit if it bothers you.
- **Cursor lifecycle.** Editor flips the cursor on/off via `Enter`/`Leave`. If you add a new path out of the editor, make sure `Leave()` runs (or set the cursor explicitly) — `GSM.ChangeState` handles this, but a hard exit wouldn't.
- **`PressedAnyKey` vs `KeyDown` vs `PressedKey`.** Pan uses `AnyKeyDown(IList<Keys>)` — held state, fires every frame. Discrete actions (Esc) use `PressedKey` — single edge.
- **`Pictures` may not be loaded when the picker is built** in principle; `LoadContent` defers non-`PreLoaded` assets to a background `Task.Run`. In practice we transition through `Startup` which waits on `Graphics.FullyLoaded`, so `LevelPicker` always sees the full set.
- **No persistence.** Shapes are plain `List<List<Vector2>>` in memory; closing the state drops them.
- **Shape selection is "first hit wins"** — iterates shapes in insertion order, returns first point within hit radius. Overlapping points from different shapes will always select the older shape.
