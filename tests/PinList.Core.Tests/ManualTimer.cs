using PinList;

namespace PinList.Tests;

/// <summary>A timer that ticks only when a test calls Fire.</summary>
sealed class ManualTimer : IUiTimer
{
    /// <summary>Every timer created since the test started, oldest first.</summary>
    public static readonly List<ManualTimer> Created = new();

    public ManualTimer() => Created.Add(this);

    public TimeSpan Interval { get; set; }
    public bool IsEnabled { get; private set; }
    public event Action? Tick;
    public void Start() => IsEnabled = true;
    public void Stop() => IsEnabled = false;

    /// <summary>The interval has passed.</summary>
    public void Fire() => Tick?.Invoke();
}
