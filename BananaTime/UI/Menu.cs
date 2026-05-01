using System;
using System.Collections.Generic;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework.Input;

namespace BananaTime.UI;

public sealed record MenuItem(string Title, Action Callback);

public sealed class Menu
{
    private static readonly Keys[] PrevKeys = { Keys.W, Keys.Up, Keys.NumPad8 };
    private static readonly Keys[] NextKeys = { Keys.S, Keys.Down, Keys.NumPad2 };
    private static readonly Keys[] ActivateKeys = { Keys.Enter, Keys.Space, Keys.Z, Keys.X };

    public IReadOnlyList<MenuItem> Items { get; }
    public int SelectedIndex { get; private set; }

    public MenuItem Selected => Items[SelectedIndex];

    public Menu(IReadOnlyList<MenuItem> items)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("Menu requires at least one item.", nameof(items));

        Items = items;
        SelectedIndex = 0;
    }

    public void Input(KeyboardManager keyboard)
    {
        if (keyboard.PressedAnyKey(PrevKeys))
            SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count;
        else if (keyboard.PressedAnyKey(NextKeys))
            SelectedIndex = (SelectedIndex + 1) % Items.Count;

        if (keyboard.PressedAnyKey(ActivateKeys))
            Items[SelectedIndex].Callback();
    }
}
