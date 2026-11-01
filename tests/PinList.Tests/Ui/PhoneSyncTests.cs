using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>Google sync on the phone (spec 9.10, 9.11).</summary>
public partial class PhoneTests
{
    readonly FakeAuth _phoneAuth = new() { UnavailableText = "Sync isn't available on this phone." };
    readonly FakeCloud _phoneCloud = new();

    PhoneView ShowWithSync()
    {
        _phoneCloud.Authorized = () => _phoneAuth.TokenValid;
        Show();
        _view.Sync = new SyncService(_phoneAuth, () => _phoneCloud, "Pixel 9");
        Pump();
        return _view;
    }

    void OpenAppMenu() => Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);

    void PhoneSignIn()
    {
        OpenAppMenu();
        Choose("Google sync");
        Choose("Sign in with Google");
        Assert.NotNull(_view.Sync!.Account);
    }

    PathIcon CardSyncIcon(Board b) => Find<PathIcon>(Card(b), p => p.Classes.Contains("sync"))!;
    Button HeaderSyncBtn() => Find<Button>(_view, x => x.Name == "BoardSyncBtn")!;

    static bool Is(Control c, string key) => ReferenceEquals(c.FindResource(key), c is PathIcon p ? p.Data : ((PathIcon)((Button)c).Content!).Data);

    [AvaloniaFact]
    public void No_auth_hides_google_sync()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        OpenAppMenu();
        Assert.DoesNotContain("Google sync", Options());
        Assert.True(_view.GoBack()); // closes the menu
        LongPress(Card(b));
        Assert.DoesNotContain(Options(), o => o.EndsWith("Sync this list"));
        Assert.False(CardSyncIcon(b).IsVisible);
    }

    [AvaloniaFact]
    public void Sheet_offers_sign_in_then_shows_account()
    {
        MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        OpenAppMenu();
        Assert.Equal(new[] { "Archive", "App theme", "Google sync", "Export", "Import" }, Options());
        Choose("Google sync");
        Assert.Equal("Google sync", _view.SheetTitleText);
        Assert.Equal(new[] { "Sign in with Google" }, Options());
        Choose("Sign in with Google");
        Assert.Equal("me@example.com", _view.Sync!.Account!.Email);

        OpenAppMenu();
        Choose("Google sync");
        Assert.Equal("Signed in as me@example.com · Last synced: just now", _view.SheetTitleText);
        Assert.Equal(new[] { "Sync now", "Sign out", "Sign out and delete cloud data" }, Options());
        int calls = _phoneCloud.Calls;
        Choose("Sync now");
        Assert.True(_phoneCloud.Calls > calls);

        OpenAppMenu();
        Choose("Google sync");
        Choose("Sign out and delete cloud data");
        Assert.Equal("This removes PinList's data from Google Drive for all your devices. Your lists stay on every device.", _view.SheetTitleText);
        Choose("Cancel");
        Assert.NotNull(_view.Sync.Account);
        OpenAppMenu();
        Choose("Google sync");
        Choose("Sign out and delete cloud data");
        Choose("Delete");
        Assert.Null(_view.Sync.Account);
        Assert.Equal(new[] { "reset.json" }, _phoneCloud.Names);

        PhoneSignIn();
        OpenAppMenu();
        Choose("Google sync");
        Choose("Sign out");
        Assert.Null(_view.Sync.Account);
    }

    [AvaloniaFact]
    public void Phone_without_google_play_says_so()
    {
        _phoneAuth.IsAvailable = false;
        ShowWithSync();
        OpenAppMenu();
        Choose("Google sync");
        Assert.Equal("Sync isn't available on this phone.", _view.SheetTitleText);
        Assert.Equal(new[] { "OK" }, Options());
        Choose("OK");
    }

    [AvaloniaFact]
    public void Sync_this_list_toggles_and_card_shows_icon()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        b.Title = "Groceries";
        ShowWithSync();
        Assert.False(CardSyncIcon(b).IsVisible); // signed out: no icon
        PhoneSignIn();
        Assert.True(CardSyncIcon(b).IsVisible);
        Assert.True(Is(CardSyncIcon(b), "Icon.CloudOff"));
        Assert.Equal("Not synced", AutomationProperties.GetName(CardSyncIcon(b)));

        LongPress(Card(b));
        Assert.Equal(new[] { "Open", "Color", "Sync this list", "Delete list" }, Options());
        Choose("Sync this list");
        Assert.True(b.Synced);
        Assert.Contains(_phoneCloud.Names, n => n.StartsWith($"w-{b.Id}."));
        Assert.True(Is(CardSyncIcon(b), "Icon.CloudCheck"));

        _view.ShowBoard(b);
        Pump();
        Assert.True(HeaderSyncBtn().IsVisible);
        Assert.True(Is(HeaderSyncBtn(), "Icon.CloudCheck"));
        Assert.Equal("Synced · just now", AutomationProperties.GetName(HeaderSyncBtn()));
        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Assert.Contains("✓ Sync this list", Options());
        Choose("✓ Sync this list");
        Assert.False(b.Synced);
        Assert.True(Is(HeaderSyncBtn(), "Icon.CloudOff"));
    }

    [AvaloniaFact]
    public void Sync_this_list_when_signed_out_signs_in_first()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        LongPress(Card(b));
        Choose("Sync this list");
        Assert.NotNull(_view.Sync!.Account);
        Assert.True(b.Synced);
    }

    [AvaloniaFact]
    public void Problem_icon_explains_and_signs_in_again()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        PhoneSignIn();
        LongPress(Card(b));
        Choose("Sync this list");
        _view.ShowBoard(b);
        _phoneAuth.TokenValid = false;
        Await(_view.Sync!.SyncNow());
        Pump();
        Assert.True(Is(HeaderSyncBtn(), "Icon.CloudProblem"));
        Click(HeaderSyncBtn());
        Assert.Equal("Google signed you out. Sign in again to keep syncing.", _view.SheetTitleText);
        Choose("Sign in again");
        Assert.True(Is(HeaderSyncBtn(), "Icon.CloudCheck"));

        _phoneCloud.Offline = true;
        for (int i = 0; i < 5; i++) Await(_view.Sync.SyncNow());
        Pump();
        Click(HeaderSyncBtn());
        Assert.Equal("Can't reach Google Drive. PinList keeps trying, and your changes stay on this phone until it works again.", _view.SheetTitleText);
        Choose("OK");
        _phoneCloud.Offline = false;

        Await(_view.Sync.SyncNow());
        Pump();
        Click(HeaderSyncBtn()); // a working icon only shows its state
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Remote_changes_wait_while_something_is_held_or_dragged()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        var other = new TodoItem();
        Assert.False(_view.Sync!.IsBusy(other));
        var p = At(Card(b));
        _window.MouseDown(p, Avalonia.Input.MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150)); // held: the card is lifted
        Assert.True(_view.Sync.IsBusy(other));
        _window.MouseUp(p, Avalonia.Input.MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
        Assert.False(_view.Sync.IsBusy(other));
    }

    [AvaloniaFact]
    public void Deleting_a_synced_list_mentions_other_devices()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        b.Title = "Groceries";
        b.Synced = true;
        Show();
        LongPress(Card(b));
        Choose("Delete list");
        Assert.Equal("Delete \"Groceries\" with 1 items? They don't go to the archive. It will also be deleted from your other devices.",
            _view.SheetTitleText);
    }

    [AvaloniaFact]
    public void Remote_change_updates_the_open_list()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        PhoneSignIn();
        LongPress(Card(b));
        Choose("Sync this list");
        _view.ShowBoard(b);
        Pump();

        // the PC edits the item and adds a list
        var pc = Store.Parse(Store.Serialize(Store.Data), out _);
        pc.SyncInfo = new DeviceInfo { DeviceId = "pc" };
        pc.Boards[0].Categories[0].Items[0].Text = "edited on the PC";
        var newList = new Board { Title = "From the PC", Synced = true };
        newList.Categories.Add(new Category { Name = "General" });
        pc.Boards.Add(newList);
        ChangeTracker.StampChanges(pc, DateTime.UtcNow.AddMinutes(1));
        foreach (var l in pc.Boards)
            _phoneCloud.Put($"w-{l.Id}.pc.json", ListCopy.ToJson(ListCopy.From(l, pc.Archive, pc.SyncInfo, "PC")));

        Await(_view.Sync!.SyncNow());
        Pump();
        Assert.NotNull(Find<TextBlock>(_view, t => t.Text == "edited on the PC" && t.IsEffectivelyVisible));
        Assert.Contains(_view.CardList, c => c.Board.Title == "From the PC");
        Assert.Equal(SyncStatus.UpToDate, _view.Sync.StateOf(_view.CardList.Single(c => c.Board.Title == "From the PC").Board).Status);
    }
}

/// <summary>The phone app starts sync when the platform gives it a Google sign-in.</summary>
public class PhoneAppSyncTests : UiTest
{
    [AvaloniaFact]
    public void Start_with_a_sign_in_starts_sync()
    {
        PhoneApp.DataDir = Path.Combine(Dir, "phone");
        var auth = new FakeAuth();
        var cloud = new FakeCloud();
        PhoneApp.Auth = auth;
        PhoneApp.Cloud = () => cloud;
        PhoneApp.DeviceName = "Pixel 9";
        int settings = 0;
        PhoneApp.SyncSettingsChanged = () => settings++;
        var app = new PhoneApp();
        try
        {
            var view = app.Start();
            Assert.NotNull(view.Sync);
            Assert.Same(view.Sync, SyncService.Current);
            Assert.True(Await(view.Sync.SignIn(CancellationToken.None)));
            Assert.True(settings > 0); // Android re-plans its background sync

            // a background job while the screen is open runs on the screen's thread, with its data
            Assert.NotNull(PhoneApp.OnUiThread);
            var onUi = PhoneApp.OnUiThread!.Invoke(() => Task.FromResult(Avalonia.Threading.Dispatcher.UIThread.CheckAccess()));
            Assert.True(UiHost.WaitUntil(() => onUi.IsCompleted));
            Assert.True(onUi.Result);

            var first = view.Sync;
            app.Start(); // the activity was made again
            Assert.NotSame(first, app.View!.Sync);

            PhoneApp.Auth = null; // a phone build without sync
            app.Start();
            Assert.Null(app.View!.Sync);
        }
        finally
        {
            PhoneApp.DataDir = null;
            PhoneApp.Auth = null;
            PhoneApp.Cloud = null;
            PhoneApp.SyncSettingsChanged = null;
            PhoneApp.OnUiThread = null;
            app.ReminderService?.Stop();
            App.ConfigureCore(); // the desktop app's settings back
            Store.UseDirectory(Dir);
            Store.Saver = Saver;
        }
    }

    static T Await<T>(Task<T> t) => t.GetAwaiter().GetResult();


}

/// <summary>Android makes a new screen when PinList is opened again after Back; the same view moves into it.</summary>
public class PhoneAppScreenTests : UiTest
{
    [AvaloniaFact]
    public void Opening_again_after_back_moves_the_view_into_the_new_screen()
    {
        PhoneApp.DataDir = Path.Combine(Dir, "phone");
        var app = new PhoneApp();
        Window? first = null, second = null;
        try
        {
            first = new Window { Content = app.MainView(), Width = 400, Height = 700 };
            first.Show();
            Pump();
            var view = app.View!;

            // the old screen is still there (Android destroys it later) when the new one asks for the view
            second = new Window { Content = app.MainView(), Width = 400, Height = 700 };
            second.Show();
            Pump();
            Assert.Same(view, app.View); // the same data and state, not a second copy
            Assert.Same(second, TopLevel.GetTopLevel(view));
        }
        finally
        {
            first?.Close();
            second?.Close();
            PhoneApp.DataDir = null;
            app.ReminderService?.Stop();
            App.ConfigureCore();
            Store.UseDirectory(Dir);
            Store.Saver = Saver;
        }
    }
}

