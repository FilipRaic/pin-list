using System;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace PinList;

#if WINDOWS
/// <summary>Windows settings PinList follows, read from the registry (there's no WPF API for them).</summary>
public static class WindowsSettings
{
    const string Accessibility = @"HKEY_CURRENT_USER\Software\Microsoft\Accessibility";
    const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string CurrentVersion = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>Reads a registry value; null if it (or its key) doesn't exist or can't be read.</summary>
    internal static object? Read(string keyName, string valueName)
    {
        try { return Registry.GetValue(keyName, valueName, null); }
        catch (Exception ex)
        {
            Store.Log($"Reading {keyName}\\{valueName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Settings → Accessibility → Text size, as a factor from 1.0 to 2.25.</summary>
    public static double TextScale() => ParseTextScale(Read(Accessibility, "TextScaleFactor"));

    internal static double ParseTextScale(object? value) =>
        value is int percent && percent is >= 100 and <= 225 ? percent / 100.0 : 1.0;

    /// <summary>Settings → Personalization → Colors → "Choose your app mode" is Dark.</summary>
    public static bool AppsUseDark() => ParseAppsUseDark(Read(Personalize, "AppsUseLightTheme"));

    /// <summary>"Windows 11 26H2 (build 26300.9711)", for the feedback form.</summary>
    public static string SystemName() =>
        Feedback.WindowsName(Environment.OSVersion.Version, Read(CurrentVersion, "DisplayVersion") as string,
            Read(CurrentVersion, "UBR") as int?);

    /// <summary>Dark unless the value says light (dark is also the default when the value is missing).</summary>
    internal static bool ParseAppsUseDark(object? value) => value is not int light || light == 0;
}
#endif
