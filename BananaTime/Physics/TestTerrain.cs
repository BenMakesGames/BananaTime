using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace BananaTime.Physics;

public sealed class TestTerrain
{
    public XnaVector2[] VerticesPixels { get; }

    public TestTerrain(World world)
    {
        // Pixel coords on the 640x360 internal render target.
        // Forms ground with a ramp up, plateau, and step down.
        VerticesPixels = new[]
        {
            new XnaVector2(-10000, 320),
            new XnaVector2(   180, 320),
            new XnaVector2(   300, 240),
            new XnaVector2(   460, 240),
            new XnaVector2(   520, 280),
            new XnaVector2( 10000, 280),
        };

        var verts = new Vertices(VerticesPixels.Length);
        foreach (var v in VerticesPixels)
            verts.Add((v * PhysicsConstants.MetersPerPixel).ToAether());

        var body = world.CreateBody(AetherVector2.Zero, 0f, BodyType.Static);
        var chain = body.CreateChainShape(verts);
        chain.Friction = 0.8f;
        chain.Restitution = 0.0f;
    }
}
