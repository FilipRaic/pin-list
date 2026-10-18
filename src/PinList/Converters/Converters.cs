using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PinList;

/// <summary>true → Visible. With ConverterParameter=invert the other way round.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value is true;
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Multi-line text → first line + " …" (for the archive table).</summary>
public sealed class FirstLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        int i = s.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? s : s[..i].TrimEnd() + "  …";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Empty string → Visible (for placeholder text).</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
