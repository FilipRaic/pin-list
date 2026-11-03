using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PinList;

public static class Converters
{
    /// <summary>Multi-line text → first line + " …" (for the archive table).</summary>
    public static readonly IValueConverter FirstLine = new FuncValueConverter<string?, string>(s =>
    {
        s ??= "";
        int i = s.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? s : s[..i].TrimEnd() + "  …";
    });
}

/// <summary>
/// Brush for one part of a category. Values: the category's stored color (string) and the list's style key (string).
/// ConverterParameter: the <see cref="CategoryPart"/> name (Text, Ring, Line, Pill, Fill or Tick), or "Dot" for the small dot.
/// Further values (a counter that changes when the colors do) only make the binding update.
/// </summary>
public sealed class CategoryShadeConverter : IMultiValueConverter
{
    public static readonly CategoryShadeConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        string? hex = values.Count > 0 ? values[0] as string : null;
        string? style = values.Count > 1 ? values[1] as string : null;
        if (parameter as string == "Dot") return new ImmutableSolidColorBrush(Themes.CategoryDot(hex, style));
        var part = parameter is string name && Enum.TryParse<CategoryPart>(name, out var p) ? p : CategoryPart.Text;
        return new ImmutableSolidColorBrush(Themes.CategoryColor(hex, style, part));
    }
}
