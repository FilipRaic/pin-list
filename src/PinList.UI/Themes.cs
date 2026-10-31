using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PinList;

/// <summary>A widget color option (the key is what gets stored in data.json).</summary>
public record BoardStyle(string Key, string Name, string Background, bool IsDark);

/// <summary>The colors of the operating system's contrast theme (Windows: Settings → Accessibility → Contrast themes).</summary>
public record ContrastColors(Color Window, Color WindowText, Color Highlight, Color HighlightText, Color HotTrack);

/// <summary>
/// All colors used by the app. Each widget gets its own brushes in its own resources (so every window can have a
/// different color); menus, tooltips, dialogs and the archive use the app theme (light or dark) from the
/// application resources.
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
    public static (string Key, string Name)[] AppThemes =>
        new[] { ("System", SystemThemeName), ("Light", "Light"), ("Dark", "Dark") };

    /// <summary>Menu text for following the operating system ("Use Windows setting" on Windows).</summary>
    public static string SystemThemeName { get; set; } = "Use system setting";

    public static BoardStyle Get(string? key) =>
        BoardStyles.FirstOrDefault(s => s.Key == key) ?? BoardStyles[0];

    /// <summary>True if the operating system is set to dark mode for apps.</summary>
    public static bool SystemUsesDark() => SystemDarkSource();

    /// <summary>Where the light/dark setting comes from. Each platform sets it; tests replace it.</summary>
    public static Func<bool> SystemDarkSource { get; set; } = () => true;

    /// <summary>Resolves the "System" / "Light" / "Dark" setting to dark or not.</summary>
    public static bool IsDark(string? setting) => setting switch
    {
        "Light" => false,
        "Dark" => true,
        _ => SystemUsesDark(),
    };

    const byte TransparentAlpha = 0xE6; // ~90 % opacity

    /// <summary>
    /// The contrast theme's colors when one is on (then all colors come from it, and widget colors and
    /// transparency are ignored), otherwise null. Each platform sets it; tests replace it.
    /// </summary>
    public static Func<ContrastColors?> ContrastSource { get; set; } = () => null;

    public static bool HighContrast => ContrastSource() != null;

    /// <summary>
    /// Brushes for a single widget. Small text (TextMuted, TextSecondary) has at least 4.5:1 contrast
    /// on every widget color (WCAG AA), measured on the solid background.
    /// </summary>
    public static void ApplyBoard(IResourceDictionary r, BoardStyle style, bool transparent)
    {
        if (ContrastSource() is { } hc)
        {
            ApplyBoardHighContrast(r, hc);
            return;
        }

        var bg = Parse(style.Background);
        Set(r, "BoardBg", Color.FromArgb(transparent ? TransparentAlpha : (byte)0xFF, bg.R, bg.G, bg.B));

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

    static void ApplyBoardHighContrast(IResourceDictionary r, ContrastColors c)
    {
        Set(r, "BoardBg", c.Window);
        Set(r, "BoardBorder", c.WindowText);
        Set(r, "TextPrimary", c.WindowText);
        Set(r, "TextSecondary", c.WindowText);
        Set(r, "TextMuted", c.WindowText);
        // Hover and highlight keep the background: a Highlight background would need HighlightText on top,
        // which the rows can't switch to. The highlighted row gets a Highlight-colored frame instead.
        Set(r, "HoverBg", c.Window);
        Set(r, "ActiveBg", c.Window);
        Set(r, "Accent", c.Highlight);
        Set(r, "Done", c.Highlight);
        Set(r, "Overdue", c.HotTrack);
        Set(r, "ScrollThumb", c.WindowText);
    }

    /// <summary>Brushes for menus, tooltips, dialogs and the archive window.</summary>
    public static void ApplyChrome(IResourceDictionary r, bool dark)
    {
        if (ContrastSource() is { } hc)
        {
            ApplyChromeHighContrast(r, hc);
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

        MapFluent(r);
    }

    static void ApplyChromeHighContrast(IResourceDictionary r, ContrastColors c)
    {
        Set(r, "MenuBg", c.Window);
        Set(r, "MenuBorder", c.WindowText);
        Set(r, "MenuText", c.WindowText);
        Set(r, "MenuTextMuted", c.WindowText);
        Set(r, "MenuHover", c.Highlight);
        Set(r, "MenuHoverText", c.HighlightText);
        Set(r, "ControlBg", c.Window);
        Set(r, "ControlHover", c.Window);
        Set(r, "ControlBorder", c.WindowText);
        Set(r, "AccentFill", c.Highlight);
        Set(r, "AccentText", c.HighlightText);
        Set(r, "DangerFill", c.Highlight);
        Set(r, "DangerText", c.HighlightText);

        Set(r, "ArcBg", c.Window);
        Set(r, "ArcSurface", c.Window);
        Set(r, "ArcBorder", c.WindowText);
        Set(r, "ArcText", c.WindowText);
        Set(r, "ArcMuted", c.WindowText);
        Set(r, "ArcHover", c.Window);
        Set(r, "ArcSelected", c.Highlight);
        Set(r, "ArcSelectedText", c.HighlightText);
        Set(r, "Accent", c.Highlight);
        Set(r, "ScrollThumb", c.WindowText);

        MapFluent(r);
    }

    /// <summary>Menus and tooltips come from Avalonia's Fluent theme; give them the app's colors.</summary>
    static void MapFluent(IResourceDictionary r)
    {
        void Same(string fluentKey, string ourKey) => r[fluentKey] = r[ourKey];
        Same("MenuFlyoutPresenterBackground", "MenuBg");
        Same("MenuFlyoutPresenterBorderBrush", "MenuBorder");
        Same("MenuFlyoutItemForeground", "MenuText");
        Same("MenuFlyoutItemForegroundPointerOver", "MenuHoverText");
        Same("MenuFlyoutItemForegroundPressed", "MenuHoverText");
        Same("MenuFlyoutItemForegroundDisabled", "MenuTextMuted");
        Same("MenuFlyoutItemBackgroundPointerOver", "MenuHover");
        Same("MenuFlyoutItemBackgroundPressed", "MenuHover");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForeground", "MenuTextMuted");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "MenuHoverText");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "MenuHoverText");
        Same("MenuFlyoutSubItemChevron", "MenuText");
        Same("MenuFlyoutSubItemChevronPointerOver", "MenuHoverText");
        Same("MenuFlyoutSubItemChevronPressed", "MenuHoverText");
        Same("MenuFlyoutSubItemChevronSubMenuOpened", "MenuHoverText");
        Same("ToolTipBackground", "MenuBg");
        Same("ToolTipForeground", "MenuText");
        Same("ToolTipBorderBrush", "MenuBorder");
    }

    public static Color Parse(string hex) => Color.Parse(hex);

    public static IImmutableSolidColorBrush Brush(string hex) => new ImmutableSolidColorBrush(Parse(hex));

    static void Set(IResourceDictionary r, string key, string hex) => r[key] = Brush(hex);

    static void Set(IResourceDictionary r, string key, Color c) => r[key] = new ImmutableSolidColorBrush(c);
}
