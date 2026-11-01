using System;
#if WINDOWS
using WinForms = System.Windows.Forms;
#endif

namespace PinList;

/// <summary>The notification area icon. Tests use a fake one, so nothing shows up in the real taskbar.</summary>
public interface ITrayIcon : IDisposable
{
    event Action? LeftClick;
    event Action? RightClick;
    event Action? NotificationClicked;

    /// <summary>Shows a notification (reminders).</summary>
    void ShowNotification(string title, string text);

    /// <summary>
    /// The menu's entries, for platforms whose tray menu is native (macOS menu bar). On Windows the app shows its
    /// own menu on right click instead.
    /// </summary>
    void SetMenu(System.Collections.Generic.IReadOnlyList<TrayEntry> entries);
}

/// <summary>One tray menu entry; <see cref="Separator"/> draws a line. A disabled entry is a line of text (e.g. the account).</summary>
public sealed record TrayEntry(string Text, Action? Action, bool Enabled = true)
{
    public static readonly TrayEntry Separator = new("-", null);
}

#if WINDOWS
/// <summary>The real tray icon (a WinForms NotifyIcon; WPF has none).</summary>
public sealed class TrayIcon : ITrayIcon
{
    readonly WinForms.NotifyIcon _icon;

    public event Action? LeftClick;
    public event Action? RightClick;
    public event Action? NotificationClicked;

    public TrayIcon(System.Drawing.Icon icon, bool visible = true)
    {
        _icon = new WinForms.NotifyIcon { Icon = icon, Text = "PinList", Visible = visible };
        _icon.MouseUp += (_, e) => OnMouseUp(e.Button);
        _icon.BalloonTipClicked += (_, _) => NotificationClicked?.Invoke();
    }

    /// <summary>
    /// Left click shows the widgets, right click opens the menu. Selecting the icon with the keyboard
    /// (Win+B, then Enter) arrives as a right click, so it opens the menu too.
    /// </summary>
    internal void OnMouseUp(WinForms.MouseButtons button)
    {
        if (button == WinForms.MouseButtons.Left) LeftClick?.Invoke();
        else if (button == WinForms.MouseButtons.Right) RightClick?.Invoke();
    }

    public void ShowNotification(string title, string text) =>
        _icon.ShowBalloonTip(10000, title, text, WinForms.ToolTipIcon.None);

    public void SetMenu(System.Collections.Generic.IReadOnlyList<TrayEntry> entries) { } // right click shows the app's menu

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
#endif
