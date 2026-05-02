using System;
using System.IO;
using System.Linq;
using BananaTime.Levels;
using BananaTime.UI;
using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace BananaTime.GameStates;

public sealed class SavedLevelPicker : GameState
{
    private static readonly string LevelDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Levels");

    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private KeyboardManager Keyboard { get; }

    private readonly Menu Menu;
    private string? LoadError;

    public SavedLevelPicker(GraphicsManager graphics, GameStateManager gsm, KeyboardManager keyboard)
    {
        Graphics = graphics;
        GSM = gsm;
        Keyboard = keyboard;

        var files = Directory.Exists(LevelDirectory)
            ? Directory.GetFiles(LevelDirectory, "*.json").OrderBy(f => f).ToArray()
            : Array.Empty<string>();

        var items = files
            .Select(f => new MenuItem(Path.GetFileNameWithoutExtension(f), () => LoadAndEdit(f)))
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

    private void LoadAndEdit(string path)
    {
        try
        {
            var level = LevelStorage.Load(path);
            GSM.ChangeState<LevelEditor, LevelEditorConfig>(new LevelEditorConfig(level));
        }
        catch (Exception ex)
        {
            LoadError = $"{Path.GetFileName(path)}: {ex.Message}";
        }
    }

    public override void Draw(GameTime gameTime)
    {
        Graphics.Clear(new Color(20, 30, 40));

        const string title = "EDIT SAVED LEVEL";
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

        if (LoadError != null)
            Graphics.DrawText("Font", 4, Graphics.Height - 24, LoadError, Color.OrangeRed);

        Graphics.DrawText("Font", 4, Graphics.Height - 12, "Esc to return", Color.LightGray);
    }
}
