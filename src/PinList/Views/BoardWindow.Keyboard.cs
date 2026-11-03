using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

    void Row_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && ((Control)sender!).Tag is Observable o)
            SetActive(o);
    }

    static bool IsMenuKey(KeyEventArgs e) =>
        e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift);

    /// <summary>Alt+Up / Alt+Down. Returns -1, +1 or 0.</summary>
    static int AltArrow(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt) return 0;
        return e.Key switch { Key.Up => -1, Key.Down => +1, _ => 0 };
    }

    void Item_KeyDown(object? sender, KeyEventArgs e)
    {
        var row = (Control)sender!;
        if (!ReferenceEquals(e.Source, row) || row.Tag is not TodoItem item) return;

        if (IsMenuKey(e)) ShowItemMenu(row, item, PlacementMode.Bottom);
        else if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None) ToggleCompleting(item);
        else if ((e.Key == Key.Enter || e.Key == Key.F2) && e.KeyModifiers == KeyModifiers.None) item.IsEditing = true;
        else if (AltArrow(e) is int d and not 0) MoveItemBy(item, d);
        else if (!MoveFocusUpDown(row, e)) return;
        e.Handled = true;
    }

    void Cat_KeyDown(object? sender, KeyEventArgs e)
    {
        var row = (Control)sender!;
        if (!ReferenceEquals(e.Source, row) || row.Tag is not Category cat) return;

        if (IsMenuKey(e)) ShowCategoryMenu(row, cat, PlacementMode.Bottom);
        else if ((e.Key == Key.Enter || e.Key == Key.Space) && e.KeyModifiers == KeyModifiers.None) ToggleCollapsed(cat);
        else if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None) cat.IsEditing = true;
        else if (AltArrow(e) is int d and not 0) MoveCategoryBy(cat, d);
        else if (!MoveFocusUpDown(row, e)) return;
        e.Handled = true;
    }

    /// <summary>Up/Down move focus like Shift+Tab/Tab.</summary>
    bool MoveFocusUpDown(Control from, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return false;
        NavigationDirection? direction = e.Key switch
        {
            Key.Up => NavigationDirection.Previous,
            Key.Down => NavigationDirection.Next,
            _ => null,
        };
        if (direction == null) return false;
        MoveFocus(from, direction.Value);
        return true;
    }

    /// <summary>
    /// Everything the keyboard can reach in the widget, in the order it appears: the header buttons, then every
    /// category header, its items and its "Add item" box. (Computed here because Avalonia's own Tab order doesn't
    /// continue from one category into the next.)
    /// </summary>
    internal List<InputElement> FocusOrder() =>
        this.GetVisualDescendants().OfType<InputElement>()
            .Where(e => e.Focusable && KeyboardNavigation.GetIsTabStop(e) && e.IsEffectivelyVisible && e.IsEffectivelyEnabled)
            .ToList();

    /// <summary>Moves focus to the next or previous element (wrapping around, like Tab in a window).</summary>
    void MoveFocus(InputElement from, NavigationDirection direction)
    {
        var order = FocusOrder();
        if (order.Count == 0) return;
        int i = order.IndexOf(from);
        int next = direction == NavigationDirection.Previous
            ? (i <= 0 ? order.Count - 1 : i - 1)
            : (i < 0 || i == order.Count - 1 ? 0 : i + 1);
        order[next].Focus(direction == NavigationDirection.Next || direction == NavigationDirection.Previous ? NavigationMethod.Tab : NavigationMethod.Directional);
    }

    // ================= Actions with announcements =================

    void ToggleCollapsed(Category cat)
    {
        cat.Collapsed = !cat.Collapsed;
        Store.MarkDirty();
        A11y.Announce(this, cat.Collapsed ? $"{cat.Name} collapsed" : $"{cat.Name} expanded");
        FocusRow(cat);
    }

    CompleteFlow? _completeFlow;

    /// <summary>The steps of completing: crossed out, sliding away, archived (see CompleteFlow).</summary>
    CompleteFlow Flow => _completeFlow ??= new CompleteFlow(CompleteNow);

    /// <summary>Starts completing, or cancels if the item is already completing. Same as clicking the circle.</summary>
    void ToggleCompleting(TodoItem item)
    {
        if (Flow.Toggle(item) == CompleteResult.Cancelled) A11y.Announce(this, "Cancelled");
    }

    /// <summary>Archives the item once its row has left (also at once when the delay is zero).</summary>
    void CompleteNow(TodoItem item)
    {
        bool hadFocus = IsRowFocused(item);
        var next = NeighborAfterRemoval(item);
        var category = Store.Locate(item)?.category;
        Actions.Complete(item);
        A11y.Announce(this, $"Completed {item.Text.Split('\n')[0]}. {OsText.Undo} undoes.");
        ShowUndoPill(item.Text);
        if (category != null) Bump(category);
        if (hadFocus) FocusRowOrContent(next);
    }

    /// <summary>The category's count does a small bump when an item leaves it.</summary>
    void Bump(Category category)
    {
        if (FindTagged(category) is not { } header) return;
        if (header.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("countpill")) is not { } pill) return;
        pill.Classes.Add("bump");
        DispatcherTimer.RunOnce(() => pill.Classes.Remove("bump"), TimeSpan.FromMilliseconds(300));
    }

    // ================= Undo pill =================

    /// <summary>How long the Undo pill stays after an item was completed; tests shorten it.</summary>
    internal static TimeSpan UndoPillDuration { get; set; } = TimeSpan.FromSeconds(4);

    DispatcherTimer? _pillTimer;

    /// <summary>The undo history as it was right after the completion the pill offers to undo.</summary>
    int _pillUndoVersion;

    /// <summary>Shows "Completed ..." with an Undo button at the bottom of the widget for a few seconds.</summary>
    internal void ShowUndoPill(string itemText)
    {
        UndoText.Text = $"Completed {Quote(itemText)}";
        UndoPill.IsVisible = true;
        _pillUndoVersion = Actions.UndoVersion;
        if (_pillTimer == null)
        {
            _pillTimer = new DispatcherTimer();
            _pillTimer.Tick += (_, _) => HideUndoPill();
        }
        _pillTimer.Stop();
        _pillTimer.Interval = UndoPillDuration;
        _pillTimer.Start();
    }

    /// <summary>Any other change or undo, also in another widget: the pill's Undo would undo something else now.</summary>
    void OnUndoChanged()
    {
        // the history can change off this window's thread (background work, tests): check the pill on its own thread
        if (!CheckAccess())
        {
            this.Dispatcher.Post(OnUndoChanged);
            return;
        }
        if (UndoPill.IsVisible && Actions.UndoVersion != _pillUndoVersion) HideUndoPill();
    }

    void HideUndoPill()
    {
        _pillTimer?.Stop();
        UndoPill.IsVisible = false;
    }

    void Undo_Click(object? sender, RoutedEventArgs e) => UndoLast();

    /// <summary>Ctrl+Z and the Undo button.</summary>
    void UndoLast()
    {
        HideUndoPill();
        A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
    }

    /// <summary>
    /// Deletes an item or category after asking (spec 4.5) and moves focus to its neighbor, so keyboard users stay
    /// in the list.
    /// </summary>
    async void DeleteWithFocus(Observable element)
    {
        if (!await Dialogs.Ask(this, DeleteQuestion(element), DeleteDetails(element), "Delete")) return;
        var next = NeighborAfterRemoval(element);
        switch (element)
        {
            case TodoItem item:
                Actions.Delete(item);
                A11y.Announce(this, $"Deleted {item.Text.Split('\n')[0]}. {OsText.Undo} undoes.");
                break;
            case Category cat:
                Actions.DeleteCategory(cat);
                A11y.Announce(this, $"Deleted category {cat.Name}. {OsText.Undo} undoes.");
                break;
        }
        SetActive(null);
        FocusRowOrContent(next);
    }

    static string DeleteQuestion(Observable element) => element is TodoItem item
        ? $"Delete {Quote(item.Text)}?"
        : $"Delete category {Quote(((Category)element).Name)}?";

    static string DeleteDetails(Observable element) => element switch
    {
        Category { Items.Count: 1 } => $"Its 1 item is deleted too. {OsText.Undo} brings it back.",
        Category { Items.Count: > 1 } c => $"Its {c.Items.Count} items are deleted too. {OsText.Undo} brings them back.",
        _ => $"{OsText.Undo} brings it back.",
    };

    void MoveItemBy(TodoItem item, int delta)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var (board, cat) = loc.Value;
        int index = cat.Items.IndexOf(item);
        int catIndex = board.Categories.IndexOf(cat);

        var before = Reorder.Capture(this);
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
        Reorder.Animate(this, before);

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
        var before = Reorder.Capture(this);
        Actions.MoveCategory(cat, board, delta < 0 ? target : target + 1);
        Reorder.Animate(this, before);
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

    bool IsRowFocused(object tag) => FocusManager?.GetFocusedElement() is AccessibleRow row
                                     && ReferenceEquals(row.Tag, tag) && this.IsVisualAncestorOf(row);

    /// <summary>Focuses the row of an item or category once it's on screen.</summary>
    void FocusRow(object tag) => Dispatcher.UIThread.Post(() =>
    {
        if (FindTagged(tag) is { } row)
        {
            row.BringIntoView();
            row.Focus(NavigationMethod.Tab);
        }
    }, DispatcherPriority.Loaded);

    void FocusRowOrContent(object? tag)
    {
        if (tag != null) FocusRow(tag);
        else Dispatcher.UIThread.Post(FocusContent, DispatcherPriority.Loaded);
    }

    /// <summary>Puts keyboard focus on the first thing in the widget (used when the tray icon or a second start brings it forward).</summary>
    public void FocusContent()
    {
        var first = this.GetVisualDescendants().OfType<AccessibleRow>().FirstOrDefault(r => r.IsEffectivelyVisible);
        if (first != null) first.Focus(NavigationMethod.Tab);
        else FocusManager?.FindFirstFocusableElement()?.Focus(NavigationMethod.Tab);
    }

    // ================= Moving and resizing the widget =================

    /// <summary>Ctrl+Arrows move the widget, Ctrl+Shift+Arrows resize it (⌘ on a Mac). Returns true if handled.</summary>
    bool HandleWindowKeys(KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        // on a Mac, Ctrl+Arrows switch desktops, so ⌘ does it there
        if (OsText.IsCommand(mods & ~KeyModifiers.Shift)) mods = (mods & KeyModifiers.Shift) | KeyModifiers.Control;
        if (mods != KeyModifiers.Control && mods != (KeyModifiers.Control | KeyModifiers.Shift)) return false;
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;
        if (!ShortcutsAllowed()) return false;
        if (Board.Locked)
        {
            A11y.Announce(this, "The list is locked. Unlock it to move or resize.");
            return true;
        }

        double dx = e.Key == Key.Left ? -KeyboardStep : e.Key == Key.Right ? KeyboardStep : 0;
        double dy = e.Key == Key.Up ? -KeyboardStep : e.Key == Key.Down ? KeyboardStep : 0;
        if (mods == KeyModifiers.Control)
        {
            Position = Displays.ToPixels(Left + dx, Top + dy);
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
