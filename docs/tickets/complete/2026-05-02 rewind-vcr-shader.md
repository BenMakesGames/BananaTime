# Rewind VCR Shader

## Context
**Current behavior**: When the player triggers rewind, `Playing` runs `StepRewind` each fixed frame and draws the world normally — there's no visual cue beyond the `REWINDING` text in the HUD. The world simply plays back in reverse.

**New behavior**: While `IsRewinding`, the world layer (background + banana + optional debug terrain) is composited through a custom HLSL pixel shader that emulates a VCR rewind look — horizontal jitter, scrolling tracking-band distortion, scanlines, color bleed, and noise. The HUD draws over the top **unshaded**, so `REWINDING` and the rewind buffer text remain crisp. The shader file is authored as part of this ticket; it does not exist today.

## Prerequisites
None.

## Scope
### In scope
- New HLSL shader `Content/Shaders/VcrRewind.fx` checked into the repo and built via the MGCB content pipeline.
- MGCB entry for the new shader in `BananaTime/Content/Content.mgcb`.
- Asset registration in `Program.cs` (`PixelShaderMeta`).
- Wrap the world-layer draws in `Playing.Draw` with `Graphics.WithSceneShader("VcrRewind", …)` only while `IsRewinding`. HUD continues to draw outside the shader scope.
- Pass per-frame time into the shader so the effect animates (jitter / band scroll / noise).

### Out of scope
- Sound effects (VCR whir / static) — no audio system wired yet.
- A different shader for normal gameplay or for the moment of trigger / end-of-rewind transitions. The effect is binary — on while rewinding, off otherwise. Fade-in/out is an Open Decision (default off).
- Per-platform shader variants beyond what the standard MonoGame `OPENGL` macro pattern provides (project ships DesktopGL — that's the only target).
- A settings toggle to disable the effect for accessibility / motion-sensitivity.
- Applying the shader to the HUD or to non-`Playing` game states.

## Relevant Docs & Anchors
- `docs/REFERENCE.md §Rewind` — rewind state machine: `IsRewinding`, `BeginRewind`, `StepRewind`, `EndRewind`, playback duration ≈ 3 s real-time.
- **PlayPlayMini source-of-truth**: `D:\Development\C-sharp\PlayPlayMini` (per `memory/reference_ppm_source.md`).
  - `BenMakesGames.PlayPlayMini\Services\GraphicsManager.cs` — `WithSceneShader(string, Action<Effect>?)` is the right primitive (composites the wrapped layer through the shader, so neighbor sampling works for jitter/scanlines). `WithShader` is **wrong** here — it runs per draw call against each draw's own source texture and would distort the banana sprite alone, not the assembled scene.
  - `BenMakesGames.PlayPlayMini\Model\PixelShaderMeta.cs` — asset registration shape (`new PixelShaderMeta("VcrRewind", "Shaders/VcrRewind")`).
  - `BenMakesGames.PlayPlayMini\UPGRADE.md §SetPostProcessingPixelShader is gone` — example of `WithSceneShader` usage with a `Time` parameter.
- **Code anchors**:
  - `Playing.Draw` — current call sequence is `Clear → DrawBackground → (debug) DrawTerrain → DrawBanana → DrawHud`. Wrap the first four; leave `DrawHud` outside.
  - `Playing.IsRewinding` — the gate flag.
  - `Program.cs` — `AddAssets([...])` call is where `PixelShaderMeta` joins the existing `FontMeta` / `PictureMeta` entries.
  - `BananaTime/Content/Content.mgcb` — existing `Graphics/*.png` entries are the template; shader entry uses `EffectImporter` / `EffectProcessor`.

## Constraints & Gotchas
- **MonoGame DesktopGL shader profile**: project is `DesktopGL` with `/profile:Reach` in the MGCB. The shader must compile via MojoShader (HLSL → GLSL). Use the standard cross-platform technique block:
  ```hlsl
  #if OPENGL
      #define SV_POSITION POSITION
      #define VS_SHADERMODEL vs_3_0
      #define PS_SHADERMODEL ps_3_0
  #else
      #define VS_SHADERMODEL vs_4_0_level_9_1
      #define PS_SHADERMODEL ps_4_0_level_9_1
  #endif
  ```
  Use these macros in the `technique`'s `VertexShader = compile VS_SHADERMODEL …` / `PixelShader = compile PS_SHADERMODEL …` lines.
- **SpriteBatch sampler**: `ShaderScope` / `SceneShaderScope` begin the batch with `SamplerState.PointClamp`. The sampler register the shader binds to is the implicit SpriteBatch sampler — do not declare a second sampler unless you also bind a second texture. Sample the source layer through the standard SpriteBatch idiom (`sampler2D` over the input texture).
- **Effect parameter access pattern**: PPM passes `Action<Effect>` to `WithSceneShader`'s configure callback. Set parameters by name there each frame: `e.Parameters["Time"].SetValue(seconds)`. Setting a parameter that isn't declared in the shader throws — keep the names aligned.
- **Time source**: `GameTime` is available on `Draw(GameTime)`. Use `(float)gameTime.TotalGameTime.TotalSeconds` (wraps eventually but not within a session) or accumulate a `_shaderTime` field on `Playing` that resets at `BeginRewind`. Implementer's call — see Open Decisions.
- **MGCB content syntax**: shader entries use a different `/importer` and `/processor` than textures. Do not copy `TextureImporter` for the `.fx` file — see Implementation step 2 for the exact stanza.
- **Internal resolution is 640×360** (per REFERENCE.md). Effect parameters that scale with screen size (scanline density, jitter pixel offset) should be authored against this fixed resolution; no need to expose `ViewportSize` as a uniform unless an effect explicitly needs it.
- **HUD must stay readable**: `DrawHud` reads `REWINDING` cue and the buffer text. Wrap only the world draws — `DrawHud` stays outside the `using` block. Acceptance Criterion below codifies this.
- **Compositor disposal**: `WithSceneShader` returns an `IDisposable`. Use a `using` block (or `using var`); don't store and never-dispose, or the layer render target leaks back into the pool in an inconsistent state.

## Open Decisions
1. **Effect intensity & exact look** — number/spacing of scanlines, jitter pixel offset (1–3 px), tracking-band height/scroll speed, noise strength, color-channel offset (chromatic aberration). No correct answer; aim for "obviously a VCR rewind", not realistic. Default: tune by eyeballing during manual test. Implementer picks numbers that feel right.
2. **Time source** — `(float)gameTime.TotalGameTime.TotalSeconds` (cheap, monotonic, but accumulates from app launch) vs. a `_rewindElapsedSeconds` accumulator on `Playing` that resets at `BeginRewind`. Default: **`TotalGameTime.TotalSeconds`** — simplest, and the shader doesn't need a 0-based clock for a continuously-animating effect. If a particular effect element needs "progress through this rewind", switch to the accumulator.
3. **Fade-in / fade-out** — instant on at `BeginRewind` and instant off at `EndRewind`, or a short ramp via an `Intensity` shader uniform (0..1) that lerps over ~150 ms at each transition. Default: **instant**. Cheaper, and a 3 s rewind is short enough that fades steal a meaningful slice of it. Add later if the snap-on feels harsh.
4. **Shader file location** — `Content/Shaders/VcrRewind.fx` (new folder under `Content/`) vs. `Content/Graphics/VcrRewind.fx` (alongside textures). Default: **`Content/Shaders/`** — separates by asset *kind*, matches `PixelShaderMeta` doc example (`"Shader/IFrames"`). Asset key: **`VcrRewind`**.
5. **Wrap debug terrain inside or outside the shader?** Default: **inside** — `DrawTerrain` is debug-only and gated on `ShowDebugInfo`, but it's part of the world view. Drawing it through the VCR shader matches the rest of the world; pulling it out adds branching in the wrap with no win. Implementer can flip if it interferes with debugging.

## Acceptance Criteria
- [ ] `BananaTime/Content/Shaders/VcrRewind.fx` exists, contains a single `technique` with one pass that compiles a vertex + pixel shader pair, declares a `float Time` parameter, and uses the cross-platform `OPENGL` profile macro pattern (per Constraints).
- [ ] The shader compiles cleanly via `dotnet build` on Windows DesktopGL — no MGCB errors, no MojoShader errors.
- [ ] `Content.mgcb` includes a `Shaders/VcrRewind.fx` stanza using `EffectImporter` / `EffectProcessor`.
- [ ] `Program.cs` registers the shader: `new PixelShaderMeta("VcrRewind", "Shaders/VcrRewind")` (or `PreLoaded: true` if Open Decision lands there) added to the `AddAssets([...])` array alongside the existing fonts and pictures.
- [ ] In `Playing.Draw`, the `Clear` + world draws (background, optional debug terrain, banana) execute inside a `using (Graphics.WithSceneShader("VcrRewind", e => …))` block **only when `IsRewinding == true`**. When `IsRewinding == false`, the draw path is identical to today (no shader scope, no allocations from `WithSceneShader`).
- [ ] `DrawHud` is called **outside** the shader scope in both branches, so the HUD renders unshaded over the (possibly shaded) world layer.
- [ ] The shader's `Time` parameter is set each shaded frame to a per-frame-changing value (so the jitter / scroll / noise animate). Using a parameter name that isn't declared in the shader throws at runtime — verify by playing through a rewind without crashes.
- [ ] Visually distinct effect during rewind: at minimum the world has horizontal jitter or a scrolling tracking-band distortion that wasn't present during forward play. Exact aesthetic per Open Decision #1.
- [ ] HUD elements (`REWINDING`, rewind buffer text) remain crisp and unaffected by the shader.

## Implementation

### 1. Author the shader
Create `BananaTime/Content/Shaders/VcrRewind.fx`. Structure:

- Cross-platform shader-model macros (per Constraints).
- `sampler2D` over the SpriteBatch input texture (the assembled scene layer). Standard MonoGame idiom: declare a texture + linear sampler with point-clamp filtering, names like `Texture` / `TextureSampler`.
- `float Time` parameter — drives jitter + band scroll + noise hash.
- Pixel shader composes the look from a small set of cheap effects layered together. Build at least:
  - **Horizontal row jitter**: offset `uv.x` by a small noise function of `uv.y` and `Time`. Use a hash like `frac(sin(uv.y * k + Time) * m)` to keep it cheap and shader-model-2 compatible.
  - **Scrolling tracking band**: a horizontal band a few pixels tall whose vertical position is `frac(Time * speed)`. Inside the band, distort `uv.x` more strongly and brighten / desaturate.
  - **Scanlines**: multiplicative attenuation of brightness based on `sin(uv.y * lineCount)` or `step(0.5, frac(uv.y * lineCount * 0.5))`. Subtle — don't crush the picture.
  - **Color-channel offset (chromatic aberration)**: sample R from `uv + (smallOffset, 0)`, G from `uv`, B from `uv - (smallOffset, 0)`. Combine.
  - **Static / noise**: small additive noise via the same hash function, weighted low.
- A `technique` with a single `pass P0` whose `VertexShader = compile VS_SHADERMODEL …;` and `PixelShader = compile PS_SHADERMODEL …;`. The vertex shader can be the standard SpriteBatch passthrough (or omitted — SpriteBatch supplies a default VS if only the pixel shader is provided; safer to include both so the effect compiles standalone).

Keep the file under ~80 lines. Tune constants by feel; see Open Decision #1.

### 2. Add the MGCB stanza
In `BananaTime/Content/Content.mgcb`, append after the existing `Graphics/StoneHenge.jpg` block:

```
#begin Shaders/VcrRewind.fx
/importer:EffectImporter
/processor:EffectProcessor
/processorParam:DebugMode=Auto
/build:Shaders/VcrRewind.fx
```

(Importer and processor names differ from texture entries — do not paste a texture stanza and rename the path.)

### 3. Register the shader as a PPM asset
In `Program.cs`, add to the `AddAssets([...])` array (alongside the existing `FontMeta` and `PictureMeta` entries):

```csharp
new PixelShaderMeta("VcrRewind", "Shaders/VcrRewind"),
```

`PreLoaded` defaults to false — that's fine; the lookup at first rewind will load it on demand. Set `PreLoaded: true` only if the first-rewind hitch is noticeable in manual testing.

### 4. Wrap the world draw in `Playing.Draw`
Refactor `Playing.Draw` so the world draws (currently `Clear`, `DrawBackground`, optional `DrawTerrain`, `DrawBanana`) live inside a conditional `using` block. Pseudocode:

```csharp
public override void Draw(GameTime gameTime)
{
    if (IsRewinding)
    {
        using (Graphics.WithSceneShader("VcrRewind", e =>
            e.Parameters["Time"].SetValue((float)gameTime.TotalGameTime.TotalSeconds)))
        {
            DrawWorld();
        }
    }
    else
    {
        DrawWorld();
    }

    DrawHud();
}

private void DrawWorld()
{
    Graphics.Clear(new Color(40, 40, 60));
    DrawBackground();
    if (ShowDebugInfo) DrawTerrain();
    DrawBanana();
}
```

Extract `DrawWorld` as a private helper to avoid duplicating the four-line draw sequence across the two branches. The exact factoring is implementer's call — duplicating is also fine if it reads cleaner. Either way: `DrawHud` stays outside.

If Open Decision #2 lands on a `_rewindElapsedSeconds` accumulator instead of `TotalGameTime`, increment it in `Update` (not `FixedUpdate` — animation timing is frame-rate-independent and runs even on rewind frames where physics doesn't advance) and reset to 0 at `BeginRewind`.

### 5. Smoke-test the shader path
With the build green, launch and roll the banana for >6 s to fill the buffer, press R, and watch the rewind. Confirm the effect is visible, the HUD remains crisp, and ending rewind cleanly returns to a non-shaded view. See Test Plan for the full pass.

## Test Plan
- [ ] `dotnet build` from `BananaTime/` succeeds with no new warnings and no MGCB errors. Specifically check the build log for `Shaders/VcrRewind.fx` being processed by `EffectProcessor`.
- [ ] Launch the game and play forward for ≥6 s without rewinding — visuals are identical to before this ticket (no shader applied, HUD as before).
- [ ] Fill the rewind buffer, press R: the world view immediately picks up the VCR effect (jitter / scanlines / band — whichever combination was tuned). The `REWINDING` HUD text and the rewind buffer text remain crisp and undistorted.
- [ ] Rewind plays out for ~3 s real-time and ends cleanly: shader switches off the moment `IsRewinding` flips false. No visual residue on the next frame.
- [ ] Multiple rewinds in a single session: trigger a rewind, play forward, fill the buffer again, rewind again. Effect engages each time. No exception, no leaked render targets (no growing memory in a long session — eyeball Task Manager if curious; not a hard requirement).
- [ ] `F6` debug overlay during rewind: the `grounded` / `angVel` / `linVel` text remains readable (it's part of `DrawHud` so should be unshaded). The debug `DrawTerrain` lines (if enabled) render through the shader along with the rest of the world — confirm they're visible enough to still be useful, or note in the PR if Open Decision #5 should flip.
- [ ] Pause-on-defocus (`LostFocus`) during rewind: tab away mid-rewind and back. No crash. (Not strictly a shader concern, but the shader scope shouldn't break the existing pause path.)
- [ ] Editor still works (open level editor from the title menu, edit and save). Editor draws aren't gated on `IsRewinding` so the shader never engages there — confirm by inspection.

## Learnings

### Architectural decisions

- **Open Decisions resolved with the listed defaults**: tuned-by-feel intensity (#1), `gameTime.TotalGameTime.TotalSeconds` as the time source (#2), instant on/off — no fade (#3), `Content/Shaders/` as the file location (#4), debug terrain drawn inside the shader scope (#5).
- **`DrawWorld()` extracted as a private helper** (per the ticket's preferred factoring). Avoids duplicating the four-line draw sequence between the rewind-on and rewind-off branches; the only difference between the branches is the `using` wrapper.
- **Shader is PS-only** — relies on SpriteBatch's default vertex shader (which handles the world-view-projection matrix). The ticket allowed either approach; PS-only is the more common MonoGame-SpriteBatch idiom and avoids having to re-implement the matrix transform in a custom VS.
- **Standard `SpriteTexture` / `SpriteTextureSampler` naming** follows the MonoGame content-pipeline `.fx` template — same convention used by `dotnet new mgcontent` shader templates, so future contributors will recognize the layout.

### Problems encountered

- None during initial implementation — shader compiled clean on first MGCB pass and the build log shows `Shaders/VcrRewind.fx` processed by `EffectProcessor` as expected.

### Interesting tidbits

- `WithSceneShader` (PPM 8.2) renders into a pooled, layer-sized render target and composites at `Dispose` time. That's why the pixel shader can sample neighboring scene content for chroma/jitter — at PS run time the source texture is the assembled world layer, not individual draw-call textures.
- The `Effect.Parameters["X"].SetValue(...)` API throws on missing names. The configure callback runs on every wrapped `Begin` (every frame while rewinding). Keeping shader uniforms and the C# `SetValue` calls aligned is enforced at runtime, not build time — a typo only surfaces during the first rewind. Verified clean by the test plan.
- The MGCB `EffectImporter` / `EffectProcessor` stanza accepts no texture-style processor params (no `ColorKey`, `PremultiplyAlpha`, etc.). Only `DebugMode=Auto` is wired. Pasting a texture stanza and renaming the path would have failed; the ticket's "do not paste a texture stanza" note was load-bearing.
- The shader's `frac(sin(dot(...)) * 43758.5453)` hash is a well-known cheap pseudo-noise idiom — works fine on SM2 / `ps_3_0` and translates cleanly through MojoShader to GLSL on DesktopGL. No texture-based noise lookup needed.

### Workarounds / limitations

- `Time` is `(float)gameTime.TotalGameTime.TotalSeconds` cast from a `double`. Float precision of `Time` degrades after ~hours of play (single precision starts losing sub-frame resolution past 16777216 seconds, but the noise/sin patterns visibly stop scrolling well before that — practically a non-issue for a game-jam game with short sessions).
- Effect is hard-coded against the 640×360 internal resolution (e.g., scanline density, hash row count). If `Width`/`Height` ever changes, the visual will need re-tuning. Not exposed as uniforms because nothing else changes resolution.

### Related areas affected

- `Playing.Draw` refactored into `Draw` + `DrawWorld` private helper — a cleanup that's mildly nice independent of the shader (clearer separation of world vs. HUD layers). No other callers.
- `Content.mgcb` now has its first non-texture entry; pattern is a useful reference for future shader/effect adds.
- `Program.cs` `AddAssets([...])` learns about `PixelShaderMeta` for the first time.

### Rejected alternatives

- **Custom vertex shader passthrough**: ticket allowed it; rejected as unnecessary complexity. SpriteBatch's default VS handles the projection. Including a VS would force re-implementing the world-view-projection transform.
- **`_rewindElapsedSeconds` accumulator** (Open Decision #2 alt): rejected — the effect animates continuously off `Time` and doesn't need a 0-based clock per rewind. Default `TotalGameTime` is one less field on `Playing`.
- **Fade-in/out via `Intensity` uniform** (Open Decision #3 alt): rejected — 3 s rewind is short, fades would steal a meaningful slice. Easy to add later if instant-on feels harsh.
- **Settings toggle for motion sensitivity**: explicitly out of scope. If accessibility matters later, expose `Intensity = 0.0` as the off path.
