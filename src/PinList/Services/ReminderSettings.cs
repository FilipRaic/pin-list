using System;
using System.Collections.Generic;

namespace PinList;

/// <summary>
/// Whether the system shows reminders the way PinList means them: on macOS as Alerts that stay until closed and are
/// kept in Notification Center. An app can't change these settings, so Settings shows what's off with a button to
/// the system's notification settings. (On Windows PinList's notifications always stay, so nothing is checked.)
/// </summary>
static class ReminderSettings
{
    /// <summary>What's off, for the Settings page; null when everything is fine or not known yet.</summary>
    public static string? Problem { get; private set; }

    /// <summary><see cref="Problem"/> changed.</summary>
    public static event Action? Changed;

    /// <summary>Asks the system again; the answer comes later through <see cref="Report"/>. Tests replace it.</summary>
    internal static Action Check { get; set; } = CheckSystem;

    static void CheckSystem()
    {
#if !WINDOWS
        MacNotifications.CheckSettings();
#endif
    }

    /// <summary>The system answered (on the UI thread).</summary>
    internal static void Report(string? problem)
    {
        if (problem == Problem) return;
        Problem = problem;
        Changed?.Invoke();
    }

    /// <summary>
    /// The text for macOS' notification settings: <paramref name="status"/> is UNAuthorizationStatus (0 not asked,
    /// 1 denied), <paramref name="center"/> UNNotificationSetting (1 off) and <paramref name="style"/> UNAlertStyle
    /// (0 none, 1 banners, 2 alerts). The style that stays is called "Alerts" in System Settings before macOS 15 and
    /// "Persistent" since, so the text says "Persistent Alerts".
    /// </summary>
    public static string? Describe(long status, long center, long style)
    {
        if (status == 0) return null;
        if (status == 1) return "Notifications are off, so reminders show in PinList's own window.";
        var parts = new List<string>();
        if (style == 0) parts.Add("don't show on the screen (choose Persistent Alerts)");
        else if (style == 1) parts.Add("go away after a few seconds (choose Persistent Alerts)");
        if (center == 1) parts.Add("aren't kept in Notification Center");
        return parts.Count == 0 ? null : "Reminders " + string.Join(" and ", parts) + ".";
    }

    /// <summary>Opens PinList's page of the system's notification settings.</summary>
    public static void Open() => App.OpenUrl(SettingsUrl(OperatingSystem.IsMacOS(), Environment.OSVersion.Version.Major));

    internal static string SettingsUrl(bool mac, int macMajor) =>
        !mac ? "ms-settings:notifications"
        : macMajor >= 13 ? "x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=com.pinlist.app"
        : "x-apple.systempreferences:com.apple.preference.notifications";
}
