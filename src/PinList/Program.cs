using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;

namespace PinList;

[ExcludeFromCodeCoverage(Justification = "Starts the real app; tests start it on the headless platform.")]
static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
