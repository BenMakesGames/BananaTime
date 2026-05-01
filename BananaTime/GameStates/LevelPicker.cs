using System.Linq;
using BananaTime.UI;
using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace BananaTime.GameStates;

public sealed class LevelPicker : GameState
{
    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private KeyboardManager Keyboard { get; }

    private readonly Menu Menu;

    public LevelPicker(GraphicsManager graphics, GameStateManager gsm, KeyboardManager keyboard)
    {
        Graphics = graphics;
        GSM = gsm;
        Keyboard = keyboard;

        var items = Graphics.Pictures.Keys
            .OrderBy(k => k)
            .Select(k => new MenuItem(k, () => GSM.ChangeState<LevelEditor, LevelEditorConfig>(new LevelEditorConfig(k))))
            .Append(new MenuItem("Back", () => GSM.ChangeState<TitleScreen>()))
            .ToArray();

        Menu = new Menu(items);
    }

    public override void Input(GameTime gameTime)
    {
        if (Keyboard.PressedKey(Keys.Escape))
        {
            GSM.ChangeState<TitleScreen>();
            return;
        }

        Menu.Input(Keyboard);
    }

    public override void Draw(GameTime gameTime)
    {
        Graphics.Clear(new Color(20, 30, 40));

        const string title = "PICK A LEVEL";
        int titleX = (Graphics.Width - title.Length * 6) / 2;
        Graphics.DrawText("Font", titleX, 16, title, Color.Yellow);

        int y = 48;
        for (int i = 0; i < Menu.Items.Count; i++)
        {
            var item = Menu.Items[i];
            bool selected = i == Menu.SelectedIndex;
            string label = selected ? $"> {item.Title} <" : item.Title;
            int x = (Graphics.Width - label.Length * 6) / 2;
            Graphics.DrawText("Font", x, y, label, selected ? Color.Cyan : Color.White);
            y += 12;
        }

        Graphics.DrawText("Font", 4, Graphics.Height - 12, "Esc to return", Color.LightGray);
    }
}
