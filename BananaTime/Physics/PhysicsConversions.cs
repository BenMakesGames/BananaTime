using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace BananaTime.Physics;

public static class PhysicsConversions
{
    public static AetherVector2 ToAether(this XnaVector2 v) => new(v.X, v.Y);
    public static XnaVector2 ToXna(this AetherVector2 v) => new(v.X, v.Y);
}
