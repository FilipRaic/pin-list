using System.Collections.Generic;
using System.Linq;

namespace PinList;

/// <summary>
/// Items as plain text, for copying them into a message, a note or another app. One item per line with "- " in
/// front. The later lines of a multi-line item are indented, so it still reads as one item.
/// </summary>
public static class CopyText
{
    /// <summary>The category's items, without the category name.</summary>
    public static string Category(Category cat) => string.Join("\n", cat.Items.Select(Item));

    /// <summary>Every category with items: its name (if it has one), then its items. A blank line between them.</summary>
    public static string Board(Board board) => string.Join("\n\n", board.Categories
        .Where(c => c.Items.Count > 0)
        .Select(c => string.IsNullOrWhiteSpace(c.Name) ? Category(c) : c.Name.Trim() + "\n" + Category(c)));

    /// <summary>How many items <see cref="Board"/> copies.</summary>
    public static int Count(Board board) => board.Categories.Sum(c => c.Items.Count);

    /// <summary>What the screen reader says and the phone shows after copying.</summary>
    public static string Announcement(int count) => count == 1 ? "Copied 1 item" : $"Copied {count} items";

    /// <summary>One item, as one line (more when its text has several).</summary>
    public static string Item(TodoItem item) =>
        "- " + string.Join("\n  ", item.Text.Replace("\r\n", "\n").Split('\n'));
}
