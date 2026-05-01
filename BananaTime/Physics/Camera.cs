using System;
using Microsoft.Xna.Framework;

namespace BananaTime.Physics;

public sealed class Camera
{
    public Vector2 PositionPixels;
    public Vector2 ViewSizePixels;

    private Vector2 ViewCenter => new(ViewSizePixels.X * 0.5f, ViewSizePixels.Y * 0.5f);

    public Camera(Vector2 viewSizePixels, Vector2 initialTargetPixels)
    {
        ViewSizePixels = viewSizePixels;
        PositionPixels = initialTargetPixels - new Vector2(viewSizePixels.X * 0.5f, viewSizePixels.Y * 0.5f);
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
        PositionPixels = Vector2.Lerp(PositionPixels, idealCameraPos, t);
    }

    // Snap to integer to avoid pixel-art jitter on subpixel camera offsets.
    public Vector2 WorldToScreen(Vector2 worldPx)
    {
        var snapped = new Vector2(MathF.Round(PositionPixels.X), MathF.Round(PositionPixels.Y));
        return worldPx - snapped;
    }
}
