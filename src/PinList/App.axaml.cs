using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PinList.Sync;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace PinList;

public partial class App : Application
{
    public static App Instance => (App)Current!;
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

    internal static Action<string> OpenFolder { get; set; } = OpenInFileManager;

    internal static Action<App> ShutdownApp { get; set; } = ShutdownProcess;

    /// <summary>Opens a web page in the default browser (Google sign-in).</summary>
    internal static Action<string> OpenUrl { get; set; } = OpenInBrowser;

    /// <summary>Creates the sync service when the app starts (tests use one without Google).</summary>
    internal static Func<SyncService> CreateSyncService { get; set; } = CreateRealSync;

    /// <summary>Creates the update check when the app starts (tests use one without GitHub).</summary>
    internal static Func<Updates> CreateUpdates { get; set; } = CreateRealUpdates;

    // These act on the operating system itself (an icon in the real taskbar, a file manager window, ending the
    // process), so tests replace them and they're left out of the coverage numbers.

    [ExcludeFromCodeCoverage(Justification = "Puts a real icon in the taskbar; tests use a fake tray icon.")]
#if WINDOWS
    static ITrayIcon CreateRealTrayIcon() => new TrayIcon(LoadTrayIcon());
#else
    static ITrayIcon CreateRealTrayIcon() => new AvaloniaTrayIcon();
#endif

    [ExcludeFromCodeCoverage(Justification = "Opens a file manager window.")]
    static void OpenInFileManager(string path) =>
        Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : "open", $"\"{path}\"") { UseShellExecute = true });

    [ExcludeFromCodeCoverage(Justification = "Opens the browser.")]
    static void OpenInBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    [ExcludeFromCodeCoverage(Justification = "Talks to Google with this build's client id; tests use fakes.")]
    static SyncService CreateRealSync()
    {
        var auth = new DesktopGoogleAuth(GoogleClient.BuiltIn, TokenStores.ForThisOs(Store.Dir), Http,
            url => { OpenUrl(url); return Task.CompletedTask; });
        return new SyncService(auth, () => new DriveCloudStore(Http, auth.AccessToken), Environment.MachineName);
    }

    [ExcludeFromCodeCoverage(Justification = "Asks GitHub for PinList's latest release; tests use fakes.")]
    static Updates CreateRealUpdates() => Updates.ForGitHub(Http, typeof(App).Assembly.GetName().Version!);

    [ExcludeFromCodeCoverage(Justification = "Ends the app (in tests: the test run).")]
    static void ShutdownProcess(App app) =>
        (app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    /// <summary>The app theme (menus, dialogs, archive) changed.</summary>
    public static event Action? ThemeChanged;

    SingleInstance? _instance;
    ITrayIcon? _tray;
    Window? _menuHost;
    readonly List<BoardWindow> _windows = new();
    readonly Reminders _reminders = new();
    IReadOnlyList<TodoItem> _lastReminders = Array.Empty<TodoItem>();
    Updates? _updates;
    bool _lastNotificationIsUpdate;
    static bool _errorLoggingHooked;

    internal IReadOnlyList<BoardWindow> OpenWindows => _windows;
    internal Updates? UpdateCheck => _updates;
    internal Reminders ReminderService => _reminders;
    internal ITrayIcon? Tray => _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    [ExcludeFromCodeCoverage(Justification = "Only runs with the real desktop lifetime; tests call Start themselves.")]
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Stop();
            desktop.ShutdownRequested += OnShutdownRequested;
            if (!Start())
            {
                ShuttingDown = true;
                ShutdownApp(this);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    // Before anything else (the reminder service above already needs a timer), and before loading the data,
    // so messages about damaged data reach a dialog
    static App() => ConfigureCore();

    /// <summary>Connects PinList.Core and PinList.UI to this app and this operating system.</summary>
    internal static void ConfigureCore()
    {
        UiTimers.Factory = () => new AvaloniaUiTimer();
        Store.Notify = ShowStoreMessage;
        Themes.ContrastSource = Native.SystemContrastColors;
#if WINDOWS
        Themes.SystemThemeName = "Use Windows setting";
        Themes.SystemDarkSource = WindowsSettings.AppsUseDark;
        A11y.TextScaleSource = WindowsSettings.TextScale;
#else
        Themes.SystemDarkSource = () => Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
#endif
        A11y.RefreshTextScale();
    }

    /// <summary>How PinList.Core's messages are shown: a dialog in the app's style.</summary>
    internal static void ShowStoreMessage(string message, bool isError) =>
        _ = Dialogs.Info(null, isError ? "PinList couldn't load your lists" : "PinList", message);

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
            // First run: one empty widget (in the system color), start with Windows
            Store.Data.Boards.Add(Store.DefaultBoard(Displays.Primary.ToWorkArea(), Themes.SystemUsesDark() ? "Dark" : "Light"));
            if (ManageAutostart) Autostart.Set(true);
            Store.SaveNow();
        }
        if (Store.Data.Boards.Count == 0)
        {
            Store.Data.Boards.Add(Store.DefaultBoard(Displays.Primary.ToWorkArea()));
            Store.SaveNow();
        }
        if (ManageAutostart) Autostart.RefreshPath();

        Actions.BoardAdded += OpenBoard;
        Actions.BoardDeleted += OnBoardDeleted;
        Actions.WidgetChanged += OnWidgetChanged;
        UseSync(CreateSyncService());
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChange;
        OpenAllBoards();
        CreateTray();
        UseUpdates(CreateUpdates());
        if (_updates!.Enabled == null) Dispatcher.UIThread.Post(() => _ = AskAboutUpdates());
        _ = SyncService.Current?.SyncNow(); // on start (spec 5.2)
        _instance.ListenForShow(() => Dispatcher.UIThread.Post(ShowAllBoards));

        _reminders.Due += ShowReminders;
        _reminders.Start();

#if WINDOWS
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
#endif
        return true;
    }

    /// <summary>Saves and releases everything (on exit).</summary>
    internal void Stop()
    {
        ShuttingDown = true;
        Actions.BoardAdded -= OpenBoard;
        Actions.BoardDeleted -= OnBoardDeleted;
        Actions.WidgetChanged -= OnWidgetChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkChange;
        UseSync(null);
        UseUpdates(null);
        _lastNotificationIsUpdate = false;
#if WINDOWS
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
#endif
        _displaySettle?.Stop();
        BoardWindow.DisplayChanging = false;
        _reminders.Due -= ShowReminders;
        _reminders.Stop();
        Store.Flush();
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
    }

    public async void ExitApp()
    {
        ShuttingDown = true;
        Store.Flush();
        // one quick sync, never longer than 3 seconds (spec 5.2)
        if (SyncService.Current is { } sync) await sync.SyncBeforeExit(TimeSpan.FromSeconds(3));
        ShutdownApp(this);
    }

    /// <summary>
    /// Windows is shutting down, or Quit on a Mac (Cmd+Q, the Dock). Avalonia asks this before it closes the
    /// windows. PinList saves and never says no.
    /// </summary>
    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e) =>
        OnSessionEnding(sender, EventArgs.Empty);

    internal void OnSessionEnding(object? sender, EventArgs e)
    {
        ShuttingDown = true;
        Store.Flush();
        // the system is shutting down and doesn't wait for us: start the quick sync and let it run while it can
        _ = SyncService.Current?.SyncBeforeExit(TimeSpan.FromSeconds(3));
    }

    // ================= Sync =================

    [ExcludeFromCodeCoverage(Justification = "The operating system's network event; tests call OnNetworkAvailabilityChanged.")]
    void OnNetworkChange(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) =>
        OnNetworkAvailabilityChanged(sender, e.IsAvailable);

    /// <summary>The network came back: sync now instead of waiting for the next try (spec 5.2).</summary>
    internal void OnNetworkAvailabilityChanged(object? sender, bool available)
    {
        if (available) Dispatcher.UIThread.Post(() => _ = SyncService.Current?.SyncNow());
    }

    /// <summary>Replaces the sync service (on start, on exit, and in tests).</summary>
    internal void UseSync(SyncService? service)
    {
        if (SyncService.Current is { } old)
        {
            old.Stop();
            old.Changed -= OnSyncChanged;
            old.Message -= OnSyncMessage;
        }
        SyncService.Current = service;
        if (service != null)
        {
            service.WorkArea = () => Displays.Primary.ToWorkArea();
            // remote changes wait while something is dragged, in a widget or in "All lists" (spec 4.6)
            service.IsBusy = o => BoardWindow.IsDragging || _main?.Lists.IsDragging == true || SyncService.IsEditing(o);
            service.Changed += OnSyncChanged;
            service.Message += OnSyncMessage;
            service.Start();
        }
        OnSyncChanged();
    }

    /// <summary>Sign-in or a list's sync state changed: the header icons and the tray menu follow.</summary>
    void OnSyncChanged()
    {
        foreach (var w in _windows) w.RefreshSync();
        _tray?.SetMenu(TrayMenuEntries());
    }

    void OnSyncMessage(string message) => _ = Dialogs.Info(null, "PinList", message);

    /// <summary>
    /// A deleted list's widget closes: deleted on another device, or here in "All lists". A widget that deleted its
    /// own list is already closing.
    /// </summary>
    void OnBoardDeleted(Board board, bool byUser)
    {
        _windows.FirstOrDefault(w => w.Board == board && !w.UserDeleted)?.CloseForReload();
    }

    /// <summary>Signs in, with the "waiting for your browser" dialog. True when signed in.</summary>
    internal Task<bool> SignIn(Window? owner) => Dialogs.Current.WaitForSignIn(owner, SyncService.Current!.SignIn);

    /// <summary>"Sync this list": turning it on signs in first when needed (spec 4.3).</summary>
    internal async Task SetBoardSynced(Board board, bool on, Window owner)
    {
        var sync = SyncService.Current!;
        if (on && sync.Account == null && !await SignIn(owner)) return;
        await sync.SetSynced(board, on);
    }

    internal async Task SignOutAndDeleteCloudData(Window? owner)
    {
        if (await Dialogs.Ask(owner, "Delete PinList's cloud data?",
                "This removes PinList's data from Google Drive for all your devices. Your lists stay on every device.", "Delete"))
            await SyncService.Current!.SignOut(true);
    }

    /// <summary>What a red sync icon means and how to fix it (spec 4.4).</summary>
    internal async Task ExplainSyncProblem(Window owner, string problem)
    {
        var sync = SyncService.Current!;
        if (sync.NeedsSignIn)
        {
            if (await Dialogs.Current.Choose(owner, "Sync stopped", "Google signed you out. Sign in again to keep syncing.", 1,
                    ("Not now", DialogButtonKind.Normal), ("Sign in again", DialogButtonKind.Accent)) == 1)
                await SignIn(owner);
            return;
        }
        await Dialogs.Info(owner, "Sync problem", problem == "Can't reach Google Drive."
            ? "Can't reach Google Drive. PinList keeps trying, and your changes stay on this device until it works again."
            : problem);
    }

    // ================= Updates =================

    /// <summary>Replaces the update check (on start, on exit, and in tests).</summary>
    internal void UseUpdates(Updates? updates)
    {
        if (_updates is { } old)
        {
            old.Stop();
            old.Changed -= OnUpdatesChanged;
            old.Found -= OnUpdateFound;
        }
        _updates = updates;
        if (updates != null)
        {
            updates.Changed += OnUpdatesChanged;
            updates.Found += OnUpdateFound;
            updates.Start();
        }
        OnUpdatesChanged();
    }

    /// <summary>A new version or the setting changed: the tray menu follows.</summary>
    void OnUpdatesChanged() => _tray?.SetMenu(TrayMenuEntries());

    /// <summary>A new version, found for the first time: a notification that opens its download page.</summary>
    void OnUpdateFound(UpdateInfo update)
    {
        _lastNotificationIsUpdate = true;
        _tray!.ShowNotification($"PinList {update.Version} is available", "Click here to open the download page.");
    }

    /// <summary>Opens the download page of the new version (or of the latest release).</summary>
    internal void OpenUpdatePage() => OpenUrl(_updates?.Available?.PageUrl ?? Updates.ReleasesPage);

    /// <summary>Asked once (on the first start of a version with the update check), until it's answered.</summary>
    internal async Task AskAboutUpdates()
    {
        if (_updates is not { Enabled: null } updates) return;
        int choice = await Dialogs.Current.Choose(null, "Check for updates?",
            "PinList can look for a new version about once a day and tell you when there is one. " +
            "It only asks GitHub for the number of the latest version. Nothing about you or your lists is sent.\n\n" +
            "You can change this later with Check for updates in Settings.", 1,
            ("No", DialogButtonKind.Normal), ("Yes", DialogButtonKind.Accent));
        if (choice >= 0) updates.Enabled = choice == 1; // closed without answering: asked again on the next start
    }

    /// <summary>"Get PinList x.y.z" at the top of the tray menu, while a newer version is available.</summary>
    IEnumerable<TrayEntry> UpdateEntries() => _updates?.Available is { } u
        ? new[] { new TrayEntry($"Get PinList {u.Version}", OpenUpdatePage), TrayEntry.Separator }
        : Array.Empty<TrayEntry>();

    // ================= Errors =================

    void HookErrorLogging()
    {
        if (_errorLoggingHooked) return;
        _errorLoggingHooked = true;
        Dispatcher.UIThread.UnhandledException += (_, ex) => OnUiError(ex);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => OnFatalError(ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => OnBackgroundError(ex);
    }

    /// <summary>UI thread: log and keep running.</summary>
    internal static void OnUiError(DispatcherUnhandledExceptionEventArgs e)
    {
        Store.Log($"Error: {e.Exception}");
        e.Handled = true;
    }

    /// <summary>Other threads: the app can't continue, but the error is logged and pending changes are saved.</summary>
    internal static void OnFatalError(object error)
    {
        Store.Log($"Fatal error: {error}");
        // Save on the UI thread. If saving fails there, the UI error handler logs it.
        try { Dispatcher.UIThread.Invoke(Store.Flush, DispatcherPriority.Send, default, TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { Store.Log("Saving after the error: " + ex.Message); }
    }

    /// <summary>Background tasks whose errors nobody awaited.</summary>
    internal static void OnBackgroundError(UnobservedTaskExceptionEventArgs e)
    {
        Store.Log($"Background error: {e.Exception}");
        e.SetObserved();
    }

    // ================= Theme =================

    internal void ApplyTheme()
    {
        Themes.ApplyBoard(Resources, Themes.Get("Dark"), true); // defaults; every widget overrides these
        bool dark = Themes.IsDark(Store.Data.Settings.Theme);
        Themes.ApplyChrome(Resources, dark);
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// The operating system changed something that affects every window: light/dark, a contrast theme, or the
    /// text size.
    /// </summary>
    internal void RefreshAllWindows()
    {
        A11y.RefreshTextScale();
        ApplyTheme();
        foreach (var w in _windows) w.RefreshAppearance();
        _main?.RefreshAppearance();
    }

    public void SetTheme(string key)
    {
        Store.Data.Settings.Theme = key;
        Store.MarkDirty();
        ApplyTheme();
    }

#if WINDOWS
    /// <summary>
    /// Follows Windows switching between light and dark (while the setting is "Use Windows setting"),
    /// turning a contrast theme on or off, and changing the text size.
    /// </summary>
    internal void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshAllWindows, DispatcherPriority.Background);
#endif

    // ================= Windows =================

    /// <summary>
    /// How long after the last monitor change the widgets are put back once more. When a monitor wakes up or is
    /// plugged in, Windows keeps moving windows around for a moment after it reports the change.
    /// </summary>
    internal static TimeSpan DisplaySettleDelay { get; set; } = TimeSpan.FromSeconds(2);

    DispatcherTimer? _displaySettle;

    internal void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            BoardWindow.DisplayChanging = true; // moves until the monitors settle aren't the user's
            RestoreWidgetPositions();
            if (_displaySettle == null)
            {
                _displaySettle = new DispatcherTimer();
                _displaySettle.Tick += (_, _) =>
                {
                    _displaySettle.Stop();
                    RestoreWidgetPositions();
                    BoardWindow.DisplayChanging = false;
                };
            }
            _displaySettle.Stop(); // restart the wait on every change
            _displaySettle.Interval = DisplaySettleDelay;
            _displaySettle.Start();
        });

#if WINDOWS
    internal void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        OnDisplaySettingsChanged(sender, e);
        Dispatcher.UIThread.Post(() => _ = SyncService.Current?.SyncNow()); // spec 5.2: after waking up
    }
#endif

    void RestoreWidgetPositions()
    {
        if (ShuttingDown) return;
        foreach (var w in _windows.ToList()) w.EnsureOnScreen();
    }

    /// <summary>Opens a widget for every list that is one on this computer, or "All lists" when none is.</summary>
    internal void OpenAllBoards()
    {
        foreach (var board in Store.Data.Boards.Where(b => b.Widget).ToList())
            OpenBoard(board);
        _main?.Reload();
        if (_windows.Count == 0) ShowListsWindow();
    }

    /// <summary>A list became a widget on this computer (it opens where it was) or stopped being one (it closes).</summary>
    void OnWidgetChanged(Board board)
    {
        var open = _windows.FirstOrDefault(w => w.Board == board);
        if (board.Widget && open == null && Store.Data.Boards.Contains(board)) OpenBoard(board);
        else if (!board.Widget)
        {
            open?.CloseForReload();
            if (_windows.Count == 0) ShowListsWindow(); // the last widget went: keep a way back to the lists
        }
    }

    MainWindow? _main;

    /// <summary>The PinList window (All lists, Archive, Settings), once it was opened. It is hidden, not closed, when the user closes it.</summary>
    internal MainWindow? MainWindow => _main;

    /// <summary>Shows the PinList window on a section (opens it the first time, brings it to the front after that).</summary>
    internal void ShowMain(MainSection section)
    {
        if (_main == null)
        {
            _main = new MainWindow();
            var created = _main;
            created.Closed += (_, _) => { if (_main == created) _main = null; };
        }
        if (!_main.IsVisible) _main.Show();
        _main.ShowSection(section);
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    /// <summary>Shows "All lists".</summary>
    internal void ShowListsWindow() => ShowMain(MainSection.Lists);

    /// <summary>Shows the archive.</summary>
    public void ShowArchive() => ShowMain(MainSection.Archive);

    /// <summary>Shows Settings.</summary>
    internal void ShowSettings() => ShowMain(MainSection.Settings);

    /// <summary>Closes the PinList window for good (tests use it, the app closes it on exit through ShuttingDown).</summary>
    internal void CloseMainWindow()
    {
        var main = _main;
        _main = null;
        if (main == null) return;
        bool wasShuttingDown = ShuttingDown;
        ShuttingDown = true; // so it closes instead of hiding
        main.Close();
        ShuttingDown = wasShuttingDown;
    }

    /// <summary>How long to wait before reopening a widget that the system closed (e.g. because Explorer restarted).</summary>
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
        if (last == null)
        {
            ShowListsWindow(); // no widgets on this computer: the lists are there
            return;
        }
        last.Activate();
        // keyboard users can start working right away (Tab moves to the other parts of the widget)
        if (last.FocusManager?.GetFocusedElement() is not Visual v || !last.IsVisualAncestorOf(v)) last.FocusContent();
    }

    /// <summary>Any open PinList window (dialogs and file pickers need one to belong to).</summary>
    internal Window AnyWindow() =>
        (Window?)_windows.FirstOrDefault(w => w.IsVisible) ?? _main ?? MenuHost();

    /// <summary>New widget on the monitor under the mouse (tray menu).</summary>
    internal void NewBoardAtCursor() => Actions.NewBoard(Displays.WorkAreaAtCursor().ToWorkArea(), _windows.LastOrDefault()?.Board);

    // ================= Reminders =================

    internal void ShowReminders(IReadOnlyList<TodoItem> due)
    {
        if (_tray == null) return;
        _lastReminders = due;
        _lastNotificationIsUpdate = false;
        var (title, text) = Reminders.Describe(due);
        _tray.ShowNotification(title, text);
    }

    /// <summary>
    /// Clicking a reminder brings the widgets to the front and highlights the item. Clicking the notification
    /// about a new version opens its download page.
    /// </summary>
    internal void OnReminderClicked()
    {
        if (_lastNotificationIsUpdate)
        {
            _lastNotificationIsUpdate = false;
            OpenUpdatePage();
            return;
        }
        ShowAllBoards();
        var item = _lastReminders.FirstOrDefault(i => Store.Locate(i) != null);
        if (item != null) BoardWindow.Highlight(item);
    }

    // ================= Export and import =================

    public async Task ExportData(Window? owner)
    {
        var path = await Dialogs.Current.SaveFile(owner, "Export PinList data", $"PinList-{DateTime.Now:yyyy-MM-dd}.json");
        if (path == null) return;

        try
        {
            Store.Export(path);
        }
        catch (Exception ex)
        {
            Store.Log($"Export: {ex}");
            await Dialogs.Info(owner, "Export failed", ex.Message);
        }
    }

    public async Task ImportData(Window? owner)
    {
        var path = await Dialogs.Current.OpenFile(owner, "Import PinList data");
        if (path == null) return;

        AppData imported;
        int fileVersion;
        try
        {
            imported = Store.ReadFile(path, out fileVersion);
        }
        catch (Exception ex)
        {
            Store.Log($"Import: {ex}");
            await Dialogs.Info(owner, "This file can't be imported",
                "It isn't a PinList export or data.json file, or it is damaged.\n\n" + ex.Message);
            return;
        }

        int lists = imported.Boards.Count;
        int items = imported.Boards.SelectMany(b => b.Categories).Sum(c => c.Items.Count);
        int archived = imported.Archive.Count;
        string message =
            $"{Plural(lists, "list")} with {Plural(items, "item")}, and {Plural(archived, "completed item")} in the archive.\n\n" +
            "Add them next to your current lists, or replace everything you have now? " +
            "Replacing keeps a copy of your current lists in the backups folder.";
        if (Store.HasSyncedBoards)
            message += "\n\nSynced lists aren't replaced.";
        if (fileVersion > AppData.CurrentVersion)
            message += "\n\nThe file is from a newer version of PinList, so some details may be lost.";

        int choice = await Dialogs.Current.Choose(owner, $"Import {Path.GetFileName(path)}?", message, 2,
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
                foreach (var w in _windows.ToList()) w.CloseForReload();
                Store.ReplaceAll(imported);
                Actions.ClearUndo();
                if (Store.Data.Boards.Count == 0)
                {
                    Store.Data.Boards.Add(Store.DefaultBoard(Displays.Primary.ToWorkArea()));
                    Store.SaveNow();
                }
                OpenAllBoards();
                _reminders.Check();
            }
        }
        catch (Exception ex)
        {
            Store.Log($"Import: {ex}");
            await Dialogs.Info(owner, "Import failed", ex.Message);
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
        _tray.SetMenu(TrayMenuEntries());
    }

    /// <summary>The tray menu's entries (shared by the Windows menu and the macOS menu bar menu).</summary>
    internal IReadOnlyList<TrayEntry> TrayMenuEntries() => UpdateEntries().Concat(new[]
    {
        new TrayEntry("All lists", ShowListsWindow),
        new TrayEntry("New list", NewBoardAtCursor),
        new TrayEntry("Archive", ShowArchive),
        new TrayEntry("Show all widgets", ShowAllBoards),
        TrayEntry.Separator,
        new TrayEntry("Settings", ShowSettings),
        new TrayEntry(OsText.Exit, ExitApp),
    }).ToList();

    /// <summary>Invisible window the tray menu belongs to (a menu needs a window; this one sits under the cursor).</summary>
    Window MenuHost()
    {
        if (_menuHost == null)
        {
            _menuHost = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Width = 1,
                Height = 1,
                CanResize = false,
                Position = new PixelPoint(-32000, -32000),
            };
            _menuHost.Opened += (_, _) => Native.MakeToolWindow(_menuHost);
            _menuHost.Show();
        }
        return _menuHost;
    }

    /// <summary>The tray menu that was opened last (tests look at it).</summary>
    internal ContextMenu? LastTrayMenu { get; private set; }

    /// <summary>The tray menu is an Avalonia ContextMenu, so it looks like every other menu in the app.</summary>
    internal ContextMenu ShowTrayMenu()
    {
        var host = MenuHost();
#if WINDOWS
        host.Position = Native.CursorScreenPosition();
#endif
        host.Activate();
        Native.BringToForeground(host);

        var menu = new ContextMenu { Placement = PlacementMode.TopEdgeAlignedLeft };
        if (_updates?.Available is { } update)
        {
            menu.Items.Add(CreateMenuItem($"Get PinList {update.Version}", OpenUpdatePage, icon: "Line.Download"));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(CreateMenuItem("All lists", ShowListsWindow, icon: "Line.Grid"));
        menu.Items.Add(CreateMenuItem("New list", NewBoardAtCursor, icon: "Line.Add"));
        menu.Items.Add(CreateMenuItem("Archive", ShowArchive, icon: "Line.Archive"));
        menu.Items.Add(CreateMenuItem("Show all widgets", ShowAllBoards, icon: "Line.RemoveFromDesktop"));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Settings", ShowSettings, icon: "Line.Settings"));
        menu.Items.Add(CreateMenuItem(OsText.Exit, ExitApp, icon: "Line.Exit"));
        menu.Open(host);
        LastTrayMenu = menu;
        return menu;
    }

    /// <summary>
    /// A menu entry; <paramref name="gesture"/> is shown on the right in muted text (e.g. "Del"), <paramref name="icon"/> is
    /// a Line.* resource key. A <paramref name="danger"/> entry (delete) is red.
    /// </summary>
    public static MenuItem CreateMenuItem(string header, Action action, string? gesture = null, string? icon = null, bool danger = false)
    {
        var mi = new MenuItem { Header = header };
        if (gesture != null)
        {
            var text = new TextBlock { Text = header, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var g = new TextBlock
            {
                Text = gesture,
                Margin = new Thickness(28, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            g.Bind(TextBlock.ForegroundProperty, g.GetResourceObservable("Text3"));
            var panel = new DockPanel();
            DockPanel.SetDock(g, Dock.Right);
            panel.Children.Add(g);
            panel.Children.Add(text);
            mi.Header = panel;
            Avalonia.Automation.AutomationProperties.SetName(mi, header);
            Avalonia.Automation.AutomationProperties.SetAcceleratorKey(mi, gesture);
        }
        if (icon != null) mi.Icon = MenuIcon(icon);
        if (danger) mi.Classes.Add("danger");
        // deferred so the menu closes before the action runs (e.g. before a TextBox takes focus)
        mi.Click += (_, _) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        return mi;
    }

    /// <summary>A line icon for a menu entry (<paramref name="key"/> is a Line.* resource).</summary>
    static LineIcon MenuIcon(string key) => new() { Data = (Geometry)Current!.FindResource(key)!, Width = 15, Height = 15 };

    /// <summary>
    /// A menu entry with a switch on the right (Transparent, Sync this list). Clicking it flips the switch and runs
    /// <paramref name="toggle"/>. <c>IsChecked</c> tells the state, and screen readers hear a checkable entry.
    /// </summary>
    public static MenuItem CreateSwitch(string header, bool isOn, Action toggle, string? icon = null)
    {
        var knob = new Avalonia.Controls.Shapes.Ellipse { Width = 12, Height = 12 };
        var track = new Border { Width = 30, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = knob };
        track.Classes.Add("switchtrack");
        track.Margin = new Thickness(28, 0, 0, 0);
        DockPanel.SetDock(track, Dock.Right);
        var text = new TextBlock { Text = header, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var mi = new MenuItem
        {
            Header = new DockPanel { Children = { track, text } },
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isOn,
        };
        mi.Classes.Add("switch");
        Avalonia.Automation.AutomationProperties.SetName(mi, header);
        track.Classes.Set("on", isOn);
        mi.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.IsCheckedProperty) track.Classes.Set("on", mi.IsChecked);
        };
        if (icon != null) mi.Icon = MenuIcon(icon);
        mi.Click += (_, _) => Dispatcher.UIThread.Post(toggle, DispatcherPriority.Background);
        return mi;
    }

#if WINDOWS
    /// <summary>Address of the icon.</summary>
    internal static string TrayIconUri { get; set; } = "avares://PinList/Assets/app.ico";

    internal static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(TrayIconUri));
            return new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Store.Log("Tray icon: " + ex.Message);
            return System.Drawing.SystemIcons.Application;
        }
    }
#endif
}
