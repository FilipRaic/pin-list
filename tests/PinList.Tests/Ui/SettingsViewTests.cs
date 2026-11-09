using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PinList.Tests;

/// <summary>A settings host the tests can change.</summary>
sealed class FakeSettingsHost : ISettingsHost
{
    public string Theme { get; set; } = "Dark";
    public void SetTheme(string key) { Theme = key; ThemeSet.Add(key); }
    public List<string> ThemeSet { get; } = new();
    public SyncSummary? Sync { get; set; }
    public string? UnavailableSyncText { get; set; }
    public string? AutostartText { get; set; } = "Start with Windows";
    public bool AutostartOn { get; set; }
    public bool CanChangeAutostart { get; set; } = true;
    public void SetAutostart(bool on) => AutostartOn = on;
    public bool? UpdatesOn { get; set; } = false;
    public void SetUpdates(bool on) => UpdatesOn = on;
    public string? ReminderProblem { get; set; }
    public int NotificationSettingsOpened;
    public void OpenNotificationSettings() => NotificationSettingsOpened++;
    public string VersionText { get; set; } = "You're on 2.4.0, the newest";
    public Action? GetUpdate { get; set; }
    public int Exports, Imports, FolderOpens, Exits;
    public Task Export() { Exports++; return Task.CompletedTask; }
    public Task Import() { Imports++; return Task.CompletedTask; }
    public bool HasDataFolder { get; set; } = true;
    public void OpenDataFolder() => FolderOpens++;
    public int FeedbackOpens;
    public void SendFeedback() => FeedbackOpens++;
    public string? ExitText { get; set; } = "Exit";
    public void Exit() => Exits++;
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

public class SettingsViewTests : UiTest
{
    readonly FakeSettingsHost _host = new();
    readonly List<string> _ran = new();
    Window? _window;

    SettingsView Show()
    {
        var view = new SettingsView(_host);
        _window = new Window { Width = 600, Height = 800, Content = view };
        _window.Show();
        Pump();
        return view;
    }

    public override void Dispose()
    {
        _window?.Close();
        base.Dispose();
    }

    static Button ButtonNamed(Control root, string text) =>
        FindAll<Button>(root).First(b => b.Content as string == text);

    static IEnumerable<string> Texts(Control root) => FindAll<TextBlock>(root).Select(t => t.Text ?? "");

    SyncSummary SignedIn(string? problem = null) =>
        new("me@example.com", "Synced 2 minutes ago", problem, new[]
        {
            new SettingsAction("Sync now", () => _ran.Add("sync")),
            new SettingsAction("Sign out", () => _ran.Add("out")),
            new SettingsAction("Sign out and delete cloud data", () => _ran.Add("delete"), Danger: true),
            new SettingsAction("Disabled", () => _ran.Add("never"), Enabled: false),
        });

    [AvaloniaFact]
    public void Theme_buttons_show_and_set_the_theme()
    {
        var view = Show();
        var buttons = FindAll<ToggleButton>(view).Where(b => b.Classes.Contains("seg")).ToList();
        Assert.Equal(new[] { "System", "Light", "Dark" }, buttons.Select(b => (string)b.Content!));
        Assert.Equal(new[] { false, false, true }, buttons.Select(b => b.IsChecked == true));
        Assert.Equal(new[] { Themes.SystemThemeName, "Light", "Dark" }, buttons.Select(b => AutomationProperties.GetName(b)));

        Click(buttons[1]);
        Assert.Equal(new[] { "Light" }, _host.ThemeSet);
        Assert.Equal(new[] { false, true, false },
            FindAll<ToggleButton>(view).Where(b => b.Classes.Contains("seg")).Select(b => b.IsChecked == true)); // the page followed
    }

    [AvaloniaFact]
    public void Signed_out_shows_sign_in()
    {
        _host.Sync = new SyncSummary(null, "", null, new[] { new SettingsAction("Sign in with Google", () => _ran.Add("in")) });
        var view = Show();
        var button = ButtonNamed(view, "Sign in with Google");
        Assert.Contains("accent", button.Classes);
        Click(button);
        Assert.Equal(new[] { "in" }, _ran);
        Assert.DoesNotContain(Texts(view), t => t.StartsWith("Synced"));
    }

    [AvaloniaFact]
    public void Signed_in_shows_account_status_and_actions()
    {
        _host.Sync = SignedIn();
        var view = Show();
        Assert.Contains("me@example.com", Texts(view));
        Assert.Contains("Synced 2 minutes ago", Texts(view));
        Click(ButtonNamed(view, "Sync now"));
        Click(ButtonNamed(view, "Sign out"));
        Click(ButtonNamed(view, "Sign out and delete cloud data"));
        Assert.Contains("danger", ButtonNamed(view, "Sign out and delete cloud data").Classes);
        Assert.False(ButtonNamed(view, "Disabled").IsEnabled);
        Assert.Equal(new[] { "sync", "out", "delete" }, _ran);
    }

    [AvaloniaFact]
    public void A_sync_problem_is_shown_in_red()
    {
        _host.Sync = SignedIn("Can't reach Google Drive.");
        var view = Show();
        var problem = FindAll<TextBlock>(view).Single(t => t.Text == "Can't reach Google Drive.");
        Assert.Contains("problem", problem.Classes);
        Assert.DoesNotContain(Texts(view), t => t == "Synced 2 minutes ago"); // the problem replaces the status
    }

    [AvaloniaFact]
    public void A_reminder_problem_shows_in_red_with_a_button_to_the_system_settings()
    {
        var view = Show();
        Assert.DoesNotContain(Texts(view), t => t == "Reminders"); // nothing wrong: no row

        _host.ReminderProblem = "Reminders aren't kept in Notification Center.";
        _host.RaiseChanged();
        Pump();
        Assert.Contains("Reminders", Texts(view));
        var problem = FindAll<TextBlock>(view).Single(t => t.Text == _host.ReminderProblem);
        Assert.Contains("problem", problem.Classes);
        var open = ButtonNamed(view, "Open settings");
        Assert.Equal("Open notification settings", AutomationProperties.GetName(open));
        Click(open);
        Assert.Equal(1, _host.NotificationSettingsOpened);
    }

    [AvaloniaFact]
    public void No_sync_hides_the_group_and_unavailable_sync_explains()
    {
        var view = Show();
        Assert.DoesNotContain(Texts(view), t => t == "Google Drive sync");

        _host.UnavailableSyncText = "Sync isn't available in this build.";
        _host.RaiseChanged();
        Pump();
        Assert.Contains("Google Drive sync", Texts(view));
        Assert.Contains("Sync isn't available in this build.", Texts(view));
        Assert.DoesNotContain(FindAll<Button>(view), b => b.Content as string == "Sync now");
    }

    [AvaloniaFact]
    public void Rows_the_platform_does_not_have_are_hidden()
    {
        _host.AutostartText = null;
        _host.UpdatesOn = null;
        _host.HasDataFolder = false;
        _host.ExitText = null;
        var view = Show();
        Assert.Empty(FindAll<ToggleSwitch>(view));
        Assert.DoesNotContain(Texts(view), t => t == "Check for updates");
        Assert.DoesNotContain(FindAll<Button>(view), b => b.Content as string is "Open" or "Exit");
        Assert.Contains("You're on 2.4.0, the newest", Texts(view)); // without an update row the version is at the bottom
        Assert.Contains("Nothing to set here on this device.", Texts(view));
        Assert.Contains("Export", FindAll<Button>(view).Select(b => b.Content as string));
    }

    [AvaloniaFact]
    public void Switches_and_buttons_call_the_host()
    {
        var view = Show();
        var switches = FindAll<ToggleSwitch>(view).ToList();
        Assert.Equal(new[] { "Start with Windows", "Check for updates" }, switches.Select(s => AutomationProperties.GetName(s)));
        switches[0].IsChecked = true;
        switches[1].IsChecked = true;
        Assert.True(_host.AutostartOn);
        Assert.True(_host.UpdatesOn);
        Assert.Contains("You're on 2.4.0, the newest", Texts(view));

        Click(ButtonNamed(view, "Export"));
        Click(ButtonNamed(view, "Import"));
        var open = ButtonNamed(view, "Open");
        Assert.Equal("Open data folder", AutomationProperties.GetName(open));
        Click(open);
        Click(ButtonNamed(view, "Exit"));
        Assert.Equal((1, 1, 1, 1), (_host.Exports, _host.Imports, _host.FolderOpens, _host.Exits));

        Assert.Contains("Found a bug or have an idea?", Texts(view));
        Click(ButtonNamed(view, "Send feedback"));
        Assert.Equal(1, _host.FeedbackOpens);
    }

    [AvaloniaFact]
    public void Get_it_shows_next_to_Check_for_updates_while_a_new_version_is_out()
    {
        _host.UpdatesOn = true;
        _host.VersionText = "PinList 2.5.0 is out";
        _host.GetUpdate = () => _ran.Add("get");
        var view = Show();
        var get = ButtonNamed(view, "Get it");
        Assert.Equal("Get the new version", AutomationProperties.GetName(get));
        Assert.Contains("accent", get.Classes);
        Click(get);
        Assert.Equal(new[] { "get" }, _ran);

        _host.GetUpdate = null;
        view.Refresh();
        Assert.DoesNotContain(FindAll<Button>(view), b => b.Content as string == "Get it");
    }

    [AvaloniaFact]
    public void Autostart_can_be_disabled_by_the_platform()
    {
        _host.CanChangeAutostart = false;
        var view = Show();
        Assert.False(FindAll<ToggleSwitch>(view).First().IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Changed_refreshes_the_page()
    {
        var view = Show();
        Assert.DoesNotContain(Texts(view), t => t == "me@example.com");
        _host.Sync = SignedIn();
        _host.RaiseChanged();
        Assert.Contains("me@example.com", Texts(view));
    }

    [AvaloniaFact]
    public void A_page_that_left_the_screen_stops_listening()
    {
        var view = Show();
        _window!.Content = null;
        Pump();
        _host.Sync = SignedIn();
        _host.RaiseChanged();
        Assert.DoesNotContain(Texts(view), t => t == "me@example.com");
    }

    [AvaloniaFact]
    public void Every_control_has_an_accessible_name()
    {
        _host.Sync = SignedIn();
        var view = Show();
        foreach (var button in FindAll<Button>(view).Where(b => b.GetType() == typeof(Button)))
            Assert.False(string.IsNullOrEmpty(button.Content as string), "a button without text");
        foreach (var toggle in FindAll<ToggleSwitch>(view).Cast<Control>().Concat(FindAll<ToggleButton>(view)))
            Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(toggle)), "a toggle without a name");
    }

    [AvaloniaFact]
    public void The_phone_class_gives_touch_sizes()
    {
        var plain = Show();
        var plainSize = ButtonNamed(plain, "Export").FontSize;
        _window!.Close();
        var view = new SettingsView(_host);
        view.Classes.Add("phone");
        view.Refresh();
        _window = new Window { Width = 400, Height = 800, Content = view };
        _window.Show();
        Pump();
        var export = ButtonNamed(view, "Export");
        Assert.True(export.FontSize > plainSize);
        Assert.Contains("setrow", export.Classes); // on the phone an action is a whole row you tap
        Assert.True(export.Bounds.Height >= 48);
        var feedback = ButtonNamed(view, "Send feedback");
        Assert.Contains("setrow", feedback.Classes);
        Click(feedback);
        Assert.Equal(1, _host.FeedbackOpens);
    }

    [AvaloniaFact]
    public void Rows_of_a_group_have_a_line_between_them()
    {
        var view = Show();
        var groups = FindAll<Border>(view).Where(b => b.Classes.Contains("group")).ToList();
        var data = groups[^2]; // Your data (Help is the last group)
        Assert.Single(FindAll<Border>(data), b => b.Classes.Contains("divider")); // "Back up or move your lists" and "Data folder"
        Assert.Contains("Back up or move your lists", Texts(data));
    }

    [AvaloniaFact]
    public void The_account_row_shows_the_status_with_a_dot_and_the_danger_action_on_its_own_row()
    {
        _host.Sync = SignedIn();
        var view = Show();
        Assert.Contains(FindAll<Avalonia.Controls.Shapes.Ellipse>(view), e => e.Classes.Contains("synced"));
        Assert.Contains(FindAll<Border>(view), b => b.Classes.Contains("avatar"));
        Assert.Contains("small", ButtonNamed(view, "Sign out and delete cloud data").Classes);
    }
}
