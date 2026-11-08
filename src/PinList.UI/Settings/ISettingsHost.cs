using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PinList;

/// <summary>One button of the sync box (Sync now, Sign out, ...).</summary>
public sealed record SettingsAction(string Text, Action Run, bool Danger = false, bool Enabled = true);

/// <summary>Null Account means signed out. Problem is shown in red instead of Status.</summary>
public sealed record SyncSummary(string? Account, string Status, string? Problem, IReadOnlyList<SettingsAction> Actions);

/// <summary>
/// What each platform gives the shared Settings page: the desktop wraps the tray menu's settings, the phone wraps the
/// sheets of its old ⋮ menu. A row the platform does not have is hidden (null or false).
/// </summary>
public interface ISettingsHost
{
    /// <summary>"System", "Light" or "Dark".</summary>
    string Theme { get; }
    void SetTheme(string key);

    /// <summary>Null: sync isn't on this platform or build.</summary>
    SyncSummary? Sync { get; }

    /// <summary>Shown instead of the sync box content when sync exists but can't run in this build.</summary>
    string? UnavailableSyncText { get; }

    /// <summary>OsText.Autostart on the desktop, null on the phone (the row is hidden).</summary>
    string? AutostartText { get; }
    bool AutostartOn { get; }
    bool CanChangeAutostart { get; }
    void SetAutostart(bool on);

    /// <summary>Null: no update check in this build (the row is hidden).</summary>
    bool? UpdatesOn { get; }
    void SetUpdates(bool on);

    /// <summary>
    /// Why reminders don't show as they should (macOS: notifications off, not Alerts, not in Notification Center);
    /// null hides the row.
    /// </summary>
    string? ReminderProblem { get; }

    /// <summary>Opens the system's notification settings for PinList (the button of the reminder row).</summary>
    void OpenNotificationSettings();

    /// <summary>"You're on 2.4.0, the newest" or "PinList 2.5.0 is out".</summary>
    string VersionText { get; }

    /// <summary>
    /// Opens the download of the newer version (a Get it button next to Check for updates). Null while there is
    /// none, and on the phone, which has its own bar for it at the top of Settings.
    /// </summary>
    Action? GetUpdate { get; }

    Task Export();
    Task Import();

    bool HasDataFolder { get; }
    void OpenDataFolder();

    /// <summary>OsText.Exit on the desktop, null on the phone.</summary>
    string? ExitText { get; }
    void Exit();

    /// <summary>Something shown on the page changed: the page reads everything again.</summary>
    event Action? Changed;
}
