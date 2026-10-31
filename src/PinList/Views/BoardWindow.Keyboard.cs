using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace PinList;

/// <summary>
/// Everything that can be done with the mouse can also be done with the keyboard:
///   Tab / Up / Down        move between the header buttons, categories, items and "Add item" boxes
///   Space                  complete the focused item (Space again within ~1 s cancels)
///   Enter or F2            edit the item / Enter collapses a category, F2 renames it
///   Delete                 delete the item or category (Ctrl+Z undoes)
///   Alt+Up / Alt+Down      move the item or category
///   Context menu key, Shift+F10   open its menu (due date, reminder, color, …)
///   Ctrl+Arrows            move the widget; Ctrl+Shift+Arrows resize it (unless it's locked)
/// Every change is announced to screen readers.
/// </summary>
public partial class BoardWindow
{
    const double KeyboardStep = 10;

    void Row_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, sender) && ((FrameworkElement)sender).Tag is Observable o)
            SetActive(o);
    }

    static bool IsMenuKey(KeyEventArgs e) =>
        e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10 && InputState.Modifiers() == ModifierKeys.Shift);

    /// <summary>Alt+Up / Alt+Down arrive as Key.System. Returns -1, +1 or 0.</summary>
    static int AltArrow(KeyEventArgs e)
    {
        if (e.Key != Key.System || InputState.Modifiers() != ModifierKeys.Alt) return 0;
        return e.SystemKey switch { Key.Up => -1, Key.Down => +1, _ => 0 };
    }

    void Item_KeyDown(object sender, KeyEventArgs e)
    {
        var row = (FrameworkElement)sender;
        if (!ReferenceEquals(e.OriginalSource, row) || row.Tag is not TodoItem item) return;

        if (IsMenuKey(e)) ShowItemMenu(row, item, PlacementMode.Bottom);
        else if (e.Key == Key.Space && InputState.Modifiers() == ModifierKeys.None) ToggleCompleting(item);
        else if ((e.Key == Key.Enter || e.Key == Key.F2) && InputState.Modifiers() == ModifierKeys.None) item.IsEditing = true;
        else if (AltArrow(e) is int d and not 0) MoveItemBy(item, d);
        else if (!MoveFocusUpDown(row, e)) return;
        e.Handled = true;
    }

    void Cat_KeyDown(object sender, KeyEventArgs e)
    {
        var row = (FrameworkElement)sender;
        if (!ReferenceEquals(e.OriginalSource, row) || row.Tag is not Category cat) return;

        if (IsMenuKey(e)) ShowCategoryMenu(row, cat, PlacementMode.Bottom);
        else if ((e.Key == Key.Enter || e.Key == Key.Space) && InputState.Modifiers() == ModifierKeys.None) ToggleCollapsed(cat);
        else if (e.Key == Key.F2 && InputState.Modifiers() == ModifierKeys.None) cat.IsEditing = true;
        else if (AltArrow(e) is int d and not 0) MoveCategoryBy(cat, d);
        else if (!MoveFocusUpDown(row, e)) return;
        e.Handled = true;
    }

    /// <summary>Up/Down move focus like Shift+Tab/Tab.</summary>
    static bool MoveFocusUpDown(FrameworkElement from, KeyEventArgs e)
    {
        if (InputState.Modifiers() != ModifierKeys.None) return false;
        var direction = e.Key switch
        {
            Key.Up => FocusNavigationDirection.Previous,
            Key.Down => FocusNavigationDirection.Next,
            _ => (FocusNavigationDirection?)null,
        };
        if (direction == null) return false;
        from.MoveFocus(new TraversalRequest(direction.Value));
        return true;
    }

    // ================= Actions with announcements =================

    void ToggleCollapsed(Category cat)
    {
        cat.Collapsed = !cat.Collapsed;
        Store.MarkDirty();
        A11y.Announce(this, cat.Collapsed ? $"{cat.Name} collapsed" : $"{cat.Name} expanded");
        FocusRow(cat);
    }

    /// <summary>Starts completing (with ~1 s to cancel), or cancels if it's already completing. Same as clicking the circle.</summary>
    void ToggleCompleting(TodoItem item)
    {
        if (item.IsCompleting)
        {
            item.IsCompleting = false;
            A11y.Announce(this, "Cancelled");
            return;
        }

        item.IsCompleting = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (item.IsCompleting) CompleteNow(item);
        };
        timer.Start();
    }

    void CompleteNow(TodoItem item)
    {
        bool hadFocus = IsRowFocused(item);
        var next = NeighborAfterRemoval(item);
        Actions.Complete(item);
        A11y.Announce(this, $"Completed {item.Text.Split('\n')[0]}. Ctrl+Z undoes.");
        if (hadFocus) FocusRowOrContent(next);
    }

    /// <summary>Deletes an item or category and moves focus to its neighbor, so keyboard users stay in the list.</summary>
    void DeleteWithFocus(Observable element)
    {
        var next = NeighborAfterRemoval(element);
        switch (element)
        {
            case TodoItem item:
                Actions.Delete(item);
                A11y.Announce(this, $"Deleted {item.Text.Split('\n')[0]}. Ctrl+Z undoes.");
                break;
            case Category cat:
                Actions.DeleteCategory(cat);
                A11y.Announce(this, $"Deleted category {cat.Name}. Ctrl+Z undoes.");
                break;
        }
        SetActive(null);
        FocusRowOrContent(next);
    }

    void MoveItemBy(TodoItem item, int delta)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var (board, cat) = loc.Value;
        int index = cat.Items.IndexOf(item);
        int catIndex = board.Categories.IndexOf(cat);

        if (delta < 0 && index > 0) Actions.MoveItem(item, cat, index - 1);
        else if (delta > 0 && index < cat.Items.Count - 1) Actions.MoveItem(item, cat, index + 2); // drop index counts the item itself
        else if (delta < 0 && catIndex > 0)
        {
            var target = board.Categories[catIndex - 1];
            Actions.MoveItem(item, target, target.Items.Count);
            target.Collapsed = false;
        }
        else if (delta > 0 && catIndex < board.Categories.Count - 1)
        {
            var target = board.Categories[catIndex + 1];
            Actions.MoveItem(item, target, 0);
            target.Collapsed = false;
        }
        else
        {
            A11y.Announce(this, delta < 0 ? "Already at the top" : "Already at the bottom");
            return;
        }

        var now = Store.Locate(item)!.Value.category;
        A11y.Announce(this, $"Moved to position {now.Items.IndexOf(item) + 1} of {now.Items.Count} in {now.Name}");
        FocusRow(item);
    }

    void MoveCategoryBy(Category cat, int delta)
    {
        var board = Store.BoardOf(cat);
        if (board == null) return;
        int index = board.Categories.IndexOf(cat);
        int target = index + delta;
        if (target < 0 || target >= board.Categories.Count)
        {
            A11y.Announce(this, delta < 0 ? "Already at the top" : "Already at the bottom");
            return;
        }
        Actions.MoveCategory(cat, board, delta < 0 ? target : target + 1);
        A11y.Announce(this, $"Moved to position {board.Categories.IndexOf(cat) + 1} of {board.Categories.Count}");
        FocusRow(cat);
    }

    // ================= Focus helpers =================

    /// <summary>What should get focus after an item or category disappears: the next one, else the previous, else the category.</summary>
    static Observable? NeighborAfterRemoval(Observable element)
    {
        switch (element)
        {
            case TodoItem item when Store.Locate(item) is { } loc:
                var items = loc.category.Items;
                int i = items.IndexOf(item);
                if (i + 1 < items.Count) return items[i + 1];
                if (i > 0) return items[i - 1];
                return loc.category;
            case Category cat when Store.BoardOf(cat) is { } board:
                var cats = board.Categories;
                int c = cats.IndexOf(cat);
                if (c + 1 < cats.Count) return cats[c + 1];
                if (c > 0) return cats[c - 1];
                return null;
        }
        return null;
    }

    bool IsRowFocused(object tag) => Keyboard.FocusedElement is FrameworkElement fe && ReferenceEquals(fe.Tag, tag)
                                     && fe is AccessibleRow && IsAncestorOf(fe);

    /// <summary>Focuses the row of an item or category once it's on screen.</summary>
    void FocusRow(object tag) => Dispatcher.InvokeAsync(() =>
    {
        if (FindTagged(this, tag) is { } row)
        {
            row.BringIntoView();
            row.Focus();
        }
    }, DispatcherPriority.Loaded);

    void FocusRowOrContent(object? tag)
    {
        if (tag != null) FocusRow(tag);
        else Dispatcher.InvokeAsync(FocusContent, DispatcherPriority.Loaded);
    }

    /// <summary>Puts keyboard focus on the first thing in the widget (used when the tray icon or a second start brings it forward).</summary>
    public void FocusContent()
    {
        var first = FindFirstRow(this);
        if (first != null) first.Focus();
        else MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    static AccessibleRow? FindFirstRow(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is AccessibleRow { IsVisible: true } row) return row;
            if (FindFirstRow(child) is { } found) return found;
        }
        return null;
    }

    // ================= Moving and resizing the widget =================

    /// <summary>Ctrl+Arrows move the widget, Ctrl+Shift+Arrows resize it. Returns true if handled.</summary>
    bool HandleWindowKeys(KeyEventArgs e)
    {
        var mods = InputState.Modifiers();
        if (mods != ModifierKeys.Control && mods != (ModifierKeys.Control | ModifierKeys.Shift)) return false;
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;
        if (!ShortcutsAllowed()) return false;
        if (Board.Locked)
        {
            A11y.Announce(this, "The window is locked. Unlock it to move or resize.");
            return true;
        }

        double dx = e.Key == Key.Left ? -KeyboardStep : e.Key == Key.Right ? KeyboardStep : 0;
        double dy = e.Key == Key.Up ? -KeyboardStep : e.Key == Key.Down ? KeyboardStep : 0;
        if (mods == ModifierKeys.Control)
        {
            Left += dx;
            Top += dy;
        }
        else
        {
            Width = Math.Max(MinWidth, Width + dx);
            Height = Math.Max(MinHeight, Height + dy);
        }
        SaveBounds();
        return true;
    }
}
