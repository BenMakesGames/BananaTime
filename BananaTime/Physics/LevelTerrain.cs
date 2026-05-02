using System.Collections.Generic;
using BananaTime.Levels;
using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace BananaTime.Physics;

public sealed class LevelTerrain
{
    public IReadOnlyList<XnaVector2[]> ShapesPixels => _shapesPixels;

    private readonly List<XnaVector2[]> _shapesPixels;

    public LevelTerrain(World world, LevelData level)
    {
        _shapesPixels = new List<XnaVector2[]>(level.Shapes.Count);

        foreach (var shape in level.Shapes)
        {
            if (shape.Points.Count < 2) continue;

            var px = shape.Points.ToArray();
            _shapesPixels.Add(px);

            var verts = new Vertices(px.Length);
            foreach (var v in px)
                verts.Add((v * PhysicsConstants.MetersPerPixel).ToAether());

            var body = world.CreateBody(AetherVector2.Zero, 0f, BodyType.Static);
            // Closed loop for shapes with 3+ vertices, open chain for 2-vertex segments.
            var chain = px.Length >= 3 ? body.CreateLoopShape(verts) : body.CreateChainShape(verts);
            chain.Friction = 0.8f;
            chain.Restitution = 0.0f;
        }
    }
}
