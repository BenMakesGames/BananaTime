using System;
using System.Collections.Generic;
using BananaTime.Input;

namespace BananaTime.UI;

public sealed record MenuItem(string Title, Action Callback);

public sealed class Menu
{
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

    public void Input(PlayerInput input)
    {
        switch (input.DirectionPressed)
        {
            case Direction.Up:
                SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count;
                break;
            case Direction.Down:
                SelectedIndex = (SelectedIndex + 1) % Items.Count;
                break;
        }

        if (input.AcceptPressed)
            Items[SelectedIndex].Callback();
    }
}
