using System;
using Microsoft.Xna.Framework;

namespace BananaTime.Physics;

public sealed class Camera
{
    public Vector2 PositionPixels;
    public Vector2 ViewSizePixels;
    public Vector2? WorldSizePixels;

    private Vector2 ViewCenter => new(ViewSizePixels.X * 0.5f, ViewSizePixels.Y * 0.5f);

    public Camera(Vector2 viewSizePixels, Vector2 initialTargetPixels, Vector2? worldSizePixels = null)
    {
        ViewSizePixels = viewSizePixels;
        WorldSizePixels = worldSizePixels;
        PositionPixels = ClampToBounds(initialTargetPixels - new Vector2(viewSizePixels.X * 0.5f, viewSizePixels.Y * 0.5f));
    }

    public void Follow(Vector2 targetWorldPixels, float deltaSeconds)
    {
        var targetScreenPos = targetWorldPixels - PositionPixels;
        var delta = targetScreenPos - ViewCenter;
        float deadzone = PhysicsConstants.CameraDeadzoneRadiusPixels;
        if (delta.LengthSquared() <= deadzone * deadzone)
            return;

        var idealCameraPos = targetWorldPixels - ViewCenter;
        // Frame-rate independent exponential damping: distance to target decays by factor
        // exp(-rate * dt) per second of elapsed time.
        float t = 1f - MathF.Exp(-PhysicsConstants.CameraSmoothRatePerSecond * deltaSeconds);
        PositionPixels = ClampToBounds(Vector2.Lerp(PositionPixels, idealCameraPos, t));
    }

    private Vector2 ClampToBounds(Vector2 pos)
    {
        if (!WorldSizePixels.HasValue) return pos;
        var size = WorldSizePixels.Value;
        float maxX = MathF.Max(0f, size.X - ViewSizePixels.X);
        float maxY = MathF.Max(0f, size.Y - ViewSizePixels.Y);
        return new Vector2(Math.Clamp(pos.X, 0f, maxX), Math.Clamp(pos.Y, 0f, maxY));
    }

    // Snap to integer to avoid pixel-art jitter on subpixel camera offsets.
    public Vector2 WorldToScreen(Vector2 worldPx)
    {
        var snapped = new Vector2(MathF.Round(PositionPixels.X), MathF.Round(PositionPixels.Y));
        return worldPx - snapped;
    }
}
