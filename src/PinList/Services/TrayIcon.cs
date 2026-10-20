using System;
using WinForms = System.Windows.Forms;

namespace PinList;

/// <summary>The notification area icon. Tests use a fake one, so nothing shows up in the real taskbar.</summary>
public interface ITrayIcon : IDisposable
{
    event Action? LeftClick;
    event Action? RightClick;
    event Action? NotificationClicked;

    /// <summary>Shows a Windows notification (reminders).</summary>
    void ShowNotification(string title, string text);
}

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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
