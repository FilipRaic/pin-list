using System;
using System.Diagnostics.CodeAnalysis;

namespace PinList;

/// <summary>
/// Words that differ between Windows and macOS. Each test run covers only one of the two systems, so these are
/// left out of the coverage numbers.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Each test run sees only one operating system.")]
static class OsText
{
    /// <summary>"Exit" on Windows, "Quit PinList" on a Mac.</summary>
    public static string Exit => OperatingSystem.IsMacOS() ? "Quit PinList" : "Exit";

    /// <summary>The copy shortcut shown in menus.</summary>
    public static string CopyGesture => OperatingSystem.IsMacOS() ? "Cmd+C" : "Ctrl+C";

    /// <summary>The menu entry that starts PinList with the computer.</summary>
    public static string Autostart => OperatingSystem.IsWindows() ? "Start with Windows" : "Open at login";

    /// <summary>Where the app's menu is: the tray icon's on Windows, the menu bar icon's on a Mac.</summary>
    public static string AppMenu => OperatingSystem.IsWindows() ? "tray menu" : "menu bar menu";
}
