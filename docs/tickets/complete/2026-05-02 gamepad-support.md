# Gamepad Support

## Context
**Current behavior**: Game and menus poll `KeyboardManager` directly — no gamepad input, scattered key arrays (`Playing.RotateCWKeys`, `Menu.PrevKeys`, etc.), and jump uses a single-frame `JumpQueued` bool with no buffer.

**New behavior**: A new singleton service abstracts player input across keyboard + all connected gamepads. Title screen, level picker, and saved-level picker drive selection from `Direction?` + accept; in-game rotation and jump come from the service (rotation is a signed analog `[-1, 1]`, jump has a 100 ms input buffer). The level editor is **unchanged** — it continues to read the keyboard directly for editing-specific keys.

## Prerequisites
None.

## Scope
### In scope
- New service in `BananaTime` that implements `IServiceInput` and registers automatically.
- Service tracks current + previous `KeyboardState` and the four `GamePadState`s (`PlayerIndex.One`–`Four`), then exposes four logical inputs:
  1. Signed clockwise rotation strength `[-1, 1]`.
  2. Jump buffered within last 100 ms (with a way to consume the buffer).
  3. Newly-pressed `Direction?` (one-shot per press).
  4. Newly-pressed accept (one-shot, no buffer).
- A `Direction` enum (`Up`, `Left`, `Down`, `Right`).
- Menus (`TitleScreen`, `LevelPicker`, `SavedLevelPicker`, and the `Menu` helper they share) drive selection movement from the new service's direction + accept.
- `Playing` drives banana rotation from the new service's rotate value, and jumps from the buffered jump.
- Player-facing HUD/footer hint strings updated where the listed control names diverge from what the new service accepts (e.g., `Playing`'s "A/D rotate, Space jump, R rewind" footer).

### Out of scope
- Level editor input (`LevelEditor.cs`) — stays on `KeyboardManager` directly. Designer-only state, not part of player-facing input.
- Rebinding UI / settings menu / per-player profiles.
- Vibration / rumble.
- Triggers, right stick, shoulder buttons (no game action uses them today; don't add API surface for them).
- Keyboard-only `R` rewind key — leave it on `KeyboardManager` in `Playing`. The brief did not list rewind among the four logical inputs and PR scope should match the brief.
- F6 debug-info toggle in `Playing` — also stays on `KeyboardManager`.
- Hot-plug detection beyond what `GamePad.GetState` already does each tick. Polling all four slots every tick handles connect/disconnect implicitly.

## Relevant Docs & Anchors
- `docs/REFERENCE.md §Input Bindings` — current keyboard map (will need an update after this ticket lands).
- **PlayPlayMini source-of-truth**: `D:\Development\C-sharp\PlayPlayMini` (per `memory/reference_ppm_source.md`).
  - `BenMakesGames.PlayPlayMini\IServiceInput.cs` — interface to implement (`void Input(GameTime)`).
  - `BenMakesGames.PlayPlayMini\Attributes\DI\AutoRegister.cs` — singleton-by-default registration attribute.
  - `BenMakesGames.PlayPlayMini\Services\KeyboardManager.cs`, `MouseManager.cs` — exemplars of the same pattern (private `Previous*State` + `*State` swapped each `Input` call).
- **Code anchors**:
  - `Playing.Input` (the `RotateCWKeys` / `RotateCCWKeys` / `JumpKeys` reads + `JumpQueued` set) and `Playing.FixedUpdate` (where `JumpQueued` is consumed).
  - `Menu.Input` in `BananaTime/UI/Menu.cs` — sole consumer for menu nav + activate.
  - `TitleScreen.Input`, `LevelPicker.Input`, `SavedLevelPicker.Input` — call sites that pass `KeyboardManager` to `Menu.Input`.
  - `Program.cs` — DI/Autofac setup. The new service auto-registers via `[AutoRegister]`; no change needed here unless registration needs tweaking (it shouldn't).

## Constraints & Gotchas
- **Auto-registration**: marking the service `[AutoRegister]` (default `Lifetime.Singleton`) plus implementing `IServiceInput` is enough — the `GameStateManagerBuilder.Run` reflection scan picks it up and the harness calls `Input(GameTime)` each frame before any `GameState.Input`. Do *not* manually register inside `Program.cs.AddServices` — that would double-register.
- **MonoGame Y-axis on `ThumbSticks`**: `GamePadState.ThumbSticks.Left.Y` is positive when the stick is pushed *up* (XInput convention), opposite of MonoGame's screen-Y-down. When mapping the left stick to `Direction`, treat positive Y as `Up`. The X axis needs no flip.
- **Stick deadzone for direction**: a raw stick read crossing zero will spam direction events. Use a magnitude-with-hysteresis or just `GamePadDeadZone.Circular` via `GamePad.GetState(idx, GamePadDeadZone.Circular)` and a magnitude threshold (~0.5) before classifying into a discrete direction. The implementer chooses the exact scheme — see Open Decisions.
- **Multi-pad merge**: any of the four gamepads may be active. For analog rotation, take the signed value with the largest absolute magnitude (so an idle stick at 0 doesn't cancel an active one at 0.8). For one-shot inputs (jump press, direction, accept), OR the per-pad edge-detected events together. The keyboard contributes alongside as a fifth source.
- **Jump buffer is wall-clock-ish, not frame-count**: the brief says "within the last 100 ms". `Input` runs once per frame at ~60Hz, so frame-count works as a proxy (6 frames ≈ 100 ms), but `gameTime` is available on the `Input(GameTime)` call — using elapsed time is more correct under variable frame rates. Implementer's call; the API surface is the same either way.
- **Existing `Menu` activate keys** include `Space`, `Z`, `X`, `Enter`. The brief restricts accept to "enter or A" — the new behavior drops Space/Z/X from menu activation. This is intentional; do not preserve the legacy keys "for compatibility".

## Open Decisions
1. **Service name** — `PlayerInput`, `InputManager`, `GameInput`. Default: **`PlayerInput`**, since it represents the player's *logical* input layer (rotate / jump / direction / accept), distinct from PPM's raw `KeyboardManager` / `MouseManager`.
2. **File / folder placement** — new `BananaTime/Input/PlayerInput.cs` + `BananaTime/Input/Direction.cs` vs. dropping both into `BananaTime/Services/`. Default: **`BananaTime/Input/`** as a new folder, since the project has no `Services/` folder today.
3. **Jump-consume API shape** — `bool JumpBuffered { get; }` + `void ConsumeJump()` (explicit two-call), vs. `bool TryConsumeJump()` (read-and-clear), vs. just `bool JumpPressedRecently` with no consume (current behavior in `Playing` already drops the queued jump on every tick). Default: **`TryConsumeJump()`** — matches how `Playing` will use it (one call site, atomic). Drop the buffer on `EndRewind`-style state-change exits if needed (see Implementation step on `Playing`).
4. **Stick-to-direction classification** — pick the larger-magnitude axis once it crosses a threshold (~0.5), or partition the stick into 8 quadrants and snap to nearest cardinal. Default: **threshold + larger axis wins**, with previous-direction tracked across frames so "newly pressed" only fires on transitions (`null → Up`, `Up → Right`, etc., but not every frame `Up` is held).
5. **D-pad / stick / arrow-key precedence for direction** — if the player presses Up on the keyboard *and* Down on the D-pad on the same frame, what wins? Default: **first-non-null wins in fixed order** (keyboard → D-pad → left stick). Edge case is rare; do not engineer a tie-break beyond this.
6. **Rotate-key set** — current `RotateCWKeys` is `D / Right / NumPad6` and `RotateCCWKeys` is `A / Left / NumPad4`. Default: **carry over unchanged** — the new service maps the same keys to its signed rotate value.
7. **Jump-key set** — currently `W / Z / X / Space`. Default: **carry over unchanged** (`W / Z / X / Space`), plus gamepad A button. Keyboard jump and keyboard accept are deliberately *not* the same set: keyboard accept is `Enter` only. (On gamepad, A serves both — that's intentional and matches platform convention.)

## Acceptance Criteria
- [ ] `BananaTime` declares a public `Direction` enum with members `Up`, `Left`, `Down`, `Right` (no `None` — null is the absence of a direction).
- [ ] `BananaTime` declares a public sealed singleton service (`PlayerInput` per Open Decision #1) decorated with `[AutoRegister]` and implementing `IServiceInput`.
- [ ] The service exposes:
  - `float RotateClockwise` — signed `[-1, 1]`, positive = CW, negative = CCW. Keyboard CW key → +1 when held; CCW key → −1 when held; both/neither → 0. Gamepad: largest-absolute left-stick-X among connected pads, after applying deadzone.
  - `bool JumpBuffered` (or equivalent — see Open Decision #3) — true if jump was pressed within the last 100 ms and not yet consumed. Pressing `W` / `Z` / `X` / `Space` or any gamepad `A` arms the buffer.
  - `bool TryConsumeJump()` (per Open Decision #3 default) — returns the buffered state and clears it.
  - `Direction? DirectionPressed` — the direction newly entered this frame, or `null` if no transition happened (re-fires only on state change, not every frame the input is held).
  - `bool AcceptPressed` — true on the frame `Enter` (keyboard) or any gamepad `A` is newly pressed; false otherwise. No buffer. Keyboard accept is **`Enter` only** — the jump keys (`W`/`Z`/`X`/`Space`) do *not* accept menu items. On gamepad the `A` button intentionally does both jobs (jump in gameplay, accept in menus); the asymmetry is by design.
- [ ] PPM's auto-discovery wires the service into the per-frame input pipeline without manual registration in `Program.cs`. (Verify by setting a breakpoint or log line in `Input` and confirming it ticks.)
- [ ] `Menu.Input` no longer references `KeyboardManager`; it takes the new service (or the two values it needs) and uses `DirectionPressed` (Up = previous, Down = next; Left/Right are no-op on the vertical menu) + `AcceptPressed` for activation.
- [ ] All three menu-driving game states (`TitleScreen`, `LevelPicker`, `SavedLevelPicker`) construct/inject the new service and pass it (or its values) into `Menu.Input`. Their `Esc`-to-return checks remain on `KeyboardManager`.
- [ ] `Playing.Input` no longer reads `RotateCWKeys` / `RotateCCWKeys` / `JumpKeys`; it instead caches the new service's rotate value (or reads it directly in `FixedUpdate`) and consumes the jump buffer in `FixedUpdate`. The rewind `R` key and the F6 debug toggle remain on `KeyboardManager`.
- [ ] `Banana` exposes a way to apply signed analog rotation (e.g., a new `Rotate(float intensity)` method, or `Playing` drives `Body.AngularVelocity` directly using the existing constant). Existing `RotateCW()` / `RotateCCW()` may be removed if no remaining caller, or kept if they have other uses — implementer's call.
- [ ] `LevelEditor` is byte-for-byte unchanged — still reads the keyboard directly.
- [ ] Disconnecting / reconnecting a gamepad mid-session does not throw. Re-connect makes its inputs work again on the next tick.

## Implementation

### 1. Create the `Direction` enum
Add `BananaTime/Input/Direction.cs` (per Open Decision #2). Four members: `Up`, `Left`, `Down`, `Right`. No `None` — callers use `Direction?` and check for null.

### 2. Create the `PlayerInput` service
Add `BananaTime/Input/PlayerInput.cs`. Mirror the `KeyboardManager` shape: a `[AutoRegister]` `sealed` class implementing `IServiceInput`, with `Previous*` / current-tick state pairs swapped at the top of `Input(GameTime)`. State to track:
- `KeyboardState` previous + current.
- `GamePadState[4]` previous + current — one slot per `PlayerIndex`. Use `GamePad.GetState(PlayerIndex.X, GamePadDeadZone.Circular)` so dead-zone-corrected stick values come out of the box.
- Jump-buffer remaining time (or last-press timestamp / frame index — see Constraints).
- Last-frame `Direction?` for transition detection.

In `Input(GameTime)`:
1. Snapshot previous → current as in `KeyboardManager.Input`.
2. Decrement (or check) the jump buffer. Re-arm to 100 ms if any jump key is *newly* pressed (keyboard edge OR any pad-A edge).
3. Compute the current `Direction?` from keyboard arrows/WASD/numpad, then D-pad, then left stick (per Open Decision #5 precedence). Compare to `Last-frame Direction?` to set `DirectionPressed` (the public, edge-only value).
4. Compute `AcceptPressed` from `Enter` edge or any pad-A edge.
5. Store rotate value: keyboard contributes `(CWHeld ? 1 : 0) - (CCWHeld ? 1 : 0)`; each connected pad contributes `LeftThumbStick.X`. Take the value with the largest absolute magnitude across all sources.

Public surface: properties for `RotateClockwise`, `JumpBuffered` (or no-getter — see #3), `DirectionPressed`, `AcceptPressed`, plus `TryConsumeJump()` (default per #3). Rotate and direction are read-only properties; jump is the one consumable.

### 3. Wire `Menu` to the new service
`Menu.Input` currently takes a `KeyboardManager`. Change the signature to take the new service (or take a small struct of the two values it needs — `(Direction? direction, bool accept)` — implementer's preference). Inside, switch on `direction`: `Up` → previous index, `Down` → next index, `Left` / `Right` / `null` → no-op. On `accept`, invoke `Items[SelectedIndex].Callback()`. Drop the `PrevKeys` / `NextKeys` / `ActivateKeys` arrays.

### 4. Update menu game states to inject and pass through
`TitleScreen`, `LevelPicker`, `SavedLevelPicker`: add the new service to the constructor (Autofac will supply it), store it, and pass the relevant value(s) into `Menu.Input`. Leave each state's `Esc → return` check on `KeyboardManager`.

### 5. Rewire `Playing` to use rotate + jump-buffer
- Inject the new service alongside the existing `KeyboardManager` (the latter still serves `R` rewind and `F6` debug toggle).
- Drop the static `RotateCWKeys` / `RotateCCWKeys` / `JumpKeys` arrays.
- In `Input(GameTime)`: keep `F6` and `R` on `KeyboardManager`; remove the rotate/jump key reads.
- In `FixedUpdate`: read `PlayerInput.RotateClockwise`. If non-zero, apply to the body — either via a new `Banana.Rotate(float intensity)` method (preferred — encapsulates the constant) or directly `Banana.Body.AngularVelocity = intensity * PhysicsConstants.RotateAngularVelocityRadiansPerSecond`. If zero, leave angular velocity to physics damping (matches today's behavior when neither key is held).
- Replace the `JumpQueued` bool with `if (PlayerInput.TryConsumeJump()) Banana.Jump();` (or equivalent per #3). Note `Banana.Jump` is already grounded-gated via `TryGetJumpDirection`.
- During rewind (`IsRewinding`), the existing code zeroes input vars; for the new model, don't read `PlayerInput` at all on rewind frames, and call `TryConsumeJump()` once on `BeginRewind` (or similar) to drain any stale buffered press so it doesn't fire the moment rewind ends.

### 6. (If created) Add `Banana.Rotate(float intensity)`
Tiny helper: `Body.AngularVelocity = intensity * PhysicsConstants.RotateAngularVelocityRadiansPerSecond;`. Decide whether to keep `RotateCW` / `RotateCCW` (no remaining callers — likely delete) or keep for API symmetry.

### 7. Update `Playing`'s footer hint
The current `"A/D rotate, Space jump, R rewind"` line in `DrawHud` no longer reflects the gamepad mappings. Update to something honest about both inputs (or generic — implementer's eye). Don't list every key/button.

### 8. Update `docs/REFERENCE.md §Input Bindings`
Mention gamepad mappings (left stick X / D-pad / A button / Enter / etc.) alongside the existing keyboard table. Keep it brief — a second column or a short prose note is fine.

## Test Plan
- [ ] `dotnet build` from `BananaTime/` succeeds with no new warnings.
- [ ] Launch with no gamepad connected. Title screen: arrow keys move selection, Enter activates. `Space` / `Z` / `X` / `W` do *not* activate menu items (verify each does nothing on the title screen — none of the four keyboard jump keys should double as accept).
- [ ] Same launch: `New Level` and `Edit Saved Level` menus also navigate with arrows + Enter only.
- [ ] In `Playing`: hold A or Left arrow — banana rotates CCW. Hold D or Right arrow — banana rotates CW. Press Space / W / Z / X to jump. Press R to rewind once buffer is full. F6 still toggles debug overlay.
- [ ] In `Playing`: tap jump just before landing on the ground — the banana jumps the moment it touches down (input buffer working). Tap jump in mid-air with > 100 ms before landing — it does *not* fire on landing (buffer expired).
- [ ] Connect a gamepad and re-launch. Title screen: D-pad and left stick both navigate; A activates; B / X / Y do nothing.
- [ ] In `Playing` with gamepad: tilting left stick partway left rotates banana CCW slowly; full-left rotates at the same speed as keyboard (since rotate constant is shared and intensity 1 maps to the same `AngularVelocity`). Tilt right → CW. A button jumps.
- [ ] Hot-plug: unplug gamepad mid-game — keyboard still works, no exception. Plug it back in — gamepad inputs resume on the next tick.
- [ ] Two gamepads connected: input from either drives the banana.
- [ ] Open the level editor (`New Level` → pick a picture). Confirm WASD/arrows still pan, mouse still adds points, X exports — editor input is unchanged.
- [ ] Trigger a rewind, then press jump during rewind playback. After rewind ends, the banana should *not* immediately jump from a stale buffered press (covered by step 5's drain).

## Learnings

### Architectural decisions

- **Open Decisions all resolved with the listed defaults**: `PlayerInput` (#1), `BananaTime/Input/` folder (#2), peek + explicit consume jump API (#3 — see deviation below), threshold + larger-axis-wins for stick→direction (#4), keyboard → D-pad → stick precedence (#5), unchanged keybindings for rotate (#6) and jump (#7).
- **Deviation from #3 default**: ticket suggested `TryConsumeJump()` (read-and-clear) as the canonical API. Implemented surface is *both* `JumpBuffered` (peek) and `ConsumeJump()` (drain), plus a convenience `TryConsumeJump()` per the AC. `Playing` uses peek-then-consume because the buffer must persist across frames until the jump *actually* fires (e.g., on landing). A pure read-and-clear would have cleared the buffer on every fixed tick, defeating the buffering test plan ("tap jump just before landing — banana jumps the moment it touches down"). `Banana.Jump()` was changed to return `bool` to support this.
- **Rewind drain at `EndRewind`, not `BeginRewind`**: ticket said "drain on `BeginRewind` (or similar)". Drained at `EndRewind` instead — handles both stale-press-before-rewind *and* press-during-rewind in one place. PlayerInput keeps polling during rewind (Playing just doesn't read it), so any A-button press during the 3 s playback would otherwise fire the moment forward play resumes.
- **`Banana.RotateCW`/`RotateCCW` removed entirely**: only caller was `Playing`. Replaced with one `Rotate(float intensity)` taking signed `[-1, 1]`. Cleaner API and matches the new analog rotate input.
- **Keyboard direction for menus reuses the rotate keys** (`A`/`D`/arrows): same physical keys serve different roles in different game states. PlayerInput exposes both `RotateClockwise` and `DirectionPressed`; each game state reads only what it cares about. No conflict because Playing doesn't read direction and menus don't read rotate.

### Problems encountered

- First build failed because dropping `KeyboardManager` from `TitleScreen`'s ctor also dropped the `BenMakesGames.PlayPlayMini.Services` using directive — but `GraphicsManager` lives in that namespace too. Re-added the using.
- **Auto-register caveat**: PlayPlayMini's reflection scan picks up `[AutoRegister]` + `IServiceInput` automatically. Manual registration in `Program.cs.AddServices` would have double-registered (per Constraints note). Verified by skipping the manual-register step entirely.

### Interesting tidbits

- `GamePadState.IsConnected` makes hot-plug graceful — `GamePad.GetState` always returns a state, just with `IsConnected=false` and zeroed buttons/sticks for absent pads. Polling all four slots every frame *is* the hot-plug detection.
- `ThumbSticks.Left.Y` is positive when the stick is pushed *up* (XInput convention) — opposite of MonoGame's screen Y. Mapped to `Direction.Up` accordingly.
- `GamePadDeadZone.Circular` applied at `GamePad.GetState` time means rotation analog values come out already deadzone-corrected — no extra magnitude check needed for `RotateClockwise`. Direction classification still needs its own threshold (0.5) because we want a *cardinal commitment*, not just non-zero.
- Multi-pad merge: rotate uses largest-absolute-magnitude (so an idle stick at 0 doesn't cancel an active stick at 0.8); jump/accept/direction OR per-pad edges across pads.

### Workarounds / limitations

- Edge detection on reconnect: if a player is holding A *as a pad reconnects*, `_previousPads[i]` is the cached pre-disconnect (zeroed) state, so reconnecting with A held will arm the jump buffer once. Acceptable — alternative would require tracking connection-state transitions, and the failure mode is one stray jump on reconnect.
- Per-pad asymmetry by design: A button serves jump (Playing) and accept (menus) on gamepad, but the four keyboard jump keys (`W`/`Z`/`X`/`Space`) do *not* double as menu accept (Enter only). Gamepad vs. keyboard convention differ; the asymmetry is intentional.

### Related areas affected

- `Banana.Jump()` signature changed `void → bool` — only caller was `Playing`, no other ripples.
- `Menu.Input` signature changed `KeyboardManager → PlayerInput` — all three callers (`TitleScreen`, `LevelPicker`, `SavedLevelPicker`) updated.
- `LevelEditor` confirmed unchanged (still on `KeyboardManager` directly per scope).
- `docs/REFERENCE.md §Input Bindings` rewritten to a two-column keyboard/gamepad table.

### Rejected alternatives

- **Stick → 8-quadrant snap** (Open Decision #4 alternative): rejected — threshold + larger-axis is simpler and covers the menu-nav use case. No diagonals are meaningful for the current game (vertical menus, horizontal rotate).
- **Single-call `bool TryConsumeJump()` in `Playing`**: see deviation note above. Pure read-and-clear breaks the buffering test plan.
- **Tracking connection-state transitions to suppress phantom presses on reconnect**: rejected as out of scope; acceptable failure mode.
