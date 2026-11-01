using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PinList.Sync;

namespace PinList;

/// <summary>
/// The phone app (Android). The platform project sets <see cref="DataDir"/> and <see cref="ShowNotification"/>,
/// then starts it.
/// </summary>
public class PhoneApp : Application
{
    /// <summary>Where data.json lives (the app's private storage). Set by the platform before start.</summary>
    public static string? DataDir { get; set; }

    /// <summary>Shows a system notification for due reminders (title, text). Set by the platform.</summary>
    public static Action<string, string>? ShowNotification { get; set; }

    /// <summary>Called after every save (the platform schedules the reminder alarms then).</summary>
    public static Action? Saved { get; set; }

    /// <summary>Google sign-in on this phone; null when the build has no sync (nothing about sync shows then).</summary>
    public static IGoogleAuth? Auth { get; set; }

    /// <summary>The cloud folder; null uses Google Drive (tests give a fake one).</summary>
    public static Func<ICloudStore>? Cloud { get; set; }

    /// <summary>The phone's name in sync messages and files (manufacturer and model).</summary>
    public static string DeviceName { get; set; } = "Android phone";

    /// <summary>
    /// Sign-in or a list's sync switch changed (Android plans or cancels its background sync then).
    /// </summary>
    public static Action? SyncSettingsChanged { get; set; }

    /// <summary>
    /// Runs work on the app's UI thread, with the data the screen shows. Null until the screen exists; the Android
    /// background job uses it to sync the live data instead of loading data.json again (spec 9.9).
    /// </summary>
    public static Func<Func<System.Threading.Tasks.Task<bool>>, System.Threading.Tasks.Task<bool>>? OnUiThread { get; set; }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>This app's version (set by the platform). Without it there is no update check.</summary>
    public static Version? AppVersion { get; set; }

    /// <summary>Creates the update check when the app starts (tests give one without GitHub).</summary>
    public static Func<Updates?> CreateUpdates { get; set; } = () => AppVersion is { } v ? Updates.ForGitHub(Http, v) : null;

    Updates? _updates;

    public PhoneView? View { get; private set; }
    public Reminders? ReminderService { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://PinList.UI/")) { Source = new Uri("avares://PinList.UI/Styles/Common.axaml") });
    }

    [ExcludeFromCodeCoverage(Justification = "Only runs inside the Android app; tests call Start themselves.")]
    public override void OnFrameworkInitializationCompleted()
    {
        var view = Start();
        if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = view;
        else if (ApplicationLifetime is IActivityApplicationLifetime activity) activity.MainViewFactory = MainView;
        base.OnFrameworkInitializationCompleted();
    }

    ContentControl? _screen;

    /// <summary>
    /// The view for a new Android screen. Opening PinList again after Back makes a new screen while the old one may
    /// still hold the view, so the view moves: the old screen's holder lets go of it first (same data, same state).
    /// </summary>
    internal Control MainView()
    {
        var view = View ?? Start();
        if (_screen != null) _screen.Content = null;
        return _screen = new ContentControl { Content = view };
    }

    /// <summary>Loads the data and makes the phone screen.</summary>
    public PhoneView Start()
    {
        UiTimers.Factory = () => new AvaloniaUiTimer();
        Themes.SystemThemeName = "Use phone setting";
        Themes.SystemDarkSource = () => PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        Store.Saver = new DebouncedSaveScheduler(() => { Store.SaveNow(); Saved?.Invoke(); });
        if (DataDir != null) Store.UseDirectory(DataDir);

        View = new PhoneView();
        Store.Notify = (message, _) => View.ShowMessageForStore(message);
        PhoneView.SetTheme = SetTheme;

        if (!Store.Load() || Store.Data.Boards.Count == 0)
        {
            // First start: one empty list to begin with
            Store.Data.Boards.Add(Store.DefaultBoard(new WorkArea(0, 0, 1280, 800), Themes.SystemUsesDark() ? "Dark" : "Light"));
            Store.SaveNow();
        }
        ApplyTheme();
        View.Reload();

        StartSync();
        StartUpdates();
        OnUiThread = work => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(work);

        ReminderService?.Stop();
        ReminderService = new Reminders();
        ReminderService.Due += due =>
        {
            var (title, text) = Reminders.Describe(due);
            ShowNotification?.Invoke(title, text);
        };
        ReminderService.Start();
        if (PlatformSettings != null) PlatformSettings.ColorValuesChanged += (_, _) => ApplyTheme();
        return View;
    }

    /// <summary>Starts sync when the platform gave a sign-in (one service at a time, also after a restart of the screen).</summary>
    void StartSync()
    {
        SyncService.Current?.Stop();
        SyncService.Current = null;
        if (Auth is not { } auth) return;
        var sync = new SyncService(auth, Cloud ?? (() => new DriveCloudStore(Http, auth.AccessToken)), DeviceName);
        sync.Changed += () => SyncSettingsChanged?.Invoke();
        SyncService.Current = sync;
        View!.Sync = sync;
        sync.Start();
        _ = sync.SyncNow(); // on start (spec 9.9)
    }

    /// <summary>Starts the update check (one at a time, also after a restart of the screen).</summary>
    void StartUpdates()
    {
        _updates?.Stop();
        _updates = CreateUpdates();
        View!.UpdateCheck = _updates;
        _updates?.Start();
    }

    internal void SetTheme(string key)
    {
        Store.Data.Settings.Theme = key;
        Store.MarkDirty();
        ApplyTheme();
    }

    internal void ApplyTheme()
    {
        Themes.ApplyBoard(Resources, Themes.Get("Dark"), false); // defaults; the board screen sets its own
        bool dark = Themes.IsDark(Store.Data.Settings.Theme);
        Themes.ApplyChrome(Resources, dark);
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        View!.App_ThemeOrDataChanged(); // the view is made before the theme is first applied
    }
}
