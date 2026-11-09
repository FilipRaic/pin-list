using System;

namespace PinList;

/// <summary>
/// The feedback form on PinList's website. PinList itself sends nothing: Settings opens the page in the browser,
/// with the app version and the system filled in, and the user decides what is sent.
/// </summary>
public static class Feedback
{
    public const string Page = "https://filipraic.github.io/pin-list/feedback";

    /// <summary>The form's address with the version (like 2.8.0) and the system (like "Android") in the query.</summary>
    public static string Url(Version version, string system) =>
        $"{Page}?v={Uri.EscapeDataString(version.ToString(3))}&os={Uri.EscapeDataString(system.Trim())}";

    /// <summary>
    /// "Windows 11 26H2 (build 26300.9711)". Windows 11 still says it's version 10.0, only its build (22000 and up)
    /// tells it apart from Windows 10. The release (26H2) and the update number (9711) come from the registry.
    /// </summary>
    public static string WindowsName(Version version, string? release, int? update)
    {
        var name = version.Major == 10
            ? (version.Build >= 22000 ? "Windows 11" : "Windows 10")
            : $"Windows {version.Major}.{version.Minor}";
        if (!string.IsNullOrWhiteSpace(release)) name += " " + release.Trim();
        return $"{name} (build {version.Build}{(update is { } u ? "." + u : "")})";
    }

    /// <summary>"macOS 15.1" (the patch number only when there is one: "macOS 15.1.2").</summary>
    public static string MacName(Version version) => "macOS " + version.ToString(version.Build > 0 ? 3 : 2);

    /// <summary>"Android 15 (Xiaomi 23078PND5G)", the phone's name only when it's known.</summary>
    public static string AndroidName(string? release, string? phone)
    {
        var name = string.IsNullOrWhiteSpace(release) ? "Android" : "Android " + release.Trim();
        return string.IsNullOrWhiteSpace(phone) ? name : $"{name} ({phone.Trim()})";
    }
}
