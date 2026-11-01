using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

[assembly: AvaloniaTestApplication(typeof(PinList.Tests.UiHost))]

// Store, Actions and the app are static, so tests must not run at the same time.
// With xUnit v3 4.x this becomes [assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)].
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PinList.Tests;

/// <summary>
/// Runs the real PinList app for UI tests on Avalonia's headless platform (no window ever shows on the screen
/// or takes focus from you), with its own data folder, a fake tray icon and scripted dialogs.
/// Tests run on the app's UI thread (the [AvaloniaFact] attribute does that).
/// </summary>
public static class UiHost
{
    public static readonly string DataDir = Path.Combine(Path.GetTempPath(), "PinListTests", "app-" + Environment.ProcessId);
    public const string TestRunKey = @"Software\PinListTests\Run";

    public static readonly FakeTray Tray = new();
    public static readonly FakeDialogs Dialogs = new();
    public static readonly List<string> OpenedFolders = new();
    public static int ShutdownRequests;

    /// <summary>Errors that escaped to the dispatcher (they'd otherwise only be logged). UI tests fail on them.</summary>
    public static readonly ConcurrentQueue<Exception> UnhandledErrors = new();

    static bool _hooked;
    static App? _app;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <summary>
    /// Runs before any test: App reads PINLIST_DATA_DIR once, when the class is first used,
    /// so the variable must be set before that.
    /// </summary>
    [ModuleInitializer]
    internal static void SetDataFolder() => Environment.SetEnvironmentVariable("PINLIST_DATA_DIR", DataDir);

    /// <summary>
    /// Starts the test's app like Program.Main would. The test framework gives every test a new App instance,
    /// so the previous one is stopped first (it lets go of the single-instance lock and of static events).
    /// </summary>
    public static void EnsureStarted()
    {
        if (_app == App.Instance) return;
        _app?.Stop();
        _app = App.Instance;
        Tray.Disposed = false;
        Directory.CreateDirectory(DataDir);
        App.CreateTrayIcon = () => Tray;
        PinList.Dialogs.Current = Dialogs;
        App.OpenFolder = path => OpenedFolders.Add(path);
        App.ShutdownApp = _ => ShutdownRequests++;
        App.CreateSyncService = UnavailableSync;
        App.ManageAutostart = true;
        Autostart.RunKey = TestRunKey;
        if (!_hooked)
        {
            _hooked = true;
            Dispatcher.UIThread.UnhandledException += (_, e) => { UnhandledErrors.Enqueue(e.Exception); e.Handled = true; };
        }
        if (!App.Instance.Start()) throw new InvalidOperationException("The app didn't start.");
        Pump();
    }

    /// <summary>Sync as in a build without a Google client (whether or not a developer has one locally).</summary>
    public static PinList.Sync.SyncService UnavailableSync() =>
        new(new DesktopGoogleAuth(null, new MemoryTokenStore(), new HttpClient(), _ => Task.CompletedTask),
            () => throw new InvalidOperationException("No cloud in tests."), "Test PC");

    // The headless platform has no OS timer that wakes the dispatcher, so due DispatcherTimers are started by hand
    static readonly System.Reflection.MethodInfo PromoteTimers =
        typeof(Dispatcher).GetMethod("PromoteTimers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    /// <summary>Lets the app process everything that's queued (layout, rendering, deferred menu actions, due timers, …).</summary>
    public static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            PromoteTimers.Invoke(Dispatcher.UIThread, null);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Keeps the app running for a while (for timers).</summary>
    public static void Wait(TimeSpan time)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < time)
        {
            Pump();
            Thread.Sleep(5);
        }
        Pump();
    }

    /// <summary>Waits (pumping) until the condition is true.</summary>
    public static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var end = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > end) return false;
            Wait(TimeSpan.FromMilliseconds(20));
        }
        return true;
    }
}
