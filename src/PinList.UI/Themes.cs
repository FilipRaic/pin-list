using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PinList;

/// <summary>The parts of a category that are colored with its shade (see <see cref="Themes.CategoryColor"/>).</summary>
public enum CategoryPart { Text, Ring, Line, Pill, Fill, Tick }

/// <summary>A widget color option (the key is what gets stored in data.json).</summary>
public record BoardStyle(string Key, string Name, string Background, bool IsDark);

/// <summary>The colors of the operating system's contrast theme (Windows: Settings → Accessibility → Contrast themes).</summary>
public record ContrastColors(Color Window, Color WindowText, Color Highlight, Color HighlightText, Color HotTrack);

/// <summary>
/// All colors used by the app. Each widget gets its own brushes in its own resources (so every window can have a
/// different color); menus, tooltips, dialogs and the PinList window use the app theme (light or dark) from the
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
        Set(r, "Line",          d ? "#17FFFFFF" : "#14000000");
        Set(r, "TextPrimary",   d ? "#F2FFFFFF" : "#E6000000");
        Set(r, "TextSecondary", d ? "#B3FFFFFF" : "#B8000000");
        Set(r, "TextMuted",     d ? "#8CFFFFFF" : "#96000000");
        Set(r, "HoverBg",       d ? "#14FFFFFF" : "#0F000000");
        Set(r, "ActiveBg",      d ? "#26FFFFFF" : "#1C000000");
        Set(r, "Accent",        d ? "#60CDFF"   : "#005FB8");
        Set(r, "Done",          d ? "#6CCB5F"   : "#0F7B0F");
        Set(r, "Overdue",       d ? "#FF99A4"   : "#B3261E");
        Set(r, "ScrollThumb",   d ? "#40FFFFFF" : "#40000000");
        // chip backgrounds: neutral, and a 16 % tint of the accent and the overdue color
        Set(r, "ChipBg",        d ? "#14FFFFFF" : "#0F000000");
        Set(r, "AccentBg",      Tint(d ? "#60CDFF" : "#005FB8"));
        Set(r, "OverdueBg",     Tint(d ? "#FF99A4" : "#B3261E"));
    }

    static Color Tint(string hex)
    {
        var c = Parse(hex);
        return Color.FromArgb(PillAlpha, c.R, c.G, c.B);
    }

    static void ApplyBoardHighContrast(IResourceDictionary r, ContrastColors c)
    {
        Set(r, "BoardBg", c.Window);
        Set(r, "BoardBorder", c.WindowText);
        Set(r, "Line", c.WindowText);
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
        Set(r, "ChipBg", c.Window);
        Set(r, "AccentBg", c.Window);
        Set(r, "OverdueBg", c.Window);
    }

    /// <summary>
    /// Brushes for everything outside the widgets: menus, tooltips, dialogs, the PinList window and the phone.
    /// One scheme: Bg (screens), Surface (boxes, cards), Raised (menus, sheets, dialogs), Line, Text1 to Text3,
    /// Hover, Selected, Accent, Danger and Done. ItemHover and ItemHoverText are what a hovered menu entry or
    /// list row shows (they differ from Hover only in high contrast, where menus need the Highlight colors).
    /// </summary>
    public static void ApplyChrome(IResourceDictionary r, bool dark)
    {
        if (ContrastSource() is { } hc)
        {
            ApplyChromeHighContrast(r, hc);
            return;
        }

        bool d = dark;
        Set(r, "Bg",         d ? "#18181A" : "#F4F4F6");
        Set(r, "Surface",    d ? "#222225" : "#FFFFFF");
        Set(r, "Raised",     d ? "#2C2C2F" : "#FFFFFF");
        Set(r, "Line",       d ? "#17FFFFFF" : "#14000000");
        Set(r, "Text1",      d ? "#F0FFFFFF" : "#E0000000");
        Set(r, "Text2",      d ? "#B3FFFFFF" : "#B8000000");
        Set(r, "Text3",      d ? "#8CFFFFFF" : "#96000000");
        Set(r, "Hover",      d ? "#0FFFFFFF" : "#0A000000");
        Set(r, "Selected",   d ? "#2E6CB8FF" : "#1F0A64C8");
        Set(r, "Accent",     d ? "#6CB8FF" : "#0A64C8");
        Set(r, "AccentText", d ? "#04223F" : "#FFFFFF");
        Set(r, "Danger",     d ? "#FF8A8A" : "#C42B1C");
        Set(r, "DangerText", d ? "#000000" : "#FFFFFF");
        Set(r, "Done",       d ? "#5FD08A" : "#1D8A4A");
        Set(r, "ScrollThumb", d ? "#40FFFFFF" : "#40000000");
        Set(r, "ItemHover",     d ? "#17FFFFFF" : "#0F000000");
        Set(r, "ItemHoverText", d ? "#F0FFFFFF" : "#E0000000");
        Set(r, "SelectedText",  d ? "#F0FFFFFF" : "#E0000000");
        Set(r, "SwitchKnob",    "#FFFFFF");

        MapFluent(r);
    }

    static void ApplyChromeHighContrast(IResourceDictionary r, ContrastColors c)
    {
        Set(r, "Bg", c.Window);
        Set(r, "Surface", c.Window);
        Set(r, "Raised", c.Window);
        Set(r, "Line", c.WindowText);
        Set(r, "Text1", c.WindowText);
        Set(r, "Text2", c.WindowText);
        Set(r, "Text3", c.WindowText);
        Set(r, "Hover", c.Window);
        Set(r, "Selected", c.Highlight);
        Set(r, "Accent", c.Highlight);
        Set(r, "AccentText", c.HighlightText);
        Set(r, "Danger", c.Highlight);
        Set(r, "DangerText", c.HighlightText);
        Set(r, "Done", c.Highlight);
        Set(r, "ScrollThumb", c.WindowText);
        Set(r, "ItemHover", c.Highlight);
        Set(r, "ItemHoverText", c.HighlightText);
        Set(r, "SelectedText", c.HighlightText);
        Set(r, "SwitchKnob", c.HighlightText);

        MapFluent(r);
    }

    /// <summary>Menus and tooltips come from Avalonia's Fluent theme; give them the app's colors.</summary>
    static void MapFluent(IResourceDictionary r)
    {
        void Same(string fluentKey, string ourKey) => r[fluentKey] = r[ourKey];
        Same("MenuFlyoutPresenterBackground", "Raised");
        Same("MenuFlyoutPresenterBorderBrush", "Line");
        Same("MenuFlyoutItemForeground", "Text1");
        Same("MenuFlyoutItemForegroundPointerOver", "ItemHoverText");
        Same("MenuFlyoutItemForegroundPressed", "ItemHoverText");
        Same("MenuFlyoutItemForegroundDisabled", "Text3");
        Same("MenuFlyoutItemBackgroundPointerOver", "ItemHover");
        Same("MenuFlyoutItemBackgroundPressed", "ItemHover");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForeground", "Text3");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "ItemHoverText");
        Same("MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "ItemHoverText");
        Same("MenuFlyoutSubItemChevron", "Text1");
        Same("MenuFlyoutSubItemChevronPointerOver", "ItemHoverText");
        Same("MenuFlyoutSubItemChevronPressed", "ItemHoverText");
        Same("MenuFlyoutSubItemChevronSubMenuOpened", "ItemHoverText");
        Same("ToolTipBackground", "Raised");
        Same("ToolTipForeground", "Text1");
        Same("ToolTipBorderBrush", "Line");
        // switches in the app's accent color, not the system one
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Same("ToggleSwitchFillOn" + state, "Accent");
            Same("ToggleSwitchStrokeOn" + state, "Accent");
            Same("ToggleSwitchKnobFillOn" + state, "SwitchKnob");
            // Fluent's own "accent" buttons (Sign in with Google) in the app's accent color too
            Same("AccentButtonBackground" + state, "Accent");
            Same("AccentButtonForeground" + state, "AccentText");
        }
        Same("AccentButtonBorderBrush", "Accent");
    }

    /// <summary>
    /// WCAG contrast ratio of two colors (1 to 21). A translucent first color is laid over the second one first,
    /// like it looks on screen.
    /// </summary>
    public static double Contrast(Color foreground, Color background)
    {
        if (foreground.A < 255)
        {
            double a = foreground.A / 255.0;
            foreground = Color.FromRgb(
                (byte)Math.Round(foreground.R * a + background.R * (1 - a)),
                (byte)Math.Round(foreground.G * a + background.G * (1 - a)),
                (byte)Math.Round(foreground.B * a + background.B * (1 - a)));
        }
        double l1 = Luminance(foreground), l2 = Luminance(background);
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    const string FallbackCategoryColor = "#A0A0A0";

    /// <summary>
    /// The shade of a category color that is shown on a list. The stored colors (Actions.Palette) are bright and were
    /// picked for dark backgrounds, so on light lists they are darkened (and on dark lists lightened) until the
    /// category name has 4.5:1 contrast and the check circle's outline 3:1. An invalid or empty color counts as gray.
    /// </summary>
    public static Color CategoryShade(string? storedHex, string? boardStyleKey)
    {
        var style = Get(boardStyleKey);
        var bg = Parse(style.Background);
        var color = StoredColor(storedHex) ?? Parse(FallbackCategoryColor);
        var target = style.IsDark ? Colors.White : Colors.Black;
        // Blending all the way gives black or white, which pass on every list color, so the loop always ends
        for (int step = 0; ; step++)
        {
            double t = Math.Min(1, step * 0.04);
            var shade = Color.FromRgb(Mix(color.R, target.R, t), Mix(color.G, target.G, t), Mix(color.B, target.B, t));
            var ring = Color.FromArgb(RingAlpha, shade.R, shade.G, shade.B);
            if (Contrast(shade, bg) >= 4.5 && Contrast(ring, bg) >= 3.0) return shade;
        }
    }

    /// <summary>The category's dot: its shade, or in high contrast its own stored color (so categories can be told apart).</summary>
    public static Color CategoryDot(string? storedHex, string? boardStyleKey) =>
        (ContrastSource() != null ? StoredColor(storedHex) : null) ?? CategoryShade(storedHex, boardStyleKey);

    /// <summary>A stored category color (#RRGGBB), or null when it is empty or isn't a hex color (names like "red" don't count).</summary>
    static Color? StoredColor(string? hex) =>
        hex is ['#', ..] && Color.TryParse(hex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : null;

    const byte RingAlpha = 0xB3; // 70 %
    const byte LineAlpha = 0x59; // 35 %
    const byte PillAlpha = 0x29; // 16 %

    static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);

    /// <summary>
    /// A color for one part of a category (name, dot, guide line, circle, count pill) on a list.
    /// In high contrast the parts use the system colors, only the dot keeps the category color.
    /// </summary>
    public static Color CategoryColor(string? storedHex, string? boardStyleKey, CategoryPart part)
    {
        if (ContrastSource() is { } hc)
        {
            return part switch
            {
                CategoryPart.Fill => hc.Highlight,
                CategoryPart.Tick => hc.HighlightText,
                CategoryPart.Pill => Colors.Transparent,
                _ => hc.WindowText,
            };
        }
        var shade = CategoryShade(storedHex, boardStyleKey);
        return part switch
        {
            CategoryPart.Ring => Color.FromArgb(RingAlpha, shade.R, shade.G, shade.B),
            CategoryPart.Line => Color.FromArgb(LineAlpha, shade.R, shade.G, shade.B),
            CategoryPart.Pill => Color.FromArgb(PillAlpha, shade.R, shade.G, shade.B),
            CategoryPart.Tick => TickOn(shade),
            _ => shade,
        };
    }

    static readonly Color DarkTick = Color.Parse("#10131A");

    /// <summary>The tick on a filled circle: dark on light fills, white on the darkened shades of light lists.</summary>
    static Color TickOn(Color fill) => Contrast(DarkTick, fill) >= Contrast(Colors.White, fill) ? DarkTick : Colors.White;

    public static Color Parse(string hex) => Color.Parse(hex);

    public static IImmutableSolidColorBrush Brush(string hex) => new ImmutableSolidColorBrush(Parse(hex));

    static void Set(IResourceDictionary r, string key, string hex) => r[key] = Brush(hex);

    static void Set(IResourceDictionary r, string key, Color c) => r[key] = new ImmutableSolidColorBrush(c);
}
