using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Xunit;

namespace PinList.Tests;

/// <summary>The update check in the desktop app: the question, the notification and the tray menu.</summary>
public class UpdateUiTests : UiTest
{
    /// <summary>The Windows download of the new version (Get it downloads it).</summary>
    const string Exe = "https://github.com/FilipRaic/pin-list/releases/download/v2.3.0/PinList.exe";
    const string MacZip = "https://github.com/FilipRaic/pin-list/releases/download/v2.3.0/PinList.zip";
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
    public void A_new_version_shows_a_dialog_once_and_Get_it_opens_its_page()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe, Notes: "### Added\n- Something new");
        Dialogs.UpdateAnswer = true; // Get it
        var updates = UseUpdates();
        Pump();

        Assert.Equal(new[] { "update: 2.3.0" }, Dialogs.Shown);
        Assert.Empty(Tray.Notifications); // the dialog instead of a notification
        Assert.Equal(new[] { Exe }, _opened);

        // found again (the next day): not shown again
        _ = updates.CheckNow();
        Pump();
        Assert.Single(Dialogs.Shown);
    }

    [AvaloniaFact]
    public void A_check_that_failed_offline_is_tried_again_when_the_network_comes_back()
    {
        Store.Data.Settings.CheckForUpdates = true;
        bool online = false;
        var updates = new Updates(_ => online
            ? Task.FromResult<UpdateInfo?>(new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe))
            : throw new HttpRequestException("offline"), new Version(2, 2, 0));
        App.UseUpdates(updates); // started before the computer was online
        Pump();
        Assert.True(updates.LastCheckFailed);
        Assert.Empty(Dialogs.Shown);

        online = true;
        App.OnNetworkAvailabilityChanged(null, false); // going offline does nothing
        Pump();
        Assert.Empty(Dialogs.Shown);

        App.OnNetworkAvailabilityChanged(null, true);
        Assert.True(UiHost.WaitUntil(() => Dialogs.Shown.Count == 1));
        Assert.Equal("update: 2.3.0", Dialogs.Shown[0]);
        Assert.False(updates.LastCheckFailed);
    }

    [AvaloniaFact]
    public async Task Later_closes_the_dialog_without_opening_the_page()
    {
        UseUpdates();
        await App.ShowUpdate(new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe));
        Assert.Equal(new[] { "update: 2.3.0" }, Dialogs.Shown);
        Assert.Empty(_opened);
    }

    [AvaloniaFact]
    public void Settings_has_a_dot_and_a_Get_it_button_while_a_new_version_is_out()
    {
        var updates = UseUpdates();
        App.ShowSettings();
        Pump();
        var main = App.MainWindow!;
        var dot = Named<Avalonia.Controls.Shapes.Ellipse>(main, "UpdateDot");
        var nav = Named<Button>(main, "NavSettings");
        Assert.False(dot.IsVisible);
        Assert.Equal("Settings", Avalonia.Automation.AutomationProperties.GetName(nav));
        Assert.DoesNotContain(FindAll<Button>(main.Settings), b => b.Content as string == "Get it");

        _latest = new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe);
        updates.Enabled = true;
        Assert.True(UiHost.WaitUntil(() => dot.IsVisible));
        Assert.Equal("Settings, PinList 2.3.0 is available", Avalonia.Automation.AutomationProperties.GetName(nav));
        Assert.Equal("PinList 2.3.0 is available", ToolTip.GetTip(nav));

        Assert.True(UiHost.WaitUntil(() => FindAll<Button>(main.Settings).Any(b => b.Content as string == "Get it")));
        Click(FindAll<Button>(main.Settings).First(b => b.Content as string == "Get it"));
        Assert.Equal(new[] { Exe }, _opened);

        updates.Enabled = false; // no check, no dot
        Assert.False(dot.IsVisible);
        Assert.Null(ToolTip.GetTip(nav));
    }

    [AvaloniaFact]
    public void The_tray_menu_offers_the_new_version_first()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe);
        UseUpdates();
        Pump();

        var menu = App.ShowTrayMenu();
        Assert.Equal("Get PinList 2.3.0", Headers(menu)[0]);
        Assert.IsType<Separator>(menu.Items[1]);
        ChooseFromMenu(menu, "Get PinList 2.3.0");
        Assert.Equal(new[] { Exe }, _opened);

        // the macOS menu bar menu too
        Assert.Equal(new[] { "Get PinList 2.3.0", "-", "All lists" }, Tray.Menu.Take(3).Select(e => e.Text));
        Tray.Menu[0].Action!();
        Assert.Equal(2, _opened.Count);
    }

    [AvaloniaFact]
    public void Check_for_updates_in_settings_turns_it_on_and_off()
    {
        var updates = UseUpdates();
        App.ShowSettings();
        Pump();
        var toggle = FindAll<ToggleSwitch>(App.MainWindow!.Settings).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Check for updates");
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Assert.True(updates.Enabled);
        Assert.True(UiHost.WaitUntil(() => FindAll<ToggleSwitch>(App.MainWindow!.Settings).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Check for updates").IsChecked == true));

        FindAll<ToggleSwitch>(App.MainWindow!.Settings).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Check for updates").IsChecked = false;
        Assert.False(updates.Enabled);
        Assert.DoesNotContain("Get PinList", string.Join(",", Headers(App.ShowTrayMenu())));
    }

    [AvaloniaFact]
    public void A_Mac_downloads_the_zip()
    {
        bool wasMac = OsText.IsMac;
        OsText.IsMac = true;
        try
        {
            Store.Data.Settings.CheckForUpdates = true;
            _latest = new UpdateInfo(new Version(2, 3, 0), ExeUrl: Exe, MacZip);
            UseUpdates();
            Pump();
            App.GetUpdate();
            Assert.Equal(new[] { MacZip }, _opened);
        }
        finally { OsText.IsMac = wasMac; }
    }

    [AvaloniaFact]
    public void Without_the_file_or_a_new_version_the_download_section_of_the_website_opens()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = new UpdateInfo(new Version(2, 3, 0), MacUrl: MacZip); // no Windows file in the release
        var updates = UseUpdates();
        Pump();
        App.GetUpdate();

        updates.Enabled = false; // no new version
        App.GetUpdate();
        Assert.Equal(new[] { Updates.DownloadsPage, Updates.DownloadsPage }, _opened);
    }
}

/// <summary>The update check on the phone: the question, the bar and the ⋮ menu.</summary>
public class PhoneUpdateTests : UiTest
{
    const string Apk = "https://github.com/FilipRaic/pin-list/releases/download/v2.3.0/PinList.apk";
    Window _window = null!;
    PhoneView _view = null!;
    readonly List<string> _opened = new();
    readonly Func<TopLevel, string, Task> _realOpenUrl = PhoneView.OpenUrl;
    UpdateInfo? _latest = new(new Version(2, 3, 0), ApkUrl: Apk);

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

    /// <summary>The sheet's choices: its rows, or the buttons side by side at its bottom.</summary>
    Button[] SheetButtons() => _view.SheetPanel!.GetLogicalDescendants().OfType<Button>().ToArray();

    static string TextOf(Button b) => b.Content as string ?? Avalonia.Automation.AutomationProperties.GetName(b) ?? "";

    string[] Options() => SheetButtons().Select(TextOf).ToArray();

    void Choose(string option)
    {
        Click(SheetButtons().Single(x => TextOf(x) == option));
        Pump();
    }

    void OpenMenu() => Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);

    [AvaloniaFact]
    public void The_first_start_asks_and_a_yes_finds_the_new_version()
    {
        var updates = Show();
        Assert.True(_view.SheetOpen);
        Assert.Equal("Check for updates?", _view.SheetTitleText);
        var text = FindAll<TextBlock>(_view.SheetPanel!).Single(t => t.Classes.Contains("sheettext"));
        Assert.StartsWith("PinList can look for a new version about once a day", text.Text);

        // under each other, the main one at the bottom and filled
        Assert.Equal(new[] { "Don't check", "Check for updates" }, Options());
        var question = SheetButtons();
        Assert.IsType<StackPanel>(question[0].Parent);
        Assert.Same(question[0].Parent, question[1].Parent);
        Assert.DoesNotContain("accent", question[0].Classes);
        Assert.Contains("accent", question[1].Classes);

        Choose("Check for updates");
        Assert.True(updates.Enabled);
        Assert.True(UiHost.WaitUntil(() => _view.SheetOpen));
        Assert.Equal("PinList 2.3.0 is available", _view.SheetTitleText);
        Assert.Equal(new[] { "Later", "Get it" }, Options()); // side by side, Get it on the right and filled
        var buttons = SheetButtons();
        Assert.All(buttons, b => Assert.Contains("sheetbutton", b.Classes));
        Assert.DoesNotContain("accent", buttons[0].Classes);
        Assert.Contains("accent", buttons[1].Classes);
        Assert.Equal(new[] { 0, 2 }, buttons.Select(Grid.GetColumn));
        Assert.Single(FindAll<LineIcon>(buttons[1])); // the download icon
        Assert.True(Named<Avalonia.Controls.Shapes.Ellipse>(_view, "UpdateDot").IsVisible);
        Assert.Equal("Settings, PinList 2.3.0 is available",
            Avalonia.Automation.AutomationProperties.GetName(Named<Button>(_view, "TabSettings")));

        Choose("Get it");
        Assert.Equal(new[] { Apk }, _opened); // the APK downloads in the browser
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void The_sheet_shows_what_s_new_and_Later_closes_it()
    {
        Store.Data.Settings.CheckForUpdates = true;
        _latest = _latest! with { Notes = "### Added\n- **Bold** thing\n\n### Fixed\n- A *bug*" };
        Show();
        Assert.True(UiHost.WaitUntil(() => _view.SheetOpen));
        var notes = FindAll<StackPanel>(_view.SheetPanel!).Single(p => p.Classes.Contains("releasenotes"));
        Assert.Equal(new[] { "Added", "•", "Bold thing", "Fixed", "•", "A bug" },
            FindAll<TextBlock>(notes).Select(t => t.Inlines is { Count: > 0 } i
                ? string.Concat(i.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text))
                : t.Text));

        Choose("Later");
        Assert.False(_view.SheetOpen);
        Assert.Empty(_opened);
        Assert.True(Named<Avalonia.Controls.Shapes.Ellipse>(_view, "UpdateDot").IsVisible); // stays until updated
    }

    [AvaloniaFact]
    public void No_asks_again_never_and_settings_can_turn_it_on()
    {
        var updates = Show();
        Choose("Don't check");
        Assert.False(updates.Enabled);
        Assert.False(_view.SheetOpen);

        _view.ShowSection(PhoneSection.Settings);
        Pump();
        var toggle = FindAll<ToggleSwitch>(_view.SettingsPage!).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Check for updates");
        Assert.False(toggle.IsChecked);
        toggle.IsChecked = true;
        Assert.True(updates.Enabled);

        // the new version is a row at the top of Settings, and opens the website without an APK
        Assert.True(UiHost.WaitUntil(() => Named<Button>(_view, "UpdateRow").IsVisible));
        Assert.Equal("Get PinList 2.3.0", Named<Button>(_view, "UpdateRow").Content);
        _latest = _latest! with { ApkUrl = null };
        Store.Data.Settings.CheckForUpdates = false;
        updates.Enabled = true;
        Pump();
        Click(Named<Button>(_view, "UpdateRow"));
        Assert.Equal(new[] { Updates.DownloadsPage }, _opened);

        updates.Enabled = false;
        _view.UpdateCheck = null; // no check in this build: no row and no dot
        Assert.False(Named<Button>(_view, "UpdateRow").IsVisible);
        Assert.False(Named<Avalonia.Controls.Shapes.Ellipse>(_view, "UpdateDot").IsVisible);
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
        Assert.Equal("Check for updates?", _view.SheetTitleText); // still the first question
        Assert.False(Named<Avalonia.Controls.Shapes.Ellipse>(_view, "UpdateDot").IsVisible);
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
