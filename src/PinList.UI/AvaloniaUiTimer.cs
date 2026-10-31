using System;
using Avalonia.Threading;

namespace PinList;

/// <summary>PinList.Core's timers on Avalonia's UI thread.</summary>
public sealed class AvaloniaUiTimer : IUiTimer
{
    readonly DispatcherTimer _timer = new();

    public AvaloniaUiTimer() => _timer.Tick += (_, _) => Tick?.Invoke();

    public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
    public bool IsEnabled => _timer.IsEnabled;
    public event Action? Tick;
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
