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
}
