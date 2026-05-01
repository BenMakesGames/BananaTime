using Microsoft.Xna.Framework;
using nkast.Aether.Physics2D.Dynamics;

namespace BananaTime.Physics;

public sealed class Banana
{
    public Body Body { get; }

    public Vector2 PositionMeters => Body.Position.ToXna();
    public Vector2 PositionPixels => PositionMeters * PhysicsConstants.PixelsPerMeter;
    public float Rotation => Body.Rotation;

    public Banana(World world, Vector2 spawnMeters)
    {
        Body = world.CreateBody(spawnMeters.ToAether(), 0f, BodyType.Dynamic);
        Body.SleepingAllowed = false;
        Body.AngularDamping = 0.2f;
        Body.LinearDamping = 0.0f;

        foreach (var (offset, radius) in PhysicsConstants.BananaCircles)
        {
            var fixture = Body.CreateCircle(radius, PhysicsConstants.BananaDensity, offset.ToAether());
            fixture.Friction = PhysicsConstants.BananaFriction;
            fixture.Restitution = PhysicsConstants.BananaRestitution;
        }
    }

    public bool IsGrounded()
    {
        var ce = Body.ContactList;
        while (ce != null)
        {
            var contact = ce.Contact;
            if (contact.IsTouching)
            {
                contact.GetWorldManifold(out var normal, out _);
                // Box2D normal points from FixtureA to FixtureB. Flip if banana is FixtureB so it always points away from banana.
                float ny = (contact.FixtureA.Body == Body) ? normal.Y : -normal.Y;
                // If normal points downward (into surface below banana), banana is sitting on top → grounded.
                if (ny > 0.5f) return true;
            }
            ce = ce.Next;
        }
        return false;
    }

    /// <summary>
    /// Returns the direction the banana should jump: the sum of all touching contacts'
    /// surface-perpendicular vectors (each pointing from the surface into the banana),
    /// normalized. Returns false if no contacts. If contacts cancel (pinched between
    /// surfaces), falls back to straight up.
    /// </summary>
    public bool TryGetJumpDirection(out Vector2 direction)
    {
        var sum = Vector2.Zero;
        bool any = false;
        var ce = Body.ContactList;
        while (ce != null)
        {
            var contact = ce.Contact;
            if (contact.IsTouching)
            {
                contact.GetWorldManifold(out var n, out _);
                // Box2D normal points from FixtureA to FixtureB. We want the vector from
                // surface toward banana (the direction we should push to leave the surface).
                // If banana is A → flip to point back at banana. If banana is B → keep.
                float sign = (contact.FixtureA.Body == Body) ? -1f : 1f;
                sum += new Vector2(n.X * sign, n.Y * sign);
                any = true;
            }
            ce = ce.Next;
        }

        if (!any)
        {
            direction = Vector2.Zero;
            return false;
        }

        if (sum.LengthSquared() < 0.0001f)
        {
            // Pinched between opposing surfaces: fall back to straight up.
            direction = new Vector2(0f, -1f);
            return true;
        }

        sum.Normalize();
        direction = sum;
        return true;
    }

    public void RotateCW()
    {
        Body.AngularVelocity = PhysicsConstants.RotateAngularVelocityRadiansPerSecond;
    }

    public void RotateCCW()
    {
        Body.AngularVelocity = -PhysicsConstants.RotateAngularVelocityRadiansPerSecond;
    }

    public void Jump()
    {
        if (!TryGetJumpDirection(out var dir)) return;
        var impulse = dir * (PhysicsConstants.JumpImpulseMetersPerSecond * Body.Mass);
        Body.ApplyLinearImpulse(impulse.ToAether());
    }
}
