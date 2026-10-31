using System;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace PinList;

#if WINDOWS
/// <summary>Start with Windows via HKCU\...\Run (no admin rights needed).</summary>
static class Autostart
{
    /// <summary>Registry key under HKCU. Tests use their own key, so the real "Start with Windows" list isn't touched.</summary>
    internal static string RunKey { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "PinList";

    static string ExePath => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, ExePath);
            else key.DeleteValue(ValueName, false);
        }
        catch (Exception ex) { Store.Log("Autostart: " + ex.Message); }
    }

    /// <summary>If the exe was moved, update the path in the registry.</summary>
    public static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string current &&
                !string.Equals(current, ExePath, StringComparison.OrdinalIgnoreCase))
                Set(true);
        }
        catch { }
    }
}
#else
/// <summary>
/// Open at login on macOS: a LaunchAgent in ~/Library/LaunchAgents that starts the app bundle when the user
/// signs in (no admin rights needed).
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "macOS only; checked on a Mac.")]
static class Autostart
{
    /// <summary>The LaunchAgent file. Tests use their own.</summary>
    internal static string RunKey { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.pinlist.app.plist");

    /// <summary>The .app bundle around the running executable (…/PinList.app/Contents/MacOS/PinList).</summary>
    static string AppPath
    {
        get
        {
            var exe = Environment.ProcessPath ?? "";
            int i = exe.IndexOf(".app/", StringComparison.Ordinal);
            return i < 0 ? exe : exe[..(i + 4)];
        }
    }

    static string Plist(string app) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key><string>com.pinlist.app</string>
            <key>ProgramArguments</key><array><string>/usr/bin/open</string><string>{System.Security.SecurityElement.Escape(app)}</string></array>
            <key>RunAtLoad</key><true/>
        </dict>
        </plist>
        """;

    public static bool IsEnabled() => System.IO.File.Exists(RunKey);

    public static void Set(bool enabled)
    {
        try
        {
            if (enabled)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RunKey)!);
                System.IO.File.WriteAllText(RunKey, Plist(AppPath));
            }
            else System.IO.File.Delete(RunKey);
        }
        catch (Exception ex) { Store.Log("Autostart: " + ex.Message); }
    }

    /// <summary>If the app was moved, update the path in the LaunchAgent.</summary>
    public static void RefreshPath()
    {
        try
        {
            if (IsEnabled() && !System.IO.File.ReadAllText(RunKey).Contains(System.Security.SecurityElement.Escape(AppPath)!))
                Set(true);
        }
        catch { }
    }
}
#endif
