using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PinList.Tests;

/// <summary>The update check in the desktop app: the question, the notification and the tray menu.</summary>
public class UpdateUiTests : UiTest
{
    const string Page = "https://github.com/FilipRaic/pin-list/releases/tag/v2.3.0";
    readonly List<string> _opened = new();
    readonly Action<string> _realOpenUrl = App.OpenUrl;
    UpdateInfo? _latest;

    public UpdateUiTests() => App.OpenUrl = _opened.Add;

    public override void Dispose()
    {
        App.OpenUrl = _realOpenUrl;
        base.Dispose();
    }

    /// <summary>The app's update check, against a GitHub that answers with <see cref="_latest"/>.</summary>
    Updates UseUpdates()
    {
        var updates = new Updates(_ => Task.FromResult(_latest), new Version(2, 2, 0));
        App.UseUpdates(updates);
        return updates;
    }

    static string[] Headers(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToArray();

    [AvaloniaFact]
    public async Task The_user_is_asked_once_until_they_answer()
    {
        var updates = UseUpdates();

        await App.AskAboutUpdates(); // closed without an answer: still not decided
        Assert.StartsWith("Check for updates?: PinList can look for a new version", Assert.Single(Dialogs.Shown));
        Assert.Null(updates.Enabled);

        Dialogs.ChooseAnswer = _ => 0; // No
        await App.AskAboutUpdates();
        Assert.False(updates.Enabled);

        await App.AskAboutUpdates(); // answered: not asked again
        Assert.Equal(2, Dialogs.Shown.Count);

        Store.Data.Settings.CheckForUpdates = null;
        Dialogs.ChooseAnswer = _ => 1; // Yes
        await App.AskAboutUpdates();
        Assert.True(Store.Data.Settings.CheckForUpdates);
    }

    [AvaloniaFact]
    public void A_new_version_shows_a_notification_that_opens_its_page()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = new UpdateInfo(new Version(2, 3, 0), Page, null);
        UseUpdates();
        Pump();

        Assert.Equal(("PinList 2.3.0 is available", "Click here to open the download page."), Assert.Single(Tray.Notifications));
        Tray.ClickNotification();
        Assert.Equal(new[] { Page }, _opened);

        // a reminder after it: clicking that one is about the reminder again
        var w = OpenNew(("A", new[] { "call" }));
        App.ShowReminders(new[] { w.Board.Categories[0].Items[0] });
        Tray.ClickNotification();
        Assert.Single(_opened);
    }

    [AvaloniaFact]
    public void The_tray_menu_offers_the_new_version_first()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = new UpdateInfo(new Version(2, 3, 0), Page, null);
        UseUpdates();
        Pump();

        var menu = App.ShowTrayMenu();
        Assert.Equal("Get PinList 2.3.0", Headers(menu)[0]);
        Assert.IsType<Separator>(menu.Items[1]);
        ChooseFromMenu(menu, "Get PinList 2.3.0");
        Assert.Equal(new[] { Page }, _opened);

        // the macOS menu bar menu too
        Assert.Equal(new[] { "Get PinList 2.3.0", "-", "New window" }, Tray.Menu.Take(3).Select(e => e.Text));
        Tray.Menu[0].Action!();
        Assert.Equal(2, _opened.Count);
    }

    [AvaloniaFact]
    public void Check_for_updates_in_the_tray_menu_turns_it_on_and_off()
    {
        var updates = UseUpdates();
        var item = App.ShowTrayMenu().Items.OfType<MenuItem>().Single(m => (string)m.Header! == "Check for updates");
        Assert.False(item.IsChecked);
        Assert.False(Tray.Menu.Single(e => e.Text == "Check for updates").Checked);

        ChooseFromMenu(App.ShowTrayMenu(), "Check for updates");
        Assert.True(updates.Enabled);
        Assert.True(App.ShowTrayMenu().Items.OfType<MenuItem>().Single(m => (string)m.Header! == "Check for updates").IsChecked);
        Assert.True(Tray.Menu.Single(e => e.Text == "Check for updates").Checked);

        Tray.Menu.Single(e => e.Text == "Check for updates").Action!(); // from the menu bar menu
        Assert.False(updates.Enabled);
        Assert.DoesNotContain("Get PinList", string.Join(",", Headers(App.ShowTrayMenu())));
    }

    [AvaloniaFact]
    public void Without_a_new_version_the_page_of_the_latest_release_opens()
    {
        UseUpdates();
        App.OpenUpdatePage();
        Assert.Equal(new[] { Updates.ReleasesPage }, _opened);
    }
}

/// <summary>The update check on the phone: the question, the bar and the ⋮ menu.</summary>
public class PhoneUpdateTests : UiTest
{
    const string Apk = "https://github.com/FilipRaic/pin-list/releases/download/v2.3.0/PinList-android.apk";
    const string Page = "https://github.com/FilipRaic/pin-list/releases/tag/v2.3.0";
    Window _window = null!;
    PhoneView _view = null!;
    readonly List<string> _opened = new();
    readonly Func<TopLevel, string, Task> _realOpenUrl = PhoneView.OpenUrl;
    UpdateInfo? _latest = new(new Version(2, 3, 0), Page, Apk);

    public PhoneUpdateTests() => PhoneView.OpenUrl = (_, url) => { _opened.Add(url); return Task.CompletedTask; };

    public override void Dispose()
    {
        _window?.Close();
        PhoneView.OpenUrl = _realOpenUrl;
        base.Dispose();
    }

    Updates Show()
    {
        _view = new PhoneView();
        _window = new Window { Content = _view, Width = 400, Height = 760 };
        _window.Show();
        _view.Reload();
        var updates = new Updates(_ => Task.FromResult(_latest), new Version(2, 2, 0));
        _view.UpdateCheck = updates;
        updates.Start();
        Pump();
        return updates;
    }

    string[] Options() => _view.SheetPanel!.Children.OfType<Button>().Select(x => (string)x.Content!).ToArray();

    void Choose(string option)
    {
        Click(_view.SheetPanel!.Children.OfType<Button>().Single(x => (string)x.Content! == option));
        Pump();
    }

    void OpenMenu() => Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);

    [AvaloniaFact]
    public void The_first_start_asks_and_a_yes_finds_the_new_version()
    {
        var updates = Show();
        Assert.True(_view.SheetOpen);
        Assert.StartsWith("PinList can look for a new version about once a day", _view.SheetTitleText);
        Assert.Equal(new[] { "Check for updates", "Don't check" }, Options());

        Choose("Check for updates");
        Assert.True(updates.Enabled);
        Assert.True(_view.SnackbarShown);
        Assert.Equal("PinList 2.3.0 is available", _view.SnackbarMessage);
        Assert.Equal("Get it", Named<Button>(_view, "UndoButton").Content);

        Click(Named<Button>(_view, "UndoButton"));
        Assert.Equal(new[] { Apk }, _opened); // the APK downloads in the browser
        Assert.False(_view.SnackbarShown);
    }

    [AvaloniaFact]
    public void No_asks_again_never_and_the_menu_can_turn_it_on()
    {
        var updates = Show();
        Choose("Don't check");
        Assert.False(updates.Enabled);
        Assert.False(_view.SnackbarShown);

        OpenMenu();
        Assert.Equal(new[] { "Archive", "App theme", "Updates", "Export", "Import" }, Options());
        Choose("Updates");
        Assert.Equal(new[] { "Check about once a day", "✓ Don't check" }, Options());
        Choose("Check about once a day");
        Assert.True(updates.Enabled);

        // the new version is first in the menu, and gets the release page without an APK
        OpenMenu();
        Assert.Equal("Get PinList 2.3.0", Options()[0]);
        Choose("Updates");
        Assert.Equal(new[] { "✓ Check about once a day", "Don't check" }, Options());
        Choose("✓ Check about once a day");

        _latest = _latest! with { ApkUrl = null };
        Store.Data.Settings.CheckForUpdates = false;
        updates.Enabled = true;
        Pump();
        OpenMenu();
        Choose("Get PinList 2.3.0");
        Assert.Equal(new[] { Page }, _opened);
    }

    [AvaloniaFact]
    public void Closing_the_question_asks_again_next_time_and_an_answer_is_kept()
    {
        Show();
        Assert.True(_view.GoBack()); // Back closes it without an answer
        _view.AskAboutUpdates(); // the next start
        Assert.True(_view.SheetOpen);

        Store.Data.Settings.CheckForUpdates = false;
        Assert.True(_view.GoBack()); // Back closes it without an answer
        _view.AskAboutUpdates();
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Another_update_check_replaces_the_old_one()
    {
        var first = Show();
        _view.UpdateCheck = null;
        first.Enabled = true; // the old one is no longer listened to
        Pump();
        Assert.False(_view.SnackbarShown);
        Assert.Null(_view.UpdateCheck);
    }
}

/// <summary>The phone app creates the update check when the platform gave it a version.</summary>
public class PhoneAppUpdateTests : UiTest
{
    [AvaloniaFact]
    public void Start_gives_the_screen_an_update_check()
    {
        PhoneApp.DataDir = Path.Combine(Dir, "phone");
        var real = PhoneApp.CreateUpdates;
        var app = new PhoneApp();
        try
        {
            Assert.Null(app.Start().UpdateCheck); // no version (a test or a build without it): no check

            var updates = new Updates(_ => Task.FromResult<UpdateInfo?>(null), new Version(2, 2, 0));
            PhoneApp.CreateUpdates = () => updates;
            Assert.Same(updates, app.Start().UpdateCheck);
        }
        finally
        {
            PhoneApp.CreateUpdates = real;
            PhoneApp.DataDir = null;
            app.ReminderService?.Stop();
            App.ConfigureCore(); // the desktop app's settings back
            Store.UseDirectory(Dir);
            Store.Saver = Saver;
        }
    }
}
