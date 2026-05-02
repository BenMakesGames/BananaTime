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

    // Charge jump tuning. Hold below MinChargeSecondsToFire = no fire (a tap).
    // At/above MinChargeSecondsToFire = fires; impulse scales linearly between
    // MinJumpImpulseScale and MaxJumpImpulseScale (relative to JumpImpulseMetersPerSecond)
    // up to MaxChargeSeconds.
    public const float MinChargeSecondsToFire = 0.1f;
    public const float MaxChargeSeconds = 0.8f;
    public const float MinJumpImpulseScale = 0.4f;
    public const float MaxJumpImpulseScale = 1.6f;
    // Window after losing solid contact (or angle deviating > 45°) in which a release still fires.
    public const float LostContactGraceSeconds = 0.5f;
    // Friction multiplier applied to all banana fixtures while charging (any sub-state).
    public const float ChargingFrictionMultiplier = 2f;
    // At max charge, sprite is squashed to (1 - MaxSquishDepth) along the squish axis.
    public const float MaxSquishDepth = 0.3f;
    // Precomputed cos(45°): two unit vectors are within 45° iff Dot(a,b) >= this.
    public const float MaxJumpAngleDeviationDegrees = 45f;
    public static readonly float MaxJumpAngleDeviationCosine =
        (float)System.Math.Cos(MaxJumpAngleDeviationDegrees * System.Math.PI / 180.0);

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
    // Centroid in sprite-local pixel space. Use as SpriteBatch.Draw `origin` so the body
    // centroid pivots on its screen position under arbitrary rotation/scale.
    public static readonly Vector2 BananaSpriteOriginPixels;

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
    }

    public static Vector2 ToMeters(Vector2 pixels) => pixels * MetersPerPixel;
    public static Vector2 ToPixels(Vector2 meters) => meters * PixelsPerMeter;
    public static float ToMeters(float pixels) => pixels * MetersPerPixel;
    public static float ToPixels(float meters) => meters * PixelsPerMeter;
}
