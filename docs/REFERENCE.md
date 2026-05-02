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
- `JumpImpulseMetersPerSecond = 2.5` (multiplied by mass at apply-time; charge scales it via `[MinJumpImpulseScale, MaxJumpImpulseScale]`)
- `RotateAngularVelocityRadiansPerSecond = 6` (~1 rev/sec while held)
- `MinChargeSecondsToFire = 0.1`, `MaxChargeSeconds = 0.8` — held below min = no fire (tap), held to max = full impulse
- `MinJumpImpulseScale = 0.4`, `MaxJumpImpulseScale = 1.6` — linear lerp from min charge to max charge
- `LostContactGraceSeconds = 0.5`, `MaxJumpAngleDeviationDegrees = 45` (precomputed cosine in `MaxJumpAngleDeviationCosine`)
- `ChargingFrictionMultiplier = 2`, `MaxSquishDepth = 0.3` (sprite squashes to 70% along banana-local Y at max charge)
- `FixedTimestepSeconds = 1/60`
- `RewindCaptureSeconds = 6`, `RewindFrames = 360`, `RewindPlaybackSpeed = 2`
- `CameraDeadzoneRadiusPixels = 80`, `CameraSmoothRatePerSecond = 6`

### Fixed-step
- PlayPlayMini's `FixedUpdate` runs at fixed 60Hz with built-in accumulator. All physics + input application + rewind capture happen there. `Update` runs camera follow (uses real `deltaSeconds` for frame-rate-independent smoothing).

### Grounded check
- Walks `Body.ContactList`, looks for any touching contact whose world-space normal Y component (pointing away from banana) is > 0.5. Cheap heuristic; may need refinement for steep slopes.

### Rotation control
- While CW key held: `Body.AngularVelocity = +K`. CCW: `-K`. When neither held: physics damping decays. Direct override (not torque-based) for snappier puzzle feel.

### Jump (charge model)
- **Direction source**: `Banana.TryGetJumpDirection` sums all touching contact normals (each pointing surface→banana) and normalizes — falls back to straight up if contacts cancel. Returns `false` when no contacts. This is the source-of-truth for both the tracked angle while charging and the impulse direction at fire time.
- **Impulse**: `Banana.ApplyJumpImpulse(direction, speedMetersPerSecond)` applies `mass × speed × direction` as a linear impulse. The caller picks the direction; charge state owns the magnitude (charge-elapsed mapped through `[MinJumpImpulseScale, MaxJumpImpulseScale]`).
- **Charge state machine** lives in `Playing` and runs only on the forward branch — `BeginRewind` and `Respawn` reset to `Idle` and restore base friction. Three states:
  - `Idle`: not charging. While `JumpHeld == true`, the moment a contact appears, `EnterCharging` fires (zeros elapsed, captures tracked direction, raises fixture friction by `ChargingFrictionMultiplier`, drains the 100 ms buffer for hygiene).
  - `OnSurface`: charge-elapsed accumulates each fixed step (capped at `MaxChargeSeconds`); tracked direction continuously updates to the current jump direction; rotation input is ignored. On release: fires along *current tracked* if elapsed ≥ `MinChargeSecondsToFire`, else no-op (tap). Transitions to `InGrace` when contacts vanish OR when `Dot(tracked, current) < MaxJumpAngleDeviationCosine` (deviation > 45°) — tracked freezes at the previous-frame value.
  - `InGrace`: tracked frozen, charge frozen, friction stays raised, grace timer counts down from `LostContactGraceSeconds`. Three exits, in priority order: (a) release → fires coyote-style along the *frozen* tracked direction with whatever charge accumulated (no min-charge gate); (b) timer ≤ 0 → jump lost, no fire; (c) re-acquired contact within 45° of frozen tracked → returns to `OnSurface` and re-syncs tracked to the current direction (the new commit, not the old one).
- **Single source-of-truth** for friction restore: `ExitChargingToIdle` is the only place that resets state + calls `Banana.SetFixtureFriction(BananaFriction)`. Every charge-end path goes through it.
- **Visual squish**: `Playing.DrawBanana` uses `SpriteBatch.Draw` directly with non-uniform `scale`. Squash factor along banana-local Y = `1 - MaxSquishDepth × (chargeElapsed / MaxChargeSeconds)`. The `origin` parameter is set to `BananaSpriteOriginPixels` (the centroid in sprite-local pixels), so SpriteBatch pivots rotation/scale around the centroid and lands it on the body's screen position — no manual sprite-center compensation needed. Squish freezes during `InGrace` (charge-elapsed is frozen).

## Rewind

- `Physics/BananaSnapshot.cs` — readonly struct holding `Position`, `LinearVelocity`, `Rotation`, `AngularVelocity`. Capture/Apply against an Aether `Body`.
- `Playing.cs` owns ring buffer `BananaSnapshot[RewindFrames]` plus `RewindHead`, `RewindCount`, `RewindReadIndex`, `IsRewinding`.
- Each forward `FixedUpdate`: step world, then capture into `RewindHead`, advance head mod `RewindFrames`, increment count (saturating at `RewindFrames`).
- Trigger gated on `RewindCount == RewindFrames`. R key sets `RewindQueued`.
- During rewind: input zeroed, `World.Step` skipped. Each fixed frame advances `RewindReadIndex` backward by `RewindPlaybackSpeed` (2) and applies the landing snapshot directly to the body. Intermediate snapshots skipped — only the landing frame matters since writes overwrite.
- On finish: buffer wipes (`RewindCount = 0`, `RewindHead = 0`), state returns to forward play. Body resumes with the velocity from the oldest applied snapshot, so post-rewind sim continues smoothly.
- Zero allocations per frame: structs by value into pre-allocated array.
- **Visual treatment**: `Playing.Draw` wraps the world layer in `Graphics.WithSceneShader("VcrRewind", e => e.Parameters["Time"].SetValue(...))` while `IsRewinding`. HUD draws outside the scope and stays crisp. Shader file at `Content/Shaders/VcrRewind.fx`.

## Shaders

- Author HLSL `.fx` files under `Content/Shaders/`. Use the cross-platform shader-model macros (`#if OPENGL` / `VS_SHADERMODEL` / `PS_SHADERMODEL`) — DesktopGL build runs HLSL through MojoShader to GLSL.
- MGCB stanza: `EffectImporter` + `EffectProcessor` + `DebugMode=Auto`. **Do not** copy a `TextureImporter` stanza — texture-style processor params (`ColorKey`, `PremultiplyAlpha`, …) don't apply to shaders.
- Register via `new PixelShaderMeta("Key", "Shaders/PathWithoutExtension")` in `Program.cs.AddAssets`.
- For per-sprite tints / palette swaps: `Graphics.WithShader(...)` (samples each draw's own texture).
- For effects that need to sample neighboring scene content (rewind VCR, blurs, ripples, chroma): `Graphics.WithSceneShader(...)` — composites the wrapped layer through the shader at end-of-scope. Always wrap with `using` so the pooled render target releases.
- Effect parameters: pass `Action<Effect>` configure callback; `e.Parameters["Name"].SetValue(...)` throws on missing names — keep shader uniforms and C# call sites in sync.
- SpriteBatch supplies a default vertex shader; pixel-shader-only `.fx` files work fine and avoid having to re-implement the world-view-projection transform. Standard sampler declaration: `Texture2D SpriteTexture; sampler2D SpriteTextureSampler = sampler_state { Texture = <SpriteTexture>; };`.

## Camera

- World-space camera (top-left in world pixels). Follows banana with **deadzone**: ignores motion while banana is within `CameraDeadzoneRadiusPixels` of view center; once outside, decays toward centering using frame-rate-independent exponential damping (`1 - exp(-rate * dt)`); stops the moment banana re-enters radius.
- Clamped to background-picture bounds (`[0, picSize - viewSize]` per axis) when `Camera.WorldSizePixels` is set, so the view never reveals space beyond the level art.
- Camera follow runs in `Update` (not `FixedUpdate`) using real elapsed seconds.
- Position snapped to integer pixels in `WorldToScreen` to avoid pixel-art subpixel jitter.
- PPM uses `SpriteSortMode.Immediate` SpriteBatch with no transform hook → no matrix-based camera. Implemented as manual offset via `Camera.WorldToScreen(worldPx)` per draw call. HUD draws screen-space, unaffected.

## Input Bindings

Player-facing input goes through `Input/PlayerInput.cs` (`[AutoRegister]` singleton, `IServiceInput`). Service merges keyboard + up to four gamepads. Editor input and rewind/debug keys still read `KeyboardManager` directly.

| Action | Keyboard | Gamepad |
|---|---|---|
| Rotate (signed) | D / Right / NumPad6 (CW), A / Left / NumPad4 (CCW) | Left stick X (analog, circular deadzone) |
| Jump (hold to charge; 100 ms press-buffer for landing) | W, Z, X, Space | A button |
| Menu direction | WASD / Arrows / NumPad 8/2/4/6 | D-pad, left stick (≥ 0.5 magnitude, larger axis wins) |
| Menu accept | Enter | A button |
| Rewind (Playing) | R | — |
| Debug overlay (Playing) | F6 | — |
| Back to title (pickers) | Esc | — |
| Editor (pan / add / export) | WASD/Arrows, mouse, X | — (editor stays keyboard-only) |

Keyboard accept is **Enter only** — the four jump keys do *not* double as menu accept. On gamepad, A intentionally serves both jump (Playing) and accept (menus).

Direction precedence is keyboard → D-pad → left stick; first non-null wins. `DirectionPressed` is edge-only — the same direction held across frames fires once. `RotateClockwise` is the signed source (keyboard ±1 or any pad's left-stick X) with the largest absolute magnitude.

Jump exposes both `JumpBuffered` (true if pressed within last 100 ms; survives cross-frame so a press just before landing converts to a charge on landing) and `JumpHeld` (current frame's button-down state — the charge state machine reads this).

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

## Kill Shapes

- `LevelShape.IsKill` flag marks a shape as lethal. Editor toggle: select shape, press **K**. Kill shapes render magenta in the editor (priority over the white/red winding-validity color).
- `LevelTerrain` builds kill shapes as **sensor** fixtures (`chain.IsSensor = true`) and tags the `Body.Tag` with the static sentinel `LevelTerrain.KillTag`. Sensors detect contact without solving manifolds, so the banana phases through and respawns rather than bouncing.
- `Playing.FixedUpdate` walks `Banana.Body.ContactList` after `CaptureSnapshot()` and checks each touching contact's other body for `Body.Tag == LevelTerrain.KillTag`. On hit: position reset to `StartPositionMeters`, velocities + rotation zeroed, rewind buffer cleared.
- Kill check runs **only on the forward branch** — never inside `StepRewind`. World isn't stepped during rewind so contacts wouldn't update anyway.
- Pattern for future contact-driven mechanics (collectibles, level-end, checkpoints): set a `Body.Tag` sentinel on the body in `LevelTerrain`, walk `ContactList` in `Playing.FixedUpdate` and reference-equality-check the tag. Decouples identification from sensor/solid choice.

## Not Yet Implemented

- More levels (currently one: StoneHenge)
- Level transitions / win condition
- Sound, music

## Known Quirks

- Banana inner crescent (concave side) is hollow — terrain corners can poke through visually since no fixture covers it. Intended jank / gameplay feature.
- Red 2×2 pixel debug dot rendered at body position (centroid). Useful for verifying sprite-pivot alignment.
- `MakeCircleTexture` helper in `Playing.cs` is unused. Available if circle visualization wanted.
