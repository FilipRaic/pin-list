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

    // One menu for the icon's whole life. Avalonia's macOS menu code ties the native menu to the first NativeMenu it
    // sees, so giving the icon a new NativeMenu later throws "The menu being updated does not match". Only its items
    // change (SetMenu).
    readonly NativeMenu _menu = new();

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
                Menu = _menu, // before the icon is added below, so Avalonia never makes a menu of its own
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

    /// <summary>New entries in the same menu. A menu that can't be updated is logged, PinList keeps running.</summary>
    public void SetMenu(IReadOnlyList<TrayEntry> entries)
    {
        try
        {
            _menu.Items.Clear();
            Fill(_menu, entries);
        }
        catch (Exception ex) { Store.Log("Menu bar menu: " + ex.Message); }
    }

    /// <summary>A new native menu from the entries (the app menu, which is set once).</summary>
    internal static NativeMenu BuildMenu(IReadOnlyList<TrayEntry> entries)
    {
        var menu = new NativeMenu();
        Fill(menu, entries);
        return menu;
    }

    static void Fill(NativeMenu menu, IReadOnlyList<TrayEntry> entries)
    {
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
    }

    public void ShowNotification(string title, string text) =>
        ReminderPopup.Show(title, text, () => NotificationClicked?.Invoke(), closeAfter: TimeSpan.FromSeconds(10));

    /// <summary>A macOS notification, or PinList's own popup when macOS doesn't allow notifications. Both stay until closed.</summary>
    public void ShowReminder(string title, string text, Action onClick)
    {
        if (MacNotifications.TryShow(title, text, onClick)) return;
        ReminderPopup.Show(title, text, onClick);
    }

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
