using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PinList.Sync;

namespace PinList;

/// <summary>
/// What the Settings tab shows on the phone. The same choices as the old ⋮ menu had: theme, Google sync, update check,
/// export and import. The phone has no autostart, data folder or exit row.
/// </summary>
sealed class PhoneSettingsHost : ISettingsHost
{
    readonly PhoneView _view;
    readonly Action<Action> _confirmDeleteCloudData;
    SyncService? _sync;
    Updates? _updates;

    /// <param name="confirmDeleteCloudData">Asks before the cloud data is deleted and runs the given action on yes.</param>
    public PhoneSettingsHost(PhoneView view, Action<Action> confirmDeleteCloudData)
    {
        _view = view;
        _confirmDeleteCloudData = confirmDeleteCloudData;
    }

    public event Action? Changed;

    /// <summary>The view calls this when sync, the update check or the theme changed.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    void Follow()
    {
        if (!ReferenceEquals(_sync, _view.Sync))
        {
            if (_sync != null) _sync.Changed -= RaiseChanged;
            _sync = _view.Sync;
            if (_sync != null) _sync.Changed += RaiseChanged;
        }
        if (!ReferenceEquals(_updates, _view.UpdateCheck))
        {
            if (_updates != null) _updates.Changed -= RaiseChanged;
            _updates = _view.UpdateCheck;
            if (_updates != null) _updates.Changed += RaiseChanged;
        }
    }

    public string Theme => Store.Data.Settings.Theme;

    public void SetTheme(string key) => PhoneView.SetTheme(key);

    public SyncSummary? Sync
    {
        get
        {
            Follow();
            if (_sync is not { IsAvailable: true } sync) return null;
            if (sync.Account is not { } account)
                return new SyncSummary(null, "", null, new[] { new SettingsAction("Sign in with Google", () => _ = sync.SignIn(CancellationToken.None)) });

            string status = sync.IsSyncing ? "Syncing"
                : sync.LastSyncUtc == null ? "Not synced yet"
                : $"Synced {SyncIcon.Ago(sync.LastSyncUtc, DateTime.UtcNow)}";
            string? problem = sync.NeedsSignIn ? "Google signed you out. Sign in again to keep syncing." : null;
            var actions = new List<SettingsAction>();
            if (sync.NeedsSignIn) actions.Add(new SettingsAction("Sign in again", () => _ = sync.SignIn(CancellationToken.None)));
            actions.Add(new SettingsAction("Sync now", () => _ = sync.SyncNow()));
            actions.Add(new SettingsAction("Sign out", () => _ = sync.SignOut(false)));
            actions.Add(new SettingsAction("Sign out and delete cloud data", () => _confirmDeleteCloudData(() => _ = sync.SignOut(true)), Danger: true));
            return new SyncSummary(account.Email, status, problem, actions);
        }
    }

    public string? UnavailableSyncText
    {
        get
        {
            Follow();
            return _sync is { IsAvailable: false } sync ? sync.UnavailableText : null;
        }
    }

    public string? AutostartText => null;
    public bool AutostartOn => false;
    public bool CanChangeAutostart => false;
    public void SetAutostart(bool on) { }

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
        if (_view.UpdateCheck is { } updates) updates.Enabled = on;
    }

    /// <summary>The phone's reminders are Android notifications; Android shows its own hints.</summary>
    public string? ReminderProblem => null;

    public void OpenNotificationSettings() { }

    public string VersionText
    {
        get
        {
            var updates = _view.UpdateCheck;
            if (updates?.Available is { } update) return $"PinList {update.Version.ToString(3)} is out";
            return updates == null ? "PinList" : $"You're on {updates.Current.ToString(3)}" + (updates.Enabled == true ? ", the newest" : "");
        }
    }

    public Task Export() => _view.Export();
    public Task Import() => _view.Import();

    public bool HasDataFolder => false;
    public void OpenDataFolder() { }

    public string? ExitText => null;
    public void Exit() { }
}
