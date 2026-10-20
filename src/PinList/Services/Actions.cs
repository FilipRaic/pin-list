using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PinList;

/// <summary>
/// Every data change goes through this class, so each one is saved automatically.
/// Almost every change can be undone with Ctrl+Z while the app is running. There is one undo history
/// for the whole app (not one per widget), because items and categories can move between widgets.
/// </summary>
public static class Actions
{
    public static readonly string[] Palette =
        { "#60CDFF", "#6CCB5F", "#F7C948", "#FF8C42", "#F1707A", "#C084FC", "#4FD1C5", "#A0A0A0" };

    public static readonly string[] PaletteNames =
        { "Blue", "Green", "Yellow", "Orange", "Red", "Purple", "Teal", "Gray" };

    /// <summary>A new widget was added (the app opens a window for it).</summary>
    public static event Action<Board>? BoardAdded;

    const int MaxUndo = 50;
    static readonly List<(string what, Action undo)> UndoStack = new();

    /// <param name="what">What is undone, for the screen reader ("Undone: completing “Send the invoice”").</param>
    static void PushUndo(string what, Action undo)
    {
        UndoStack.Add((what, undo));
        if (UndoStack.Count > MaxUndo) UndoStack.RemoveAt(0);
    }

    /// <summary>Text in quotes, shortened to its first line and 60 characters.</summary>
    static string Quote(string text)
    {
        var line = text.Split('\n')[0];
        if (line.Length > 60) line = line[..60] + "…";
        return $"“{line}”";
    }

    public static bool CanUndo => UndoStack.Count > 0;

    public static bool Undo() => Undo(out _);

    /// <summary>Undoes the last change; <paramref name="what"/> describes it (empty if there was nothing to undo).</summary>
    public static bool Undo(out string what)
    {
        what = "";
        if (UndoStack.Count == 0) return false;
        var entry = UndoStack[^1];
        UndoStack.RemoveAt(UndoStack.Count - 1);
        entry.undo();
        what = entry.what;
        Store.MarkDirty();
        return true;
    }

    /// <summary>Forgets the undo history (after all data was replaced by an import).</summary>
    public static void ClearUndo() => UndoStack.Clear();

    // ================= Items =================

    /// <summary>Puts an item into a category (or the first existing one if that category was deleted), removing it from where it is now.</summary>
    static void PutItem(TodoItem item, string categoryId, int index)
    {
        var cat = Store.FindCategory(categoryId) ?? Store.FirstCategory();
        if (cat == null) return;
        Store.Locate(item)?.category.Items.Remove(item);
        item.IsCompleting = false;
        item.IsEditing = false;
        cat.Items.Insert(Math.Clamp(index, 0, cat.Items.Count), item);
    }

    public static TodoItem AddItem(Category category, string text)
    {
        var item = new TodoItem { Text = text };
        category.Items.Add(item);
        PushUndo($"adding {Quote(item.Text)}", () => { Store.Locate(item)?.category.Items.Remove(item); item.IsActive = false; });
        Store.MarkDirty();
        return item;
    }

    public static void SetItemText(TodoItem item, string text)
    {
        var old = item.Text;
        if (old == text) return;
        item.Text = text;
        PushUndo($"editing {Quote(old)}", () => item.Text = old);
        Store.MarkDirty();
    }

    public static void SetDueDate(TodoItem item, DateTime? due)
    {
        var old = item.DueDate;
        if (old == due?.Date) return;
        item.DueDate = due;
        PushUndo($"due date of {Quote(item.Text)}", () => item.DueDate = old);
        Store.MarkDirty();
    }

    public static void SetReminder(TodoItem item, DateTime? at)
    {
        var (oldAt, oldShown) = (item.ReminderAt, item.ReminderShown);
        item.ReminderAt = at;
        item.ReminderShown = false;
        PushUndo($"reminder of {Quote(item.Text)}", () => { item.ReminderAt = oldAt; item.ReminderShown = oldShown; });
        Store.MarkDirty();
    }

    /// <summary>Completes an item: removes it from the list and stores it in the archive with date and time.</summary>
    public static void Complete(TodoItem item)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var (board, cat) = loc.Value;

        int index = cat.Items.IndexOf(item);
        cat.Items.RemoveAt(index);
        item.IsCompleting = false;
        item.IsActive = false;

        var archived = new ArchivedItem
        {
            Text = item.Text,
            CreatedAt = item.CreatedAt,
            CompletedAt = DateTime.Now,
            DueDate = item.DueDate,
            CategoryId = cat.Id,
            CategoryName = cat.Name,
            CategoryColor = cat.Color,
            BoardId = board.Id,
            BoardTitle = board.Title,
        };
        Store.Data.Archive.Insert(0, archived);

        string catId = cat.Id;
        PushUndo($"completing {Quote(item.Text)}", () =>
        {
            Store.Data.Archive.Remove(archived);
            PutItem(item, catId, index);
        });
        Store.MarkDirty();
    }

    /// <summary>Deletes an item without archiving it.</summary>
    public static void Delete(TodoItem item)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var cat = loc.Value.category;

        int index = cat.Items.IndexOf(item);
        cat.Items.RemoveAt(index);
        item.IsActive = false;

        string catId = cat.Id;
        PushUndo($"deleting {Quote(item.Text)}", () => PutItem(item, catId, index));
        Store.MarkDirty();
    }

    public static void MoveItem(TodoItem item, Category target, int index)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var source = loc.Value.category;
        int from = source.Items.IndexOf(item);

        if (source == target)
        {
            if (index > from) index--;
            index = Math.Clamp(index, 0, target.Items.Count - 1);
            if (index == from) return;
            target.Items.Move(from, index);
        }
        else
        {
            source.Items.RemoveAt(from);
            target.Items.Insert(Math.Clamp(index, 0, target.Items.Count), item);
        }

        string sourceId = source.Id;
        PushUndo($"moving {Quote(item.Text)}", () => PutItem(item, sourceId, from));
        Store.MarkDirty();
    }

    // ================= Archive =================

    /// <summary>Permanently deletes items from the archive.</summary>
    public static void DeleteArchived(IEnumerable<ArchivedItem> items)
    {
        var removed = items
            .Select(a => (item: a, index: Store.Data.Archive.IndexOf(a)))
            .Where(x => x.index >= 0)
            .OrderBy(x => x.index)
            .ToList();
        if (removed.Count == 0) return;

        foreach (var (item, _) in removed) Store.Data.Archive.Remove(item);

        PushUndo(removed.Count == 1 ? "deleting 1 archived item" : $"deleting {removed.Count} archived items", () => ReinsertArchived(removed));
        Store.MarkDirty();
    }

    static void ReinsertArchived(IEnumerable<(ArchivedItem item, int index)> entries)
    {
        foreach (var (item, index) in entries.OrderBy(x => x.index)) // ascending, so the indexes stay correct
            Store.Data.Archive.Insert(Math.Min(index, Store.Data.Archive.Count), item);
    }

    /// <summary>
    /// Restores archived items to the end of their original category. If that category was deleted, they go to the
    /// first existing one. Returns the category each item went to (all can be undone with one Ctrl+Z).
    /// </summary>
    public static List<(ArchivedItem archived, Category category)> Restore(IEnumerable<ArchivedItem> items)
    {
        var done = new List<(ArchivedItem archived, Category category)>();
        var undo = new List<(ArchivedItem archived, int index, TodoItem item)>();

        foreach (var a in items.ToList())
        {
            var cat = Store.FindCategory(a.CategoryId) ?? Store.FirstCategory();
            int index = Store.Data.Archive.IndexOf(a);
            if (cat == null || index < 0) continue;

            var item = new TodoItem { Text = a.Text, CreatedAt = a.CreatedAt, DueDate = a.DueDate };
            cat.Items.Add(item);
            Store.Data.Archive.RemoveAt(index);
            undo.Add((a, index, item));
            done.Add((a, cat));
        }
        if (done.Count == 0) return done;

        PushUndo(done.Count == 1 ? $"restoring {Quote(done[0].archived.Text)}" : $"restoring {done.Count} items", () =>
        {
            foreach (var (_, _, item) in undo) Store.Locate(item)?.category.Items.Remove(item);
            // undo in reverse order, so each archive index is valid again when its item goes back
            foreach (var (a, index, _) in Enumerable.Reverse(undo))
                Store.Data.Archive.Insert(Math.Min(index, Store.Data.Archive.Count), a);
        });
        Store.MarkDirty();
        return done;
    }

    // ================= Categories =================

    public static Category AddCategory(Board board)
    {
        var used = board.Categories.Select(c => c.Color).ToHashSet();
        var color = Palette.FirstOrDefault(p => !used.Contains(p)) ?? Palette[board.Categories.Count % Palette.Length];
        var cat = new Category { Name = "New category", Color = color };
        board.Categories.Add(cat);
        PushUndo("adding a category", () => { Store.BoardOf(cat)?.Categories.Remove(cat); cat.IsActive = false; });
        Store.MarkDirty();
        return cat;
    }

    public static void RenameCategory(Category cat, string name)
    {
        var old = cat.Name;
        if (old == name) return;
        cat.Name = name;
        PushUndo($"renaming category {Quote(old)}", () => cat.Name = old);
        Store.MarkDirty();
    }

    public static void SetCategoryColor(Category cat, string color)
    {
        var old = cat.Color;
        if (old == color) return;
        cat.Color = color;
        PushUndo($"color of {Quote(cat.Name)}", () => cat.Color = old);
        Store.MarkDirty();
    }

    /// <summary>Puts a category into a board (or the first one if that board was deleted), removing it from where it is now.</summary>
    static void PutCategory(Category category, Board board, int index)
    {
        var target = Store.Data.Boards.Contains(board) ? board : Store.Data.Boards.FirstOrDefault();
        if (target == null) return;
        Store.BoardOf(category)?.Categories.Remove(category);
        target.Categories.Insert(Math.Clamp(index, 0, target.Categories.Count), category);
    }

    /// <summary>Deletes a category with all its items (without archiving).</summary>
    public static void DeleteCategory(Category category)
    {
        var board = Store.BoardOf(category);
        if (board == null) return;

        int index = board.Categories.IndexOf(category);
        board.Categories.RemoveAt(index);
        category.IsActive = false;

        PushUndo($"deleting category {Quote(category.Name)}", () => PutCategory(category, board, index));
        Store.MarkDirty();
    }

    public static void MoveCategory(Category category, Board target, int index)
    {
        var source = Store.BoardOf(category);
        if (source == null) return;
        int from = source.Categories.IndexOf(category);

        if (source == target)
        {
            if (index > from) index--;
            index = Math.Clamp(index, 0, target.Categories.Count - 1);
            if (index == from) return;
            target.Categories.Move(from, index);
        }
        else
        {
            source.Categories.RemoveAt(from);
            target.Categories.Insert(Math.Clamp(index, 0, target.Categories.Count), category);
        }

        PushUndo($"moving category {Quote(category.Name)}", () => PutCategory(category, source, from));
        Store.MarkDirty();
    }

    // ================= Widgets =================

    public static void RenameBoard(Board board, string title)
    {
        var old = board.Title;
        if (old == title) return;
        board.Title = title;
        PushUndo("renaming the window", () => board.Title = old);
        Store.MarkDirty();
    }

    /// <summary>New widget near the middle of the given work area; inherits color and transparency from the widget it was opened from.</summary>
    public static Board NewBoard(Rect workArea, Board? template = null)
    {
        int n = Store.Data.Boards.Count;
        var b = new Board
        {
            Title = "New list",
            Width = 320,
            Height = 400,
            X = Math.Round(workArea.Left + workArea.Width / 2 - 160 + (n % 5) * 30),
            Y = Math.Round(workArea.Top + workArea.Height / 2 - 200 + (n % 5) * 30),
            Style = template?.Style ?? "Dark",
            Transparent = template?.Transparent ?? true,
        };
        b.Categories.Add(new Category { Name = "General", Color = Palette[0] });
        Store.Data.Boards.Add(b);
        Store.MarkDirty();
        BoardAdded?.Invoke(b);
        return b;
    }

    /// <summary>Deletes a widget. Not undoable: the user confirms it first.</summary>
    public static void DeleteBoard(Board board)
    {
        Store.Data.Boards.Remove(board);
        Store.MarkDirty();
    }
}
