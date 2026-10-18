using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace PinList;

/// <summary>One desktop widget = one Board.</summary>
public partial class BoardWindow : Window
{
    const string DragFormat = "PinList.Drag";

    // Drag and drop also works between widgets because they all live in the same process
    static object? _dragPayload;
    static Border? _indicator;
    static Thickness _indicatorPadding;
    static int _fallbackIndex;

    // Auto-scroll while dragging near the top/bottom edge of the list
    const double AutoScrollZone = 40;     // px from the edge where scrolling starts
    const double AutoScrollMaxStep = 12;  // px per step (~60 steps per second)
    static DispatcherTimer? _autoScrollTimer;

    // Active element (last clicked or added) – the Delete key acts on it
    static Observable? _active;

    public Board Board { get; }
    public bool UserDeleted { get; private set; }

    bool _loaded, _suppressSave;
    Point _dragStart;
    object? _dragCandidate;

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

        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        // clicking outside the widget (desktop, another app, another widget) clears the highlight
        Deactivated += (_, _) => { if (_active != null && BelongsToThisBoard(_active)) SetActive(null); };
        Loaded += (_, _) => _loaded = true;
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
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
            if (IsReachable(saved))
            {
                Left = saved.X;
                Top = saved.Y;
            }
            else
            {
                var wa = SystemParameters.WorkArea;
                int k = _fallbackIndex++ % 5;
                Left = wa.Right - Width - 24 - 30 * k;
                Top = wa.Top + 24 + 30 * k;
            }
        }
        finally { _suppressSave = false; }
    }

    static bool IsReachable(Rect r)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var header = new Rect(r.X, r.Y, Math.Max(1, r.Width), 30);
        var visible = Rect.Intersect(screen, header);
        return !visible.IsEmpty && visible.Width >= 60 && visible.Height >= 20;
    }

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
    }

    void Lock_Click(object sender, RoutedEventArgs e)
    {
        Board.Locked = !Board.Locked;
        ApplyLock();
        Store.MarkDirty();
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

    bool BelongsToThisBoard(Observable element) => element switch
    {
        TodoItem i => Store.Locate(i)?.board == Board,
        Category c => Board.Categories.Contains(c),
        _ => false,
    };

    /// <summary>Clicking empty space also clears the highlight (clicking an item/category sets it again right away).</summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        SetActive(null);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    /// <summary>Deletes the active element without archiving it (Ctrl+Z brings it back).</summary>
    static void DeleteActive()
    {
        switch (_active)
        {
            case TodoItem item: Actions.Delete(item); break;
            case Category cat: Actions.DeleteCategory(cat); break;
        }
        SetActive(null);
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
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && ShortcutsAllowed())
        {
            Actions.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && ShortcutsAllowed() && _active != null)
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

    // ================= Menus =================

    static MenuItem Item(string header, Action action, string? gesture = null) =>
        App.CreateMenuItem(header, action, gesture);

    /// <summary>Menus are always dark (colors come from the application resources).</summary>
    static ContextMenu NewMenu(UIElement target, PlacementMode placement) =>
        new() { PlacementTarget = target, Placement = placement };

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu(MenuBtn, PlacementMode.Bottom);
        var colors = new MenuItem { Header = "Window color" };
        foreach (var style in Themes.BoardStyles)
        {
            var mi = Item(style.Name, () => SetStyle(style.Key));
            mi.Icon = Swatch(style.Background, true);
            mi.IsChecked = style.Key == CurrentStyle.Key;
            colors.Items.Add(mi);
        }
        menu.Items.Add(colors);

        var transparent = Item("Transparent background", ToggleTransparency);
        transparent.IsChecked = Board.Transparent;
        menu.Items.Add(transparent);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("New window", () => Actions.NewBoard(Board)));
        menu.Items.Add(Item("Archive", App.Instance.ShowArchive));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete window", DeleteThisBoard));
        menu.IsOpen = true;
    }

    static Ellipse Swatch(string hex, bool outlined) => new()
    {
        Width = 12,
        Height = 12,
        Fill = Themes.Brush(hex),
        Stroke = outlined ? Themes.Brush("#40808080") : null,
        StrokeThickness = outlined ? 1 : 0,
    };

    void Cat_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Category cat) return;
        SetActive(cat);

        var menu = NewMenu((UIElement)sender, PlacementMode.MousePoint);
        menu.Items.Add(Item("Rename", () => cat.IsEditing = true, "Double-click"));

        var colors = new MenuItem { Header = "Color" };
        for (int i = 0; i < Actions.Palette.Length; i++)
        {
            string hex = Actions.Palette[i];
            var mi = Item(Actions.PaletteNames[i], () => { cat.Color = hex; Store.MarkDirty(); });
            mi.Icon = Swatch(hex, false);
            mi.IsChecked = string.Equals(cat.Color, hex, StringComparison.OrdinalIgnoreCase);
            colors.Items.Add(mi);
        }
        menu.Items.Add(colors);
        menu.Items.Add(Item(cat.Collapsed ? "Expand" : "Collapse", () => { cat.Collapsed = !cat.Collapsed; Store.MarkDirty(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete category", () => { Actions.DeleteCategory(cat); SetActive(null); }, "Del"));
        menu.IsOpen = true;
        e.Handled = true;
    }

    void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not TodoItem item) return;
        SetActive(item);

        var menu = NewMenu((UIElement)sender, PlacementMode.MousePoint);
        menu.Items.Add(Item("Edit", () => item.IsEditing = true, "Double-click"));
        menu.Items.Add(Item("Complete", () => Actions.Complete(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete", () => { Actions.Delete(item); SetActive(null); }, "Del"));
        menu.IsOpen = true;
        e.Handled = true;
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
        if (((FrameworkElement)sender).Tag is Category cat)
        {
            cat.Collapsed = !cat.Collapsed;
            Store.MarkDirty();
        }
    }

    void DeleteThisBoard()
    {
        int items = 0;
        foreach (var c in Board.Categories) items += c.Items.Count;
        string msg = items > 0
            ? $"This removes the window with {Board.Categories.Count} categories and {items} items. Items are not moved to the archive."
            : "This removes the window from your desktop.";
        string title = string.IsNullOrWhiteSpace(Board.Title) ? "Delete this window?" : $"Delete \"{Board.Title}\"?";
        if (!ConfirmDialog.Ask(this, title, msg, "Delete"))
            return;

        Actions.DeleteBoard(Board);
        UserDeleted = true;
        Close();
    }

    // ================= Items =================

    void Check_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not TodoItem item) return;

        if (item.IsCompleting) // second click during the animation = cancel
        {
            item.IsCompleting = false;
            return;
        }

        item.IsCompleting = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (item.IsCompleting) Actions.Complete(item);
        };
        timer.Start();
    }

    void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        if (e.Key == Key.Enter && !IsShiftDown()) // Shift+Enter = new line
        {
            var text = Normalize(tb.Text);
            if (text.Length > 0 && tb.Tag is Category cat)
                SetActive(Actions.AddItem(cat, text)); // last added = active (Delete removes it)
            tb.Clear(); // focus stays, so the next item can be typed right away
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
        if (sender is not TextBox tb || !tb.IsVisible) return;

        tb.Text = tb.DataContext switch
        {
            TodoItem i => i.Text,
            Category c => c.Name,
            Board b => b.Title,
            _ => "",
        };
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
        if (e.Key == Key.Enter && !IsShiftDown()) { CommitEdit(tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(tb); e.Handled = true; }
    }

    void EditBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitEdit((TextBox)sender);

    static bool IsShiftDown() => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

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
                else if (i.Text != text) { i.Text = text; Store.MarkDirty(); }
                break;
            case Category c when c.IsEditing:
                c.IsEditing = false;
                if (text.Length > 0 && c.Name != text) { c.Name = text; Store.MarkDirty(); }
                break;
            case Board b when b.IsEditingTitle:
                b.IsEditingTitle = false;
                // the title is optional: empty text = widget without a title
                if (b.Title != text) { b.Title = text; Store.MarkDirty(); }
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

    // ================= Drag & drop =================

    void Drag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        SetActive(el.Tag as Observable);
        if (IsInteractive(e.OriginalSource as DependencyObject, el))
        {
            _dragCandidate = null;
            return;
        }

        // move focus out of text boxes so Delete acts on the clicked element
        if (Keyboard.FocusedElement is TextBox) Keyboard.Focus(this);

        if (e.ClickCount == 2) // double-click = edit
        {
            _dragCandidate = null;
            if (el.Tag is TodoItem item) item.IsEditing = true;
            else if (el.Tag is Category cat) cat.IsEditing = true;
            e.Handled = true;
            return;
        }

        _dragCandidate = el.Tag;
        _dragStart = e.GetPosition(this);
    }

    void Drag_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var el = (FrameworkElement)sender;
        _dragPayload = _dragCandidate;
        _dragCandidate = null;
        el.Opacity = 0.4;
        StartAutoScroll();
        try
        {
            DragDrop.DoDragDrop(el, new DataObject(DragFormat, "1"), DragDropEffects.Move);
        }
        finally
        {
            StopAutoScroll();
            el.Opacity = 1;
            _dragPayload = null;
            ClearIndicator();
        }
    }

    // ---- Auto-scroll while dragging ----
    // The timer runs for the whole drag and reads the cursor position itself, so scrolling continues
    // while the mouse rests at the edge, and also works in another widget if the element is dragged there.

    static void StartAutoScroll()
    {
        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScrollTimer.Tick += AutoScrollTick;
        }
        _autoScrollTimer.Start();
    }

    static void StopAutoScroll() => _autoScrollTimer?.Stop();

    static void AutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragPayload == null) { StopAutoScroll(); return; }
        var cursor = Native.CursorScreenPosition();
        foreach (var w in Application.Current.Windows.OfType<BoardWindow>())
            w.AutoScrollAt(cursor);
    }

    void AutoScrollAt(Point screen)
    {
        if (!IsVisible || Scroller.ScrollableHeight <= 0) return;

        Point inWindow, inList;
        try
        {
            inWindow = PointFromScreen(screen);
            inList = Scroller.PointFromScreen(screen);
        }
        catch (InvalidOperationException) { return; }

        // the cursor must be inside this widget
        if (inWindow.X < 0 || inWindow.Y < 0 || inWindow.X > ActualWidth || inWindow.Y > ActualHeight) return;

        double height = Scroller.ActualHeight;
        double step = 0;
        if (inList.Y < AutoScrollZone)                       // top edge (including the header above the list)
            step = -Step(AutoScrollZone - inList.Y);
        else if (inList.Y > height - AutoScrollZone)         // bottom edge
            step = Step(inList.Y - (height - AutoScrollZone));

        if (step != 0)
            Scroller.ScrollToVerticalOffset(Math.Clamp(Scroller.VerticalOffset + step, 0, Scroller.ScrollableHeight));
    }

    /// <summary>The closer to the edge, the faster (2 to AutoScrollMaxStep px per step).</summary>
    static double Step(double depth) =>
        Math.Max(2, AutoScrollMaxStep * Math.Min(1, depth / AutoScrollZone));

    /// <summary>Clicking a button or a TextBox must not start a drag.</summary>
    static bool IsInteractive(DependencyObject? d, DependencyObject stop)
    {
        while (d != null && d != stop)
        {
            if (d is ButtonBase || d is TextBoxBase) return true;
            d = d is Visual || d is Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    static bool IsLowerHalf(DragEventArgs e, FrameworkElement el) =>
        e.GetPosition(el).Y > el.ActualHeight / 2;

    static void ShowIndicator(Border b, bool below)
    {
        if (_indicator != b)
        {
            ClearIndicator();
            _indicator = b;
            _indicatorPadding = b.Padding;
        }
        var p = _indicatorPadding;
        // the line thickness is taken from the padding so the height doesn't change (no flicker)
        if (below)
        {
            b.BorderThickness = new Thickness(0, 0, 0, 2);
            b.Padding = new Thickness(p.Left, p.Top, p.Right, Math.Max(0, p.Bottom - 2));
        }
        else
        {
            b.BorderThickness = new Thickness(0, 2, 0, 0);
            b.Padding = new Thickness(p.Left, Math.Max(0, p.Top - 2), p.Right, p.Bottom);
        }
    }

    static void ClearIndicator()
    {
        if (_indicator == null) return;
        _indicator.BorderThickness = new Thickness(0);
        _indicator.Padding = _indicatorPadding;
        _indicator = null;
    }

    static void Deny(DragEventArgs e)
    {
        ClearIndicator();
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    void Window_DragOver(object sender, DragEventArgs e) => Deny(e);
    void Window_Drop(object sender, DragEventArgs e) => ClearIndicator();

    // --- over an item ---
    void Item_DragOver(object sender, DragEventArgs e)
    {
        var row = (Border)sender;
        if (_dragPayload is TodoItem dragged && dragged != row.Tag)
        {
            ShowIndicator(row, IsLowerHalf(e, row));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else Deny(e);
    }

    void Item_Drop(object sender, DragEventArgs e)
    {
        var row = (Border)sender;
        bool below = IsLowerHalf(e, row);
        ClearIndicator();
        e.Handled = true;

        if (_dragPayload is not TodoItem dragged || row.Tag is not TodoItem target || dragged == target) return;
        var loc = Store.Locate(target);
        if (loc is null) return;
        var cat = loc.Value.category;
        Actions.MoveItem(dragged, cat, cat.Items.IndexOf(target) + (below ? 1 : 0));
    }

    // --- over a category header ---
    void Cat_DragOver(object sender, DragEventArgs e)
    {
        var header = (Border)sender;
        if (_dragPayload is Category dragged && dragged != header.Tag)
        {
            ShowIndicator(header, IsLowerHalf(e, header));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (_dragPayload is TodoItem)
        {
            ShowIndicator(header, true); // the item goes to the top of the category
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else Deny(e);
    }

    void Cat_Drop(object sender, DragEventArgs e)
    {
        var header = (Border)sender;
        bool below = IsLowerHalf(e, header);
        ClearIndicator();
        e.Handled = true;
        if (header.Tag is not Category target) return;

        if (_dragPayload is Category dragged && dragged != target)
        {
            var board = Store.BoardOf(target);
            if (board != null)
                Actions.MoveCategory(dragged, board, board.Categories.IndexOf(target) + (below ? 1 : 0));
        }
        else if (_dragPayload is TodoItem item)
        {
            Actions.MoveItem(item, target, 0);
        }
    }

    // --- over the "Add item" box = end of the category ---
    void AddRow_DragOver(object sender, DragEventArgs e)
    {
        if (_dragPayload is TodoItem)
        {
            ShowIndicator((Border)sender, false);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else Deny(e);
    }

    void AddRow_Drop(object sender, DragEventArgs e)
    {
        ClearIndicator();
        e.Handled = true;
        if (_dragPayload is TodoItem item && ((FrameworkElement)sender).Tag is Category cat)
            Actions.MoveItem(item, cat, cat.Items.Count);
    }

    // --- empty space at the bottom of the widget ---
    void End_DragOver(object sender, DragEventArgs e)
    {
        if (_dragPayload is Category)
        {
            ShowIndicator((Border)sender, false);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else Deny(e);
    }

    void End_Drop(object sender, DragEventArgs e)
    {
        ClearIndicator();
        e.Handled = true;
        if (_dragPayload is Category cat)
            Actions.MoveCategory(cat, Board, Board.Categories.Count);
    }
}
