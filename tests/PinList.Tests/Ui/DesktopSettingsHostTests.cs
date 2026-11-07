using Avalonia.Headless.XUnit;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>What the desktop's Settings page offers: the choices the tray menu used to have.</summary>
public class DesktopSettingsHostTests : UiTest
{
    readonly FakeAuth _auth = new();
    readonly FakeCloud _cloud = new();
    DesktopSettingsHost _host = null!;

    static void Await(Task t)
    {
        Assert.True(UiHost.WaitUntil(() => t.IsCompleted));
        t.GetAwaiter().GetResult();
    }

    DesktopSettingsHostTests UseFakeSync()
    {
        _cloud.Authorized = () => _auth.TokenValid;
        App.UseSync(new SyncService(_auth, () => _cloud, "This PC"));
        return this;
    }

    Updates UseUpdates(UpdateInfo? latest = null)
    {
        var updates = new Updates(_ => Task.FromResult(latest), new Version(2, 4, 0));
        App.UseUpdates(updates);
        return updates;
    }

    DesktopSettingsHost Host() => _host = new DesktopSettingsHost(App);

    [AvaloniaFact]
    public void Theme_is_read_and_set()
    {
        var host = Host();
        Store.Data.Settings.Theme = "Dark";
        Assert.Equal("Dark", host.Theme);
        int changes = 0;
        host.Changed += () => changes++;
        host.SetTheme("Light");
        Assert.Equal("Light", Store.Data.Settings.Theme);
        Assert.True(Saver.Requests > 0); // the choice is saved
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, App.RequestedThemeVariant);
        Assert.True(changes > 0); // the page follows
    }

    [AvaloniaFact]
    public void Reminder_settings_that_are_off_show_and_the_button_opens_the_system_settings()
    {
        var host = Host();
        var realCheck = ReminderSettings.Check;
        var realOpen = App.OpenUrl;
        var opened = new List<string>();
        int checks = 0, changes = 0;
        ReminderSettings.Check = () => checks++;
        App.OpenUrl = opened.Add;
        host.Changed += () => changes++;
        try
        {
            Assert.Null(host.ReminderProblem); // Windows: nothing to check
            Assert.Equal(1, checks);           // reading it asks the system again

            ReminderSettings.Report(ReminderSettings.Describe(2, 1, 2));
            Assert.Equal("Reminders aren't kept in Notification Center.", host.ReminderProblem);
            Assert.Equal(1, changes); // the page refreshes
            ReminderSettings.Report(host.ReminderProblem);
            Assert.Equal(1, changes); // the same answer again: no refresh (no loop)

            host.OpenNotificationSettings();
            Assert.Equal(new[] { "ms-settings:notifications" }, opened);
            host.Dispose();
            ReminderSettings.Report(null);
            Assert.Equal(1, changes); // a closed page doesn't listen any more
        }
        finally
        {
            ReminderSettings.Check = realCheck;
            App.OpenUrl = realOpen;
            ReminderSettings.Report(null);
        }
    }

    [AvaloniaFact]
    public void Reminder_settings_texts_and_addresses()
    {
        Assert.Null(ReminderSettings.Describe(0, 1, 1)); // not asked yet
        Assert.Null(ReminderSettings.Describe(2, 2, 2)); // all fine
        Assert.Equal("Notifications are off for PinList, so reminders show in PinList's own window.", ReminderSettings.Describe(1, 2, 2));
        Assert.Equal("Reminders go away after a few seconds (choose Persistent Alerts) and aren't kept in Notification Center.",
            ReminderSettings.Describe(2, 1, 1));
        Assert.Equal("Reminders don't show on the screen (choose Persistent Alerts).", ReminderSettings.Describe(3, 2, 0));

        Assert.Equal("ms-settings:notifications", ReminderSettings.SettingsUrl(mac: false, 0));
        Assert.Equal("x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=com.pinlist.app",
            ReminderSettings.SettingsUrl(mac: true, 14));
        Assert.Equal("x-apple.systempreferences:com.apple.preference.notifications", ReminderSettings.SettingsUrl(mac: true, 12));
    }

    [AvaloniaFact]
    public void A_build_without_sync_has_the_explanation()
    {
        var host = Host();
        Assert.Null(host.Sync);
        Assert.Equal("Sync isn't available in this build.", host.UnavailableSyncText);
    }

    [AvaloniaFact]
    public void Without_a_sync_service_there_is_nothing_about_sync()
    {
        var host = Host();
        SyncService.Current = null;
        Assert.Null(host.Sync);
        Assert.Null(host.UnavailableSyncText);
        App.UseSync(UiHost.UnavailableSync());
    }

    [AvaloniaFact]
    public void Signed_out_offers_sign_in_and_signing_in_shows_the_account()
    {
        UseFakeSync();
        var host = Host();
        var summary = host.Sync!;
        Assert.Null(summary.Account);
        var signIn = Assert.Single(summary.Actions);
        Assert.Equal("Sign in with Google", signIn.Text);

        int changes = 0;
        host.Changed += () => changes++;
        signIn.Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account != null));
        Assert.Contains("waiting for sign-in", Dialogs.Shown);
        Assert.True(UiHost.WaitUntil(() => changes > 0));

        summary = host.Sync!;
        Assert.Equal("me@example.com", summary.Account);
        Assert.Null(summary.Problem);
        Assert.StartsWith("Synced ", summary.Status);
        Assert.Equal(new[] { "Sync now", "Sign out", "Sign out and delete cloud data" }, summary.Actions.Select(a => a.Text));
        Assert.True(summary.Actions[2].Danger);
    }

    [AvaloniaFact]
    public void Signed_in_actions_sync_sign_out_and_delete_cloud_data()
    {
        UseFakeSync();
        var host = Host();
        host.Sync!.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account != null));

        int calls = _cloud.Calls;
        host.Sync!.Actions.Single(a => a.Text == "Sync now").Run();
        Assert.True(UiHost.WaitUntil(() => _cloud.Calls > calls));

        host.Sync!.Actions.Single(a => a.Text == "Sign out and delete cloud data").Run(); // Esc in the question
        Pump();
        Assert.Contains(Dialogs.Shown, d => d.Contains("This removes PinList's data from Google Drive for all your devices. Your lists stay on every device."));
        Assert.NotNull(SyncService.Current!.Account);

        Dialogs.ChooseAnswer = _ => 1;
        host.Sync!.Actions.Single(a => a.Text == "Sign out and delete cloud data").Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account == null));
        Assert.Equal(new[] { "reset.json" }, _cloud.Names);

        host.Sync!.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account != null));
        host.Sync!.Actions.Single(a => a.Text == "Sign out").Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account == null));
        Assert.Equal(2, _auth.SignOuts);
    }

    [AvaloniaFact]
    public void A_revoked_sign_in_offers_signing_in_again()
    {
        UseFakeSync();
        var host = Host();
        host.Sync!.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => SyncService.Current!.Account != null));

        _auth.TokenValid = false;
        Await(SyncService.Current!.SyncNow());
        var summary = host.Sync!;
        Assert.Equal("Google signed you out. Sign in again to keep syncing.", summary.Problem);
        Assert.Equal("Sign in again", summary.Actions[0].Text);
        summary.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _auth.SignIns >= 2));
    }

    [AvaloniaFact]
    public void Autostart_follows_the_registry_entry_and_the_development_copy_cannot_change_it()
    {
        Autostart.RunKey = @"Software\PinListTests\SettingsAutostart";
        try
        {
            Autostart.Set(false);
            var host = Host();
            Assert.Equal(OsText.Autostart, host.AutostartText);
            Assert.False(host.AutostartOn);
            Assert.True(host.CanChangeAutostart);
            host.SetAutostart(true);
            Assert.True(host.AutostartOn);
            host.SetAutostart(false);
            Assert.False(Autostart.IsEnabled());

            App.ManageAutostart = false;
            Assert.False(host.CanChangeAutostart);
        }
        finally
        {
            App.ManageAutostart = true;
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\PinListTests\SettingsAutostart", false);
            Autostart.RunKey = UiHost.TestRunKey;
        }
    }

    [AvaloniaFact]
    public void The_update_check_can_be_turned_on_and_off()
    {
        var host = Host();
        var updates = UseUpdates();
        Assert.False(host.UpdatesOn);
        host.SetUpdates(true);
        Assert.True(updates.Enabled);
        Assert.True(host.UpdatesOn);
        host.SetUpdates(false);
        Assert.False(updates.Enabled);

        App.UseUpdates(null); // a build without the check
        Assert.Null(host.UpdatesOn);
        host.SetUpdates(true); // nothing to turn on
    }

    [AvaloniaFact]
    public void The_version_text_says_if_a_newer_version_is_out()
    {
        var host = Host();
        var none = UseUpdates();
        Assert.Equal("You're on 2.4.0", host.VersionText); // the check is off
        none.Enabled = true;
        Await(none.CheckNow());
        Assert.Equal("You're on 2.4.0, the newest", host.VersionText);

        var found = UseUpdates(new UpdateInfo(new Version(2, 5, 0), "https://example.com/page", null));
        found.Enabled = true;
        Await(found.CheckNow());
        Assert.Equal("PinList 2.5.0 is out", host.VersionText);

        App.UseUpdates(null);
        Assert.StartsWith("You're on ", host.VersionText);
    }

    [AvaloniaFact]
    public void Export_import_the_data_folder_and_exit()
    {
        var host = Host();
        Await(host.Export());
        Await(host.Import());
        Assert.True(host.HasDataFolder);
        host.OpenDataFolder();
        Assert.Equal(new[] { Store.Dir }, UiHost.OpenedFolders);
        Assert.Equal(OsText.Exit, host.ExitText);
        host.Exit();
        Assert.True(UiHost.WaitUntil(() => UiHost.ShutdownRequests == 1));
        App.ShuttingDown = false;
    }

    [AvaloniaFact]
    public void The_host_follows_a_replaced_sync_service_and_update_check()
    {
        var host = Host(); // starts with the unavailable sync of the test build
        Assert.NotNull(host.UnavailableSyncText);
        int changes = 0;
        host.Changed += () => changes++;

        UseFakeSync();
        Assert.NotNull(host.Sync);
        Assert.Equal(0, changes);
        var other = new SyncService(new FakeAuth(), () => _cloud, "Other PC"); // replaced by another one
        App.UseSync(other);
        Assert.NotNull(host.Sync);
        SyncService.Current = null;
        Assert.Null(host.Sync);
        Assert.Null(host.UnavailableSyncText);
        App.UseSync(UiHost.UnavailableSync());

        var first = UseUpdates();
        Assert.False(host.UpdatesOn);
        var second = UseUpdates();
        Assert.False(host.UpdatesOn);
        second.Enabled = true;
        Assert.True(UiHost.WaitUntil(() => changes > 0)); // the new update check is followed
        App.UseUpdates(null);
        Assert.Null(host.UpdatesOn);
        Assert.NotNull(first);

        host.Dispose();
        host.Dispose(); // closing twice is fine
    }
}
