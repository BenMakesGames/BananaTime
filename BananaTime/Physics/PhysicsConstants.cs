using Microsoft.Xna.Framework;

namespace BananaTime.Physics;

public static class PhysicsConstants
{
    public const float PixelsPerMeter = 256f;
    public const float MetersPerPixel = 1f / PixelsPerMeter;

    public const float FixedTimestepSeconds = 1f / 60f;

    public const float Gravity = 9.8f;

    public const int RewindCaptureSeconds = 6;
    // FixedTimestepSeconds is 1/60s — 60 frames per second of capture.
    public const int RewindFrames = RewindCaptureSeconds * 60;
    public const int RewindPlaybackSpeed = 2;

    public const float BananaDensity = 1f;
    public const float BananaFriction = 0.7f;
    public const float BananaRestitution = 0.05f;

    public const float JumpImpulseMetersPerSecond = 2.5f;
    public const float RotateAngularVelocityRadiansPerSecond = 6f;

    public const float CameraDeadzoneRadiusPixels = 80f;
    // Higher = camera catches up faster. Frame-rate independent (per-second exponential decay).
    public const float CameraSmoothRatePerSecond = 6f;

    public const int BananaSpriteWidthPixels = 52;
    public const int BananaSpriteHeightPixels = 97;

    // Artist source-of-truth: each circle as bounding box on the 52x97 sprite,
    // upper-left origin in pixels.
    public static readonly (Vector2 TopLeftPixels, float SizePixels)[] BananaCircleBoxesPixels =
    {
        (new Vector2(38,  0), 14f),
        (new Vector2(26,  0), 18f),
        (new Vector2(12,  5), 24f),
        (new Vector2( 2, 20), 26f),
        (new Vector2( 1, 36), 25f),
        (new Vector2( 5, 50), 25f),
        (new Vector2(17, 65), 19f),
        (new Vector2(32, 79),  8f),
        (new Vector2(38, 82),  8f),
        (new Vector2(44, 88),  6f),
        (new Vector2(48, 93),  3f),
    };

    public static readonly (Vector2 OffsetMeters, float RadiusMeters)[] BananaCircles;
    public static readonly Vector2 BananaSpriteOriginPixels;

    // PPM rotates sprites around their integer-divided center. To make the physics centroid
    // (the body origin) land on the body's screen position, we offset the draw position by
    // the rotated vector from centroid to sprite-center.
    public static readonly Vector2 BananaSpriteCenterPixels =
        new Vector2(BananaSpriteWidthPixels / 2, BananaSpriteHeightPixels / 2);
    public static readonly Vector2 BananaSpriteCenterMinusCentroidPixels;

    static PhysicsConstants()
    {
        // Convert each box → (center pixel, radius pixel).
        var centersPx = new Vector2[BananaCircleBoxesPixels.Length];
        var radiiPx = new float[BananaCircleBoxesPixels.Length];
        for (int i = 0; i < BananaCircleBoxesPixels.Length; i++)
        {
            var (topLeft, size) = BananaCircleBoxesPixels[i];
            radiiPx[i] = size * 0.5f;
            centersPx[i] = topLeft + new Vector2(radiiPx[i], radiiPx[i]);
        }

        // Area-weighted centroid (π cancels, use r²).
        Vector2 weightedSum = Vector2.Zero;
        float totalWeight = 0f;
        for (int i = 0; i < centersPx.Length; i++)
        {
            float w = radiiPx[i] * radiiPx[i];
            weightedSum += centersPx[i] * w;
            totalWeight += w;
        }
        BananaSpriteOriginPixels = weightedSum / totalWeight;

        // Shift so body origin = centroid, then convert to meters.
        BananaCircles = new (Vector2, float)[centersPx.Length];
        for (int i = 0; i < centersPx.Length; i++)
        {
            BananaCircles[i] = (
                (centersPx[i] - BananaSpriteOriginPixels) * MetersPerPixel,
                radiiPx[i] * MetersPerPixel
            );
        }

        BananaSpriteCenterMinusCentroidPixels = BananaSpriteCenterPixels - BananaSpriteOriginPixels;
    }

    public static Vector2 ToMeters(Vector2 pixels) => pixels * MetersPerPixel;
    public static Vector2 ToPixels(Vector2 meters) => meters * PixelsPerMeter;
    public static float ToMeters(float pixels) => pixels * MetersPerPixel;
    public static float ToPixels(float meters) => meters * PixelsPerMeter;
}
