using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PinList;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public static bool ShuttingDown { get; internal set; }

    /// <summary>
    /// Set PINLIST_DATA_DIR to use another data folder while developing (the tests do too). Such a run doesn't
    /// touch "Start with Windows" and can run next to the normal PinList.
    /// </summary>
    internal static readonly string? DevDataDir = Environment.GetEnvironmentVariable("PINLIST_DATA_DIR");

    /// <summary>Name for the single-instance check; a development copy gets its own (stable across processes).</summary>
    internal static string InstanceName => DevDataDir == null ? "PinList" : "PinList." + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DevDataDir.ToLowerInvariant())))[..16];

    // ---- Things tests replace before the app starts ----

    /// <summary>Whether this run adds PinList to "Start with Windows" on first start and keeps its path current.</summary>
    internal static bool ManageAutostart { get; set; } = DevDataDir == null;

    internal static Func<ITrayIcon> CreateTrayIcon { get; set; } = CreateRealTrayIcon;

    internal static Action<string> OpenFolder { get; set; } = OpenInExplorer;

    internal static Action<App> ShutdownApp { get; set; } = ShutdownProcess;

    // These three act on Windows itself (an icon in the real taskbar, an Explorer window, ending the process),
    // so tests replace them and they're left out of the coverage numbers.

    [ExcludeFromCodeCoverage(Justification = "Puts a real icon in the taskbar; tests use a fake tray icon.")]
    static ITrayIcon CreateRealTrayIcon() => new TrayIcon(LoadTrayIcon());

    [ExcludeFromCodeCoverage(Justification = "Opens an Explorer window.")]
    static void OpenInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    [ExcludeFromCodeCoverage(Justification = "Ends the app (in tests: the test run).")]
    static void ShutdownProcess(App app) => app.Shutdown();

    /// <summary>The app theme (menus, dialogs, archive) changed.</summary>
    public static event Action? ThemeChanged;

    SingleInstance? _instance;
    ITrayIcon? _tray;
    Window? _menuHost;
    ArchiveWindow? _archive;
    readonly List<BoardWindow> _windows = new();
    readonly Reminders _reminders = new();
    IReadOnlyList<TodoItem> _lastReminders = Array.Empty<TodoItem>();
    static bool _errorLoggingHooked;

    internal IReadOnlyList<BoardWindow> OpenWindows => _windows;
    internal ArchiveWindow? Archive => _archive;
    internal Reminders ReminderService => _reminders;
    internal ITrayIcon? Tray => _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!Start())
        {
            ShuttingDown = true;
            ShutdownApp(this);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Stop();
        base.OnExit(e);
    }

    /// <summary>
    /// Starts PinList: loads the data and opens the widgets, tray icon and reminders. Returns false if another
    /// PinList is already running (it was told to show its widgets, and this one should quit).
    /// </summary>
    internal bool Start()
    {
        // Single instance only. Starting PinList again brings the running one's widgets to the front.
        var instance = new SingleInstance(InstanceName);
        if (!instance.IsFirst)
        {
            instance.SignalFirst();
            instance.Dispose();
            return false;
        }
        _instance = instance;
        ShuttingDown = false;
        HookErrorLogging();

        if (DevDataDir != null) Store.UseDirectory(DevDataDir);
        ApplyTheme(); // before loading, so a message about damaged data already has its colors

        bool firstRun = !Store.Load();
        ApplyTheme(); // now with the saved theme setting

        if (firstRun)
        {
            // First run: one empty widget (in the Windows color), start with Windows
            Store.Data.Boards.Add(Store.DefaultBoard(Screens.Primary, Themes.SystemUsesDark() ? "Dark" : "Light"));
            if (ManageAutostart) Autostart.Set(true);
            Store.SaveNow();
        }
        if (Store.Data.Boards.Count == 0)
        {
            Store.Data.Boards.Add(Store.DefaultBoard(Screens.Primary));
            Store.SaveNow();
        }
        if (ManageAutostart) Autostart.RefreshPath();

        Actions.BoardAdded += OpenBoard;
        OpenAllBoards();
        CreateTray();
        _instance.ListenForShow(() => Dispatcher.InvokeAsync(ShowAllBoards));

        _reminders.Due += ShowReminders;
        _reminders.Start();

        SessionEnding += OnSessionEnding;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        return true;
    }

    /// <summary>Saves and releases everything (on exit).</summary>
    internal void Stop()
    {
        ShuttingDown = true;
        Actions.BoardAdded -= OpenBoard;
        SessionEnding -= OnSessionEnding;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        _reminders.Due -= ShowReminders;
        _reminders.Stop();
        Store.Flush();
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
    }

    public void ExitApp()
    {
        ShuttingDown = true;
        Store.Flush();
        ShutdownApp(this);
    }

    void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        ShuttingDown = true;
        Store.Flush();
    }

    // ================= Errors =================

    void HookErrorLogging()
    {
        if (_errorLoggingHooked) return;
        _errorLoggingHooked = true;
        DispatcherUnhandledException += (_, ex) => OnUiError(ex);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => OnFatalError(ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => OnBackgroundError(ex);
    }

    /// <summary>UI thread: log and keep running.</summary>
    internal static void OnUiError(DispatcherUnhandledExceptionEventArgs e)
    {
        Store.Log("Error: " + e.Exception);
        e.Handled = true;
    }

    /// <summary>Other threads: the app can't continue, but the error is logged and pending changes are saved.</summary>
    internal void OnFatalError(object error)
    {
        Store.Log("Fatal error: " + error);
        // Save on the UI thread. If saving fails there, the UI error handler logs it; if the UI thread is
        // already gone, Invoke simply does nothing.
        Dispatcher.Invoke(Store.Flush, TimeSpan.FromSeconds(2));
    }

    /// <summary>Background tasks whose errors nobody awaited.</summary>
    internal static void OnBackgroundError(UnobservedTaskExceptionEventArgs e)
    {
        Store.Log("Background error: " + e.Exception);
        e.SetObserved();
    }

    // ================= Theme =================

    internal void ApplyTheme()
    {
        Themes.ApplyBoard(Resources, Themes.Get("Dark"), true); // defaults; every widget overrides these
        Themes.ApplyChrome(Resources, Themes.IsDark(Store.Data.Settings.Theme));
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Windows changed something that affects every window: light/dark, a contrast theme, or the text size.
    /// </summary>
    internal void RefreshAllWindows()
    {
        A11y.RefreshTextScale();
        ApplyTheme();
        foreach (Window w in Windows)
        {
            if (w is BoardWindow board) board.RefreshAppearance();
            else A11y.ApplyTextScale(w);
        }
    }

    public void SetTheme(string key)
    {
        Store.Data.Settings.Theme = key;
        Store.MarkDirty();
        ApplyTheme();
    }

    /// <summary>
    /// Follows Windows switching between light and dark (while the setting is "Use Windows setting"),
    /// turning a contrast theme on or off, and changing the text size.
    /// </summary>
    internal void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshAllWindows, DispatcherPriority.Background);

    internal void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.InvokeAsync(RefreshAllWindows);
    }

    /// <summary>"App theme" submenu, used in the tray menu and in each widget's ⋯ menu.</summary>
    public MenuItem ThemeMenu()
    {
        var menu = new MenuItem { Header = "App theme" };
        foreach (var (key, name) in Themes.AppThemes)
        {
            var mi = CreateMenuItem(name, () => SetTheme(key));
            mi.IsChecked = Store.Data.Settings.Theme == key;
            menu.Items.Add(mi);
        }
        return menu;
    }

    // ================= Windows =================

    internal void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var w in _windows.ToList()) w.EnsureOnScreen();
        });

    internal void OpenAllBoards()
    {
        foreach (var board in Store.Data.Boards.ToList())
            OpenBoard(board);
    }

    /// <summary>How long to wait before reopening a widget that Windows closed (e.g. because Explorer restarted).</summary>
    internal static TimeSpan ReopenDelay { get; set; } = TimeSpan.FromSeconds(2);

    public void OpenBoard(Board board)
    {
        var w = new BoardWindow(board);
        w.Closed += (_, _) =>
        {
            _windows.Remove(w);
            // When Explorer restarts, Windows destroys our widgets too (their owner is the desktop).
            // Reopen them; the data is still in memory and on disk.
            if (!w.UserDeleted && !ShuttingDown && Store.Data.Boards.Contains(board))
            {
                var t = new DispatcherTimer { Interval = ReopenDelay };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (!ShuttingDown && Store.Data.Boards.Contains(board) && _windows.All(x => x.Board != board))
                        OpenBoard(board);
                };
                t.Start();
            }
        };
        _windows.Add(w);
        w.Show();
    }

    public void ShowAllBoards()
    {
        foreach (var w in _windows.ToList())
        {
            if (!w.IsVisible) w.Show();
            w.Topmost = true;   // briefly on top so it shows above other apps
            w.Topmost = false;
        }
        var last = _windows.LastOrDefault();
        if (last == null) return;
        last.Activate();
        // keyboard users can start working right away (Tab moves to the other parts of the widget)
        if (Keyboard.FocusedElement is not DependencyObject d || !last.IsAncestorOf(d)) last.FocusContent();
    }

    public void ShowArchive()
    {
        if (_archive == null)
        {
            _archive = new ArchiveWindow();
            _archive.Closed += (_, _) => _archive = null;
            _archive.Show();
        }
        else
        {
            if (_archive.WindowState == WindowState.Minimized) _archive.WindowState = WindowState.Normal;
            _archive.Activate();
        }
    }

    /// <summary>New widget on the monitor under the mouse (tray menu).</summary>
    internal void NewBoardAtCursor() => Actions.NewBoard(Screens.WorkAreaAtCursor(), _windows.LastOrDefault()?.Board);

    // ================= Reminders =================

    internal void ShowReminders(IReadOnlyList<TodoItem> due)
    {
        if (_tray == null) return;
        _lastReminders = due;
        var (title, text) = Reminders.Describe(due);
        _tray.ShowNotification(title, text);
    }

    /// <summary>Clicking the notification brings the widgets to the front and highlights the item.</summary>
    internal void OnReminderClicked()
    {
        ShowAllBoards();
        var item = _lastReminders.FirstOrDefault(i => Store.Locate(i) != null);
        if (item != null) BoardWindow.Highlight(item);
    }

    // ================= Export and import =================

    const string JsonFilter = "PinList data (*.json)|*.json";

    public void ExportData(Window? owner)
    {
        var path = Dialogs.Current.SaveFile(owner, "Export PinList data", $"PinList-{DateTime.Now:yyyy-MM-dd}.json", JsonFilter);
        if (path == null) return;

        try
        {
            Store.Export(path);
        }
        catch (Exception ex)
        {
            Store.Log("Export: " + ex);
            Dialogs.Info(owner, "Export failed", ex.Message);
        }
    }

    public void ImportData(Window? owner)
    {
        var path = Dialogs.Current.OpenFile(owner, "Import PinList data", JsonFilter + "|All files (*.*)|*.*");
        if (path == null) return;

        AppData imported;
        int fileVersion;
        try
        {
            imported = Store.ReadFile(path, out fileVersion);
        }
        catch (Exception ex)
        {
            Store.Log("Import: " + ex);
            Dialogs.Info(owner, "This file can't be imported",
                "It isn't a PinList export or data.json file, or it is damaged.\n\n" + ex.Message);
            return;
        }

        int windows = imported.Boards.Count;
        int items = imported.Boards.SelectMany(b => b.Categories).Sum(c => c.Items.Count);
        int archived = imported.Archive.Count;
        string message =
            $"{Plural(windows, "window")} with {Plural(items, "item")}, and {Plural(archived, "completed item")} in the archive.\n\n" +
            "Add them next to your current windows, or replace everything you have now? " +
            "Replacing keeps a copy of your current lists in the backups folder.";
        if (fileVersion > AppData.CurrentVersion)
            message += "\n\nThe file is from a newer version of PinList, so some details may be lost.";

        int choice = Dialogs.Current.Choose(owner, $"Import {Path.GetFileName(path)}?", message, 2,
            ("Cancel", DialogButtonKind.Normal),
            ("Replace everything", DialogButtonKind.Danger),
            ("Add", DialogButtonKind.Accent));

        try
        {
            if (choice == 2)
            {
                foreach (var board in Store.Merge(imported)) OpenBoard(board);
            }
            else if (choice == 1)
            {
                _archive?.Close();
                foreach (var w in _windows.ToList()) w.CloseForReload();
                Store.ReplaceAll(imported);
                Actions.ClearUndo();
                if (Store.Data.Boards.Count == 0)
                {
                    Store.Data.Boards.Add(Store.DefaultBoard(Screens.Primary));
                    Store.SaveNow();
                }
                OpenAllBoards();
                _reminders.Check();
            }
        }
        catch (Exception ex)
        {
            Store.Log("Import: " + ex);
            Dialogs.Info(owner, "Import failed", ex.Message);
        }
    }

    internal static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    // ================= Tray icon =================

    void CreateTray()
    {
        _tray = CreateTrayIcon();
        _tray.LeftClick += ShowAllBoards;
        _tray.RightClick += () => ShowTrayMenu();
        _tray.NotificationClicked += OnReminderClicked;
    }

    /// <summary>The tray menu is a WPF ContextMenu, so it looks like every other menu in the app.</summary>
    internal ContextMenu ShowTrayMenu()
    {
        // Invisible host window: the menu only closes on an outside click if the app is in the foreground
        if (_menuHost == null)
        {
            _menuHost = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Width = 1, Height = 1, Left = -32000, Top = -32000,
            };
            _menuHost.SourceInitialized += (_, _) => Native.MakeToolWindow(_menuHost);
            _menuHost.Show();
        }
        _menuHost.Activate();
        Native.BringToForeground(_menuHost);

        var menu = new ContextMenu { PlacementTarget = _menuHost, Placement = PlacementMode.MousePoint };
        menu.Items.Add(CreateMenuItem("New window", NewBoardAtCursor));
        menu.Items.Add(CreateMenuItem("Archive", ShowArchive));
        menu.Items.Add(CreateMenuItem("Show all windows", ShowAllBoards));
        menu.Items.Add(new Separator());
        menu.Items.Add(ThemeMenu());
        menu.Items.Add(CreateMenuItem("Export…", () => ExportData(null)));
        menu.Items.Add(CreateMenuItem("Import…", () => ImportData(null)));
        menu.Items.Add(new Separator());
        var auto = CreateMenuItem("Start with Windows", () => Autostart.Set(!Autostart.IsEnabled()));
        auto.IsChecked = Autostart.IsEnabled();
        auto.IsEnabled = ManageAutostart;
        menu.Items.Add(auto);
        menu.Items.Add(CreateMenuItem("Open data folder", () => OpenFolder(Store.Dir)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", ExitApp));
        menu.IsOpen = true;
        return menu;
    }

    public static MenuItem CreateMenuItem(string header, Action action, string? gesture = null)
    {
        var mi = new MenuItem { Header = header };
        if (gesture != null) mi.InputGestureText = gesture;
        // deferred so the menu closes before the action runs (e.g. before a TextBox takes focus)
        mi.Click += (_, _) => Current.Dispatcher.InvokeAsync(action, DispatcherPriority.ApplicationIdle);
        return mi;
    }

    /// <summary>Address of the icon (text: the pack:// scheme only exists once WPF has started).</summary>
    internal static string TrayIconUri { get; set; } = "pack://application:,,,/Assets/app.ico";

    internal static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            // a missing resource throws (IOException) rather than returning null
            using var s = GetResourceStream(new Uri(TrayIconUri))!.Stream;
            return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Store.Log("Tray icon: " + ex.Message);
            return System.Drawing.SystemIcons.Application;
        }
    }
}
