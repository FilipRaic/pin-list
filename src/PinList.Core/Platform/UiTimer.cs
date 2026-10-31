using System;

namespace PinList;

/// <summary>A timer that ticks on the UI thread (WPF, Avalonia, ... each app brings its own).</summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event Action? Tick;
    void Start();
    void Stop();
}

/// <summary>Creates timers that tick on the UI thread. Each app sets Factory at startup.</summary>
public static class UiTimers
{
    public static Func<IUiTimer> Factory { get; set; } = () =>
        throw new InvalidOperationException("UiTimers.Factory isn't set. The app sets it at startup.");

    public static IUiTimer Create() => Factory();
}
