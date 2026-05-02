using System;
using BananaTime.Input;
using BananaTime.Levels;
using BananaTime.Physics;
using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using nkast.Aether.Physics2D.Dynamics;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace BananaTime.GameStates;

public sealed record PlayingConfig(LevelData Level);

public sealed class Playing: GameState<PlayingConfig>
{
    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private MouseManager Mouse { get; }
    private KeyboardManager Keyboard { get; }
    private PlayerInput PlayerInput { get; }

    private World World;
    private Banana Banana;
    private LevelTerrain Terrain;
    private Camera Camera;
    private readonly string PictureName;
    private readonly Vector2 StartPositionMeters;

    private bool RewindQueued;
    private bool ShowDebugInfo;

    private readonly BananaSnapshot[] RewindBuffer = new BananaSnapshot[PhysicsConstants.RewindFrames];
    private int RewindHead;
    private int RewindCount;
    private int RewindReadIndex;
    private bool IsRewinding;

    private enum ChargeState { Idle, OnSurface, InGrace }
    private ChargeState _chargeState;
    private float _chargeElapsedSeconds;
    private Vector2 _trackedJumpDirection;
    private float _graceRemainingSeconds;

    public Playing(PlayingConfig config, GraphicsManager graphics, GameStateManager gsm, MouseManager mouse, KeyboardManager keyboard, PlayerInput playerInput)
    {
        Graphics = graphics;
        GSM = gsm;
        Mouse = mouse;
        Keyboard = keyboard;
        PlayerInput = playerInput;

        PictureName = config.Level.Picture;
        StartPositionMeters = config.Level.StartPosition * PhysicsConstants.MetersPerPixel;

        World = new World(new AetherVector2(0f, PhysicsConstants.Gravity));
        Terrain = new LevelTerrain(World, config.Level);
        Banana = new Banana(World, StartPositionMeters);

        Vector2? worldSize = Graphics.Pictures.TryGetValue(PictureName, out var pic)
            ? new Vector2(pic.Width, pic.Height)
            : null;
        Camera = new Camera(new Vector2(Graphics.Width, Graphics.Height), Banana.PositionPixels, worldSize);
    }

    public override void Input(GameTime gameTime)
    {
        if (Keyboard.PressedKey(Keys.F6)) ShowDebugInfo = !ShowDebugInfo;

        if (IsRewinding)
        {
            RewindQueued = false;
            return;
        }

        if (Keyboard.PressedKey(Keys.R)) RewindQueued = true;
    }

    public override void FixedUpdate(GameTime gameTime)
    {
        if (IsRewinding)
        {
            StepRewind();
            return;
        }

        if (RewindQueued)
        {
            RewindQueued = false;
            if (RewindCount == PhysicsConstants.RewindFrames)
            {
                BeginRewind();
                return;
            }
        }

        UpdateChargeStateMachine();

        // Rotation is locked while charging (both OnSurface and InGrace sub-states).
        if (_chargeState == ChargeState.Idle)
        {
            var rotate = PlayerInput.RotateClockwise;
            if (rotate != 0f) Banana.Rotate(rotate);
        }

        World.Step(PhysicsConstants.FixedTimestepSeconds);
        CaptureSnapshot();

        if (IsTouchingKillShape()) Respawn();
    }

    private void UpdateChargeStateMachine()
    {
        switch (_chargeState)
        {
            case ChargeState.Idle:
                if (!PlayerInput.JumpHeld) return;
                if (Banana.TryGetJumpDirection(out var startDir))
                    EnterCharging(startDir);
                // Else: airborne with held button. Stay Idle; we'll re-check next frame
                // and transition the moment a contact appears.
                return;

            case ChargeState.OnSurface:
                if (!PlayerInput.JumpHeld)
                {
                    if (_chargeElapsedSeconds >= PhysicsConstants.MinChargeSecondsToFire)
                        Banana.ApplyJumpImpulse(_trackedJumpDirection, ComputeChargeImpulseSpeed());
                    ExitChargingToIdle();
                    return;
                }

                if (!Banana.TryGetJumpDirection(out var current))
                {
                    // No contacts: enter grace with tracked frozen at previous-frame value.
                    EnterGrace();
                    return;
                }

                if (Vector2.Dot(_trackedJumpDirection, current) < PhysicsConstants.MaxJumpAngleDeviationCosine)
                {
                    // Deviation > 45°: enter grace, freeze tracked at previous-frame value.
                    EnterGrace();
                    return;
                }

                _chargeElapsedSeconds = System.Math.Min(
                    _chargeElapsedSeconds + PhysicsConstants.FixedTimestepSeconds,
                    PhysicsConstants.MaxChargeSeconds);
                _trackedJumpDirection = current;
                return;

            case ChargeState.InGrace:
                _graceRemainingSeconds -= PhysicsConstants.FixedTimestepSeconds;

                if (!PlayerInput.JumpHeld)
                {
                    // Coyote release: fires regardless of current contact state, along the frozen tracked angle.
                    Banana.ApplyJumpImpulse(_trackedJumpDirection, ComputeChargeImpulseSpeed());
                    ExitChargingToIdle();
                    return;
                }

                if (_graceRemainingSeconds <= 0f)
                {
                    // Grace expired: jump lost.
                    ExitChargingToIdle();
                    return;
                }

                if (Banana.TryGetJumpDirection(out var recovered)
                    && Vector2.Dot(_trackedJumpDirection, recovered) >= PhysicsConstants.MaxJumpAngleDeviationCosine)
                {
                    // Re-acquired a valid surface within the 45° window: resume charging,
                    // re-syncing tracked to the new direction. Charge does not increment this frame.
                    _trackedJumpDirection = recovered;
                    _graceRemainingSeconds = 0f;
                    _chargeState = ChargeState.OnSurface;
                }
                return;
        }
    }

    private void EnterCharging(Vector2 initialDirection)
    {
        _chargeState = ChargeState.OnSurface;
        _chargeElapsedSeconds = 0f;
        _trackedJumpDirection = initialDirection;
        _graceRemainingSeconds = 0f;
        Banana.SetFixtureFriction(PhysicsConstants.BananaFriction * PhysicsConstants.ChargingFrictionMultiplier);
        // Drain the buffered-jump latch so it doesn't double-count if the player taps and re-taps.
        PlayerInput.ConsumeJump();
    }

    private void EnterGrace()
    {
        _chargeState = ChargeState.InGrace;
        _graceRemainingSeconds = PhysicsConstants.LostContactGraceSeconds;
        // _trackedJumpDirection retains its previous-frame value (frozen).
        // _chargeElapsedSeconds does not increment this frame.
    }

    private void ExitChargingToIdle()
    {
        _chargeState = ChargeState.Idle;
        _chargeElapsedSeconds = 0f;
        _trackedJumpDirection = Vector2.Zero;
        _graceRemainingSeconds = 0f;
        Banana.SetFixtureFriction(PhysicsConstants.BananaFriction);
    }

    private float ComputeChargeImpulseSpeed()
    {
        float min = PhysicsConstants.MinChargeSecondsToFire;
        float max = PhysicsConstants.MaxChargeSeconds;
        float clamped = MathHelper.Clamp(_chargeElapsedSeconds, min, max);
        float t = (max - min) > 0f ? (clamped - min) / (max - min) : 0f;
        float scale = MathHelper.Lerp(PhysicsConstants.MinJumpImpulseScale, PhysicsConstants.MaxJumpImpulseScale, t);
        return PhysicsConstants.JumpImpulseMetersPerSecond * scale;
    }

    private bool IsTouchingKillShape()
    {
        var ce = Banana.Body.ContactList;
        while (ce != null)
        {
            var contact = ce.Contact;
            if (contact.IsTouching)
            {
                var otherBody = contact.FixtureA.Body == Banana.Body ? contact.FixtureB.Body : contact.FixtureA.Body;
                if (otherBody.Tag == LevelTerrain.KillTag) return true;
            }
            ce = ce.Next;
        }
        return false;
    }

    private void Respawn()
    {
        if (_chargeState != ChargeState.Idle) ExitChargingToIdle();
        Banana.Body.Position = StartPositionMeters.ToAether();
        Banana.Body.Rotation = 0f;
        Banana.Body.LinearVelocity = AetherVector2.Zero;
        Banana.Body.AngularVelocity = 0f;
        ClearRewindBuffer();
    }

    private void CaptureSnapshot()
    {
        RewindBuffer[RewindHead] = new BananaSnapshot(Banana.Body);
        RewindHead = (RewindHead + 1) % PhysicsConstants.RewindFrames;
        if (RewindCount < PhysicsConstants.RewindFrames) RewindCount++;
    }

    private void BeginRewind()
    {
        if (_chargeState != ChargeState.Idle) ExitChargingToIdle();
        IsRewinding = true;
        // Most recent capture sits one slot behind head.
        RewindReadIndex = (RewindHead - 1 + PhysicsConstants.RewindFrames) % PhysicsConstants.RewindFrames;
    }

    private void StepRewind()
    {
        if (RewindCount <= 0) { EndRewind(); return; }

        int steps = PhysicsConstants.RewindPlaybackSpeed;
        if (steps > RewindCount) steps = RewindCount;

        // Skip past intermediate snapshots; only the landing frame needs to be applied.
        int target = RewindReadIndex - (steps - 1);
        if (target < 0) target += PhysicsConstants.RewindFrames;

        RewindBuffer[target].Apply(Banana.Body);

        RewindReadIndex = target - 1;
        if (RewindReadIndex < 0) RewindReadIndex += PhysicsConstants.RewindFrames;
        RewindCount -= steps;

        if (RewindCount <= 0) EndRewind();
    }

    private void EndRewind()
    {
        IsRewinding = false;
        ClearRewindBuffer();
        // Drain any jump pressed during rewind so it doesn't fire on the first post-rewind frame.
        PlayerInput.ConsumeJump();
    }

    private void ClearRewindBuffer()
    {
        RewindHead = 0;
        RewindCount = 0;
    }

    public override void Update(GameTime gameTime)
    {
        Camera.Follow(Banana.PositionPixels, (float)gameTime.ElapsedGameTime.TotalSeconds);
    }

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

    private void DrawBackground()
    {
        if (!Graphics.Pictures.ContainsKey(PictureName)) return;
        var origin = Camera.WorldToScreen(Vector2.Zero);
        Graphics.DrawPicture(PictureName, (int)origin.X, (int)origin.Y);
    }

    private void DrawTerrain()
    {
        foreach (var shape in Terrain.ShapesPixels)
        {
            for (int i = 0; i < shape.Length - 1; i++)
                DrawLine(Camera.WorldToScreen(shape[i]), Camera.WorldToScreen(shape[i + 1]), Color.LimeGreen, 2f);

            if (shape.Length >= 3)
                DrawLine(Camera.WorldToScreen(shape[^1]), Camera.WorldToScreen(shape[0]), Color.LimeGreen, 2f);
        }
    }

    private void DrawBanana()
    {
        var bananaScreenPx = Camera.WorldToScreen(Banana.PositionPixels);
        var rot = Banana.Rotation;

        // Squish along banana-local Y (long axis) — at body rotation 0 this aligns roughly
        // with the surface normal on flat ground. Frozen during InGrace.
        var scale = Vector2.One;
        if (_chargeState != ChargeState.Idle)
        {
            float t = MathHelper.Clamp(_chargeElapsedSeconds / PhysicsConstants.MaxChargeSeconds, 0f, 1f);
            float depth = PhysicsConstants.MaxSquishDepth * t;
            scale = new Vector2(1f, 1f - depth);
        }

        // SpriteBatch.Draw rotates and scales around `origin` (sprite-local pixels) and
        // places that origin at `position`. Setting origin to the centroid means the
        // body's centroid lands on the body's screen position with no extra compensation.
        var pos = new Vector2(MathF.Round(bananaScreenPx.X), MathF.Round(bananaScreenPx.Y));

        Graphics.SpriteBatch.Draw(
            Graphics.Pictures["Banana"],
            pos,
            null,
            Color.White,
            rot,
            PhysicsConstants.BananaSpriteOriginPixels,
            scale,
            SpriteEffects.None,
            0f
        );

        if (ShowDebugInfo)
        {
            // Mark centroid (body origin) for debug.
            Graphics.DrawFilledRectangle((int)bananaScreenPx.X - 1, (int)bananaScreenPx.Y - 1, 2, 2, Color.Red);
        }
    }

    private void DrawHud()
    {
        if (ShowDebugInfo)
        {
            var grounded = Banana.IsGrounded();
            Graphics.DrawText("Font", 4, 4, $"grounded: {grounded}", Color.White);
            Graphics.DrawText("Font", 4, 14, $"angVel: {Banana.Body.AngularVelocity:0.00}", Color.White);
            Graphics.DrawText("Font", 4, 24, $"linVel: ({Banana.Body.LinearVelocity.X:0.00}, {Banana.Body.LinearVelocity.Y:0.00})", Color.White);
        }

        if (IsRewinding)
        {
            Graphics.DrawText("Font", 4, 38, "REWINDING", Color.Cyan);
        }
        else
        {
            float bufferSeconds = RewindCount / 60f;
            var rewindColor = RewindCount == PhysicsConstants.RewindFrames ? Color.Cyan : Color.Gray;
            Graphics.DrawText("Font", 4, 38, $"rewind: {bufferSeconds:0.00}/{PhysicsConstants.RewindCaptureSeconds}s", rewindColor);
        }

        Graphics.DrawText("Font", 4, Graphics.Height - 12, "rotate / jump / R: rewind", Color.LightGray);
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        var diff = b - a;
        float length = diff.Length();
        float angle = (float)System.Math.Atan2(diff.Y, diff.X);
        Graphics.SpriteBatch.Draw(
            Graphics.WhitePixel,
            a,
            null,
            color,
            angle,
            new Vector2(0f, 0.5f),
            new Vector2(length, thickness),
            SpriteEffects.None,
            0f
        );
    }

    private Texture2D MakeCircleTexture(int size)
    {
        var tex = new Texture2D(Graphics.GraphicsDevice, size, size);
        var data = new Color[size * size];
        float c = (size - 1) / 2f;
        float r = c;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float distSq = dx * dx + dy * dy;
                data[y * size + x] = (distSq <= r * r) ? Color.White : Color.Transparent;
            }
        }
        tex.SetData(data);
        return tex;
    }
}
