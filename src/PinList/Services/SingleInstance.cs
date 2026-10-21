using System;
using System.Threading;

namespace PinList;

/// <summary>
/// Makes sure only one PinList runs. A second start signals the first one (which then brings its widgets
/// to the front) and quits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _signalName;
    EventWaitHandle? _signal;
    RegisteredWaitHandle? _wait;

    /// <summary>True if no other PinList with this name was running.</summary>
    public bool IsFirst { get; }

    /// <param name="name">Unique per data folder, so a development copy can run next to the normal one.</param>
    public SingleInstance(string name)
    {
        _mutex = new Mutex(true, $@"Local\{name}.SingleInstance", out bool createdNew);
        IsFirst = createdNew;
        _signalName = $@"Local\{name}.ShowWidgets";
    }

    /// <summary>Calls <paramref name="onShow"/> (on a background thread) whenever another start signals this one.</summary>
    public void ListenForShow(Action onShow)
    {
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, _signalName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Tells the running instance to show its widgets. Returns false if none is listening.</summary>
    public bool SignalFirst()
    {
        if (!EventWaitHandle.TryOpenExisting(_signalName, out var signal)) return false;
        using (signal)
        {
            // Windows only lets a process the user just started take the foreground; pass that right on
            Native.AllowAnyForeground();
            signal.Set();
        }
        return true;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _signal?.Dispose();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // released by another thread than the one that took it; disposing frees it anyway
        }
        _mutex.Dispose();
    }
}
