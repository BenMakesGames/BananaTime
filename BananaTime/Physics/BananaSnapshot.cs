using nkast.Aether.Physics2D.Dynamics;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace BananaTime.Physics;

public readonly struct BananaSnapshot
{
    public readonly AetherVector2 Position;
    public readonly AetherVector2 LinearVelocity;
    public readonly float Rotation;
    public readonly float AngularVelocity;

    public BananaSnapshot(Body body)
    {
        Position = body.Position;
        LinearVelocity = body.LinearVelocity;
        Rotation = body.Rotation;
        AngularVelocity = body.AngularVelocity;
    }

    public void Apply(Body body)
    {
        body.Position = Position;
        body.Rotation = Rotation;
        body.LinearVelocity = LinearVelocity;
        body.AngularVelocity = AngularVelocity;
    }
}
