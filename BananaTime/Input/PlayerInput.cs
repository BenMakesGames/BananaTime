using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Attributes.DI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace BananaTime.Input;

[AutoRegister]
public sealed class PlayerInput : IServiceInput
{
    private const float JumpBufferSeconds = 0.1f;
    private const float StickDirectionThreshold = 0.5f;

    private static readonly Keys[] RotateCWKeys = { Keys.D, Keys.Right, Keys.NumPad6 };
    private static readonly Keys[] RotateCCWKeys = { Keys.A, Keys.Left, Keys.NumPad4 };
    private static readonly Keys[] JumpKeys = { Keys.W, Keys.Z, Keys.X, Keys.Space };
    private static readonly Keys[] UpKeys = { Keys.W, Keys.Up, Keys.NumPad8 };
    private static readonly Keys[] DownKeys = { Keys.S, Keys.Down, Keys.NumPad2 };
    private static readonly Keys[] LeftKeys = { Keys.A, Keys.Left, Keys.NumPad4 };
    private static readonly Keys[] RightKeys = { Keys.D, Keys.Right, Keys.NumPad6 };

    private static readonly PlayerIndex[] AllPads =
    {
        PlayerIndex.One, PlayerIndex.Two, PlayerIndex.Three, PlayerIndex.Four,
    };

    private KeyboardState _previousKeyboard;
    private KeyboardState _keyboard;
    private readonly GamePadState[] _previousPads = new GamePadState[4];
    private readonly GamePadState[] _pads = new GamePadState[4];

    private float _jumpBufferRemainingSeconds;
    private Direction? _lastDirection;

    public float RotateClockwise { get; private set; }
    public Direction? DirectionPressed { get; private set; }
    public bool AcceptPressed { get; private set; }
    public bool JumpBuffered => _jumpBufferRemainingSeconds > 0f;

    public PlayerInput()
    {
        _keyboard = Keyboard.GetState();
        _previousKeyboard = _keyboard;
        for (int i = 0; i < 4; i++)
        {
            _pads[i] = GamePad.GetState(AllPads[i], GamePadDeadZone.Circular);
            _previousPads[i] = _pads[i];
        }
    }

    public void ConsumeJump() => _jumpBufferRemainingSeconds = 0f;

    public bool TryConsumeJump()
    {
        if (_jumpBufferRemainingSeconds <= 0f) return false;
        _jumpBufferRemainingSeconds = 0f;
        return true;
    }

    public void Input(GameTime gameTime)
    {
        _previousKeyboard = _keyboard;
        _keyboard = Keyboard.GetState();
        for (int i = 0; i < 4; i++)
        {
            _previousPads[i] = _pads[i];
            _pads[i] = GamePad.GetState(AllPads[i], GamePadDeadZone.Circular);
        }

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_jumpBufferRemainingSeconds > 0f)
        {
            _jumpBufferRemainingSeconds -= dt;
            if (_jumpBufferRemainingSeconds < 0f) _jumpBufferRemainingSeconds = 0f;
        }

        if (JumpNewlyPressed())
            _jumpBufferRemainingSeconds = JumpBufferSeconds;

        var current = ComputeCurrentDirection();
        DirectionPressed = current != _lastDirection ? current : null;
        _lastDirection = current;

        AcceptPressed = ComputeAcceptPressed();
        RotateClockwise = ComputeRotateClockwise();
    }

    private bool JumpNewlyPressed()
    {
        foreach (var k in JumpKeys)
        {
            if (_keyboard.IsKeyDown(k) && _previousKeyboard.IsKeyUp(k)) return true;
        }
        for (int i = 0; i < 4; i++)
        {
            if (!_pads[i].IsConnected) continue;
            if (_pads[i].Buttons.A == ButtonState.Pressed && _previousPads[i].Buttons.A == ButtonState.Released)
                return true;
        }
        return false;
    }

    private bool ComputeAcceptPressed()
    {
        if (_keyboard.IsKeyDown(Keys.Enter) && _previousKeyboard.IsKeyUp(Keys.Enter))
            return true;
        for (int i = 0; i < 4; i++)
        {
            if (!_pads[i].IsConnected) continue;
            if (_pads[i].Buttons.A == ButtonState.Pressed && _previousPads[i].Buttons.A == ButtonState.Released)
                return true;
        }
        return false;
    }

    private Direction? ComputeCurrentDirection()
    {
        var keyboard = KeyboardDirection();
        if (keyboard != null) return keyboard;

        var dpad = DPadDirection();
        if (dpad != null) return dpad;

        return StickDirection();
    }

    private Direction? KeyboardDirection()
    {
        bool up = AnyDown(UpKeys);
        bool down = AnyDown(DownKeys);
        bool left = AnyDown(LeftKeys);
        bool right = AnyDown(RightKeys);
        return PickCardinal(up, down, left, right);
    }

    private Direction? DPadDirection()
    {
        bool up = false, down = false, left = false, right = false;
        for (int i = 0; i < 4; i++)
        {
            if (!_pads[i].IsConnected) continue;
            up |= _pads[i].DPad.Up == ButtonState.Pressed;
            down |= _pads[i].DPad.Down == ButtonState.Pressed;
            left |= _pads[i].DPad.Left == ButtonState.Pressed;
            right |= _pads[i].DPad.Right == ButtonState.Pressed;
        }
        return PickCardinal(up, down, left, right);
    }

    private Direction? StickDirection()
    {
        for (int i = 0; i < 4; i++)
        {
            if (!_pads[i].IsConnected) continue;
            var stick = _pads[i].ThumbSticks.Left;
            float ax = System.Math.Abs(stick.X);
            float ay = System.Math.Abs(stick.Y);
            if (ax < StickDirectionThreshold && ay < StickDirectionThreshold) continue;
            // Larger-magnitude axis wins. ThumbSticks.Y is positive when pushed up (XInput convention).
            if (ay >= ax)
                return stick.Y > 0 ? Direction.Up : Direction.Down;
            return stick.X > 0 ? Direction.Right : Direction.Left;
        }
        return null;
    }

    private static Direction? PickCardinal(bool up, bool down, bool left, bool right)
    {
        if (up) return Direction.Up;
        if (down) return Direction.Down;
        if (left) return Direction.Left;
        if (right) return Direction.Right;
        return null;
    }

    private bool AnyDown(Keys[] keys)
    {
        foreach (var k in keys)
            if (_keyboard.IsKeyDown(k)) return true;
        return false;
    }

    private float ComputeRotateClockwise()
    {
        float keyboard = (AnyDown(RotateCWKeys) ? 1f : 0f) - (AnyDown(RotateCCWKeys) ? 1f : 0f);

        float best = keyboard;
        float bestAbs = System.Math.Abs(keyboard);
        for (int i = 0; i < 4; i++)
        {
            if (!_pads[i].IsConnected) continue;
            float v = _pads[i].ThumbSticks.Left.X;
            float a = System.Math.Abs(v);
            if (a > bestAbs)
            {
                best = v;
                bestAbs = a;
            }
        }
        return best;
    }
}
