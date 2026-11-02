using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace PinList;

/// <summary>Copies items to the clipboard as text (see <see cref="CopyText"/>), on the desktop and the phone.</summary>
public static class Copying
{
    /// <summary>What is said when the clipboard can't be written (another app holds it on Windows).</summary>
    public const string Failed = "Couldn't copy. Try again.";

    /// <summary>Writes text to the clipboard. Tests replace it.</summary>
    public static Func<IClipboard, string, Task> SetClipboard { get; set; } = SystemClipboard;

    [ExcludeFromCodeCoverage(Justification = "The real clipboard call; tests replace it.")]
    public static Task SystemClipboard(IClipboard clipboard, string text) => clipboard.SetTextAsync(text);

    /// <summary>Puts the category's items on the clipboard and says how many were copied.</summary>
    public static Task<string> Category(Visual from, Category cat) =>
        ToClipboard(from, CopyText.Category(cat), cat.Items.Count);

    /// <summary>Puts every item of the list on the clipboard, with the category names, and says how many were copied.</summary>
    public static Task<string> Board(Visual from, Board board) =>
        ToClipboard(from, CopyText.Board(board), CopyText.Count(board));

    /// <summary>Puts one item on the clipboard.</summary>
    public static Task<string> Item(Visual from, TodoItem item) =>
        ToClipboard(from, CopyText.Item(item), 1);

    /// <summary>Copies, then says (and returns) how many items were copied, or that copying failed.</summary>
    static async Task<string> ToClipboard(Visual from, string text, int count)
    {
        if (TopLevel.GetTopLevel(from)?.Clipboard is not { } clipboard) return "";
        string message;
        try
        {
            await SetClipboard(clipboard, text);
            message = CopyText.Announcement(count);
        }
        catch (Exception ex)
        {
            Store.Log("Copy: " + ex.Message);
            message = Failed;
        }
        A11y.Announce(from, message);
        return message;
    }
}
