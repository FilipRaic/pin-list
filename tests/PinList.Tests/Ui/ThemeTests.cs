using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Xunit;

namespace PinList.Tests;

/// <summary>The chrome color scheme (menus, sheets, the PinList window) and the category shades.</summary>
public class ThemeColorTests : UiTest
{
    static Color C(IResourceDictionary r, string key) => ((ImmutableSolidColorBrush)r[key]!).Color;

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Chrome_text_is_readable_on_every_surface(bool dark)
    {
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark);
        foreach (var text in new[] { "Text1", "Text2", "Text3" })
            foreach (var surface in new[] { "Bg", "Surface", "Raised" })
                Assert.True(Themes.Contrast(C(r, text), C(r, surface)) >= 4.5, $"{text} on {surface}");
        Assert.True(Themes.Contrast(C(r, "AccentText"), C(r, "Accent")) >= 4.5);
        Assert.True(Themes.Contrast(C(r, "DangerText"), C(r, "Danger")) >= 4.5);
        Assert.True(Themes.Contrast(C(r, "Accent"), C(r, "Surface")) >= 3);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Old_color_keys_are_gone(bool dark)
    {
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark);
        foreach (var key in r.Keys.Select(k => k.ToString()!))
            Assert.False(key.StartsWith("Arc") || (key.StartsWith("Menu") && !key.StartsWith("MenuFlyout")), key);
        foreach (var key in new[] { "Bg", "Surface", "Raised", "Line", "Text1", "Text2", "Text3", "Hover", "Selected", "Accent", "AccentText", "Danger", "DangerText", "Done", "ScrollThumb" })
            Assert.True(r.ContainsKey(key), key);
    }

    [AvaloniaFact]
    public void High_contrast_chrome_uses_system_colors()
    {
        var hc = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Themes.ContrastSource = () => hc;
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark: false);
        foreach (var key in new[] { "Bg", "Surface", "Raised" }) Assert.Equal(Colors.Black, C(r, key));
        foreach (var key in new[] { "Text1", "Text2", "Text3" }) Assert.Equal(Colors.White, C(r, key));
        Assert.Equal(Colors.Cyan, C(r, "Accent"));
    }

    [AvaloniaFact]
    public void Contrast_math_matches_WCAG()
    {
        Assert.Equal(21, Themes.Contrast(Colors.Black, Colors.White), 3);
        Assert.Equal(1, Themes.Contrast(Colors.Gray, Colors.Gray), 3);
        // a half see-through white on black reads as mid gray
        Assert.Equal(Themes.Contrast(Color.FromRgb(128, 128, 128), Colors.Black), Themes.Contrast(Color.FromArgb(128, 255, 255, 255), Colors.Black), 1);
    }

    [AvaloniaFact]
    public void Widgets_have_a_divider_color()
    {
        var r = new ResourceDictionary();
        Themes.ApplyBoard(r, Themes.Get("Dark"), transparent: false);
        Assert.Equal(Color.Parse("#17FFFFFF"), C(r, "Line"));
        Themes.ApplyBoard(r, Themes.Get("Light"), transparent: false);
        Assert.Equal(Color.Parse("#14000000"), C(r, "Line"));
    }

    // ================= Category shades =================

    static Color Bg(string style) => Themes.Parse(Themes.Get(style).Background);

    public static TheoryData<string> Styles => new(Themes.BoardStyles.Select(s => s.Key));

    [AvaloniaTheory]
    [MemberData(nameof(Styles))]
    public void Every_palette_color_is_readable_on_every_list(string style)
    {
        foreach (var hex in Actions.Palette)
        {
            var shade = Themes.CategoryShade(hex, style);
            Assert.True(Themes.Contrast(shade, Bg(style)) >= 4.5, $"{hex} text on {style}");
            var ring = Themes.CategoryColor(hex, style, CategoryPart.Ring);
            Assert.True(Themes.Contrast(ring, Bg(style)) >= 3.0, $"{hex} ring on {style}");
        }
    }

    [AvaloniaFact]
    public void Colors_that_are_already_readable_stay_as_they_are()
    {
        Assert.Equal(Color.Parse("#60CDFF"), Themes.CategoryShade("#60CDFF", "Dark"));
    }

    [AvaloniaFact]
    public void Light_lists_darken_and_dark_lists_lighten()
    {
        var yellow = Color.Parse("#F7C948");
        var onLight = Themes.CategoryShade("#F7C948", "Yellow");
        Assert.True(onLight.R < yellow.R && onLight.G < yellow.G);
        var dark = Themes.CategoryShade("#101820", "Dark");
        Assert.True(dark.B > 0x20);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Styles))]
    public void Unknown_and_invalid_colors_are_still_readable(string style)
    {
        foreach (var hex in new[] { "#FFFFFF", "#000000", "red", "", null })
        {
            var shade = Themes.CategoryShade(hex, style);
            Assert.True(Themes.Contrast(shade, Bg(style)) >= 4.5, $"{hex ?? "null"} on {style}");
        }
        Assert.Equal(Themes.CategoryShade("#A0A0A0", style), Themes.CategoryShade("red", style));
        Assert.Equal(Themes.CategoryShade("#A0A0A0", style), Themes.CategoryShade(null, style));
        Assert.Equal(Themes.CategoryShade("#A0A0A0", style), Themes.CategoryShade("#zzz", style)); // starts with # but is no color
    }

    [AvaloniaFact]
    public void Parts_use_the_shade_with_their_own_opacity()
    {
        var shade = Themes.CategoryShade("#F7C948", "Light");
        Assert.Equal(shade, Themes.CategoryColor("#F7C948", "Light", CategoryPart.Text));
        Assert.Equal(shade, Themes.CategoryColor("#F7C948", "Light", CategoryPart.Fill));
        Assert.Equal(0xB3, Themes.CategoryColor("#F7C948", "Light", CategoryPart.Ring).A);
        Assert.Equal(0x59, Themes.CategoryColor("#F7C948", "Light", CategoryPart.Line).A);
        Assert.Equal(0x29, Themes.CategoryColor("#F7C948", "Light", CategoryPart.Pill).A);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Styles))]
    public void The_tick_is_readable_on_every_filled_circle(string style)
    {
        foreach (var hex in Actions.Palette)
        {
            var fill = Themes.CategoryColor(hex, style, CategoryPart.Fill);
            var tick = Themes.CategoryColor(hex, style, CategoryPart.Tick);
            Assert.True(Themes.Contrast(tick, fill) >= 3.0, $"{hex} tick on {style}");
        }
    }

    [AvaloniaFact]
    public void High_contrast_tick_uses_the_highlight_text_color()
    {
        var hc = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Themes.ContrastSource = () => hc;
        Assert.Equal(Colors.Navy, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Tick));
    }

    [AvaloniaFact]
    public void High_contrast_category_parts_use_system_colors()
    {
        var hc = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Themes.ContrastSource = () => hc;
        Assert.Equal(Colors.White, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Text));
        Assert.Equal(Colors.White, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Ring));
        Assert.Equal(Colors.White, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Line));
        Assert.Equal(Colors.Cyan, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Fill));
        Assert.Equal(Colors.Transparent, Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Pill));
    }

    [AvaloniaFact]
    public void Converter_returns_brushes_for_each_part()
    {
        foreach (var part in Enum.GetValues<CategoryPart>())
        {
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(
                CategoryShadeConverter.Instance.Convert(new object?[] { "#F7C948", "Yellow" }, typeof(IBrush), part.ToString(), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", part), brush.Color);
        }
        // without a part name it is the text color, missing values still give a brush
        var text = Assert.IsAssignableFrom<ISolidColorBrush>(CategoryShadeConverter.Instance.Convert(new object?[] { null }, typeof(IBrush), null, null!));
        Assert.Equal(Themes.CategoryColor(null, null, CategoryPart.Text), text.Color);
    }

    [AvaloniaFact]
    public void The_dot_keeps_the_own_color_only_in_high_contrast()
    {
        Assert.Equal(Themes.CategoryShade("#F7C948", "Yellow"), Themes.CategoryDot("#F7C948", "Yellow"));
        Themes.ContrastSource = () => new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Assert.Equal(Color.Parse("#F7C948"), Themes.CategoryDot("#F7C948", "Yellow"));
        Assert.Equal(Themes.CategoryShade("#A0A0A0", "Yellow"), Themes.CategoryDot("red", "Yellow")); // not a hex color: gray
        Assert.Equal(Themes.CategoryShade("#A0A0A0", "Yellow"), Themes.CategoryDot("", "Yellow"));
    }

    [AvaloniaFact]
    public void Converter_handles_the_dot_and_missing_values()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var dot = Assert.IsAssignableFrom<ISolidColorBrush>(CategoryShadeConverter.Instance.Convert(new object?[] { "#F7C948", "Yellow", 3 }, typeof(IBrush), "Dot", culture));
        Assert.Equal(Themes.CategoryDot("#F7C948", "Yellow"), dot.Color);
        var none = Assert.IsAssignableFrom<ISolidColorBrush>(CategoryShadeConverter.Instance.Convert(new object?[0], typeof(IBrush), "Dot", culture));
        Assert.Equal(Themes.CategoryDot(null, null), none.Color);
        var unknownPart = Assert.IsAssignableFrom<ISolidColorBrush>(CategoryShadeConverter.Instance.Convert(new object?[] { "#F7C948", "Yellow" }, typeof(IBrush), "Sideways", culture));
        Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Text), unknownPart.Color);
    }
}
