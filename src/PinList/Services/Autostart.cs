using System;
using Microsoft.Win32;

namespace PinList;

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
