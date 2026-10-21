using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PinList.Tests;

/// <summary>
/// Runs the real PinList app for UI tests: on its own STA thread, on a hidden Windows desktop (so no window
/// ever appears on the screen or takes focus from you), with its own data folder, a fake tray icon and
/// scripted dialogs. Tests run code on the app's thread with <see cref="Run"/>.
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

    static Dispatcher? _dispatcher;
    static readonly object Gate = new();

    /// <summary>
    /// Runs before any test: App reads PINLIST_DATA_DIR once, when the class is first used,
    /// so the variable must be set before that.
    /// </summary>
    [ModuleInitializer]
    internal static void SetDataFolder() => Environment.SetEnvironmentVariable("PINLIST_DATA_DIR", DataDir);

    public static Dispatcher Dispatcher
    {
        get
        {
            lock (Gate) return _dispatcher ??= StartApp();
        }
    }

    public static App App => Run(() => PinList.App.Instance);

    public static void Run(Action action) => Dispatcher.Invoke(action);

    public static T Run<T>(Func<T> func) => Dispatcher.Invoke(func);

    static Dispatcher StartApp()
    {
        Directory.CreateDirectory(DataDir);
        Dispatcher? dispatcher = null;
        Exception? error = null;
        using var ready = new ManualResetEventSlim();

        // A thread that owns a window can't move to another desktop, and an STA thread gets a hidden COM window
        // as soon as .NET starts it. So the thread is created by Windows directly (.NET then doesn't set up COM),
        // moves to the hidden desktop, and only then becomes STA.
        HiddenDesktop.StartThread(() =>
        {
            try
            {
                HiddenDesktop.Enter();
                HiddenDesktop.BecomeSta();
                PinList.App.CreateTrayIcon = () => Tray;
                PinList.Dialogs.Current = Dialogs;
                PinList.App.OpenFolder = path => OpenedFolders.Add(path);
                PinList.App.ShutdownApp = _ => ShutdownRequests++;
                PinList.App.ManageAutostart = true;
                Autostart.RunKey = TestRunKey;

                // pack://application:,,,/… URIs (like the window icon) must resolve in PinList, not in the test runner
                typeof(System.Windows.Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .SetValue(null, typeof(App).Assembly); // WPF already set it to the test runner; the property refuses a second value
                var app = new App();
                app.InitializeComponent();
                app.DispatcherUnhandledException += (_, e) => UnhandledErrors.Enqueue(e.Exception);
                // Startup runs before App.Start (base.OnStartup comes first), so signal once the queue gets back to us
                app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    dispatcher = app.Dispatcher;
                    ready.Set();
                });
                app.Run();
            }
            catch (Exception ex)
            {
                error = ex;
                ready.Set();
            }
        });

        if (!ready.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("The app didn't start.");
        if (error != null) throw new Exception("The app didn't start.", error);
        return dispatcher!;
    }

    /// <summary>Lets the app process everything that's queued (layout, deferred menu actions, …). Call on the app thread.</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        // SystemIdle is the lowest priority, so everything else queued (including the app's ApplicationIdle menu actions) runs first
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Keeps the app running for a while (for timers). Call on the app thread.</summary>
    public static void Wait(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Pump();
    }

    /// <summary>Waits (pumping) until the condition is true. Call on the app thread.</summary>
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

/// <summary>A Windows desktop nobody looks at. Windows created by a thread on it never show on the real screen.</summary>
static class HiddenDesktop
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devMode, int flags, uint access, IntPtr attributes);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr desktop);

    const uint GenericAll = 0x10000000;

    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr reserved);


    /// <summary>Turns the calling thread into a single-threaded apartment (what WPF needs), after moving desktops.</summary>
    public static void BecomeSta()
    {
        int hr = OleInitialize(IntPtr.Zero);
        if (hr < 0) throw new InvalidOperationException($"OleInitialize failed: 0x{hr:X8}");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Thread is " + Thread.CurrentThread.GetApartmentState() + " after OleInitialize");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate uint ThreadStart(IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateThread(IntPtr attributes, UIntPtr stackSize, ThreadStart start, IntPtr parameter, uint flags, out uint id);

    static ThreadStart? _keepAlive; // the native thread calls this delegate; it must not be garbage collected

    /// <summary>Starts a thread created by Windows (not by .NET, which would set up COM on it immediately).</summary>
    public static void StartThread(Action body)
    {
        _keepAlive = _ => { body(); return 0; };
        if (CreateThread(IntPtr.Zero, UIntPtr.Zero, _keepAlive, IntPtr.Zero, 0, out _) == IntPtr.Zero)
            throw new InvalidOperationException("CreateThread failed: " + Marshal.GetLastWin32Error());
    }

    /// <summary>Moves the calling thread to a new hidden desktop. Must happen before the thread creates any window.</summary>
    public static void Enter()
    {
        var desktop = CreateDesktop("PinListTests-" + Environment.ProcessId, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        if (!SetThreadDesktop(desktop)) throw new InvalidOperationException("SetThreadDesktop failed: " + Marshal.GetLastWin32Error());
    }
}
