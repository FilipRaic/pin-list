using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Microsoft.Win32;
using Xunit;

namespace PinList.Tests;

/// <summary>The app as a whole: start and stop, the tray icon, themes, system events, reminders, export and import.</summary>
public class AppTests : UiTest
{
    /// <summary>Waits (pumping) until an async action of the app has finished.</summary>
    static void Await(Task t)
    {
        Assert.True(UiHost.WaitUntil(() => t.IsCompleted));
        t.GetAwaiter().GetResult();
    }

    /// <summary>The system closes the window (like Windows does to the widgets when Explorer restarts).</summary>
    static void ClosedBySystem(Window w)
    {
        var impl = typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(w)!;
        ((Action?)impl.GetType().GetProperty("Closed")!.GetValue(impl))?.Invoke();
        Pump();
    }

    static Color Brush(string key) => ((ImmutableSolidColorBrush)App.Resources[key]!).Color;

    // ================= Start and stop =================

    [AvaloniaFact]
    public void ConfigureCore_routes_store_messages_to_dialogs()
    {
        var before = Store.Notify;
        try
        {
            Store.Notify = (_, _) => { };
            App.ConfigureCore();
            Assert.Equal((Action<string, bool>)App.ShowStoreMessage, Store.Notify);
            Assert.IsType<AvaloniaUiTimer>(UiTimers.Create());
            Assert.Equal("Use Windows setting", Themes.SystemThemeName);
        }
        finally { Store.Notify = before; }
    }

    [AvaloniaFact]
    public void Started_with_its_own_data_tray_and_autostart_entry()
    {
        Assert.StartsWith("PinList.", App.InstanceName); // its own single-instance name for the test folder
        Assert.Same(Tray, App.Tray);
        Assert.NotNull(App.ReminderService);
        Autostart.RunKey = UiHost.TestRunKey;
        Assert.True(Autostart.IsEnabled()); // first run turned it on (in the test key)
        Assert.True(File.Exists(Path.Combine(UiHost.DataDir, "data.json")));
    }

    [AvaloniaFact]
    public void Stop_saves_and_releases_and_Start_brings_everything_back()
    {
        App.Stop();
        Assert.True(App.ShuttingDown);
        Assert.True(Tray.Disposed);
        Assert.False(App.ReminderService.IsRunning);
        App.ShowReminders(new[] { new TodoItem { Text = "x" } }); // no tray any more: ignored
        Assert.Empty(Tray.Notifications);

        Tray.Disposed = false;
        Assert.True(App.Start()); // its data folder, with the widget from the first run
        Assert.False(App.ShuttingDown);
        Assert.Equal(UiHost.DataDir, Store.Dir);
        Assert.NotEmpty(App.OpenWindows);
        Assert.True(App.ReminderService.IsRunning);
        Store.UseDirectory(Dir); // back to this test's folder
    }

    [AvaloniaFact]
    public void A_second_start_signals_the_first_and_quits()
    {
        var w = OpenNew(("A", new string[0]));
        w.Hide();
        Assert.False(App.Start()); // already running: tells the running one, which shows its widgets
        Assert.True(UiHost.WaitUntil(() => w.IsVisible));
    }

    [AvaloniaFact]
    public void Exit_and_signing_out_save_first()
    {
        App.ExitApp();
        Assert.Equal(1, UiHost.ShutdownRequests);
        Assert.True(App.ShuttingDown);

        App.ShuttingDown = false;
        App.OnSessionEnding(null, EventArgs.Empty);
        Assert.True(App.ShuttingDown);
        App.ShuttingDown = false;
    }

    // ================= Errors =================

    sealed class ThrowingSaver : ISaveScheduler
    {
        public void Schedule() { }
        public bool CancelPending() => throw new IOException("disk gone");
    }

    [AvaloniaFact]
    public void Errors_are_logged()
    {
        // an error on the UI thread (the event args are made the way the dispatcher makes them)
        var uiError = (DispatcherUnhandledExceptionEventArgs)typeof(DispatcherUnhandledExceptionEventArgs)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Single()
            .Invoke(new object[] { Dispatcher.UIThread });
        var exception = typeof(DispatcherUnhandledExceptionEventArgs).GetProperty("Exception")!;
        if (exception.CanWrite) exception.SetValue(uiError, new InvalidOperationException("ui boom"));
        else typeof(DispatcherUnhandledExceptionEventArgs).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(Exception)).SetValue(uiError, new InvalidOperationException("ui boom"));
        App.OnUiError(uiError);
        Assert.True(uiError.Handled); // logged, and the app keeps running

        App.OnFatalError("fatal boom");
        Store.Saver = new ThrowingSaver();
        App.OnFatalError("second boom"); // saving fails too: logged as well
        Store.Saver = Saver; // the next test's start saves through it

        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new Exception("task boom")));
        App.OnBackgroundError(args);
        Assert.True(args.Observed);

        var log = File.ReadAllText(Path.Combine(Dir, "log.txt"));
        Assert.Contains("ui boom", log);
        Assert.Contains("fatal boom", log);
        Assert.Contains("disk gone", log);
        Assert.Contains("task boom", log);
    }

    // ================= Theme and system settings =================

    [AvaloniaFact]
    public void Theme_menu_and_following_Windows()
    {
        var menu = App.ThemeMenu();
        Assert.Equal(new object[] { "Use Windows setting", "Light", "Dark" }, menu.Items.OfType<MenuItem>().Select(m => m.Header!));

        App.SetTheme("System");
        Themes.SystemDarkSource = () => false;
        App.OnUserPreferenceChanged(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
        Pump();
        Assert.Equal(Color.Parse("#F9F9F9"), Brush("MenuBg"));
        Assert.Equal(Brush("MenuBg"), Brush("MenuFlyoutPresenterBackground")); // menus use the app's colors
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, App.RequestedThemeVariant);

        App.SetTheme("Dark");
        Assert.Equal(Color.Parse("#2C2C2C"), Brush("MenuBg"));
        Assert.True(Saver.Requests > 0); // the choice is saved
    }

    [AvaloniaFact]
    public void Contrast_theme_and_text_size_changes_reach_every_window()
    {
        var w = OpenNew(("A", new string[0]));
        App.ShowArchive();
        Pump();
        var contrast = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Black, Colors.Yellow);
        Themes.ContrastSource = () => contrast;
        App.RefreshAllWindows();
        Assert.Equal(Colors.Black, ((ImmutableSolidColorBrush)w.Resources["BoardBg"]!).Color);
        Assert.Equal(Colors.Cyan, Brush("AccentFill"));
        Assert.Equal(Colors.Cyan, Brush("MenuHover"));

        A11y.TextScaleSource = () => 2.0;
        App.RefreshAllWindows();
        Assert.Equal(2.0, ((ScaleTransform)Named<LayoutTransformControl>(w, "Scaler").LayoutTransform!).ScaleX);
        Assert.Equal(2.0, ((ScaleTransform)Named<LayoutTransformControl>(App.Archive!, "Scaler").LayoutTransform!).ScaleX);
    }

    [AvaloniaFact]
    public void Monitor_changes_bring_lost_widgets_back()
    {
        var w = OpenNew(("A", new string[0]));
        w.Board.X = -100000; // its monitor was unplugged
        App.OnDisplaySettingsChanged(null, EventArgs.Empty);
        Pump();
        Assert.InRange(w.Left, Displays.Primary.Left - 1, Displays.Primary.Right);
    }

    [AvaloniaFact]
    public void Widgets_moved_by_Windows_go_back_once_the_monitors_settle()
    {
        var old = App.DisplaySettleDelay;
        App.DisplaySettleDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            var (x, y, width) = (w.Left, w.Top, w.Width);
            App.OnDisplaySettingsChanged(null, EventArgs.Empty);
            Pump();
            Assert.True(BoardWindow.DisplayChanging);

            // Windows moves the widget once more after reporting the change (a monitor waking up)
            w.Left = x + 300;
            w.Width = width + 40;
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            Assert.Equal(x, w.Left, 0);
            Assert.Equal(width, w.Width);
            Assert.False(BoardWindow.DisplayChanging);

            // waking up from sleep does the same
            w.Top = y + 200;
            App.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            Assert.Equal(y, w.Top, 0);

            // other power events don't move anything
            w.Top = y + 200;
            App.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend));
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            Assert.Equal(y + 200, w.Top, 0);

            // nor does anything while the app is exiting
            App.ShuttingDown = true;
            App.OnDisplaySettingsChanged(null, EventArgs.Empty);
            UiHost.Wait(TimeSpan.FromMilliseconds(150));
            Assert.Equal(y + 200, w.Top, 0);
            App.ShuttingDown = false;
        }
        finally { App.DisplaySettleDelay = old; }
    }

    // ================= Widgets closed by the system =================

    [AvaloniaFact]
    public void A_widget_closed_by_Windows_is_reopened()
    {
        App.ReopenDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            ClosedBySystem(w); // like an Explorer restart
            Assert.DoesNotContain(w, App.OpenWindows);
            Assert.True(UiHost.WaitUntil(() => App.OpenWindows.Any(x => x.Board == w.Board)));

            // deleted meanwhile: not reopened
            var gone = OpenNew(("B", new string[0]));
            ClosedBySystem(gone);
            Store.Data.Boards.Remove(gone.Board);
            UiHost.Wait(TimeSpan.FromMilliseconds(150));
            Assert.DoesNotContain(App.OpenWindows, x => x.Board == gone.Board);

            // reopened meanwhile by something else: not twice
            var twice = OpenNew(("C", new string[0]));
            ClosedBySystem(twice);
            App.OpenBoard(twice.Board);
            UiHost.Wait(TimeSpan.FromMilliseconds(150));
            Assert.Single(App.OpenWindows, x => x.Board == twice.Board);
        }
        finally { App.ReopenDelay = TimeSpan.FromSeconds(2); }
    }

    [AvaloniaFact]
    public void A_widget_closed_by_Windows_while_the_app_exits_is_not_reopened()
    {
        App.ReopenDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            ClosedBySystem(w);
            App.ShuttingDown = true; // the exit started before the reopen timer ran
            UiHost.Wait(TimeSpan.FromMilliseconds(150));
            Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board);
            App.ShuttingDown = false;
        }
        finally { App.ReopenDelay = TimeSpan.FromSeconds(2); }
    }

    [AvaloniaFact]
    public void The_first_run_widget_follows_a_light_Windows_theme()
    {
        App.Stop();
        File.Delete(Path.Combine(UiHost.DataDir, "data.json"));
        Themes.SystemDarkSource = () => false;
        Tray.Disposed = false;
        Assert.True(App.Start());
        Assert.Equal("Light", Assert.Single(Store.Data.Boards).Style);
        Assert.True(File.Exists(Path.Combine(UiHost.DataDir, "data.json"))); // saved right away
        Store.UseDirectory(Dir);
    }

    [AvaloniaFact]
    public void A_development_copy_leaves_start_with_Windows_alone()
    {
        App.Stop();
        App.Stop(); // stopping twice is harmless
        File.Delete(Path.Combine(UiHost.DataDir, "data.json"));
        App.ManageAutostart = false;
        try
        {
            Autostart.Set(false);
            Tray.Disposed = false;
            Assert.True(App.Start()); // a first run, but not in "Start with Windows"
            Assert.False(Autostart.IsEnabled());
        }
        finally { App.ManageAutostart = true; Autostart.Set(true); }
        Store.UseDirectory(Dir);
    }

    [AvaloniaFact]
    public void A_data_file_without_widgets_gets_one()
    {
        App.Stop();
        File.WriteAllText(Path.Combine(UiHost.DataDir, "data.json"), """{ "version": 1, "boards": [] }""");
        Tray.Disposed = false;
        Assert.True(App.Start());
        Assert.Single(Store.Data.Boards);
        Store.UseDirectory(Dir);
    }

    // ================= Tray icon =================

    [AvaloniaFact]
    public void Tray_left_click_shows_the_widgets_with_focus_in_the_last()
    {
        var w1 = OpenNew(("A", new string[0]));
        var w2 = OpenNew(("B", new string[0]));
        w1.Hide();
        Tray.ClickLeft();
        Pump();
        Assert.True(w1.IsVisible);
        Assert.True(w2.IsKeyboardFocusWithin);

        Tray.ClickLeft(); // focus already in it: stays where it is
        foreach (var w in App.OpenWindows.ToList()) w.CloseForReload();
        Tray.ClickLeft(); // no widgets: nothing to do
    }

    [AvaloniaFact]
    public void Tray_menu()
    {
        var w = OpenNew(("A", new string[0]));
        w.Board.Style = "Green";
        Tray.ClickRight();
        var menu = App.LastTrayMenu!;
        Assert.Equal(new object[] { "New window", "Archive", "Show all windows", "App theme", "Export", "Import", "Google sync", "Start with Windows", "Open data folder", "Exit" },
            menu.Items.OfType<MenuItem>().Select(m => m.Header!));

        ChooseFromMenu(menu, "New window");
        Assert.Equal("Green", Store.Data.Boards.Last().Style); // like the last widget

        ChooseFromMenu(App.ShowTrayMenu(), "Archive");
        Assert.NotNull(App.Archive);
        ChooseFromMenu(App.ShowTrayMenu(), "Show all windows");
        ChooseFromMenu(App.ShowTrayMenu(), "App theme", "Light");
        Assert.Equal("Light", Store.Data.Settings.Theme);
        ChooseFromMenu(App.ShowTrayMenu(), "Export");
        ChooseFromMenu(App.ShowTrayMenu(), "Import");
        ChooseFromMenu(App.ShowTrayMenu(), "Open data folder");
        Assert.Equal(new[] { Store.Dir }, UiHost.OpenedFolders);
        ChooseFromMenu(App.ShowTrayMenu(), "Exit");
        Assert.Equal(1, UiHost.ShutdownRequests);
        App.ShuttingDown = false;
    }

    [AvaloniaFact]
    public void The_menu_bar_menu_has_the_same_commands()
    {
        // macOS shows these entries in its menu bar menu (Windows shows the menu above on right click)
        var entries = Tray.Menu;
        Assert.Equal(new[] { "New window", "Archive", "Show all windows", "-", "Export", "Import", "-",
                "Sync isn't available in this build.", "-", "Open data folder", "-", "Exit" },
            entries.Select(e => e.Text));
        Assert.False(entries.Single(e => e.Text.StartsWith("Sync isn't")).Enabled);
        OpenNew(("A", new string[0]));
        entries.Single(e => e.Text == "New window").Action!();
        Assert.Equal(2, App.OpenWindows.Count);
        entries.Single(e => e.Text == "Archive").Action!();
        Assert.NotNull(App.Archive);
        entries.Single(e => e.Text == "Show all windows").Action!();
        entries.Single(e => e.Text == "Export").Action!();
        entries.Single(e => e.Text == "Import").Action!();
        Pump();
        entries.Single(e => e.Text == "Open data folder").Action!();
        Assert.Equal(new[] { Store.Dir }, UiHost.OpenedFolders);
        entries.Single(e => e.Text == "Exit").Action!();
        Assert.Equal(1, UiHost.ShutdownRequests);
        App.ShuttingDown = false;
    }

    [AvaloniaFact]
    public void New_window_from_the_tray_with_no_widgets_open_uses_the_default_look()
    {
        Assert.Empty(App.OpenWindows);
        ChooseFromMenu(App.ShowTrayMenu(), "New window");
        var b = Store.Data.Boards.Last();
        Assert.Equal(("Dark", true), (b.Style, b.Transparent));
        Assert.Single(App.OpenWindows, x => x.Board == b);
    }

    [AvaloniaFact]
    public void Tray_menu_turns_start_with_Windows_on_and_off()
    {
        Autostart.RunKey = @"Software\PinListTests\TrayAutostart";
        try
        {
            Autostart.Set(false);
            var item = MenuItem(App.ShowTrayMenu(), "Start with Windows");
            Assert.False(item.IsChecked);
            Assert.True(item.IsEnabled);
            ChooseFromMenu(App.LastTrayMenu!, "Start with Windows");
            Assert.True(Autostart.IsEnabled());
            ChooseFromMenu(App.ShowTrayMenu(), "Start with Windows");
            Assert.False(Autostart.IsEnabled());

            App.ManageAutostart = false; // a development copy can't change it
            Assert.False(MenuItem(App.ShowTrayMenu(), "Start with Windows").IsEnabled);
        }
        finally
        {
            App.ManageAutostart = true;
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\PinListTests\TrayAutostart", false);
            Autostart.RunKey = UiHost.TestRunKey;
        }
    }

    [AvaloniaFact]
    public void Tray_icon_image_falls_back_when_the_app_icon_is_missing()
    {
        using var icon = App.LoadTrayIcon();
        Assert.True(icon.Width > 0);
        var real = App.TrayIconUri;
        App.TrayIconUri = "avares://PinList/Assets/missing.ico";
        try { Assert.Equal(System.Drawing.SystemIcons.Application.Size, App.LoadTrayIcon().Size); } // the standard Windows icon
        finally { App.TrayIconUri = real; }
    }

    // ================= Reminders =================

    [AvaloniaFact]
    public void Due_reminders_show_a_notification_and_clicking_it_shows_the_item()
    {
        var w = OpenNew(("A", new[] { "a", "Call the bank" }));
        var item = w.Board.Categories[0].Items[1];
        item.ReminderAt = new DateTime(2030, 1, 1, 9, 0, 0);
        App.ReminderService.Clock = () => new DateTime(2030, 1, 1, 9, 0, 5);
        App.ReminderService.Check();
        Assert.Equal(("Reminder", "Call the bank"), Tray.Notifications.Single());

        Tray.ClickNotification();
        Pump();
        Assert.True(item.IsActive);
        Assert.Same(Row(w, item), Focused(w));

        Actions.Delete(item);
        Tray.ClickNotification(); // the item is gone meanwhile: just shows the widgets
    }

    // ================= Export and import =================

    static string WriteFile(string dir, string name, string json)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    const string ImportJson = """
        { "version": 1, "boards": [ { "id": "b", "title": "Imported", "categories": [ { "id": "c", "name": "General", "items": [ { "text": "from file" } ] } ] } ],
          "archive": [ { "text": "done", "categoryId": "c", "completedAt": "2026-01-01T00:00:00" } ] }
        """;

    [AvaloniaFact]
    public void Export_writes_the_file_or_says_why_not()
    {
        MakeBoard(("A", new[] { "a" }));
        Await(App.ExportData(null)); // cancelled
        Assert.False(Directory.Exists(Path.Combine(Dir, "out")));

        Dialogs.SaveFileAnswer = Path.Combine(Dir, "export.json");
        Directory.CreateDirectory(Dir);
        Await(App.ExportData(null));
        Assert.Contains("\"a\"", File.ReadAllText(Dialogs.SaveFileAnswer));

        Dialogs.SaveFileAnswer = Path.Combine(Dir, "missing folder", "export.json");
        Await(App.ExportData(null));
        Assert.Contains(Dialogs.Shown, s => s.StartsWith("Export failed: "));
    }

    [AvaloniaFact]
    public void Import_cancelled_or_unreadable()
    {
        Await(App.ImportData(null)); // no file chosen
        Assert.Equal(new[] { "open" }, Dialogs.Shown);

        Dialogs.OpenFileAnswer = WriteFile(Dir, "bad.json", "not json");
        Await(App.ImportData(null));
        Assert.Contains(Dialogs.Shown, s => s.StartsWith("This file can't be imported: "));

        Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
        Await(App.ImportData(null)); // the question is answered with Esc
        Assert.Contains(Dialogs.Shown, s => s.StartsWith("Import good.json?: 1 window with 1 item, and 1 completed item in the archive."));
        Assert.Empty(Store.Data.Boards);
    }

    [AvaloniaFact]
    public void Import_adds_widgets_next_to_the_existing_ones()
    {
        var mine = OpenNew(("Mine", new[] { "mine" }));
        Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
        Dialogs.ChooseAnswer = _ => 2; // Add
        Await(App.ImportData(mine));
        Assert.Equal(new[] { "Test", "Imported" }, Store.Data.Boards.Select(b => b.Title));
        Assert.Contains(App.OpenWindows, w => w.Board.Title == "Imported");
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Import_can_replace_everything()
    {
        var mine = OpenNew(("Mine", new[] { "mine" }));
        App.ShowArchive();
        Actions.SetItemText(mine.Board.Categories[0].Items[0], "changed");
        Dialogs.OpenFileAnswer = WriteFile(Dir, "newer.json", ImportJson.Replace("\"version\": 1", "\"version\": 99"));
        Dialogs.ChooseAnswer = _ => 1; // Replace everything
        Await(App.ImportData(null));
        Pump();
        Assert.Contains(Dialogs.Shown, s => s.Contains("from a newer version of PinList"));
        Assert.Equal(new[] { "Imported" }, Store.Data.Boards.Select(b => b.Title));
        Assert.False(mine.IsVisible);
        Assert.Null(App.Archive); // it showed the old archive
        Assert.Single(App.OpenWindows);
        Assert.False(Actions.CanUndo); // the old undo history doesn't fit the new data
        Assert.Single(Directory.GetFiles(Path.Combine(Dir, "backups"), "before-import-*.json"));
    }

    [AvaloniaFact]
    public void Replacing_says_that_synced_windows_stay()
    {
        var mine = OpenNew(("Mine", new[] { "mine" }));
        mine.Board.Synced = true;
        Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
        Dialogs.ChooseAnswer = _ => 1; // Replace everything
        Await(App.ImportData(null));
        Pump();
        Assert.Contains(Dialogs.Shown, s => s.Contains("Synced windows aren't replaced."));
        Assert.Equal(new[] { "Test", "Imported" }, Store.Data.Boards.Select(b => b.Title));
    }

    [AvaloniaFact]
    public void Importing_a_file_without_widgets_still_leaves_one()
    {
        Dialogs.OpenFileAnswer = WriteFile(Dir, "empty.json", """{ "version": 1, "boards": [] }""");
        Dialogs.ChooseAnswer = _ => 1;
        Await(App.ImportData(null));
        Assert.Single(Store.Data.Boards);
        Assert.Single(App.OpenWindows);
    }

    [AvaloniaFact]
    public void A_failing_import_says_so()
    {
        Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
        File.WriteAllText(Path.Combine(Dir, "backups"), "a file where the backups folder should be");
        Dialogs.ChooseAnswer = _ => 1;
        Await(App.ImportData(null));
        Assert.Contains(Dialogs.Shown, s => s.StartsWith("Import failed: "));
    }

    [AvaloniaFact]
    public void Plural()
    {
        Assert.Equal("1 item", App.Plural(1, "item"));
        Assert.Equal("0 items", App.Plural(0, "item"));
    }

    [AvaloniaFact]
    public void Store_messages_use_the_app_dialog()
    {
        App.ShowStoreMessage("Backup loaded", false);
        App.ShowStoreMessage("Damaged", true);
        Assert.Equal(new[] { "PinList: Backup loaded", "PinList couldn't load your lists: Damaged" }, Dialogs.Shown);
    }

    [AvaloniaFact]
    public void Dialogs_without_a_widget_belong_to_a_hidden_helper_window()
    {
        Assert.Empty(App.OpenWindows);
        var host = App.AnyWindow();
        Assert.Same(host, App.AnyWindow()); // created once
        var w = OpenNew(("A", new string[0]));
        Assert.Same(w, App.AnyWindow()); // a widget is better
        App.ShowArchive();
        w.Hide();
        Assert.Same(App.Archive, App.AnyWindow());
    }
}
