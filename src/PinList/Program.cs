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

    // ShowInDock: on a Mac, Avalonia makes the app a normal Dock app at start, which overrides LSUIElement in
    // Info.plist. PinList is a menu bar app, so no Dock icon and no Cmd+Tab entry. Other systems ignore it.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
