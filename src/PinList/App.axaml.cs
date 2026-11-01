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
    ArchiveWindow? _archive;
    readonly List<BoardWindow> _windows = new();
    readonly Reminders _reminders = new();
    IReadOnlyList<TodoItem> _lastReminders = Array.Empty<TodoItem>();
    Updates? _updates;
    bool _lastNotificationIsUpdate;
    static bool _errorLoggingHooked;

    internal IReadOnlyList<BoardWindow> OpenWindows => _windows;
    internal Updates? UpdateCheck => _updates;
    internal ArchiveWindow? Archive => _archive;
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
            service.IsBusy = o => BoardWindow.IsDragging || SyncService.IsEditing(o); // remote changes wait (spec 4.6)
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

    /// <summary>A list deleted on another device: its window closes (one the user deleted here closed itself).</summary>
    void OnBoardDeleted(Board board, bool byUser)
    {
        if (!byUser) _windows.FirstOrDefault(w => w.Board == board)?.CloseForReload();
    }

    /// <summary>Signs in, with the "waiting for your browser" dialog. True when signed in.</summary>
    internal Task<bool> SignIn(Window? owner) => Dialogs.Current.WaitForSignIn(owner, SyncService.Current!.SignIn);

    /// <summary>"Sync this window": turning it on signs in first when needed (spec 4.3).</summary>
    internal async Task SetBoardSynced(Board board, bool on, Window owner)
    {
        var sync = SyncService.Current!;
        if (on && sync.Account == null && !await SignIn(owner)) return;
        await sync.SetSynced(board, on);
    }

    async Task SignOutAndDeleteCloudData(Window? owner)
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

    /// <summary>The tray menu's "Google sync" entries (spec 4.2).</summary>
    internal IReadOnlyList<TrayEntry> SyncEntries()
    {
        if (SyncService.Current is not { } sync) return Array.Empty<TrayEntry>(); // the app is stopping
        if (!sync.IsAvailable) return new[] { new TrayEntry(sync.UnavailableText, null, false) };
        if (sync.Account is not { } account) return new[] { new TrayEntry("Sign in with Google", () => _ = SignIn(null)) };
        return new[]
        {
            new TrayEntry($"Signed in as {account.Email}", null, false),
            new TrayEntry($"Last synced: {SyncIcon.Ago(sync.LastSyncUtc, DateTime.UtcNow)}", null, false),
            new TrayEntry("Sync now", () => _ = sync.SyncNow()),
            new TrayEntry("Sign out", () => _ = sync.SignOut(false)),
            new TrayEntry("Sign out and delete cloud data", () => _ = SignOutAndDeleteCloudData(null)),
        };
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
        var menu = OperatingSystem.IsWindows() ? "tray menu" : "menu bar menu";
        int choice = await Dialogs.Current.Choose(null, "Check for updates?",
            "PinList can look for a new version about once a day and tell you when there is one. " +
            "It only asks GitHub for the number of the latest version. Nothing about you or your lists is sent.\n\n" +
            $"You can change this later with Check for updates in the {menu}.", 1,
            ("No", DialogButtonKind.Normal), ("Yes", DialogButtonKind.Accent));
        if (choice >= 0) updates.Enabled = choice == 1; // closed without answering: asked again on the next start
    }

    void ToggleUpdates() => _updates!.Enabled = _updates.Enabled != true;

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
        _archive?.RefreshAppearance();
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

    /// <summary>"App theme" submenu, used in the tray menu and in each widget's ⋯ menu.</summary>
    public MenuItem ThemeMenu()
    {
        var menu = new MenuItem { Header = "App theme" };
        foreach (var (key, name) in Themes.AppThemes)
        {
            var mi = CreateMenuItem(name, () => SetTheme(key));
            mi.ToggleType = MenuItemToggleType.Radio;
            mi.IsChecked = Store.Data.Settings.Theme == key;
            menu.Items.Add(mi);
        }
        return menu;
    }

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

    internal void OpenAllBoards()
    {
        foreach (var board in Store.Data.Boards.ToList())
            OpenBoard(board);
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
        if (last == null) return;
        last.Activate();
        // keyboard users can start working right away (Tab moves to the other parts of the widget)
        if (last.FocusManager?.GetFocusedElement() is not Visual v || !last.IsVisualAncestorOf(v)) last.FocusContent();
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

    /// <summary>Any open PinList window (dialogs and file pickers need one to belong to).</summary>
    internal Window AnyWindow() =>
        (Window?)_windows.FirstOrDefault(w => w.IsVisible) ?? _archive ?? MenuHost();

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

        int windows = imported.Boards.Count;
        int items = imported.Boards.SelectMany(b => b.Categories).Sum(c => c.Items.Count);
        int archived = imported.Archive.Count;
        string message =
            $"{Plural(windows, "window")} with {Plural(items, "item")}, and {Plural(archived, "completed item")} in the archive.\n\n" +
            "Add them next to your current windows, or replace everything you have now? " +
            "Replacing keeps a copy of your current lists in the backups folder.";
        if (Store.HasSyncedBoards)
            message += "\n\nSynced windows aren't replaced.";
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
                _archive?.Close();
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
        new TrayEntry("New window", NewBoardAtCursor),
        new TrayEntry("Archive", ShowArchive),
        new TrayEntry("Show all windows", ShowAllBoards),
        TrayEntry.Separator,
        new TrayEntry("Export", () => _ = ExportData(null)),
        new TrayEntry("Import", () => _ = ImportData(null)),
        TrayEntry.Separator,
    }).Concat(SyncEntries()).Concat(new[]
    {
        TrayEntry.Separator,
        new TrayEntry("Check for updates", ToggleUpdates, Checked: _updates?.Enabled == true),
        new TrayEntry("Open data folder", () => OpenFolder(Store.Dir)),
        TrayEntry.Separator,
        new TrayEntry("Exit", ExitApp),
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
            menu.Items.Add(CreateMenuItem($"Get PinList {update.Version}", OpenUpdatePage));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(CreateMenuItem("New window", NewBoardAtCursor));
        menu.Items.Add(CreateMenuItem("Archive", ShowArchive));
        menu.Items.Add(CreateMenuItem("Show all windows", ShowAllBoards));
        menu.Items.Add(new Separator());
        menu.Items.Add(ThemeMenu());
        menu.Items.Add(CreateMenuItem("Export", () => _ = ExportData(null)));
        menu.Items.Add(CreateMenuItem("Import", () => _ = ImportData(null)));
        var sync = new MenuItem { Header = "Google sync" };
        foreach (var e in SyncEntries())
        {
            var mi = CreateMenuItem(e.Text, e.Action ?? (() => { }));
            mi.IsEnabled = e.Enabled;
            sync.Items.Add(mi);
        }
        menu.Items.Add(sync);
        menu.Items.Add(new Separator());
        var auto = CreateMenuItem(OperatingSystem.IsWindows() ? "Start with Windows" : "Open at login", () => Autostart.Set(!Autostart.IsEnabled()));
        auto.ToggleType = MenuItemToggleType.CheckBox;
        auto.IsChecked = Autostart.IsEnabled();
        auto.IsEnabled = ManageAutostart;
        menu.Items.Add(auto);
        var updates = CreateMenuItem("Check for updates", ToggleUpdates);
        updates.ToggleType = MenuItemToggleType.CheckBox;
        updates.IsChecked = _updates?.Enabled == true;
        menu.Items.Add(updates);
        menu.Items.Add(CreateMenuItem("Open data folder", () => OpenFolder(Store.Dir)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", ExitApp));
        menu.Open(host);
        LastTrayMenu = menu;
        return menu;
    }

    /// <summary>A menu entry; <paramref name="gesture"/> is shown on the right in muted text (e.g. "Del").</summary>
    public static MenuItem CreateMenuItem(string header, Action action, string? gesture = null)
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
            g.Bind(TextBlock.ForegroundProperty, g.GetResourceObservable("MenuTextMuted"));
            var panel = new DockPanel();
            DockPanel.SetDock(g, Dock.Right);
            panel.Children.Add(g);
            panel.Children.Add(text);
            mi.Header = panel;
            Avalonia.Automation.AutomationProperties.SetName(mi, header);
            Avalonia.Automation.AutomationProperties.SetAcceleratorKey(mi, gesture);
        }
        // deferred so the menu closes before the action runs (e.g. before a TextBox takes focus)
        mi.Click += (_, _) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
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
