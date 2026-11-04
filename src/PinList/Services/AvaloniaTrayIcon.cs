#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
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
        try
        {
            // A template image: black on transparent, macOS colors it for light and dark menu bars
            _icon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PinList/Assets/tray-mac.png"))),
                ToolTipText = "PinList",
            };
            MacOSProperties.SetIsTemplateIcon(_icon, true);
            _icon.IsVisible = true;
            _icon.Clicked += (_, _) => LeftClick?.Invoke();
            // Avalonia only creates the real menu bar item for icons in the application's TrayIcon.Icons
            Icons().Add(_icon);
            Store.Log("Menu bar icon: added to the menu bar");
        }
        catch (Exception ex)
        {
            Store.Log("Menu bar icon: " + ex.Message);
            throw;
        }
    }

    public void SetMenu(IReadOnlyList<TrayEntry> entries) => _icon.Menu = BuildMenu(entries);

    /// <summary>A native menu from the entries (the menu bar menu and the app menu).</summary>
    internal static NativeMenu BuildMenu(IReadOnlyList<TrayEntry> entries)
    {
        var menu = new NativeMenu();
        foreach (var e in entries)
        {
            if (e == TrayEntry.Separator) { menu.Items.Add(new NativeMenuItemSeparator()); continue; }
            var item = new NativeMenuItem(e.Text) { IsEnabled = e.Enabled, Gesture = e.Gesture };
            if (e.Checked is { } on)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = on;
            }
            item.Click += (_, _) => e.Action?.Invoke();
            menu.Items.Add(item);
        }
        return menu;
    }

    public void ShowNotification(string title, string text) =>
        ReminderPopup.Show(title, text, () => NotificationClicked?.Invoke());

    /// <summary>The application's tray icons (created the first time).</summary>
    static TrayIcons Icons()
    {
        var app = Application.Current!;
        if (TrayIcon.GetIcons(app) is { } icons) return icons;
        icons = new TrayIcons();
        TrayIcon.SetIcons(app, icons);
        return icons;
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        Icons().Remove(_icon);
        _icon.Dispose();
    }
}
#endif
