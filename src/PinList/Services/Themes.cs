using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PinList;

/// <summary>A widget color option (the key is what gets stored in data.json).</summary>
public record BoardStyle(string Key, string Name, string Background, bool IsDark);

/// <summary>
/// All colors used by the app. Each widget gets its own brushes in its Window.Resources
/// (so every window can have a different color); menus, tooltips, dialogs and the archive
/// window always use the dark palette from the application resources.
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

    public static BoardStyle Get(string? key) =>
        BoardStyles.FirstOrDefault(s => s.Key == key) ?? BoardStyles[0];

    const byte TransparentAlpha = 0xE6; // ~90 % opacity

    /// <summary>Brushes for a single widget.</summary>
    public static void ApplyBoard(ResourceDictionary r, BoardStyle style, bool transparent)
    {
        var bg = Parse(style.Background);
        bg.A = transparent ? TransparentAlpha : (byte)0xFF;
        Set(r, "BoardBg", bg);

        bool d = style.IsDark;
        Set(r, "BoardBorder",   d ? "#26FFFFFF" : "#1F000000");
        Set(r, "TextPrimary",   d ? "#F2FFFFFF" : "#E6000000");
        Set(r, "TextSecondary", d ? "#B3FFFFFF" : "#A6000000");
        Set(r, "TextMuted",     d ? "#70FFFFFF" : "#73000000");
        Set(r, "HoverBg",       d ? "#14FFFFFF" : "#0F000000");
        Set(r, "ActiveBg",      d ? "#26FFFFFF" : "#1C000000");
        Set(r, "Accent",        d ? "#60CDFF"   : "#005FB8");
        Set(r, "Done",          d ? "#6CCB5F"   : "#0F7B0F");
        Set(r, "ScrollThumb",   d ? "#40FFFFFF" : "#40000000");
    }

    /// <summary>Dark brushes for menus, tooltips, dialogs and the archive window.</summary>
    public static void ApplyChrome(ResourceDictionary r)
    {
        // menus, tooltips, dialogs
        Set(r, "MenuBg",        "#2C2C2C");
        Set(r, "MenuBorder",    "#33FFFFFF");
        Set(r, "MenuText",      "#F2FFFFFF");
        Set(r, "MenuTextMuted", "#99FFFFFF");
        Set(r, "MenuHover",     "#17FFFFFF");
        Set(r, "ControlBg",     "#0FFFFFFF");
        Set(r, "ControlHover",  "#1AFFFFFF");
        Set(r, "ControlBorder", "#1AFFFFFF");
        Set(r, "AccentFill",    "#60CDFF");
        Set(r, "AccentText",    "#000000");
        Set(r, "DangerFill",    "#FF99A4");
        Set(r, "DangerText",    "#000000");

        // archive window
        Set(r, "ArcBg",       "#202020");
        Set(r, "ArcSurface",  "#2A2A2A");
        Set(r, "ArcBorder",   "#1FFFFFFF");
        Set(r, "ArcText",     "#F2FFFFFF");
        Set(r, "ArcMuted",    "#99FFFFFF");
        Set(r, "ArcHover",    "#0FFFFFFF");
        Set(r, "ArcSelected", "#3360CDFF");
        Set(r, "Accent",      "#60CDFF");
        Set(r, "ScrollThumb", "#40FFFFFF");
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
