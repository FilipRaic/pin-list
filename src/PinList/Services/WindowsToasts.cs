#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace PinList;

/// <summary>
/// Windows reminders as Windows notifications. A notification with the "reminder" scenario stays on the screen until
/// the user clicks it, clicks Show or Dismiss, and it is also kept in the notification center. Clicking it (or
/// Show) while PinList runs shows its item. If Windows refuses the notification, PinList's own popup shows instead.
/// </summary>
static class WindowsToasts
{
    /// <summary>The name Windows shows notifications under (HKCU\Software\Classes\AppUserModelId\PinList).</summary>
    const string AppId = "PinList";

    static readonly Dictionary<string, (ToastNotification? Toast, Action Click)> Shown = new();
    static bool _registered;

    /// <summary>
    /// Sends the notification (id, title, text); true if Windows took it. Tests replace it, so nothing shows on the
    /// real screen and nothing is written to the registry.
    /// </summary>
    internal static Func<string, string, string, bool> Send { get; set; } = SendToWindows;

    public static void ShowReminder(string title, string text, Action onClick)
    {
        var id = Guid.NewGuid().ToString("N");
        Shown[id] = (null, onClick);
        if (Send(id, title, text)) return;
        Shown.Remove(id);
        ReminderPopup.Show(title, text, onClick);
    }

    /// <summary>A notification was clicked (on the UI thread). Dismiss doesn't come here.</summary>
    internal static void Activated(string id)
    {
        if (Shown.Remove(id, out var shown)) shown.Click();
    }

    /// <summary>The notification XML: the reminder scenario stays on the screen, and needs a button for that.</summary>
    internal static string Xml(string id, string title, string text) =>
        $"""
        <toast scenario="reminder" launch="{id}">
          <visual>
            <binding template="ToastGeneric">
              <text>{SecurityElement.Escape(title)}</text>
              <text>{SecurityElement.Escape(text)}</text>
            </binding>
          </visual>
          <actions>
            <action content="Show" arguments="{id}"/>
            <action content="Dismiss" arguments="dismiss" activationType="system"/>
          </actions>
        </toast>
        """;

    [ExcludeFromCodeCoverage(Justification = "Shows a real Windows notification; tests replace Send.")]
    static bool SendToWindows(string id, string title, string text)
    {
        try
        {
            Register();
            var doc = new XmlDocument();
            doc.LoadXml(Xml(id, title, text));
            var toast = new ToastNotification(doc) { Tag = id };
            // the toast is kept with its action, so its click handler lives as long as the notification may be clicked
            toast.Activated += (_, _) => Dispatcher.UIThread.Post(() => Activated(id));
            toast.Dismissed += (_, e) =>
            {
                if (e.Reason == ToastDismissalReason.UserCanceled) Dispatcher.UIThread.Post(() => Shown.Remove(id));
            };
            Shown[id] = (toast, Shown[id].Click);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("Windows notification: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Tells Windows PinList's name and icon for its notifications (once per run; an app that isn't installed
    /// from the Store needs this). The icon is copied next to data.json.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Writes to the real registry; only the real notifications use it.")]
    static void Register()
    {
        if (_registered) return;
        var icon = Path.Combine(Store.Dir, "notification-icon.png");
        if (!File.Exists(icon))
        {
            Directory.CreateDirectory(Store.Dir);
            using var source = AssetLoader.Open(new Uri("avares://PinList/Assets/icon-256.png"));
            using var target = File.Create(icon);
            source.CopyTo(target);
        }
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
        key.SetValue("DisplayName", "PinList");
        key.SetValue("IconUri", icon);
        _registered = true;
    }
}
#endif
