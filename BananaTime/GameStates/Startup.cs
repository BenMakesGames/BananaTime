using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;

namespace BananaTime.GameStates;

// inheriting game states is a path that leads to madness, so always seal your game states!
public sealed class Startup: GameState
{
    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private MouseManager Mouse { get; }

    public Startup(GraphicsManager graphics, GameStateManager gsm, MouseManager mouse)
    {
        Graphics = graphics;
        GSM = gsm;
        Mouse = mouse;

        Mouse.UseNoCursor();
    }

    // note: you do NOT need to call the `base.` for lifecycle methods. so save some CPU cycles,
    // and don't call them :P

    public override void Update(GameTime gameTime)
    {
        if (Graphics.FullyLoaded)
        {
            GSM.ChangeState<TitleScreen>();
        }
    }

    public override void Draw(GameTime gameTime)
    {
        // TODO: draw loading screen

    }
}
