# Charge Jump

## Context
**Current behavior**: Jump is one-shot. `Playing.FixedUpdate` checks `PlayerInput.JumpBuffered` (a 100 ms buffer armed by any keyboard jump key or gamepad A press); if buffered and `Banana.TryGetJumpDirection` returns a direction, `Banana.Jump` fires a fixed-impulse jump along the summed contact normals. Rotation is unconstrained at all times. The 11-circle compound body is rigid and renders as a uniformly-scaled sprite.

**New behavior**: Holding the jump button charges a jump instead of firing immediately. While charging, the player cannot rotate, the banana visibly squishes into its current jump-direction surface (updating dynamically as the banana slides), and the body's contact friction is raised. A "tracked jump angle" is committed when charge begins and continuously matches the current jump direction while the banana is solidly on a surface within 45° of that tracked angle. If the banana loses contact, **or** the current jump direction deviates >45° from the tracked angle, a 1/2-second grace period begins: tracked angle and visual squish freeze, charge stops accumulating but is preserved, and one of three outcomes ends the grace — (a) the player releases jump, firing a coyote-style jump along the *frozen tracked angle* with whatever charge had accumulated; (b) the banana returns to a state whose jump direction is within 45° of the tracked angle, ending the grace and resuming charging with the tracked angle re-syncing to the current direction; or (c) the 1/2-second timer expires, in which case the jump is lost — no impulse fires and all charge dissipates. Releasing while still solidly charging on a valid surface fires the jump along the current direction. Jump impulse magnitude scales with how long the player held charge before release, between a minimum-fire threshold and a max-charge cap.

## Prerequisites
None.

## Scope
### In scope
- New `JumpHeld` (current-frame button-state) signal on `PlayerInput`.
- New constants in `PhysicsConstants` for charge timing (min-fire threshold, max-charge cap), grace duration (1/2 s), the 45° angle tolerance, the friction multiplier while charging, and the min/max impulse scale relative to today's `JumpImpulseMetersPerSecond`.
- Charge state machine in `Playing` (forward-frame only — no interaction with rewind), with explicit states: `Idle`, `ChargingOnSurface`, `ChargingInGrace`. Tracked angle, charge-elapsed seconds, grace-remaining seconds, and last-known surface flag carried as fields.
- Rotation block while charging (both `OnSurface` and `InGrace` states): `PlayerInput.RotateClockwise` is ignored; `Banana.Body.AngularVelocity` is left to physics damping.
- Friction modulation: while charging (either sub-state), banana fixtures get a raised friction; on exit (any way charge ends), friction restores. Single source-of-truth: a helper method on `Banana` that walks `Body.FixtureList` and sets `Friction`.
- Visual squish: `Playing.DrawBanana` renders the banana with non-uniform scale — squashed along the tracked-angle axis, depth scaled with charge level. While `OnSurface`, squish tracks the current jump direction continuously; while `InGrace`, squish freezes at the value held when grace began.
- New `Banana.ApplyJumpImpulse(Vector2 direction, float speedMetersPerSecond)` (name flexible — see Open Decisions) that fires a directional impulse with caller-supplied magnitude, replacing the existing parameterless `Banana.Jump()`. The "find a surface" decision moves to `Playing` since charge needs the direction earlier than impulse-time.
- Grace-period coyote behavior: release-during-grace fires the impulse along the frozen tracked angle even if the banana has no current contacts.
- Buffered-press → charge-on-landing: keyboard press while airborne arms the existing 100 ms buffer; on landing, if the button is still held, the buffered press converts into the start of a fresh charge. A press that was already released by the time of landing does nothing.

### Out of scope
- Sound / particle effects for charge, charge-fail, or jump fire.
- Charge-level HUD readout (a meter, color shift, etc.). The visual squish + freeze itself is the player's feedback for now.
- Physical body deformation. The 11-circle compound body remains rigid; squish is render-only (sprite scale).
- Rewind interaction beyond "no charge state survives entering rewind". `BeginRewind` should reset charge state to `Idle` and restore friction; capturing/replaying mid-charge state is not supported.
- Editor-side or level-data changes. Charge mechanic is global, not per-level.
- Re-tuning the existing rotate or jump-impulse base constants. This ticket *adds* multipliers/thresholds; it does not change `JumpImpulseMetersPerSecond` or `RotateAngularVelocityRadiansPerSecond`.

## Relevant Docs & Anchors
- `docs/REFERENCE.md §Physics §Jump` — current jump model (one-shot, contact-summed direction). The new model still uses `TryGetJumpDirection` as the *source* of the jump direction; only the timing and magnitude change.
- `docs/REFERENCE.md §Input Bindings` — current `PlayerInput` surface (`JumpBuffered`, `TryConsumeJump()`, `RotateClockwise`). This ticket adds `JumpHeld` alongside.
- **Code anchors**:
  - `Banana.TryGetJumpDirection` — direction source-of-truth for charge tracking and impulse fire alike. Do not duplicate the contact-walking logic.
  - `Banana.Jump` — to be replaced with a parameterised impulse method (caller supplies direction + magnitude).
  - `Banana.IsGrounded` — the established `Body.ContactList` walking pattern; the new "currently on a valid surface" check follows the same shape.
  - `Banana` constructor — where fixture friction is initially set. Use the same iteration to build the runtime friction-set helper.
  - `Playing.FixedUpdate` — where the charge state machine lives, sequenced **before** `World.Step` (input → state machine → step → snapshot, matching the existing rotate/jump ordering).
  - `Playing.DrawBanana` — current rotation+offset render. Non-uniform scale support replaces the `Graphics.DrawPictureRotatedAndScaled(..., 1f, ...)` call site.
  - `PlayerInput.Input` — extend the per-frame state snapshot to also expose a `JumpHeld` (currently-down) value alongside the existing edge / buffer machinery.
  - `Playing.BeginRewind` / `EndRewind` — grace-state reset hooks (also `Respawn`).
- **Analogue ticket**: `docs/tickets/complete/2026-05-02 gamepad-support.md` — pattern for adding signals to `PlayerInput` (where to put state, how to sample keyboard vs. all four pads, how `Playing` consumes).

## Constraints & Gotchas
- **Non-uniform scale via SpriteBatch**: `Graphics.DrawPictureRotatedAndScaled` takes a single uniform `float` scale — it cannot squish along one axis. Drop to `Graphics.SpriteBatch.Draw(...)` directly for the banana, mirroring the `DrawLine` helper in `Playing.cs` (which also calls `SpriteBatch.Draw` directly for non-uniform line scale). Look up the source `Texture2D` via `Graphics.Pictures["Banana"]` (PPM keyword — verify in PPM source if uncertain).
- **Squish + centroid offset**: the existing draw compensates for sprite-center vs. body-centroid offset (`BananaSpriteCenterMinusCentroidPixels`, rotated then added to body screen position). Non-uniform scaling re-introduces this geometry: the centroid-to-sprite-center offset must be scaled by the same factors as the sprite *before* being rotated and added. Get the math right or the banana will pop sideways while squishing. The simplest workable form: draw with `origin = BananaSpriteCenterPixels - (centroid_offset)` so SpriteBatch's own pivot is the centroid, then apply scale + rotation around that origin.
- **Squish axis vs. rotation axis**: squish should compress *along* the surface normal (i.e., flatten toward the surface), not always vertically in screen space. Implementation requires composing two rotations: world-rotation of the body (the banana's own spin) plus the offset between world-axes and squish-axis (which is the tracked-angle's perpendicular). Concretely: the squish factor maps to the sprite-local axis aligned with `tracked_angle - body.Rotation`. SpriteBatch's per-call rotation only handles one of these — the cleanest path is to bake the squish into a `Vector2 scale` and let the `body.Rotation` handle world-orientation, accepting that the squish is anchored in *banana-local* space. If the player's intuition expects squish anchored in *world* space (i.e., always perpendicular to the floor regardless of banana spin), implementer should fall back to a custom shader or direct vertex math. Default: banana-local squish — much simpler, and the banana is a rigid body so the surface normal at a contact circle is roughly aligned with banana-local axes anyway. Surface this in Open Decisions.
- **Friction restore must be idempotent**: charge can end through five paths — release on-surface (fires), release during grace (fires), grace timer expiry (lost), surface-angle re-acquisition during grace (continues), and rewind/respawn pre-emption. All paths that exit charging entirely (i.e., return to `Idle`) must restore the original fixture friction. Centralise this in a single helper called from a single state-exit point, not scattered across each transition.
- **Tracked angle when grace starts due to "no contacts"**: there is no current direction to compare *against* on the frame contact is lost — the tracked angle is the value from the previous frame (last frame on a surface). Capture it before the contact-loss transition. This is implicit in "freeze the tracked angle on grace entry" but worth calling out to avoid a one-frame off-by-one where tracked is stale.
- **Angle-delta math**: 45° tolerance is on the angle between two unit vectors, not coordinate-system-dependent. Use `Vector2.Dot(tracked, current) >= cos(45°)` (≈ 0.7071) for a cheap, branchless comparison. Both vectors should be unit-length (they are — `TryGetJumpDirection` normalizes). No `Atan2` needed.
- **Grace end via re-contact must re-sync tracked angle**: per the user's example — when grace ends because the banana slid back into a 25° delta, the tracked angle *updates to the current direction* (from 80° → 105° in the example). Do not keep the original 80° as tracked — the player has implicitly committed to the new resting angle. Test plan covers this.
- **Buffered-jump on landing**: keep the existing 100 ms buffer mechanism for one specific case — the player taps jump just before landing. On landing-while-buffer-armed-AND-button-still-held, treat the landing frame as the start of a fresh charge (charge elapsed = 0) and consume the buffer. If the button is no longer held by landing, drain the buffer without action — no charge starts.
- **Rewind reset**: `BeginRewind` (and `Respawn`) must reset charge to `Idle` *and* restore friction, since the body's fixtures persist across rewind/respawn. Failing to restore friction means a respawned banana has charge-friction baked in indefinitely.
- **`World.Step` ordering**: keep state-machine update *before* `World.Step` (matches today's input → step → capture flow). Friction changes apply via fixture mutation; Aether picks them up on subsequent contact updates within the next step. Don't reorder so that friction changes happen after step — they'd be a frame late.
- **`Banana.Jump()` removal**: the existing `bool Jump()` (parameterless, returns whether a contact was found) was used only by `Playing` (verified via grep). Replacing it with `ApplyJumpImpulse(direction, speed)` requires no external migration.

## Open Decisions
1. **Charge-curve shape & timing** — minimum hold before release fires anything (default: 100 ms — short taps don't fire) and max-charge cap (default: 800 ms — feels reachable without being slow). Power scaling between min and max: linear (default), ease-out, or stepped. Implementer picks numbers that feel right during manual testing.
2. **Min-fire impulse vs. base impulse** — at the minimum-charge threshold, jump impulse = `JumpImpulseMetersPerSecond × MinScale`. At max charge = `JumpImpulseMetersPerSecond × MaxScale`. Defaults: `MinScale = 0.4f`, `MaxScale = 1.6f`. Implementer tunes by feel.
3. **Friction multiplier while charging** — default `2×` the base `BananaFriction = 0.7` (so charge-friction = 1.4). Implementer tunes; if the banana feels stuck enough that mid-charge slides become impossible, the angle-grace mechanic loses its reason to exist.
4. **Squish maximum depth** — at max charge, sprite is squashed to ~70% of full height along the squish axis (default). Implementer tunes — visible enough to read at internal-resolution 640×360 but not so much the banana looks broken.
5. **Squish reference frame — banana-local vs. world** — see Constraints. Default: banana-local (cheaper, rigid-body-native). Flip only if playtesting reveals the squish looks wrong when the banana is rotated.
6. **Squish easing** — linear with charge, or ease-out (most squish in the first ~half of charge). Default: linear; matches "obvious feedback that charge is building".
7. **Rotation block during grace too?** Default: yes — grace is *part of* the charging state, and unblocking rotation mid-grace would let the player spin out of the 45° window deliberately. If playtesting shows this feels punishing, the implementer can flip it to "rotation re-enabled during grace only"; the brief is silent.
8. **Charge accumulation freeze vs. continue during grace** — default: freeze. Matches "the jump is preserved". Continuing accumulation during grace would mean the player can charge mid-air for free, which subverts the "must be on a surface" requirement. Don't surface as a knob unless the implementer hits a reason to flip.
9. **`Banana` API name** — `ApplyJumpImpulse(Vector2 direction, float speed)` vs. `Jump(Vector2 direction, float speed)` (overload-style). Default: rename to `ApplyJumpImpulse` to make the parameterised nature explicit; the existing parameterless `Jump()` had different semantics (find-direction internally). Implementer's call.

## Acceptance Criteria
- [ ] `PlayerInput` exposes a `bool JumpHeld` property that is true on any frame when at least one jump-source input is currently down (W/Z/X/Space on keyboard, A button on any connected gamepad). This is distinct from `JumpBuffered` (which remains for landing-conversion behavior).
- [ ] `PhysicsConstants` includes new public constants for: `MinChargeSecondsToFire`, `MaxChargeSeconds`, `LostContactGraceSeconds` (= 0.5f), `MaxAngleDeviationDegrees` (= 45f) — with one of those two angle/dot constants exposing the precomputed `cos(MaxAngleDeviationDegrees * π / 180)`, `ChargingFrictionMultiplier`, `MinJumpImpulseScale`, and `MaxJumpImpulseScale`. Names flexible — match the project's existing `JumpImpulseMetersPerSecond`-style verbosity.
- [ ] `Banana` exposes a method to fire a directional impulse with caller-supplied magnitude (per Open Decision #9). The old parameterless `bool Jump()` is removed; no remaining callers (verified via grep).
- [ ] `Banana` exposes a method to set fixture friction at runtime — applied uniformly across all of `Body.FixtureList`. Used by `Playing` to swap between base and charging friction.
- [ ] `Playing` carries a charge state with at least three observable phases: not-charging (`Idle`), charging-on-surface, and charging-in-grace. Charge-elapsed time and tracked direction are tracked across frames.
- [ ] In the `Idle → ChargingOnSurface` transition: charge begins only when `JumpHeld == true` and `TryGetJumpDirection` returns a valid direction (i.e., banana has at least one contact). Charge-elapsed seconds reset to 0; tracked direction = current jump direction; fixture friction multiplier applied.
- [ ] While `ChargingOnSurface`: tracked direction is updated each frame to the current jump direction (matches user's example — sliding while charging continuously updates the commit). Charge-elapsed accumulates each fixed step. Rotation input is ignored; `Banana.Body.AngularVelocity` is not driven by `PlayerInput.RotateClockwise`. The banana's draw applies non-uniform squish along the tracked-direction axis, with depth proportional to charge-elapsed (capped at `MaxChargeSeconds`).
- [ ] `ChargingOnSurface → ChargingInGrace` transition fires when either (a) `TryGetJumpDirection` returns false (no contacts), or (b) the dot product between the previous-frame tracked direction and the current jump direction falls below `cos(45°)` (i.e., deviation > 45°). On entry, tracked direction is *frozen* at the previous-frame value, charge-elapsed pauses (does not increment), grace timer initialised at `LostContactGraceSeconds`, friction stays raised, and the squish in `DrawBanana` reads from the frozen state (not the current direction).
- [ ] `ChargingInGrace → ChargingOnSurface` transition fires when `TryGetJumpDirection` returns a valid current direction whose dot product with the frozen tracked direction is ≥ `cos(45°)`. On exit-back-to-charging, charge-elapsed *resumes* incrementing, tracked direction *updates to the current direction* (the example: 80° tracked → re-contact at 105° → new tracked = 105°), grace timer is cleared.
- [ ] `ChargingInGrace → fire (release during grace)`: when `JumpHeld` becomes false during grace, an impulse fires along the *frozen tracked direction* with magnitude scaled by `charge-elapsed` (mapped through `MinJumpImpulseScale`/`MaxJumpImpulseScale`). Fire happens regardless of current contact state. After fire, friction restores, state returns to `Idle`.
- [ ] `ChargingInGrace → Idle (lost)`: when the grace timer reaches 0, no impulse fires. Friction restores. Charge-elapsed and tracked-direction reset.
- [ ] `ChargingOnSurface → fire (release on surface)`: when `JumpHeld` becomes false while still charging on a valid surface AND `charge-elapsed >= MinChargeSecondsToFire`, an impulse fires along the *current tracked direction* with magnitude per the scaling curve. Friction restores. State returns to `Idle`.
- [ ] `ChargingOnSurface → Idle (release below min)`: if `JumpHeld` becomes false but `charge-elapsed < MinChargeSecondsToFire`, no impulse fires. Friction restores. State returns to `Idle`. (This prevents tap-firing — short taps are no-ops.)
- [ ] Buffered-press handling: a press while airborne arms `JumpBuffered` per existing logic; on landing-with-buffer-armed-AND-`JumpHeld`, the next frame's `Idle → ChargingOnSurface` transition treats the landing as charge-start (charge-elapsed = 0, buffer drained). A press that was already released by landing does nothing.
- [ ] `BeginRewind` and `Respawn` (the kill-shape respawn path) reset charge to `Idle` and restore fixture friction. No charge state survives across either.
- [ ] During rewind playback (`IsRewinding == true`), the charge state machine does not run — neither charge-elapsed nor grace timer advances, and no impulse fires.
- [ ] Squish render: `Playing.DrawBanana` uses non-uniform sprite scale derived from `(charge-elapsed / MaxChargeSeconds, tracked-direction)` while charging, and identity (uniform 1f) otherwise. The centroid-to-body-position alignment remains correct under squish (the red debug dot still sits at the visual centroid, not floating off into space).

## Implementation

### 1. Add `JumpHeld` to `PlayerInput`
In `BananaTime/Input/PlayerInput.cs`, add a `bool JumpHeld { get; private set; }` property and compute it inside `Input(GameTime)` alongside the existing `JumpNewlyPressed` check: true if any of `JumpKeys` is currently down on the keyboard, OR any connected pad's A button is currently pressed. Same source set as `JumpNewlyPressed` but state-based (no edge detection). Place the assignment near the other property updates at the bottom of `Input`. No constructor changes needed.

### 2. Add charge-related constants to `PhysicsConstants`
In `BananaTime/Physics/PhysicsConstants.cs`, append (with the rest of the public consts):
- Charge timing — `MinChargeSecondsToFire`, `MaxChargeSeconds`.
- Grace — `LostContactGraceSeconds = 0.5f`.
- Angle tolerance — `MaxJumpAngleDeviationCosine = (float)System.Math.Cos(45 * π / 180)` (precompute as a `static readonly float` since `Math.Cos` isn't constexpr in C#; or compute in the static constructor next to the centroid math).
- Friction — `ChargingFrictionMultiplier`.
- Impulse-scale range — `MinJumpImpulseScale`, `MaxJumpImpulseScale`.

Use defaults from Open Decisions; comment each constant with a one-line "what it tunes" hint matching the existing style.

### 3. Replace `Banana.Jump()` with parameterised impulse
In `BananaTime/Physics/Banana.cs`:
- Delete the existing `bool Jump()` method.
- Add the replacement (per Open Decision #9 default): `public void ApplyJumpImpulse(Microsoft.Xna.Framework.Vector2 direction, float speedMetersPerSecond)`. Body: `Body.ApplyLinearImpulse((direction * (speedMetersPerSecond * Body.Mass)).ToAether())`. The caller now owns the "do we have a direction?" decision via `TryGetJumpDirection`.
- Add a `public void SetFixtureFriction(float friction)` helper that walks `Body.FixtureList` and assigns. Initial friction in the constructor stays as today (`PhysicsConstants.BananaFriction`); the new helper is for runtime swap.

### 4. Charge state machine on `Playing`
In `BananaTime/GameStates/Playing.cs`:
- Add private fields:
  - An enum `ChargeState { Idle, OnSurface, InGrace }` and a `_chargeState` field (default `Idle`).
  - `float _chargeElapsedSeconds`.
  - `Vector2 _trackedJumpDirection` — only meaningful in non-`Idle` states; freely overwritten on `Idle → OnSurface`.
  - `float _graceRemainingSeconds` — only meaningful in `InGrace`.
- Add private helpers:
  - `void EnterCharging(Vector2 initialDirection)` — sets state to `OnSurface`, zeros elapsed, stores tracked, calls `Banana.SetFixtureFriction(BananaFriction × ChargingFrictionMultiplier)`.
  - `void ExitChargingToIdle()` — sets state to `Idle`, zeros all charge fields, calls `Banana.SetFixtureFriction(BananaFriction)`. **Single source-of-truth** for the friction restore (called from every charge-end path).
  - `float ComputeChargeImpulseSpeed()` — clamps `_chargeElapsedSeconds` to `[MinChargeSecondsToFire, MaxChargeSeconds]`, lerps `MinJumpImpulseScale → MaxJumpImpulseScale` linearly (per Open Decision default), returns `JumpImpulseMetersPerSecond × scale`.
- Restructure `FixedUpdate`'s forward branch (the part that runs when not rewinding) to a new shape:

  1. Drain or examine `JumpBuffered` only at the moment of `Idle → OnSurface` transition (covered below).
  2. Run charge state machine — see step 5.
  3. Apply rotate ONLY if `_chargeState == Idle`. Block rotate input in both charging sub-states.
  4. `World.Step(...)`, `CaptureSnapshot()`, kill-shape check (existing).

### 5. State transitions per fixed step
Within the state-machine call, branch on `_chargeState`:

- **`Idle`**:
  - If `PlayerInput.JumpHeld == false`, do nothing (also drop any stale `JumpBuffered` if needed).
  - Else (held), call `Banana.TryGetJumpDirection(out var dir)`. If `true`, call `EnterCharging(dir)` and consume any pending `JumpBuffered` (so the buffer doesn't double-count). If `false` (airborne), no transition — let the existing 100 ms buffer ride; on the frame the banana lands AND `JumpHeld` is still true, this branch will succeed.
  - Note: the buffered-press-on-landing case doesn't need a separate path. Holding through landing causes this Idle branch to enter charging the moment a contact appears. The buffer's only role now is letting `JumpHeld == true` correlate with a press registered up to 100 ms before landing — but `JumpHeld` is *current*, so as long as the player hasn't released, the buffer mechanism is implicit. Drain `JumpBuffered` at `EnterCharging` time for hygiene.

- **`OnSurface`**:
  - If `PlayerInput.JumpHeld == false`:
    - If `_chargeElapsedSeconds >= MinChargeSecondsToFire`: fire `Banana.ApplyJumpImpulse(_trackedJumpDirection, ComputeChargeImpulseSpeed())`. Then `ExitChargingToIdle()`.
    - Else: `ExitChargingToIdle()` (no fire — sub-threshold release is a tap, not a jump).
    - Done.
  - Else (still held):
    - Compute `Banana.TryGetJumpDirection(out var current)`.
    - If `false` (no contacts) → enter grace: tracked stays at previous-frame value (already in `_trackedJumpDirection`), `_graceRemainingSeconds = LostContactGraceSeconds`, `_chargeState = InGrace`. Charge does not increment this frame.
    - Else if `Vector2.Dot(_trackedJumpDirection, current) < MaxJumpAngleDeviationCosine` (deviation > 45°) → enter grace: same as above (tracked frozen at previous-frame value).
    - Else (still on a valid surface, within 45°): increment `_chargeElapsedSeconds` by `FixedTimestepSeconds`, *then* update `_trackedJumpDirection = current`. Order matters subtly — capturing the pre-update tracked is what would freeze on a grace-entry next frame, but since we only enter grace when *current* deviates, we can freely sync after.

- **`InGrace`**:
  - Decrement `_graceRemainingSeconds` by `FixedTimestepSeconds`.
  - If `PlayerInput.JumpHeld == false`: fire coyote-style — `Banana.ApplyJumpImpulse(_trackedJumpDirection, ComputeChargeImpulseSpeed())` (unconditional on min-charge — release during grace always fires per the user's clarification). Then `ExitChargingToIdle()`. Done.
  - Else if `_graceRemainingSeconds <= 0`: jump lost — `ExitChargingToIdle()` (no fire). Done.
  - Else: check for re-contact recovery. `Banana.TryGetJumpDirection(out var current)`. If `true` AND `Vector2.Dot(_trackedJumpDirection, current) >= MaxJumpAngleDeviationCosine`: end grace, `_trackedJumpDirection = current` (resync per the example), `_chargeState = OnSurface`, `_graceRemainingSeconds = 0`. Charge does not increment this frame; resumes next frame on the OnSurface branch.
  - Else: stay in grace, charge frozen, tracked unchanged.

### 6. Rotation block
In the existing `FixedUpdate` block where `Banana.Rotate(rotate)` is called, gate the call: only apply if `_chargeState == ChargeState.Idle`. Don't zero `Body.AngularVelocity` explicitly — let Aether's existing `AngularDamping = 0.2f` decay it naturally, matching how "neither key held" already behaves.

### 7. Rewind / respawn integration
- In `Playing.BeginRewind`: if `_chargeState != Idle`, call `ExitChargingToIdle()` before flipping `IsRewinding = true`. No partial-state survives entering rewind.
- In `Playing.Respawn`: same — call `ExitChargingToIdle()` before resetting position. The friction restore matters even though respawn doesn't physically move the fixture friction state (the body persists across respawn).
- During rewind (`if (IsRewinding) { StepRewind(); return; }`), the state machine never runs — that's already enforced by the early return. No changes needed there beyond the `BeginRewind` reset.

### 8. Visual squish in `DrawBanana`
Refactor `DrawBanana` to compute non-uniform scale and route through `SpriteBatch.Draw` directly:

- Compute scale: when not charging, `scale = Vector2.One`. When charging (either sub-state), depth = `Lerp(0, MaxSquishDepth, Clamp01(_chargeElapsedSeconds / MaxChargeSeconds))`. Squish vector along the tracked-direction axis: `scale = (1f, 1f - depth)` *in banana-local space* if `MaxSquishDepth = 0.3f` means "squashed to 70% along squish axis at max charge" (per Open Decision #4). The scale tuple maps sprite-local X (banana long axis) to 1f always and sprite-local Y (banana short axis) to `1 - depth` — but only if the tracked direction's banana-local angle aligns with the body's natural compression axis. Otherwise (per Open Decision #5 default — banana-local), interpret the tracked direction in banana-local frame: `local_dir = Rotate(_trackedJumpDirection, -body.Rotation)`; squish factor along `local_dir`'s perpendicular gets the `(1 - depth)` scale.
- Draw call: replace `Graphics.DrawPictureRotatedAndScaled("Banana", ...)` with a direct `Graphics.SpriteBatch.Draw(banana_texture, ...)` using:
  - `position = banana_screen_position`,
  - `sourceRectangle = null`,
  - `color = Color.White`,
  - `rotation = body.Rotation` (composed with any extra rotation needed for axis-aligned squish — see below),
  - `origin = sprite-pivot in pixel space such that the body centroid lands at draw position` (compute from `BananaSpriteOriginPixels` — this *is* the centroid in sprite-local pixels, which is what the SpriteBatch `origin` parameter expects),
  - `scale = (Vector2)scale`,
  - `effects = SpriteEffects.None`,
  - `layerDepth = 0f`.
- The `origin` choice means SpriteBatch rotates and scales around the *centroid*, which removes the `BananaSpriteCenterMinusCentroidPixels` offset compensation — that compensation was only needed because `DrawPictureRotatedAndScaled` rotates around sprite-center. Verify the red debug dot still aligns with the visual centroid after the change.
- For squish anchored along the tracked direction (banana-local default per Open Decision #5): if the implementer wants the squish axis to match the tracked-direction axis exactly (rather than always along banana-local Y), they can compose an extra rotation `delta = atan2(local_dir.Y, local_dir.X) - π/2` into the `rotation` parameter and inverse-rotate the scale vector. This is the trickiest math in the ticket — the simpler shortcut "always squish along banana-local Y" is acceptable for v1 and the scope's "implementer's call" applies. Defer real-axis tracking to a follow-up if the simple approach reads poorly.

### 9. Smoke check + tuning pass
Build, run, exercise. Tune the eight Open Decision values (charge times, friction multiplier, squish depth, impulse range) until the mechanic feels readable and fair. Update `docs/REFERENCE.md §Physics §Jump` to describe the new charge model in the `### Jump` block. The Input Bindings table needs no change (jump key set is unchanged) but a one-line note that "jump fires on release after a charge" would orient future readers.

## Test Plan
- [ ] `dotnet build` from `BananaTime/` succeeds with no new warnings.
- [ ] On the StoneHenge level: hold jump on flat ground for a brief moment (< `MinChargeSecondsToFire`), release — banana does not jump. Visible squish was small and reset cleanly.
- [ ] Hold jump on flat ground for `MinChargeSecondsToFire` to `MaxChargeSeconds`, release — banana jumps; impulse magnitude scales visibly with hold time. At max charge, impulse feels stronger than the pre-ticket fixed jump; at min charge, weaker.
- [ ] While holding jump, attempt to rotate (A/D, left stick). Banana does not respond — angular velocity decays under damping. Release jump (without firing — sub-min) — rotation responds again.
- [ ] Visual squish: hold jump on flat ground, observe banana squish toward the floor. Squish depth increases with hold time.
- [ ] Slide test (the user's example): start charging on a slope; while holding, slide the banana onto a steeper slope so the surface normal shifts. While the deviation is < 45°, the squish smoothly tracks the new surface direction and tracked angle continuously updates.
- [ ] Sharp-discontinuity test: charge on a near-flat surface, then drive the banana into a near-vertical wall (so jump direction snaps from "up" to "sideways" by > 45°). Squish freezes; charge stops accumulating; HUD-internal grace timer counts down (verify by waiting >1/2s without re-acquiring valid surface and confirming the jump is lost without firing).
- [ ] Re-contact test: charge on a flat surface (≈ 90° / "up"), then push the banana off the edge so contact is lost. Within 1/2s, get the banana back into contact with a roughly-similar surface. Confirm grace ends, charging resumes, tracked angle re-syncs to the new contact direction (release after re-sync — the jump should fire along the *new* direction, not the original 90°).
- [ ] Coyote release: charge on a surface, push the banana off so contact is lost, release jump within the 1/2s grace. Banana fires a jump along the *frozen tracked angle* — even though it has no current contact. Power scales with charge-elapsed at release time.
- [ ] Grace expiry: charge on a surface, push the banana off, release nothing. Wait >1/2s. No jump fires. Banana falls under gravity from then on.
- [ ] Buffer + landing: in mid-air (e.g., off a ledge), tap-and-hold jump 100 ms before landing. On landing, charging starts cleanly (charge-elapsed = 0 at the landing frame). Release after >MinCharge — jump fires.
- [ ] Buffer + tap: in mid-air, tap-and-release jump 100 ms before landing. On landing, no charge starts. (`JumpHeld == false` by then.)
- [ ] Friction sanity: with the banana at rest on a slight slope, hold jump and watch — banana should resist sliding more than it does without charge. Release — banana resumes sliding at the original rate.
- [ ] Rewind reset: charge to roughly half max, hold, then press R (assuming buffer is full). On rewind start, friction restores to base (verify by inspection — the banana's slide behavior post-rewind is identical to pre-charge).
- [ ] Kill-shape respawn: start charging, drive into a kill shape. Banana respawns at start position; charge state is `Idle`; friction is base. Holding jump again starts a fresh charge.
- [ ] Regression: with the buffer system intact, the original "tap jump just before landing → fires on landing" behavior is *gone* (jump now requires a hold). Confirm no test depends on the old instant-fire-on-landing behavior — it has been replaced by hold-on-landing-to-charge.
- [ ] Debug overlay (F6): existing grounded / linVel / angVel readouts still work during charging. The red centroid dot still aligns with the squished sprite's apparent centroid.

## Learnings

### Architectural decisions
- **Open Decisions resolved with listed defaults**, with two notable deviations called out below: tuning numbers were taken straight from the ticket (`MinChargeSecondsToFire = 0.1`, `MaxChargeSeconds = 0.8`, `MinJumpImpulseScale = 0.4`, `MaxJumpImpulseScale = 1.6`, `LostContactGraceSeconds = 0.5`, `ChargingFrictionMultiplier = 2`, `MaxSquishDepth = 0.3`) — final tuning is a manual playtesting pass.
- **Open Decision #5 — squish reference frame**: implemented banana-local (default). `scale = (1f, 1f - depth)` is interpreted as "squish along sprite-local Y", and `body.Rotation` rotates the squished sprite around the centroid. Cheap, rigid-body-native, and visually plausible because banana-local Y roughly aligns with the surface normal at body rotation 0 (which is how the banana rests on flat ground). Worth revisiting if playtesting shows the squish reads wrong when the banana is rotated.
- **Open Decision #9 — API name**: chose `ApplyJumpImpulse(Vector2 direction, float speedMetersPerSecond)`. Makes the parameterised nature explicit and contrasts with the old `Jump()` (which had find-direction-internally semantics).
- **Charge accumulation cap at `MaxChargeSeconds`**: capped during accumulation as well as during impulse-speed compute. Belt-and-suspenders — `_chargeElapsedSeconds` never grows unbounded if the player holds for many seconds.
- **Friction restore is centralised in `ExitChargingToIdle`**: matches the ticket's "single source-of-truth" guidance. Every charge-end path (release-on-surface fire, release-during-grace fire, grace-timer expiry, rewind preempt, respawn preempt) routes through this one helper. Releases happen inside the state machine; rewind/respawn check `_chargeState != Idle` and call out to it.
- **Unused `BananaSpriteCenterMinusCentroidPixels` and `BananaSpriteCenterPixels` removed from `PhysicsConstants`**: the new `SpriteBatch.Draw` path uses `BananaSpriteOriginPixels` (the centroid in sprite-local pixels) directly as the `origin` parameter, which removes the need for the sprite-center pivot compensation entirely. Comment on `BananaSpriteOriginPixels` updated to document its new draw-time role.

### Problems encountered / interesting tidbits
- **`SpriteBatch.Draw` origin semantics**: setting `origin = BananaSpriteOriginPixels` (the centroid) means SpriteBatch rotates *and* scales around the centroid, then places that origin at the `position` parameter. That naturally extends to non-uniform scale: the centroid stays fixed under squish, with no extra geometry. Worth remembering — the offset-rotated-sprite-center math the old draw used is unnecessary once you set `origin` correctly.
- **`MathHelper.Lerp` / `MathHelper.Clamp`** live in `Microsoft.Xna.Framework`, already imported in `Playing`. `MathF.Round` for pixel-snap needs `using System;` — Playing didn't have it before.
- **`TryGetJumpDirection` returns false only when `Body.ContactList` has zero touching contacts** — pinch / cancellation cases still return true with a fallback `(0, -1)`. So the OnSurface→Grace transition on "no contacts" actually means "zero touching contacts", not "ambiguous direction".

### Workarounds / limitations
- **Squish axis is banana-local, not world-aligned**: when the banana is rotated mid-air (charging on a tilted surface), the squish does not stay perpendicular to the actual surface — it stays perpendicular to the banana's long axis. This is the `Open Decision #5` default and matches the ticket's "v1 acceptable" guidance. Flip if playtesting demands.
- **Holding jump through rewind starts a fresh charge on EndRewind**: `EndRewind` drains `JumpBuffered`, but `JumpHeld` is poll-based — if the player still has the button down on the first post-rewind frame, the state machine will enter `OnSurface` immediately. Acceptable: the player can release.
- **`BananaSpriteWidthPixels` / `HeightPixels` left as documentation constants**: now unused in code (their only callers were in the removed `BananaSpriteCenterPixels` chain), but kept as artist source-of-truth annotations. Cheap to keep; communicate intent.

### Related areas affected
- `Banana.Jump()` (parameterless `bool`) **removed** — replaced with `ApplyJumpImpulse(Vector2, float)` + `SetFixtureFriction(float)`. Only caller was `Playing.FixedUpdate`, migrated to the new API.
- `Playing.DrawBanana` rewritten to use `SpriteBatch.Draw` directly (mirroring the existing `DrawLine` helper) since `Graphics.DrawPictureRotatedAndScaled` is uniform-scale only.
- `PhysicsConstants` lost `BananaSpriteCenterPixels` + `BananaSpriteCenterMinusCentroidPixels` (dead after the draw refactor); gained the eight charge-related constants.
- `docs/REFERENCE.md` §Physics §Jump fully rewritten to describe the charge model; constants list expanded; Input Bindings table updated to note "hold to charge".

### Rejected alternatives
- **World-aligned squish** (Open Decision #5 alternative): more correct visually but requires composing an extra rotation into SpriteBatch's single rotation slot or dropping to a custom shader. v1 banana-local is acceptable; revisit if it reads poorly.
- **Charge accumulation continues during grace** (Open Decision #8 alternative): rejected — would let the player charge mid-air for free, defeating the "must be on a surface" intent.
- **Rotation re-enabled during grace** (Open Decision #7 alternative): rejected — grace is part of charging; allowing rotation would let players spin out of the 45° window deliberately. Defaulted to "rotation locked across both sub-states".
- **Single-frame grace recovery and charge increment**: did not increment `_chargeElapsedSeconds` on the recovery frame, matching the ticket. Recovery frame transitions to `OnSurface`; the next frame's `OnSurface` branch is what resumes accumulation. Keeps the state machine's single-update-per-frame discipline clean.
