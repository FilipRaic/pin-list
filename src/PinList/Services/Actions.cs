using System;
using System.Collections.Generic;
using System.Linq;

namespace PinList;

/// <summary>
/// Every data change goes through this class, so each one is saved automatically.
/// Deleting and completing can be undone (Ctrl+Z) while the app is running.
/// </summary>
public static class Actions
{
    public static readonly string[] Palette =
        { "#60CDFF", "#6CCB5F", "#F7C948", "#FF8C42", "#F1707A", "#C084FC", "#4FD1C5", "#A0A0A0" };

    public static readonly string[] PaletteNames =
        { "Blue", "Green", "Yellow", "Orange", "Red", "Purple", "Teal", "Gray" };

    static readonly List<Action> UndoStack = new();

    static void PushUndo(Action undo)
    {
        UndoStack.Add(undo);
        if (UndoStack.Count > 50) UndoStack.RemoveAt(0);
    }

    public static bool Undo()
    {
        if (UndoStack.Count == 0) return false;
        var undo = UndoStack[^1];
        UndoStack.RemoveAt(UndoStack.Count - 1);
        undo();
        Store.MarkDirty();
        return true;
    }

    /// <summary>Puts an item back into its category (or the first existing one if that category was deleted).</summary>
    static void Reinsert(TodoItem item, string categoryId, int index)
    {
        var cat = Store.FindCategory(categoryId) ?? Store.FirstCategory();
        if (cat == null) return;
        item.IsCompleting = false;
        item.IsEditing = false;
        cat.Items.Insert(Math.Clamp(index, 0, cat.Items.Count), item);
    }

    public static TodoItem AddItem(Category category, string text)
    {
        var item = new TodoItem { Text = text };
        category.Items.Add(item);
        Store.MarkDirty();
        return item;
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
            CategoryId = cat.Id,
            CategoryName = cat.Name,
            CategoryColor = cat.Color,
            BoardId = board.Id,
            BoardTitle = board.Title,
        };
        Store.Data.Archive.Insert(0, archived);

        string catId = cat.Id;
        PushUndo(() =>
        {
            Store.Data.Archive.Remove(archived);
            Reinsert(item, catId, index);
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
        PushUndo(() => Reinsert(item, catId, index));
        Store.MarkDirty();
    }

    /// <summary>Deletes a category with all its items (without archiving).</summary>
    public static void DeleteCategory(Category category)
    {
        var board = Store.BoardOf(category);
        if (board == null) return;

        int index = board.Categories.IndexOf(category);
        board.Categories.RemoveAt(index);
        category.IsActive = false;

        PushUndo(() =>
        {
            var target = Store.Data.Boards.Contains(board) ? board : Store.Data.Boards.FirstOrDefault();
            target?.Categories.Insert(Math.Clamp(index, 0, target.Categories.Count), category);
        });
        Store.MarkDirty();
    }

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

        PushUndo(() =>
        {
            foreach (var (item, index) in removed) // ascending, so the indexes stay correct
                Store.Data.Archive.Insert(Math.Min(index, Store.Data.Archive.Count), item);
        });
        Store.MarkDirty();
    }

    /// <summary>Restores an archived item to the end of its original category (or the first existing one).</summary>
    public static bool Restore(ArchivedItem a)
    {
        var cat = Store.FindCategory(a.CategoryId) ?? Store.FirstCategory();
        if (cat == null) return false;

        cat.Items.Add(new TodoItem { Text = a.Text, CreatedAt = a.CreatedAt });
        Store.Data.Archive.Remove(a);
        Store.MarkDirty();
        return true;
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
        Store.MarkDirty();
    }

    public static Category AddCategory(Board board)
    {
        var used = board.Categories.Select(c => c.Color).ToHashSet();
        var color = Palette.FirstOrDefault(p => !used.Contains(p)) ?? Palette[board.Categories.Count % Palette.Length];
        var cat = new Category { Name = "New category", Color = color };
        board.Categories.Add(cat);
        Store.MarkDirty();
        return cat;
    }

    /// <summary>New widget; inherits color and transparency from the widget it was opened from.</summary>
    public static Board NewBoard(Board? template = null)
    {
        var wa = System.Windows.SystemParameters.WorkArea;
        int n = Store.Data.Boards.Count;
        var b = new Board
        {
            Title = "New list",
            Width = 320,
            Height = 400,
            X = wa.Left + wa.Width / 2 - 160 + (n % 5) * 30,
            Y = wa.Top + wa.Height / 2 - 200 + (n % 5) * 30,
            Style = template?.Style ?? "Dark",
            Transparent = template?.Transparent ?? true,
        };
        b.Categories.Add(new Category { Name = "General", Color = Palette[0] });
        Store.Data.Boards.Add(b);
        Store.MarkDirty();
        App.Instance.OpenBoard(b);
        return b;
    }

    public static void DeleteBoard(Board board)
    {
        Store.Data.Boards.Remove(board);
        Store.MarkDirty();
    }
}
