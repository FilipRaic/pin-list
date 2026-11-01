using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>Google sync in the desktop app: tray submenu, the widget's menu item and header icon.</summary>
public class SyncUiTests : UiTest
{
    readonly FakeAuth _auth = new();
    readonly FakeCloud _cloud = new();

    SyncService UseFakeSync()
    {
        _cloud.Authorized = () => _auth.TokenValid;
        var s = new SyncService(_auth, () => _cloud, "This PC");
        App.UseSync(s);
        return s;
    }

    static Button SyncBtn(BoardWindow w) => Named<Button>(w, "SyncBtn");

    static string? IconKeyOf(BoardWindow w)
    {
        var data = ((PathIcon)SyncBtn(w).Content!).Data;
        return new[] { "Icon.CloudCheck", "Icon.CloudOff", "Icon.Sync", "Icon.CloudWaiting", "Icon.CloudProblem" }
            .FirstOrDefault(k => ReferenceEquals(w.FindResource(k), data));
    }

    static IEnumerable<MenuItem> SyncSubmenu()
    {
        Tray.ClickRight();
        return MenuItem(App.LastTrayMenu!, "Google sync").Items.OfType<MenuItem>();
    }

    ContextMenu WindowMenu(BoardWindow w)
    {
        Click(Named<Button>(w, "MenuBtn"));
        return w.LastMenu!;
    }

    void SignIn()
    {
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sign in with Google");
        Assert.NotNull(SyncService.Current!.Account);
    }

    [AvaloniaFact]
    public void Fresh_install_has_no_sync_icon_and_tray_offers_sign_in()
    {
        var s = UseFakeSync();
        var w = OpenNew(("A", new[] { "a" }));
        Assert.False(SyncBtn(w).IsVisible);
        Assert.Equal(new[] { "Sign in with Google" }, SyncSubmenu().Select(Header));
        App.LastTrayMenu!.Close();

        SignIn();
        Assert.Contains("waiting for sign-in", Dialogs.Shown);
        Assert.Equal("me@example.com", s.Account!.Email);
        Assert.False(w.Board.Synced); // signing in turns nothing on
        Assert.True(SyncBtn(w).IsVisible);
        Assert.Equal("Icon.CloudOff", IconKeyOf(w));
        Assert.Equal("Not synced", ToolTip.GetTip(SyncBtn(w)));
        Assert.Equal("Not synced", AutomationProperties.GetName(SyncBtn(w)));
    }

    [AvaloniaFact]
    public void Unavailable_build_shows_the_disabled_entry()
    {
        var w = OpenNew(("A", new[] { "a" })); // the test build has no Google client
        var only = SyncSubmenu().Single();
        Assert.Equal("Sync isn't available in this build.", Header(only));
        Assert.False(only.IsEnabled);
        Assert.DoesNotContain(WindowMenu(w).Items.OfType<MenuItem>(), m => Header(m) == "Sync this window");
        Assert.False(SyncBtn(w).IsVisible);
    }

    [AvaloniaFact]
    public void Signed_in_tray_shows_account_last_sync_and_actions()
    {
        var s = UseFakeSync();
        SignIn();
        var items = SyncSubmenu().ToList();
        Assert.Equal(new[] { "Signed in as me@example.com", "Last synced: just now", "Sync now", "Sign out", "Sign out and delete cloud data" },
            items.Select(Header));
        Assert.False(items[0].IsEnabled);
        Assert.False(items[1].IsEnabled);
        Assert.Contains(Tray.Menu, e => e.Text == "Signed in as me@example.com" && !e.Enabled); // macOS menu bar too
        App.LastTrayMenu!.Close();

        int calls = _cloud.Calls;
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.True(_cloud.Calls > calls);

        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sign out and delete cloud data"); // Esc
        Assert.Contains(Dialogs.Shown, d => d.Contains("This removes PinList's data from Google Drive for all your devices. Your lists stay on every device."));
        Assert.NotNull(s.Account);
        Dialogs.ChooseAnswer = _ => 1;
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sign out and delete cloud data");
        Assert.Null(s.Account);
        Assert.Equal(new[] { "reset.json" }, _cloud.Names);

        SignIn();
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sign out");
        Assert.Null(s.Account);
        Assert.Equal(2, _auth.SignOuts);
    }

    [AvaloniaFact]
    public void Sync_this_window_turns_sync_on_and_shows_the_check_icon()
    {
        UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        var item = MenuItem(WindowMenu(w), "Sync this window");
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);
        ChooseFromMenu(w.LastMenu!, "Sync this window");

        Assert.True(w.Board.Synced);
        Assert.Contains(_cloud.Names, n => n.StartsWith($"w-{w.Board.Id}."));
        Assert.Equal("Icon.CloudCheck", IconKeyOf(w));
        Assert.Equal("Synced · just now", ToolTip.GetTip(SyncBtn(w)));

        Assert.True(MenuItem(WindowMenu(w), "Sync this window").IsChecked);
        ChooseFromMenu(w.LastMenu!, "Sync this window");
        Assert.False(w.Board.Synced);
        Assert.Equal("Icon.CloudOff", IconKeyOf(w));
    }

    [AvaloniaFact]
    public void Sync_this_window_when_signed_out_signs_in_first()
    {
        var s = UseFakeSync();
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WindowMenu(w), "Sync this window");
        Assert.Contains("waiting for sign-in", Dialogs.Shown);
        Assert.NotNull(s.Account);
        Assert.True(w.Board.Synced);

        // the user cancels the sign-in: nothing changes
        var other = OpenNew(("B", new string[0]));
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sign out");
        _auth.NextAccount = null;
        ChooseFromMenu(WindowMenu(other), "Sync this window");
        Assert.False(other.Board.Synced);
    }

    [AvaloniaFact]
    public void Clicking_the_local_icon_offers_to_turn_sync_on()
    {
        UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        Click(SyncBtn(w));
        ChooseFromMenu(w.LastMenu!, "Turn on sync for this window");
        Assert.True(w.Board.Synced);

        Click(SyncBtn(w)); // synced: the menu offers a sync now
        int calls = _cloud.Calls;
        ChooseFromMenu(w.LastMenu!, "Sync now");
        Assert.True(_cloud.Calls > calls);
    }

    [AvaloniaFact]
    public void Header_icon_follows_the_status()
    {
        var s = UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WindowMenu(w), "Sync this window");
        Assert.Equal("Icon.CloudCheck", IconKeyOf(w));

        _cloud.Gate = new TaskCompletionSource();
        var pass = s.SyncNow();
        Pump();
        Assert.Equal("Icon.Sync", IconKeyOf(w));
        Assert.Equal("Syncing…", AutomationProperties.GetName(SyncBtn(w)));
        Assert.Contains("spinning", SyncBtn(w).Classes);
        var gate = _cloud.Gate;
        _cloud.Gate = null;
        gate.SetResult();
        Assert.True(UiHost.WaitUntil(() => pass.IsCompleted));
        Pump();

        _cloud.Offline = true;
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.Equal("Icon.CloudWaiting", IconKeyOf(w));
        Assert.Equal("Offline · changes will sync later", ToolTip.GetTip(SyncBtn(w)));
        Assert.DoesNotContain("danger", SyncBtn(w).Classes);
        _cloud.Offline = false;
    }

    [AvaloniaFact]
    public void Problem_is_announced_and_click_explains_it()
    {
        UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WindowMenu(w), "Sync this window");

        _auth.TokenValid = false;
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.Equal("Icon.CloudProblem", IconKeyOf(w));
        Assert.Contains("danger", SyncBtn(w).Classes);
        Assert.Equal("Google signed you out.", AutomationProperties.GetName(SyncBtn(w)));
        Assert.Equal("Sync problem: Google signed you out.", A11y.LastAnnouncement);

        Click(SyncBtn(w)); // Esc: not now
        Assert.Contains("Sync stopped: Google signed you out. Sign in again to keep syncing.", Dialogs.Shown);
        Assert.Equal("Icon.CloudProblem", IconKeyOf(w));
        Dialogs.ChooseAnswer = _ => 1; // Sign in again
        Click(SyncBtn(w));
        Assert.Equal("Icon.CloudCheck", IconKeyOf(w));

        _cloud.Offline = true;
        for (int i = 0; i < 5; i++) ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.Equal("Icon.CloudProblem", IconKeyOf(w));
        Dialogs.Shown.Clear();
        Click(SyncBtn(w));
        Assert.Equal("Sync problem: Can't reach Google Drive. PinList keeps trying, and your changes stay on this device until it works again.",
            Dialogs.Shown.Single());
        _cloud.Offline = false;
    }

    [AvaloniaFact]
    public void Lists_from_other_devices_open_and_close_their_windows()
    {
        var s = UseFakeSync();
        SignIn();
        // another device turns sync on for a list
        var other = new AppData { SyncInfo = new DeviceInfo { DeviceId = "other" } };
        var b = new Board { Title = "From the Mac", Synced = true };
        b.Categories.Add(new Category { Name = "General" });
        other.Boards.Add(b);
        ChangeTracker.StampChanges(other, DateTime.UtcNow);
        var copy = ListCopy.From(b, other.Archive, other.SyncInfo, "Mac");
        _cloud.Put($"w-{b.Id}.other.json", ListCopy.ToJson(copy));

        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        var w = App.OpenWindows.Single(x => x.Board.Id == b.Id);
        Assert.True(w.IsVisible);
        Assert.Equal("Icon.CloudCheck", IconKeyOf(w));

        // and deletes it there
        _cloud.Put($"w-{b.Id}.other.json", ListCopy.ToJson(copy.ToDeletedMarker(other.SyncInfo, DateTime.UtcNow)));
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.DoesNotContain(App.OpenWindows, x => x.Board.Id == b.Id);
        UiHost.Wait(TimeSpan.FromMilliseconds(300));
        Assert.DoesNotContain(App.OpenWindows, x => x.Board.Id == b.Id); // not reopened
    }

    [AvaloniaFact]
    public void Messages_from_sync_are_shown()
    {
        UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WindowMenu(w), "Sync this window");
        _cloud.Put("reset.json", """{ "device": "other" }""");
        ChooseFromMenu(App.ShowTrayMenu(), "Google sync", "Sync now");
        Assert.Contains("PinList: Sync was turned off because the cloud data was deleted from another device.", Dialogs.Shown);
        Assert.False(w.Board.Synced);
    }

    [AvaloniaFact]
    public void Exit_sends_the_last_changes()
    {
        UseFakeSync();
        SignIn();
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WindowMenu(w), "Sync this window");
        w.Board.Categories[0].Items[0].Text = "at exit";
        App.ExitApp();
        Assert.True(UiHost.WaitUntil(() => UiHost.ShutdownRequests == 1));
        Assert.Contains("at exit", _cloud.Read(_cloud.Names.Single()));
        App.ShuttingDown = false;
    }

    [AvaloniaFact]
    public void Sign_in_dialog_waits_and_can_be_cancelled()
    {
        var done = new TaskCompletionSource<bool>();
        CancellationToken token = default;
        var waiting = new AppDialogs().WaitForSignIn(null, ct => { token = ct; return done.Task; });
        Pump();
        var dlg = Assert.IsType<ConfirmDialog>(DialogHost.Current);
        Assert.Equal("Google sign-in", dlg.Title);
        Assert.False(dlg.Topmost); // the user works in the browser meanwhile: the dialog mustn't cover it
        Assert.Equal("Waiting for Google sign-in in your browser…", AutomationProperties.GetHelpText(dlg));
        Click((Button)dlg.ButtonPanel.Children.Single()); // Cancel
        Assert.True(token.IsCancellationRequested);
        done.SetResult(false);
        Assert.True(UiHost.WaitUntil(() => waiting.IsCompleted));
        Assert.False(waiting.Result);

        // signed in in the browser: the dialog closes by itself
        var ok = new TaskCompletionSource<bool>();
        waiting = new AppDialogs().WaitForSignIn(null, _ => ok.Task);
        Pump();
        Assert.NotNull(DialogHost.Current);
        ok.SetResult(true);
        Assert.True(UiHost.WaitUntil(() => waiting.IsCompleted));
        Assert.True(waiting.Result);
        Pump();
        Assert.Null(DialogHost.Current);
    }

    [AvaloniaFact]
    public void Syncs_after_waking_up_and_when_the_network_is_back()
    {
        UseFakeSync();
        SignIn();
        int calls = _cloud.Calls;
        App.OnPowerModeChanged(null, new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Resume));
        Pump();
        Assert.True(_cloud.Calls > calls);

        calls = _cloud.Calls;
        App.OnNetworkAvailabilityChanged(null, false);
        Pump();
        Assert.Equal(calls, _cloud.Calls);
        App.OnNetworkAvailabilityChanged(null, true);
        Pump();
        Assert.True(_cloud.Calls > calls);
    }
}
