using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace PinList;

/// <summary>
/// Builds the rows of the archive list: a day heading for an <see cref="ArchiveDayHeader"/>, an item row for an
/// <see cref="ArchivedItem"/>. The templates are in Styles/ArchiveRow.axaml, so the desktop and the phone share them.
/// </summary>
public sealed class ArchiveRowTemplate : IDataTemplate
{
    public static readonly ArchiveRowTemplate Instance = new();

    public bool Match(object? data) => data is ArchivedItem or ArchiveDayHeader;

    public Control? Build(object? data)
    {
        var key = data is ArchiveDayHeader ? "ArchiveDayTemplate" : "ArchiveItemTemplate";
        return ((IDataTemplate)Application.Current!.FindResource(key)!).Build(data);
    }
}
