using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PinList.Sync;

namespace PinList;

/// <summary>
/// What the Settings page of the PinList window shows on the desktop. It does what the tray menu's settings
/// did before: theme, Google sync, start with the computer, update check, export, import and the data folder.
/// </summary>
sealed class DesktopSettingsHost : ISettingsHost, IDisposable
{
    readonly App _app;
    SyncService? _syncService;
    Updates? _updates;

    public DesktopSettingsHost(App app)
    {
        _app = app;
        App.ThemeChanged += RaiseChanged;
        ReminderSettings.Changed += RaiseChanged;
    }

    public event Action? Changed;

    void RaiseChanged() => Changed?.Invoke();

    /// <summary>Stops listening (the PinList window closed).</summary>
    public void Dispose()
    {
        App.ThemeChanged -= RaiseChanged;
        ReminderSettings.Changed -= RaiseChanged;
        if (_syncService != null) _syncService.Changed -= RaiseChanged;
        if (_updates != null) _updates.Changed -= RaiseChanged;
        _syncService = null;
        _updates = null;
    }

    /// <summary>The sync service and the update check are replaced now and then (on start, in tests): follow the current ones.</summary>
    void Follow()
    {
        if (!ReferenceEquals(_syncService, SyncService.Current))
        {
            if (_syncService != null) _syncService.Changed -= RaiseChanged;
            _syncService = SyncService.Current;
            if (_syncService != null) _syncService.Changed += RaiseChanged;
        }
        if (!ReferenceEquals(_updates, _app.UpdateCheck))
        {
            if (_updates != null) _updates.Changed -= RaiseChanged;
            _updates = _app.UpdateCheck;
            if (_updates != null) _updates.Changed += RaiseChanged;
        }
    }

    public string Theme => Store.Data.Settings.Theme;

    public void SetTheme(string key) => _app.SetTheme(key);

    public SyncSummary? Sync
    {
        get
        {
            Follow();
            if (_syncService is not { IsAvailable: true } sync) return null;
            if (sync.Account is not { } account)
                return new SyncSummary(null, "", null, new[] { new SettingsAction("Sign in with Google", () => _ = _app.SignIn(null)) });

            string status = sync.IsSyncing ? "Syncing" : $"Synced {SyncIcon.Ago(sync.LastSyncUtc, DateTime.UtcNow)}";
            if (sync.LastSyncUtc == null && !sync.IsSyncing) status = "Not synced yet";
            string? problem = sync.NeedsSignIn ? "Google signed you out. Sign in again to keep syncing." : null;
            var actions = new List<SettingsAction>();
            if (sync.NeedsSignIn) actions.Add(new SettingsAction("Sign in again", () => _ = _app.SignIn(null)));
            actions.Add(new SettingsAction("Sync now", () => _ = sync.SyncNow()));
            actions.Add(new SettingsAction("Sign out", () => _ = sync.SignOut(false)));
            actions.Add(new SettingsAction("Sign out and delete cloud data", () => _ = _app.SignOutAndDeleteCloudData(null), Danger: true));
            return new SyncSummary(account.Email, status, problem, actions);
        }
    }

    public string? UnavailableSyncText
    {
        get
        {
            Follow();
            return _syncService is { IsAvailable: false } sync ? sync.UnavailableText : null;
        }
    }

    public string? AutostartText => OsText.Autostart;
    public bool AutostartOn => Autostart.IsEnabled();
    public bool CanChangeAutostart => App.ManageAutostart;
    public void SetAutostart(bool on) => Autostart.Set(on);

    public bool? UpdatesOn
    {
        get
        {
            Follow();
            return _updates == null ? null : _updates.Enabled == true;
        }
    }

    public void SetUpdates(bool on)
    {
        if (_app.UpdateCheck is { } updates) updates.Enabled = on;
    }

    /// <summary>The last known answer; reading it also asks the system again (a change refreshes the page).</summary>
    public string? ReminderProblem
    {
        get
        {
            ReminderSettings.Check();
            return ReminderSettings.Problem;
        }
    }

    public void OpenNotificationSettings() => ReminderSettings.Open();

    public string VersionText
    {
        get
        {
            var updates = _app.UpdateCheck;
            if (updates?.Available is { } update) return $"PinList {update.Version.ToString(3)} is out";
            var current = updates?.Current ?? typeof(App).Assembly.GetName().Version!;
            return $"You're on {current.ToString(3)}" + (updates?.Enabled == true ? ", the newest" : "");
        }
    }

    public Action? GetUpdate => _app.UpdateCheck?.Available != null ? _app.GetUpdate : null;

    public Task Export() => _app.ExportData(null);
    public Task Import() => _app.ImportData(null);

    public bool HasDataFolder => true;
    public void OpenDataFolder() => App.OpenFolder(Store.Dir);

    public string? ExitText => OsText.Exit;
    public void Exit() => _app.ExitApp();
}
