#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Platform;

namespace PinList;

/// <summary>The tray / menu bar icon on macOS (Avalonia's TrayIcon, a native NSStatusItem).</summary>
[ExcludeFromCodeCoverage(Justification = "Puts a real icon in the menu bar; tests use a fake tray icon.")]
public sealed class AvaloniaTrayIcon : ITrayIcon
{
    readonly TrayIcon _icon;

    public event Action? LeftClick;
    public event Action? RightClick { add { } remove { } } // the menu bar menu opens by itself
    public event Action? NotificationClicked;

    public AvaloniaTrayIcon()
    {
        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PinList/Assets/tray.png"))),
            ToolTipText = "PinList",
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => LeftClick?.Invoke();
    }

    public void SetMenu(IReadOnlyList<TrayEntry> entries)
    {
        var menu = new NativeMenu();
        foreach (var e in entries)
        {
            if (e == TrayEntry.Separator) { menu.Items.Add(new NativeMenuItemSeparator()); continue; }
            var item = new NativeMenuItem(e.Text) { IsEnabled = e.Enabled };
            if (e.Checked is { } on)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = on;
            }
            item.Click += (_, _) => e.Action?.Invoke();
            menu.Items.Add(item);
        }
        _icon.Menu = menu;
    }

    public void ShowNotification(string title, string text) =>
        ReminderPopup.Show(title, text, () => NotificationClicked?.Invoke());

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
#endif
