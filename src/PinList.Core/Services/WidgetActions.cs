using System.Linq;

namespace PinList;

/// <summary>
/// What an Android home screen widget can change. A widget only knows ids, and what it shows may be older than the
/// data (the list changed in the app or by sync since it was drawn), so every method finds its target again and
/// does nothing when it is gone. All changes go through <see cref="Actions"/>, like the app's own.
/// </summary>
public static class WidgetActions
{
    /// <summary>The newest completion made here, and the undo history's version right after it.</summary>
    static (string itemId, int undoVersion)? _last;

    /// <summary>Completes the item (it goes to the archive). False when it no longer exists.</summary>
    public static bool Complete(string itemId)
    {
        var item = Store.AllItems().FirstOrDefault(i => i.Id == itemId);
        if (item == null) return false;
        Actions.Complete(item);
        _last = (itemId, Actions.UndoVersion);
        return true;
    }

    /// <summary>
    /// Brings back an item completed with <see cref="Complete"/>. While that completion is still the newest change,
    /// it is undone (the item returns to its place). After any other change, or in a new process, the item is
    /// restored from the archive (to the end of its category), so a different change is never undone.
    /// False when there is nothing to bring back.
    /// </summary>
    public static bool UndoComplete(string itemId)
    {
        if (_last is { } last && last.itemId == itemId && last.undoVersion == Actions.UndoVersion)
        {
            _last = null;
            return Actions.Undo();
        }

        var archived = Store.Data.Archive.FirstOrDefault(a => a.Id == itemId);
        return archived != null && Actions.Restore(new[] { archived }).Count > 0;
    }

    /// <summary>Collapses or expands the category. False when it no longer exists.</summary>
    public static bool ToggleCollapsed(string categoryId)
    {
        var cat = Store.FindCategory(categoryId);
        if (cat == null) return false;
        cat.Collapsed = !cat.Collapsed;
        Store.MarkDirty();
        return true;
    }

    /// <summary>
    /// Adds an item to the category, or to the list's first one when the category is null or no longer in the list.
    /// Null when the list is gone, has no categories, or the text is empty.
    /// </summary>
    public static TodoItem? AddItem(string boardId, string? categoryId, string text)
    {
        text = text.Trim();
        var board = Store.Data.Boards.FirstOrDefault(b => b.Id == boardId);
        var cat = board?.Categories.FirstOrDefault(c => c.Id == categoryId) ?? board?.Categories.FirstOrDefault();
        if (cat == null || text.Length == 0) return null;
        return Actions.AddItem(cat, text);
    }
}
