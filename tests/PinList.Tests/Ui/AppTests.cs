using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The app as a whole: start and stop, the tray icon, themes, Windows events, reminders, export and import.</summary>
public class AppTests : UiTest
{
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);

    static object Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!.Invoke(target, args)!;

    static T Create<T>(params object?[] args) =>
        (T)typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            .First(c => c.GetParameters().Length == args.Length).Invoke(args);

    // ================= Start and stop =================

    [Fact]
    public void Started_with_its_own_data_tray_and_autostart_entry()
    {
        Ui(() =>
        {
            Assert.StartsWith("PinList.", PinList.App.InstanceName); // its own single-instance name for the test folder
            Assert.Same(Tray, App.Tray);
            Assert.True(App.ReminderService != null);
            Autostart.RunKey = UiHost.TestRunKey;
            Assert.True(Autostart.IsEnabled()); // first run turned it on (in the test key)
            Assert.True(File.Exists(Path.Combine(UiHost.DataDir, "data.json")));
        });
    }

    [Fact]
    public void Stop_saves_and_releases_and_Start_brings_everything_back()
    {
        Ui(() =>
        {
            App.Stop();
            Assert.True(PinList.App.ShuttingDown);
            Assert.True(Tray.Disposed);
            Assert.False(App.ReminderService.IsRunning);
            App.ShowReminders(new[] { new TodoItem { Text = "x" } }); // no tray any more: ignored
            Assert.Empty(Tray.Notifications);

            Tray.Disposed = false;
            Assert.True(App.Start()); // its data folder, with the widget from the first run
            Assert.False(PinList.App.ShuttingDown);
            Assert.Equal(UiHost.DataDir, Store.Dir);
            Assert.NotEmpty(App.OpenWindows);
            Assert.True(App.ReminderService.IsRunning);
        });
    }

    [Fact]
    public void Exiting_through_OnExit_stops_everything()
    {
        Ui(() =>
        {
            Invoke(App, "OnExit", Create<ExitEventArgs>(0));
            Assert.True(Tray.Disposed);
            Tray.Disposed = false;
            Assert.True(App.Start());
        });
    }

    [Fact]
    public void A_second_start_signals_the_first_and_quits()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Hide();

            Assert.False(App.Start()); // already running: tells the running one, which shows its widgets
            Assert.True(UiHost.WaitUntil(() => w.IsVisible));

            Invoke(App, "OnStartup", Create<StartupEventArgs>()); // the same from the start-up path quits
            Assert.Equal(1, UiHost.ShutdownRequests);
            PinList.App.ShuttingDown = false;
        });
    }

    [Fact]
    public void Exit_and_signing_out_save_first()
    {
        Ui(() =>
        {
            App.ExitApp();
            Assert.Equal(1, UiHost.ShutdownRequests);
            Assert.True(PinList.App.ShuttingDown);
            PinList.App.ShuttingDown = false;

            typeof(PinList.App).GetMethod("OnSessionEnding", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(object), typeof(SessionEndingCancelEventArgs) })!
                .Invoke(App, new object?[] { null, Create<SessionEndingCancelEventArgs>(ReasonSessionEnding.Logoff) });
            Assert.True(PinList.App.ShuttingDown);
            PinList.App.ShuttingDown = false;
        });
    }

    // ================= Errors =================

    sealed class ThrowingSaver : ISaveScheduler
    {
        public void Schedule() { }
        public bool CancelPending() => throw new IOException("disk gone");
    }

    [Fact]
    public void Errors_are_logged()
    {
        Ui(() =>
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(() => throw new InvalidOperationException("ui boom"));
            Pump();
            while (UiHost.UnhandledErrors.TryDequeue(out _)) { } // this one was on purpose

            App.OnFatalError("fatal boom");
            Store.Saver = new ThrowingSaver();
            App.OnFatalError("second boom"); // saving fails too: the UI thread's error handler logs it
            while (UiHost.UnhandledErrors.TryDequeue(out _)) { }

            var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new Exception("task boom")));
            PinList.App.OnBackgroundError(args);
            Assert.True(args.Observed);

            var log = File.ReadAllText(Path.Combine(Dir, "log.txt"));
            Assert.Contains("ui boom", log);
            Assert.Contains("fatal boom", log);
            Assert.Contains("disk gone", log);
            Assert.Contains("task boom", log);
        });
    }

    // ================= Theme and Windows settings =================

    [Fact]
    public void Theme_menu_and_following_Windows()
    {
        Ui(() =>
        {
            var menu = App.ThemeMenu();
            Assert.Equal(new object[] { "Use Windows setting", "Light", "Dark" }, menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Header));

            App.SetTheme("System");
            Themes.SystemDarkSource = () => false;
            App.OnUserPreferenceChanged(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
            Pump();
            Assert.Equal(Themes.Brush("#F9F9F9").Color, ((System.Windows.Media.SolidColorBrush)App.Resources["MenuBg"]).Color);

            App.SetTheme("Dark");
            Assert.Equal(Themes.Brush("#2C2C2C").Color, ((System.Windows.Media.SolidColorBrush)App.Resources["MenuBg"]).Color);
            Assert.True(Saver.Requests > 0); // the choice is saved
        });
    }

    [Fact]
    public void Contrast_theme_and_text_size_changes_reach_every_window()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            App.ShowArchive();

            Themes.ForceHighContrast = true;
            App.OnSystemParameterChanged(null, new PropertyChangedEventArgs("FocusBorderWidth")); // other settings: ignored
            Pump();
            Assert.NotSame(SystemColors.WindowBrush, w.Resources["BoardBg"]);

            App.OnSystemParameterChanged(null, new PropertyChangedEventArgs(nameof(SystemParameters.HighContrast)));
            Pump();
            Assert.Same(SystemColors.WindowBrush, w.Resources["BoardBg"]);
            Assert.Same(SystemColors.HighlightBrush, App.Resources["AccentFill"]);

            A11y.TextScaleSource = () => 2.0;
            App.RefreshAllWindows();
            Assert.Equal(2.0, ((System.Windows.Media.ScaleTransform)((FrameworkElement)w.Content).LayoutTransform).ScaleX);
            Assert.Equal(2.0, ((System.Windows.Media.ScaleTransform)((FrameworkElement)App.Archive!.Content).LayoutTransform).ScaleX);
        });
    }

    [Fact]
    public void Monitor_changes_bring_lost_widgets_back()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Board.X = -100000; // its monitor was unplugged
            App.OnDisplaySettingsChanged(null, EventArgs.Empty);
            Pump();
            Assert.InRange(w.Left, Screens.Primary.Left, Screens.Primary.Right);
        });
    }

    // ================= Widgets closed by Windows =================

    [Fact]
    public void A_widget_closed_by_Windows_is_reopened()
    {
        Ui(() =>
        {
            PinList.App.ReopenDelay = TimeSpan.FromMilliseconds(50);
            try
            {
                var w = OpenNew(("A", new string[0]));
                DestroyWindow(new WindowInteropHelper(w).Handle); // like an Explorer restart
                Pump();
                Assert.DoesNotContain(w, App.OpenWindows);
                Assert.True(UiHost.WaitUntil(() => App.OpenWindows.Any(x => x.Board == w.Board)));

                // deleted meanwhile: not reopened
                var gone = OpenNew(("B", new string[0]));
                DestroyWindow(new WindowInteropHelper(gone).Handle);
                Store.Data.Boards.Remove(gone.Board);
                UiHost.Wait(TimeSpan.FromMilliseconds(150));
                Assert.DoesNotContain(App.OpenWindows, x => x.Board == gone.Board);

                // reopened meanwhile by something else: not twice
                var twice = OpenNew(("C", new string[0]));
                DestroyWindow(new WindowInteropHelper(twice).Handle);
                App.OpenBoard(twice.Board);
                UiHost.Wait(TimeSpan.FromMilliseconds(150));
                Assert.Single(App.OpenWindows, x => x.Board == twice.Board);
            }
            finally { PinList.App.ReopenDelay = TimeSpan.FromSeconds(2); }
        });
    }

    // ================= Tray icon =================

    [Fact]
    public void Tray_left_click_shows_the_widgets_with_focus_in_the_last()
    {
        Ui(() =>
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
        });
    }

    [Fact]
    public void Tray_menu()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Board.Style = "Green";
            Tray.ClickRight();
            var menu = OpenMenu();
            Assert.Equal(new object[] { "New window", "Archive", "Show all windows", "App theme", "Export…", "Import…", "Start with Windows", "Open data folder", "Exit" },
                menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Header));

            ChooseFromMenu(menu, "New window");
            Assert.Equal("Green", Store.Data.Boards.Last().Style); // like the last widget

            ChooseFromMenu(App.ShowTrayMenu(), "Archive");
            Assert.NotNull(App.Archive);
            ChooseFromMenu(App.ShowTrayMenu(), "Show all windows");
            ChooseFromMenu(App.ShowTrayMenu(), "App theme", "Light");
            Assert.Equal("Light", Store.Data.Settings.Theme);
            ChooseFromMenu(App.ShowTrayMenu(), "Export…");
            ChooseFromMenu(App.ShowTrayMenu(), "Import…");
            ChooseFromMenu(App.ShowTrayMenu(), "Open data folder");
            Assert.Equal(new[] { Store.Dir }, UiHost.OpenedFolders);
            ChooseFromMenu(App.ShowTrayMenu(), "Exit");
            Assert.Equal(1, UiHost.ShutdownRequests);
            PinList.App.ShuttingDown = false;
        });
    }

    [Fact]
    public void Tray_menu_turns_start_with_Windows_on_and_off()
    {
        Ui(() =>
        {
            Autostart.RunKey = @"Software\PinListTests\TrayAutostart";
            try
            {
                Autostart.Set(false);
                var item = MenuItem(App.ShowTrayMenu(), "Start with Windows");
                Assert.False(item.IsChecked);
                Assert.True(item.IsEnabled);
                ChooseFromMenu(OpenMenu(), "Start with Windows");
                Assert.True(Autostart.IsEnabled());
                ChooseFromMenu(App.ShowTrayMenu(), "Start with Windows");
                Assert.False(Autostart.IsEnabled());

                PinList.App.ManageAutostart = false; // a development copy can't change it
                Assert.False(MenuItem(App.ShowTrayMenu(), "Start with Windows").IsEnabled);
            }
            finally
            {
                PinList.App.ManageAutostart = true;
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\PinListTests\TrayAutostart", false);
                Autostart.RunKey = UiHost.TestRunKey;
            }
        });
    }

    [Fact]
    public void Tray_icon_image_falls_back_when_the_app_icon_is_missing()
    {
        Ui(() =>
        {
            using var icon = PinList.App.LoadTrayIcon();
            Assert.True(icon.Width > 0);

            var real = PinList.App.TrayIconUri;
            PinList.App.TrayIconUri = "pack://application:,,,/Assets/missing.ico";
            try { Assert.Equal(System.Drawing.SystemIcons.Application.Size, PinList.App.LoadTrayIcon().Size); } // the standard Windows icon
            finally { PinList.App.TrayIconUri = real; }
        });
    }

    // ================= Reminders =================

    [Fact]
    public void Due_reminders_show_a_notification_and_clicking_it_shows_the_item()
    {
        Ui(() =>
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
            Assert.Same(Row(w, item), System.Windows.Input.Keyboard.FocusedElement);

            Actions.Delete(item);
            Tray.ClickNotification(); // the item is gone meanwhile: just shows the widgets
        });
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
        { "version": 2, "boards": [ { "id": "b", "title": "Imported", "categories": [ { "id": "c", "name": "General", "items": [ { "text": "from file" } ] } ] } ],
          "archive": [ { "text": "done", "categoryId": "c", "completedAt": "2026-01-01T00:00:00" } ] }
        """;

    [Fact]
    public void Export_writes_the_file_or_says_why_not()
    {
        Ui(() =>
        {
            MakeBoard(("A", new[] { "a" }));
            App.ExportData(null); // cancelled
            Assert.False(Directory.Exists(Path.Combine(Dir, "out")));

            Dialogs.SaveFileAnswer = Path.Combine(Dir, "export.json");
            Directory.CreateDirectory(Dir);
            App.ExportData(null);
            Assert.Contains("\"a\"", File.ReadAllText(Dialogs.SaveFileAnswer));

            Dialogs.SaveFileAnswer = Path.Combine(Dir, "missing folder", "export.json");
            App.ExportData(null);
            Assert.Contains(Dialogs.Shown, s => s.StartsWith("Export failed: "));
        });
    }

    [Fact]
    public void Import_cancelled_or_unreadable()
    {
        Ui(() =>
        {
            App.ImportData(null); // no file chosen
            Assert.Equal(new[] { "open" }, Dialogs.Shown);

            Dialogs.OpenFileAnswer = WriteFile(Dir, "bad.json", "not json");
            App.ImportData(null);
            Assert.Contains(Dialogs.Shown, s => s.StartsWith("This file can't be imported: "));

            Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
            App.ImportData(null); // the question is answered with Esc
            Assert.Contains(Dialogs.Shown, s => s.StartsWith("Import good.json?: 1 window with 1 item, and 1 completed item in the archive."));
            Assert.Empty(Store.Data.Boards);
        });
    }

    [Fact]
    public void Import_adds_widgets_next_to_the_existing_ones()
    {
        Ui(() =>
        {
            var mine = OpenNew(("Mine", new[] { "mine" }));
            Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
            Dialogs.ChooseAnswer = _ => 2; // Add
            App.ImportData(mine);

            Assert.Equal(new[] { "Test", "Imported" }, Store.Data.Boards.Select(b => b.Title));
            Assert.Contains(App.OpenWindows, w => w.Board.Title == "Imported");
            Assert.Single(Store.Data.Archive);
        });
    }

    [Fact]
    public void Import_can_replace_everything()
    {
        Ui(() =>
        {
            var mine = OpenNew(("Mine", new[] { "mine" }));
            App.ShowArchive();
            Actions.SetItemText(mine.Board.Categories[0].Items[0], "changed");
            Dialogs.OpenFileAnswer = WriteFile(Dir, "newer.json", ImportJson.Replace("\"version\": 2", "\"version\": 99"));
            Dialogs.ChooseAnswer = _ => 1; // Replace everything
            App.ImportData(null);

            Assert.Contains(Dialogs.Shown, s => s.Contains("from a newer version of PinList"));
            Assert.Equal(new[] { "Imported" }, Store.Data.Boards.Select(b => b.Title));
            Assert.False(mine.IsVisible);
            Assert.Null(App.Archive); // it showed the old archive
            Assert.Single(App.OpenWindows);
            Assert.False(Actions.CanUndo); // the old undo history doesn't fit the new data
            Assert.Single(Directory.GetFiles(Path.Combine(Dir, "backups"), "before-import-*.json"));
        });
    }

    [Fact]
    public void Importing_a_file_without_widgets_still_leaves_one()
    {
        Ui(() =>
        {
            Dialogs.OpenFileAnswer = WriteFile(Dir, "empty.json", """{ "version": 2, "boards": [] }""");
            Dialogs.ChooseAnswer = _ => 1;
            App.ImportData(null);
            Assert.Single(Store.Data.Boards);
            Assert.Single(App.OpenWindows);
        });
    }

    [Fact]
    public void A_failing_import_says_so()
    {
        Ui(() =>
        {
            Dialogs.OpenFileAnswer = WriteFile(Dir, "good.json", ImportJson);
            File.WriteAllText(Path.Combine(Dir, "backups"), "a file where the backups folder should be");
            Dialogs.ChooseAnswer = _ => 1;
            App.ImportData(null);
            Assert.Contains(Dialogs.Shown, s => s.StartsWith("Import failed: "));
        });
    }

    [Fact]
    public void Plural()
    {
        Assert.Equal("1 item", PinList.App.Plural(1, "item"));
        Assert.Equal("0 items", PinList.App.Plural(0, "item"));
    }

    [Fact]
    public void Store_messages_use_the_app_dialog()
    {
        Ui(() =>
        {
            Store.ShowMessage("Backup loaded", false);
            Store.ShowMessage("Damaged", true);
            Assert.Equal(new[] { "PinList: Backup loaded", "PinList couldn't load your lists: Damaged" }, Dialogs.Shown);
        });
    }
}
