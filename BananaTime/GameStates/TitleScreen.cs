using BananaTime.UI;
using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;

namespace BananaTime.GameStates;

public sealed class TitleScreen: GameState
{
    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private KeyboardManager Keyboard { get; }

    private readonly Menu Menu;

    public TitleScreen(GraphicsManager graphics, GameStateManager gsm, KeyboardManager keyboard)
    {
        Graphics = graphics;
        GSM = gsm;
        Keyboard = keyboard;

        Menu = new Menu(new MenuItem[]
        {
            new("Start", () => GSM.ChangeState<Playing>()),
            new("New Level", () => GSM.ChangeState<LevelPicker>()),
            new("Edit Saved Level", () => GSM.ChangeState<SavedLevelPicker>()),
            new("Quit", () => GSM.Exit()),
        });
    }

    public override void Input(GameTime gameTime)
    {
        Menu.Input(Keyboard);
    }

    public override void Draw(GameTime gameTime)
    {
        Graphics.Clear(new Color(40, 40, 60));

        const string title = "BANANA TIME";
        int titleX = (Graphics.Width - title.Length * 6) / 2;
        Graphics.DrawText("Font", titleX, 80, title, Color.Yellow);

        int y = 180;
        for (int i = 0; i < Menu.Items.Count; i++)
        {
            var item = Menu.Items[i];
            bool selected = i == Menu.SelectedIndex;
            string label = selected ? $"> {item.Title} <" : item.Title;
            int x = (Graphics.Width - label.Length * 6) / 2;
            Graphics.DrawText("Font", x, y, label, selected ? Color.Cyan : Color.White);
            y += 16;
        }
    }
}
