using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PinList;

/// <summary>A widget color option (the key is what gets stored in data.json).</summary>
public record BoardStyle(string Key, string Name, string Background, bool IsDark);

/// <summary>
/// All colors used by the app. Each widget gets its own brushes in its Window.Resources
/// (so every window can have a different color); menus, tooltips, dialogs and the archive
/// window use the app theme (light or dark) from the application resources.
/// </summary>
public static class Themes
{
    public static readonly BoardStyle[] BoardStyles =
    {
        new("Dark",   "Dark",   "#202020", true),
        new("Light",  "Light",  "#F9F9F9", false),
        new("Yellow", "Yellow", "#FFF4C4", false),
        new("Green",  "Green",  "#DCF2CE", false),
        new("Blue",   "Blue",   "#D6EAFA", false),
        new("Purple", "Purple", "#E6DDF8", false),
        new("Pink",   "Pink",   "#FBDCE7", false),
        new("Peach",  "Peach",  "#FDE3CF", false),
        new("Gray",   "Gray",   "#E4E4E4", false),
    };

    /// <summary>App theme choices (key stored in settings, menu text).</summary>
    public static readonly (string Key, string Name)[] AppThemes =
    {
        ("System", "Use Windows setting"),
        ("Light", "Light"),
        ("Dark", "Dark"),
    };

    public static BoardStyle Get(string? key) =>
        BoardStyles.FirstOrDefault(s => s.Key == key) ?? BoardStyles[0];

    /// <summary>True if Windows is set to dark mode for apps (Settings → Personalization → Colors).</summary>
    public static bool SystemUsesDark() => SystemDarkSource();

    /// <summary>Where the Windows light/dark setting comes from. Tests replace it.</summary>
    internal static Func<bool> SystemDarkSource { get; set; } = WindowsSettings.AppsUseDark;

    /// <summary>Resolves the "System" / "Light" / "Dark" setting to dark or not.</summary>
    public static bool IsDark(string? setting) => setting switch
    {
        "Light" => false,
        "Dark" => true,
        _ => SystemUsesDark(),
    };

    const byte TransparentAlpha = 0xE6; // ~90 % opacity

    /// <summary>
    /// True when a Windows contrast theme is on (Settings → Accessibility → Contrast themes).
    /// Then all colors come from that theme, and widget colors and transparency are ignored.
    /// </summary>
    public static bool HighContrast => ForceHighContrast ?? SystemParameters.HighContrast;

    /// <summary>Tests switch contrast themes on and off with this instead of changing Windows settings.</summary>
    internal static bool? ForceHighContrast { get; set; }

    /// <summary>
    /// Brushes for a single widget. Small text (TextMuted, TextSecondary) has at least 4.5:1 contrast
    /// on every widget color (WCAG AA), measured on the solid background.
    /// </summary>
    public static void ApplyBoard(ResourceDictionary r, BoardStyle style, bool transparent)
    {
        if (HighContrast)
        {
            ApplyBoardHighContrast(r);
            return;
        }

        var bg = Parse(style.Background);
        bg.A = transparent ? TransparentAlpha : (byte)0xFF;
        Set(r, "BoardBg", bg);

        bool d = style.IsDark;
        Set(r, "BoardBorder",   d ? "#26FFFFFF" : "#1F000000");
        Set(r, "TextPrimary",   d ? "#F2FFFFFF" : "#E6000000");
        Set(r, "TextSecondary", d ? "#B3FFFFFF" : "#B8000000");
        Set(r, "TextMuted",     d ? "#8CFFFFFF" : "#96000000");
        Set(r, "HoverBg",       d ? "#14FFFFFF" : "#0F000000");
        Set(r, "ActiveBg",      d ? "#26FFFFFF" : "#1C000000");
        Set(r, "Accent",        d ? "#60CDFF"   : "#005FB8");
        Set(r, "Done",          d ? "#6CCB5F"   : "#0F7B0F");
        Set(r, "Overdue",       d ? "#FF99A4"   : "#B3261E");
        Set(r, "ScrollThumb",   d ? "#40FFFFFF" : "#40000000");
    }

    static void ApplyBoardHighContrast(ResourceDictionary r)
    {
        r["BoardBg"] = SystemColors.WindowBrush;
        r["BoardBorder"] = SystemColors.WindowTextBrush;
        r["TextPrimary"] = SystemColors.WindowTextBrush;
        r["TextSecondary"] = SystemColors.WindowTextBrush;
        r["TextMuted"] = SystemColors.WindowTextBrush;
        // Hover and highlight keep the background: a Highlight background would need HighlightText on top,
        // which the rows can't switch to. The highlighted row gets a Highlight-colored frame instead.
        r["HoverBg"] = SystemColors.WindowBrush;
        r["ActiveBg"] = SystemColors.WindowBrush;
        r["Accent"] = SystemColors.HighlightBrush;
        r["Done"] = SystemColors.HighlightBrush;
        r["Overdue"] = SystemColors.HotTrackBrush;
        r["ScrollThumb"] = SystemColors.WindowTextBrush;
    }

    /// <summary>Brushes for menus, tooltips, dialogs and the archive window.</summary>
    public static void ApplyChrome(ResourceDictionary r, bool dark)
    {
        if (HighContrast)
        {
            ApplyChromeHighContrast(r);
            return;
        }

        bool d = dark;
        // menus, tooltips, dialogs
        Set(r, "MenuBg",        d ? "#2C2C2C"   : "#F9F9F9");
        Set(r, "MenuBorder",    d ? "#33FFFFFF" : "#1F000000");
        Set(r, "MenuText",      d ? "#F2FFFFFF" : "#E4000000");
        Set(r, "MenuTextMuted", d ? "#99FFFFFF" : "#9B000000");
        Set(r, "MenuHover",     d ? "#17FFFFFF" : "#0F000000");
        Set(r, "MenuHoverText", d ? "#F2FFFFFF" : "#E4000000");
        Set(r, "ControlBg",     d ? "#0FFFFFFF" : "#FFFFFF");
        Set(r, "ControlHover",  d ? "#1AFFFFFF" : "#F5F5F5");
        Set(r, "ControlBorder", d ? "#1AFFFFFF" : "#26000000");
        Set(r, "AccentFill",    d ? "#60CDFF"   : "#005FB8");
        Set(r, "AccentText",    d ? "#000000"   : "#FFFFFF");
        Set(r, "DangerFill",    d ? "#FF99A4"   : "#C42B1C");
        Set(r, "DangerText",    d ? "#000000"   : "#FFFFFF");

        // archive window
        Set(r, "ArcBg",           d ? "#202020"   : "#F3F3F3");
        Set(r, "ArcSurface",      d ? "#2A2A2A"   : "#FFFFFF");
        Set(r, "ArcBorder",       d ? "#1FFFFFFF" : "#1A000000");
        Set(r, "ArcText",         d ? "#F2FFFFFF" : "#E4000000");
        Set(r, "ArcMuted",        d ? "#99FFFFFF" : "#9B000000");
        Set(r, "ArcHover",        d ? "#0FFFFFFF" : "#0A000000");
        Set(r, "ArcSelected",     d ? "#3360CDFF" : "#26005FB8");
        Set(r, "ArcSelectedText", d ? "#F2FFFFFF" : "#E4000000");
        Set(r, "Accent",          d ? "#60CDFF"   : "#005FB8");
        Set(r, "ScrollThumb",     d ? "#40FFFFFF" : "#40000000");
    }

    static void ApplyChromeHighContrast(ResourceDictionary r)
    {
        var window = SystemColors.WindowBrush;
        var text = SystemColors.WindowTextBrush;
        var highlight = SystemColors.HighlightBrush;
        var highlightText = SystemColors.HighlightTextBrush;

        r["MenuBg"] = window;
        r["MenuBorder"] = text;
        r["MenuText"] = text;
        r["MenuTextMuted"] = text;
        r["MenuHover"] = highlight;
        r["MenuHoverText"] = highlightText;
        r["ControlBg"] = window;
        r["ControlHover"] = window;
        r["ControlBorder"] = text;
        r["AccentFill"] = highlight;
        r["AccentText"] = highlightText;
        r["DangerFill"] = highlight;
        r["DangerText"] = highlightText;

        r["ArcBg"] = window;
        r["ArcSurface"] = window;
        r["ArcBorder"] = text;
        r["ArcText"] = text;
        r["ArcMuted"] = text;
        r["ArcHover"] = window;
        r["ArcSelected"] = highlight;
        r["ArcSelectedText"] = highlightText;
        r["Accent"] = highlight;
        r["ScrollThumb"] = text;
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(Parse(hex));
        b.Freeze();
        return b;
    }

    static void Set(ResourceDictionary r, string key, string hex) => r[key] = Brush(hex);

    static void Set(ResourceDictionary r, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }
}
