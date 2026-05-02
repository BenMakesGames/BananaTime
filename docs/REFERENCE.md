# Banana Time — Reference

## Game

Game-jam platformer. Player controls banana. Curved/asymmetric shape + physics make traversal hard.

Three controls: rotate CW, rotate CCW, jump.

Levels = navigating difficult terrain. Build as many as time allows.

Signature mechanic: **6-second rewind**. Game records banana state every fixed step into a 360-entry ring buffer (6s @ 60Hz). When buffer is full, player presses R to trigger rewind. Playback runs in reverse at 2× speed (~3s real-time), with input locked out. On finish, buffer wipes — player must accumulate another full 6s before next rewind.

## Tech Stack

| Piece | Choice |
|---|---|
| Language | C# |
| Target framework | .NET 10 |
| Game framework | MonoGame 3.8.4.1 (DesktopGL) |
| Engine layer | PlayPlayMini 8.2.0-rc1 (game-state lifecycle, fixed-step loop, asset mgmt, input, draw helpers) |
| Physics | nkast.Aether.Physics2D 2.0.0 (Box2D port, pure C#) |
| Logging | Serilog |
| Distribution | Steamworks.NET wired (currently commented in `Program.cs`) |

## Internal Resolution

640 × 360 (1920/3 × 1080/3), 2x window zoom.

## Physics

### Scale
- **256 px = 1 m**. Banana sprite (52×97 px) ≈ 0.20 × 0.38 m. Within Box2D sweet-spot (0.1–10m).

### Coordinate System
- MonoGame screen Y-down + Aether gravity `+Y` = same axis. **No Y flip** between pixel and physics space. Pixel `(x, y)` ↔ meter `(x/256, y/256)`.

### Vector2 conflict
- Aether ships `nkast.Aether.Physics2D.Common.Vector2`. Conflicts with `Microsoft.Xna.Framework.Vector2`.
- Solution: `Physics/PhysicsConversions.cs` extension methods (`ToAether()`, `ToXna()`).
- In files using both: `using XnaVector2 = ...; using AetherVector2 = ...;`

### Banana shape
- Compound body of **11 circles** along banana curve. Source-of-truth = artist-provided bounding boxes (top-left + size, in pixels, on the 52×97 sprite).
- Body origin = area-weighted centroid of circles. Computed once at static init in `PhysicsConstants`. Sprite renders with offset compensation so centroid pivot matches physics pivot.

### Constants (live in `Physics/PhysicsConstants.cs` — single source of truth, tune here)
- `Gravity = 9.8` m/s²
- `BananaDensity = 1`, `BananaFriction = 0.7`, `BananaRestitution = 0.05`
- `JumpImpulseMetersPerSecond = 2.5` (multiplied by mass at apply-time)
- `RotateAngularVelocityRadiansPerSecond = 6` (~1 rev/sec while held)
- `FixedTimestepSeconds = 1/60`
- `RewindCaptureSeconds = 6`, `RewindFrames = 360`, `RewindPlaybackSpeed = 2`
- `CameraDeadzoneRadiusPixels = 80`, `CameraSmoothRatePerSecond = 6`

### Fixed-step
- PlayPlayMini's `FixedUpdate` runs at fixed 60Hz with built-in accumulator. All physics + input application + rewind capture happen there. `Update` runs camera follow (uses real `deltaSeconds` for frame-rate-independent smoothing).

### Grounded check
- Walks `Body.ContactList`, looks for any touching contact whose world-space normal Y component (pointing away from banana) is > 0.5. Cheap heuristic; may need refinement for steep slopes.

### Rotation control
- While CW key held: `Body.AngularVelocity = +K`. CCW: `-K`. When neither held: physics damping decays. Direct override (not torque-based) for snappier puzzle feel.

### Jump
- One-shot: `PressedAnyKey` queued in `Input`, consumed in `FixedUpdate`. Fires only if `TryGetJumpDirection` returns a contact-derived direction. Applies linear impulse `mass × JumpImpulseMetersPerSecond` along that direction (sum of contact normals → handles wall-jump and pinch cases; falls back to straight up if contacts cancel).

## Rewind

- `Physics/BananaSnapshot.cs` — readonly struct holding `Position`, `LinearVelocity`, `Rotation`, `AngularVelocity`. Capture/Apply against an Aether `Body`.
- `Playing.cs` owns ring buffer `BananaSnapshot[RewindFrames]` plus `RewindHead`, `RewindCount`, `RewindReadIndex`, `IsRewinding`.
- Each forward `FixedUpdate`: step world, then capture into `RewindHead`, advance head mod `RewindFrames`, increment count (saturating at `RewindFrames`).
- Trigger gated on `RewindCount == RewindFrames`. R key sets `RewindQueued`.
- During rewind: input zeroed, `World.Step` skipped. Each fixed frame advances `RewindReadIndex` backward by `RewindPlaybackSpeed` (2) and applies the landing snapshot directly to the body. Intermediate snapshots skipped — only the landing frame matters since writes overwrite.
- On finish: buffer wipes (`RewindCount = 0`, `RewindHead = 0`), state returns to forward play. Body resumes with the velocity from the oldest applied snapshot, so post-rewind sim continues smoothly.
- Zero allocations per frame: structs by value into pre-allocated array.

## Camera

- World-space camera (top-left in world pixels). Follows banana with **deadzone**: ignores motion while banana is within `CameraDeadzoneRadiusPixels` of view center; once outside, decays toward centering using frame-rate-independent exponential damping (`1 - exp(-rate * dt)`); stops the moment banana re-enters radius.
- Clamped to background-picture bounds (`[0, picSize - viewSize]` per axis) when `Camera.WorldSizePixels` is set, so the view never reveals space beyond the level art.
- Camera follow runs in `Update` (not `FixedUpdate`) using real elapsed seconds.
- Position snapped to integer pixels in `WorldToScreen` to avoid pixel-art subpixel jitter.
- PPM uses `SpriteSortMode.Immediate` SpriteBatch with no transform hook → no matrix-based camera. Implemented as manual offset via `Camera.WorldToScreen(worldPx)` per draw call. HUD draws screen-space, unaffected.

## Input Bindings

| Action | Keys |
|---|---|
| Rotate CW | D, Right, NumPad6 |
| Rotate CCW | A, Left, NumPad4 |
| Jump | W, Z, X, Space |
| Rewind | R |

## File Layout

```
BananaTime/
  Program.cs                    PPM bootstrap, asset registration
  GameStates/
    Startup.cs                  asset-load wait, transitions to Playing
    Playing.cs                  world owner, input/update/draw, camera, rewind, HUD
    LostFocus.cs                pause-on-defocus
  Physics/
    PhysicsConstants.cs         all tunables + computed banana circles
    PhysicsConversions.cs       XNA ↔ Aether Vector2 helpers
    Banana.cs                   body wrapper, jump/rotate/grounded
    BananaSnapshot.cs           readonly struct, capture/apply body state for rewind
    TestTerrain.cs              chain-shape ground for current test scene
    Camera.cs                   deadzone-follow camera
  Content/
    Content.mgcb                MGCB content pipeline
    Graphics/
      Banana.png                52×97 banana sprite
      Cursor.png
      Font.png
```

## Test Scene (current)

Single terrain chain with vertices at:
`(-10000, 320) → (180, 320) → (300, 240) → (460, 240) → (520, 280) → (10000, 280)`

= flat ground, ramp up, plateau, step down, flat. Extends ±10000 px so banana can roll forever.

Banana spawns at world pixel `(120, 60)`. Falls onto terrain.

HUD shows: grounded flag, angular velocity, linear velocity, rewind buffer fill (`x.xx/6s`, cyan when ready), `REWINDING` indicator during playback.

## Not Yet Implemented

- Real levels (current is debug terrain only)
- Death / respawn / level transitions
- Sound, music
- Title screen, menus
- Level loader / level format

## Known Quirks

- Banana inner crescent (concave side) is hollow — terrain corners can poke through visually since no fixture covers it. Intended jank / gameplay feature.
- Red 2×2 pixel debug dot rendered at body position (centroid). Useful for verifying sprite-pivot alignment.
- `MakeCircleTexture` helper in `Playing.cs` is unused. Available if circle visualization wanted.
