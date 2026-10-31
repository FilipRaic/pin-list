using Avalonia.Data.Converters;

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
