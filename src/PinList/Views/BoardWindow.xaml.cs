using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace PinList;

/// <summary>
/// One desktop widget = one Board. The code is split over three files:
/// this one (position, keyboard, items, text editing), BoardWindow.Menus.cs and BoardWindow.DragDrop.cs.
/// </summary>
public partial class BoardWindow : Window
{
    static int _fallbackIndex;

    // Active element (last clicked or added) – the Delete key acts on it
    static Observable? _active;

    public Board Board { get; }
    public bool UserDeleted { get; private set; }

    bool _loaded, _suppressSave;

    public BoardWindow(Board board)
    {
        InitializeComponent();
        Board = board;
        DataContext = board;

        WindowStartupLocation = WindowStartupLocation.Manual;
        _suppressSave = true;
        Width = Math.Max(MinWidth, board.Width);
        Height = Math.Max(MinHeight, board.Height);
        _suppressSave = false;
        EnsureOnScreen();

        Native.PinToDesktop(this);
        ApplyLock();
        ApplyAppearance();
        A11y.ApplyTextScale(this);

        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        // clicking outside the widget (desktop, another app, another widget) clears the highlight
        Deactivated += (_, _) => { if (_active != null && BelongsToThisBoard(_active)) SetActive(null); };
        Loaded += (_, _) => _loaded = true;
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
    }

    /// <summary>Closes the window without deleting its data (all data is being replaced by an import).</summary>
    public void CloseForReload()
    {
        UserDeleted = true; // don't reopen it
        Close();
    }

    // ================= Position, size, lock =================

    /// <summary>
    /// Moves the widget to its saved position if that is visible; otherwise (e.g. a monitor was
    /// disconnected) temporarily places it on the primary screen WITHOUT overwriting the saved position.
    /// </summary>
    public void EnsureOnScreen()
    {
        var saved = new Rect(Board.X, Board.Y, Width, Height);
        _suppressSave = true;
        try
        {
            if (Screens.IsReachable(saved, Screens.WorkAreas()))
            {
                Left = saved.X;
                Top = saved.Y;
            }
            else
            {
                var wa = Screens.Primary;
                int k = _fallbackIndex++ % 5;
                Left = wa.Right - Width - 24 - 30 * k;
                Top = wa.Top + 24 + 30 * k;
            }
        }
        finally { _suppressSave = false; }
    }

    /// <summary>Work area of the monitor this widget is on.</summary>
    Rect CurrentWorkArea() => Screens.WorkAreaAt(new Point(Left + ActualWidth / 2, Top + 15));

    void SaveBounds()
    {
        if (!_loaded || _suppressSave || WindowState != WindowState.Normal) return;
        Board.X = Math.Round(Left);
        Board.Y = Math.Round(Top);
        Board.Width = Math.Round(ActualWidth);
        Board.Height = Math.Round(ActualHeight);
        Store.MarkDirty();
    }

    void ApplyLock()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
            ResizeBorderThickness = Board.Locked ? new Thickness(0) : new Thickness(6),
        });
        LockBtn.Content = Board.Locked ? "" : "";
        LockBtn.ToolTip = Board.Locked ? "Unlock position and size" : "Lock position and size";
        System.Windows.Automation.AutomationProperties.SetName(LockBtn, LockBtn.ToolTip.ToString());
    }

    void Lock_Click(object sender, RoutedEventArgs e)
    {
        Board.Locked = !Board.Locked;
        ApplyLock();
        Store.MarkDirty();
        A11y.Announce(this, Board.Locked ? "Position and size locked" : "Position and size unlocked");
    }

    /// <summary>Re-applies colors and text size (after a theme, contrast or text size change in Windows).</summary>
    public void RefreshAppearance()
    {
        ApplyAppearance();
        A11y.ApplyTextScale(this);
    }

    BoardStyle CurrentStyle => Themes.Get(Board.Style);

    void ApplyAppearance() => Themes.ApplyBoard(Resources, CurrentStyle, Board.Transparent);

    void SetStyle(string key)
    {
        Board.Style = key;
        ApplyAppearance();
        Store.MarkDirty();
    }

    void ToggleTransparency()
    {
        Board.Transparent = !Board.Transparent;
        ApplyAppearance();
        Store.MarkDirty();
    }

    // ================= Active element and the Delete key =================

    static void SetActive(Observable? element)
    {
        if (ReferenceEquals(_active, element)) return;
        switch (_active)
        {
            case TodoItem i: i.IsActive = false; break;
            case Category c: c.IsActive = false; break;
        }
        _active = element;
        switch (element)
        {
            case TodoItem i: i.IsActive = true; break;
            case Category c: c.IsActive = true; break;
        }
    }

    /// <summary>The highlighted element is always an item or a category.</summary>
    bool BelongsToThisBoard(Observable element) =>
        element is TodoItem i ? Store.Locate(i)?.board == Board : Board.Categories.Contains((Category)element);

    /// <summary>Highlights an item and scrolls it into view (after clicking a reminder notification).</summary>
    public static void Highlight(TodoItem item)
    {
        var loc = Store.Locate(item);
        if (loc is null) return;
        var (board, cat) = loc.Value;
        var w = Application.Current.Windows.OfType<BoardWindow>().FirstOrDefault(x => x.Board == board);
        if (w == null) return;

        if (cat.Collapsed) { cat.Collapsed = false; Store.MarkDirty(); }
        w.Activate();
        SetActive(item);
        w.FocusRow(item); // also scrolls it into view
    }

    /// <summary>The focusable row (item or category header) of a data object.</summary>
    static AccessibleRow? FindTagged(DependencyObject root, object tag)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is AccessibleRow row && ReferenceEquals(row.Tag, tag)) return row;
            var found = FindTagged(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Clicking empty space also clears the highlight (clicking an item/category sets it again right away).</summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        SetActive(null);
        // The highlight follows keyboard focus, so focus leaves the row too; otherwise Windows gives it back
        // after dragging the widget by its header, and the highlight would come back with it.
        if (Keyboard.FocusedElement is AccessibleRow row && IsAncestorOf(row))
        {
            FocusManager.SetFocusedElement(this, null); // otherwise focusing the window restores the row
            Keyboard.ClearFocus();
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    /// <summary>Deletes the active element without archiving it (Ctrl+Z brings it back).</summary>
    void DeleteActive()
    {
        DeleteWithFocus(_active!); // only called while something is highlighted
    }

    /// <summary>Shortcuts work when no text is being typed (an empty "Add item" box is fine).</summary>
    static bool ShortcutsAllowed() =>
        Keyboard.FocusedElement is not TextBox tb || (tb.Name == "AddBox" && tb.Text.Length == 0);

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Board.IsEditingTitle = true;
            return;
        }
        if (!Board.Locked)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 must not close a widget; deleting goes through the menu
        if (!UserDeleted && !App.ShuttingDown) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Z && InputState.Modifiers() == ModifierKeys.Control && ShortcutsAllowed())
        {
            A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
            e.Handled = true;
        }
        else if (HandleWindowKeys(e))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && InputState.Modifiers() == ModifierKeys.None && ShortcutsAllowed() && _active != null)
        {
            DeleteActive();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox)
        {
            SetActive(null);
        }
        base.OnPreviewKeyDown(e);
    }

    // ================= Categories and the window =================

    void AddCategory_Click(object sender, RoutedEventArgs e) => AddCategory();

    void AddCategory()
    {
        var cat = Actions.AddCategory(Board);
        SetActive(cat);
        cat.IsEditing = true; // type the name right away
    }

    void Collapse_Click(object sender, RoutedEventArgs e)
    {
        ToggleCollapsed((Category)((FrameworkElement)sender).Tag); // the button of a category header
    }

    void DeleteThisBoard()
    {
        int items = 0;
        foreach (var c in Board.Categories) items += c.Items.Count;
        string msg = items > 0
            ? $"This removes the window with {Board.Categories.Count} categories and {items} items. Items are not moved to the archive."
            : "This removes the window from your desktop.";
        string title = string.IsNullOrWhiteSpace(Board.Title) ? "Delete this window?" : $"Delete \"{Board.Title}\"?";
        if (!Dialogs.Ask(this, title, msg, "Delete"))
            return;

        Actions.DeleteBoard(Board);
        UserDeleted = true;
        Close();
    }

    // ================= Items =================

    /// <summary>Clicking the circle completes after ~1 s; a second click in that time cancels.</summary>
    void Check_Click(object sender, RoutedEventArgs e)
    {
        ToggleCompleting((TodoItem)((FrameworkElement)sender).Tag); // the circle of an item
    }

    void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        if (e.Key == Key.Enter && !IsShiftDown()) // Shift+Enter = new line
        {
            var text = Normalize(tb.Text);
            if (text.Length > 0 && tb.Tag is Category cat)
            {
                SetActive(Actions.AddItem(cat, text)); // last added = active (Delete removes it)
                A11y.Announce(this, $"Added {text.Split('\n')[0]}");
            }
            tb.Clear(); // focus stays, so the next item can be typed right away
            e.Handled = true;
        }
        else if ((e.Key == Key.Up || e.Key == Key.Down) && tb.Text.Length == 0 && InputState.Modifiers() == ModifierKeys.None)
        {
            // an empty box lets Up/Down move between rows, like everywhere else in the list
            tb.MoveFocus(new TraversalRequest(e.Key == Key.Up ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tb.Clear();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // ================= Text editing (item, category, title) =================

    void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.IsVisible) BeginEdit(tb);
    }

    /// <summary>
    /// A box that is created already in edit mode (the name of a just added category) never reports becoming
    /// visible, so editing also starts when it loads.
    /// </summary>
    void EditBox_Loaded(object sender, RoutedEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.DataContext is TodoItem { IsEditing: true } or Category { IsEditing: true } or Board { IsEditingTitle: true })
            BeginEdit(tb);
    }

    /// <summary>Fills the edit box with the current text and puts the caret in it.</summary>
    void BeginEdit(TextBox tb)
    {
        // every edit box belongs to an item, a category or a widget title
        tb.Text = tb.DataContext is TodoItem i ? i.Text : tb.DataContext is Category c ? c.Name : ((Board)tb.DataContext).Title;
        Dispatcher.InvokeAsync(() =>
        {
            Activate();
            tb.Focus();
            Keyboard.Focus(tb);
            // Caret at the end; only the default name of a new category is selected so it can be typed over
            if (tb.DataContext is Category { Name: "New category" }) tb.SelectAll();
            else tb.CaretIndex = tb.Text.Length;
        }, DispatcherPriority.Input);
    }

    void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        if (e.Key == Key.Enter && !IsShiftDown()) { CommitEdit(tb); FocusAfterEdit(tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(tb); FocusAfterEdit(tb); e.Handled = true; }
    }

    /// <summary>After Enter/Esc, keyboard focus goes back to the edited row instead of getting lost.</summary>
    void FocusAfterEdit(TextBox tb)
    {
        switch (tb.DataContext)
        {
            case TodoItem i when Store.Locate(i) != null: FocusRow(i); break;
            case Category c when Store.BoardOf(c) != null: FocusRow(c); break;
            default: Dispatcher.InvokeAsync(FocusContent, DispatcherPriority.Loaded); break;
        }
    }

    void EditBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitEdit((TextBox)sender);

    static bool IsShiftDown() => InputState.ShiftHeld;

    /// <summary>Unified line breaks, no blank lines at the start/end; indentation inside the text is kept.</summary>
    static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    static void CommitEdit(TextBox tb)
    {
        var text = Normalize(tb.Text);
        switch (tb.DataContext)
        {
            case TodoItem i when i.IsEditing:
                i.IsEditing = false;
                if (text.Length == 0) Actions.Delete(i);
                else Actions.SetItemText(i, text);
                break;
            case Category c when c.IsEditing:
                c.IsEditing = false;
                if (text.Length > 0) Actions.RenameCategory(c, text);
                break;
            case Board b when b.IsEditingTitle:
                b.IsEditingTitle = false;
                Actions.RenameBoard(b, text); // the title is optional: empty text = widget without a title
                break;
        }
    }

    static void CancelEdit(TextBox tb)
    {
        switch (tb.DataContext)
        {
            case TodoItem i: i.IsEditing = false; break;
            case Category c: c.IsEditing = false; break;
            case Board b: b.IsEditingTitle = false; break;
        }
    }
}
