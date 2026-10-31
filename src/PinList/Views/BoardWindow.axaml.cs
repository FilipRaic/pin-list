using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// One desktop widget = one Board. The code is split over four files: this one (position, items, text
/// editing), BoardWindow.Keyboard.cs, BoardWindow.Menus.cs and BoardWindow.DragDrop.cs.
/// </summary>
public partial class BoardWindow : Window
{
    static int _fallbackIndex;

    // Active element (last clicked or added) – the Delete key acts on it
    static Observable? _active;

    public Board Board { get; }
    public bool UserDeleted { get; private set; }

    /// <summary>For the XAML previewer only.</summary>
    public BoardWindow() : this(new Board()) { }

    public BoardWindow(Board board)
    {
        Board = board;
        DataContext = board;
        InitializeComponent();
        Look.GrayscaleText(this);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Displays.Anchor = this; // (macOS asks a window for the screens)
        Closed += (_, _) => { if (Displays.Anchor == this) Displays.Anchor = App.Instance.OpenWindows.FirstOrDefault(w => w != this); };
        EnsureOnScreen();

        ApplyLock();
        ApplyAppearance();
        ApplyTextScale();

        // Not in the taskbar or Alt+Tab, and owned by the desktop so "Show desktop" leaves it alone. Done here
        // rather than with ShowInTaskbar, which manages the window's owner itself (and would undo the desktop owner).
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Native.MakeToolWindow(this);
            Native.PinToDesktop(this);
        }, DispatcherPriority.Background);
#if WINDOWS
        Opened += (_, _) => Win32Properties.AddWndProcHookCallback(this, WndProc);
#endif
        // clicking outside the widget (desktop, another app, another widget) clears the highlight
        Deactivated += (_, _) => { if (_active != null && BelongsToThisBoard(_active)) SetActive(null); };
        PositionChanged += (_, _) => OnMovedWithoutDrag();
        Resized += (_, _) => OnMovedWithoutDrag();
        Closing += OnClosing;

        // Every edit box (item, category name, title) and every "Add item" box
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, EditBox_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, EditBox_LostFocus, RoutingStrategies.Bubble);
        foreach (var grip in Grips.Children.OfType<Border>())
        {
            grip.PointerPressed += Grip_PointerPressed;
            grip.PointerMoved += Grip_PointerMoved;
            grip.PointerReleased += Grip_PointerReleased;
            grip.PointerCaptureLost += Grip_CaptureLost;
        }

        SetupDragDrop();
    }

    /// <summary>Closes the window without deleting its data (all data is being replaced by an import).</summary>
    public void CloseForReload()
    {
        UserDeleted = true; // don't reopen it
        Close();
    }

    // ================= Position, size, lock =================

    // Only moves and resizes made by the user are saved. Windows also moves windows on its own when a monitor
    // goes to sleep, wakes up or is plugged in; saving those would lose the widget's place.

    /// <summary>True while monitors are changing (set by App): moves made then come from Windows, not the user.</summary>
    internal static bool DisplayChanging { get; set; }

    /// <summary>How long a move without a drag (Win+Arrow) waits before it's saved, so a monitor change that caused it can still cancel it.</summary>
    internal static TimeSpan KeyMoveSaveDelay { get; set; } = TimeSpan.FromSeconds(1);

    DispatcherTimer? _keyMoveSave;
    Rect _placedAt; // where EnsureOnScreen put the widget; that is not a move by the user

    /// <summary>Position in widget units (see <see cref="Screens"/>).</summary>
    internal double Left
    {
        get => Displays.FromPixels(Position).X;
        set => Position = Displays.ToPixels(value, Top);
    }

    internal double Top
    {
        get => Displays.FromPixels(Position).Y;
        set => Position = Displays.ToPixels(Left, value);
    }

    /// <summary>
    /// Moves the widget to its saved position and size if that is visible; otherwise (e.g. a monitor was
    /// disconnected) temporarily places it on the primary screen WITHOUT overwriting the saved position.
    /// </summary>
    public void EnsureOnScreen()
    {
        Width = Math.Max(MinWidth, Board.Width);
        Height = Math.Max(MinHeight, Board.Height);
        var saved = new Rect(Board.X, Board.Y, Width, Height);
        if (Displays.IsReachable(saved, Displays.WorkAreas()))
        {
            Position = Displays.ToPixels(saved.X, saved.Y);
        }
        else
        {
            var wa = Displays.Primary;
            int k = _fallbackIndex++ % 5;
            Position = Displays.ToPixels(wa.Right - Width - 24 - 30 * k, wa.Top + 24 + 30 * k);
        }
        _placedAt = new Rect(Left, Top, Width, Height);
        _keyMoveSave?.Stop();
    }

#if WINDOWS
    const uint WM_DISPLAYCHANGE = 0x007E;
    const uint WM_EXITSIZEMOVE = 0x0232;

    internal IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Dragging the widget by its header or resizing it by its border ends with WM_EXITSIZEMOVE
        if (msg == WM_EXITSIZEMOVE) SaveBounds();
        // A monitor change: a move that was just made came from Windows, not from Win+Arrow
        else if (msg == WM_DISPLAYCHANGE) _keyMoveSave?.Stop();
        return IntPtr.Zero;
    }
#endif

    /// <summary>
    /// Win+Arrow (and Win+Shift+Arrow) move the active window without a drag, so there is no WM_EXITSIZEMOVE.
    /// A move of the active widget is saved after a short wait, unless a monitor change shows it came from Windows.
    /// </summary>
    void OnMovedWithoutDrag()
    {
        if (!IsActive || DisplayChanging) return;
        if (_keyMoveSave == null)
        {
            _keyMoveSave = new DispatcherTimer();
            _keyMoveSave.Tick += (_, _) =>
            {
                _keyMoveSave.Stop();
                if (!DisplayChanging && !IsAt(_placedAt)) SaveBounds();
            };
        }
        _keyMoveSave.Stop(); // wait until the moving is over
        _keyMoveSave.Interval = KeyMoveSaveDelay;
        _keyMoveSave.Start();
    }

    bool IsAt(Rect r) =>
        Math.Abs(Left - r.X) < 1 && Math.Abs(Top - r.Y) < 1 && Math.Abs(Width - r.Width) < 1 && Math.Abs(Height - r.Height) < 1;

    /// <summary>Work area of the monitor this widget is on.</summary>
    Rect CurrentWorkArea() => Displays.WorkAreaAt(new Point(Left + Bounds.Width / 2, Top + 15));

    /// <summary>Saves the current position and size (after the user moved or resized the widget).</summary>
    internal void SaveBounds()
    {
        if (WindowState != WindowState.Normal) return;
        double x = Math.Round(Left), y = Math.Round(Top), width = Math.Round(Width), height = Math.Round(Height);
        if ((x, y, width, height) == (Board.X, Board.Y, Board.Width, Board.Height)) return; // e.g. a click on the header
        (Board.X, Board.Y, Board.Width, Board.Height) = (x, y, width, height);
        _placedAt = new Rect(x, y, width, height);
        Store.MarkDirty();
    }

    void ApplyLock()
    {
        Grips.IsVisible = !Board.Locked;
        LockIcon.Data = (Geometry)this.FindResource(Board.Locked ? "Icon.Lock" : "Icon.Unlock")!;
        var tip = Board.Locked ? "Unlock position and size" : "Lock position and size";
        ToolTip.SetTip(LockBtn, tip);
        Avalonia.Automation.AutomationProperties.SetName(LockBtn, tip);
    }

    void Lock_Click(object? sender, RoutedEventArgs e)
    {
        Board.Locked = !Board.Locked;
        ApplyLock();
        Store.MarkDirty();
        A11y.Announce(this, Board.Locked ? "Position and size locked" : "Position and size unlocked");
    }

    /// <summary>
    /// Windows resizes the widget by its grips. macOS can't do that for a window without a frame, so there
    /// PinList follows the mouse itself. Tests switch it.
    /// </summary>
    internal static bool SystemResize { get; set; } = OperatingSystem.IsWindows();

    /// <summary>
    /// What each grip (by its Tag) resizes: the edge, and which way it moves the sides. X is -1 for the left side,
    /// 1 for the right side and 0 for neither; Y is the same for the top and bottom.
    /// </summary>
    static readonly Dictionary<string, (WindowEdge Edge, int X, int Y)> GripSides = new()
    {
        ["W"] = (WindowEdge.West, -1, 0), ["E"] = (WindowEdge.East, 1, 0),
        ["N"] = (WindowEdge.North, 0, -1), ["S"] = (WindowEdge.South, 0, 1),
        ["NW"] = (WindowEdge.NorthWest, -1, -1), ["NE"] = (WindowEdge.NorthEast, 1, -1),
        ["SW"] = (WindowEdge.SouthWest, -1, 1), ["SE"] = (WindowEdge.SouthEast, 1, 1),
    };

    void Grip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Board.Locked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var side = GripSides[(string)((Control)sender!).Tag!];
        if (SystemResize) BeginResizeDrag(side.Edge, e);
        else
        {
            _resize = new(side.X, side.Y, ScreenPoint(e), Position, new Size(Width, Height));
            e.Pointer.Capture((IInputElement)sender!);
        }
        e.Handled = true;
    }

    /// <summary>A resize PinList does itself: the sides it moves, and where the mouse, the widget and its size were at the start.</summary>
    sealed record ResizeStart(int X, int Y, Point Mouse, PixelPoint Position, Size Size);
    ResizeStart? _resize;

    /// <summary>The mouse in screen pixels (the widget may move while it's resized, so not in the widget).</summary>
    Point ScreenPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(Position.X + p.X * DesktopScaling, Position.Y + p.Y * DesktopScaling);
    }

    void Grip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resize is not { } r) return;
        var mouse = ScreenPoint(e);
        double s = DesktopScaling, dx = (mouse.X - r.Mouse.X) / s, dy = (mouse.Y - r.Mouse.Y) / s;
        double width = Math.Max(MinWidth, r.Size.Width + r.X * dx);
        double height = Math.Max(MinHeight, r.Size.Height + r.Y * dy);
        // Resizing by the left or top side moves the widget, so the opposite side stays where it is
        int x = r.Position.X + (int)Math.Round((r.Size.Width - width) * s * Math.Max(0, -r.X));
        int y = r.Position.Y + (int)Math.Round((r.Size.Height - height) * s * Math.Max(0, -r.Y));
        Width = width;
        Height = height;
        Position = new PixelPoint(x, y);
    }

    void Grip_PointerReleased(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    void Grip_CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _resize = null;
        SaveBounds(); // does nothing when the size didn't change
    }

    /// <summary>Re-applies colors and text size (after a theme, contrast or text size change).</summary>
    public void RefreshAppearance()
    {
        ApplyAppearance();
        ApplyTextScale();
    }

    void ApplyTextScale() =>
        Scaler.LayoutTransform = A11y.TextScale == 1.0 ? null : new ScaleTransform(A11y.TextScale, A11y.TextScale);

    BoardStyle CurrentStyle => Themes.Get(Board.Style);

    void ApplyAppearance()
    {
        Themes.ApplyBoard(Resources, CurrentStyle, Board.Transparent);
        // text boxes, scrollbars and the like follow the widget color, not the app theme
        RequestedThemeVariant = CurrentStyle.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

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

    internal static void SetActive(Observable? element)
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
        var w = App.Instance.OpenWindows.FirstOrDefault(x => x.Board == board);
        if (w == null) return;

        if (cat.Collapsed) { cat.Collapsed = false; Store.MarkDirty(); }
        w.Activate();
        SetActive(item);
        w.FocusRow(item); // also scrolls it into view
    }

    /// <summary>The focusable row (item or category header) of a data object.</summary>
    AccessibleRow? FindTagged(object tag) =>
        this.GetVisualDescendants().OfType<AccessibleRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, tag));

    /// <summary>Clicking empty space also clears the highlight (clicking an item/category sets it again right away).</summary>
    void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SetActive(null);
        // The highlight follows keyboard focus, so focus leaves the row too
        if (FocusManager?.GetFocusedElement() is AccessibleRow row && this.IsVisualAncestorOf(row))
            ClearFocus();
    }

    /// <summary>Takes keyboard focus away from the rows and boxes (the widget's frame takes it).</summary>
    void ClearFocus() => Frame.Focus();

    /// <summary>Deletes the active element without archiving it (Ctrl+Z brings it back).</summary>
    void DeleteActive() => DeleteWithFocus(_active!); // only called while something is highlighted

    /// <summary>Shortcuts work when no text is being typed (an empty "Add item" box is fine).</summary>
    bool ShortcutsAllowed() =>
        FocusManager?.GetFocusedElement() is not TextBox tb || (tb.Classes.Contains("addbox") && string.IsNullOrEmpty(tb.Text));

    void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            Board.IsEditingTitle = true;
            return;
        }
        if (!Board.Locked) BeginMoveDrag(e);
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Alt+F4 must not close a widget; deleting goes through the menu
        if (!UserDeleted && !App.ShuttingDown) e.Cancel = true;
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control && ShortcutsAllowed())
        {
            A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
            e.Handled = true;
        }
        else if (HandleWindowKeys(e))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift)
                 && FocusManager?.GetFocusedElement() is InputElement from && this.IsVisualAncestorOf(from))
        {
            MoveFocus(from, e.KeyModifiers == KeyModifiers.Shift ? NavigationDirection.Previous : NavigationDirection.Next);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && ShortcutsAllowed() && _active != null)
        {
            DeleteActive();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _dragPayload != null)
        {
            CancelDrag();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FocusManager?.GetFocusedElement() is not TextBox)
        {
            SetActive(null);
        }
    }

    // ================= Categories and the window =================

    void AddCategory_Click(object? sender, RoutedEventArgs e) => AddCategory();

    void AddCategory()
    {
        var cat = Actions.AddCategory(Board);
        SetActive(cat);
        cat.IsEditing = true; // type the name right away
    }

    void Collapse_Click(object? sender, RoutedEventArgs e) =>
        ToggleCollapsed((Category)((Control)sender!).Tag!); // the button of a category header

    async void DeleteThisBoard()
    {
        int items = 0;
        foreach (var c in Board.Categories) items += c.Items.Count;
        string msg = items > 0
            ? $"This removes the window with {Board.Categories.Count} categories and {items} items. Items are not moved to the archive."
            : "This removes the window from your desktop.";
        string title = string.IsNullOrWhiteSpace(Board.Title) ? "Delete this window?" : $"Delete \"{Board.Title}\"?";
        if (!await Dialogs.Ask(this, title, msg, "Delete"))
            return;

        Actions.DeleteBoard(Board);
        UserDeleted = true;
        Close();
    }

    // ================= Items =================

    /// <summary>Clicking the circle completes after ~1 s; a second click in that time cancels.</summary>
    void Check_Click(object? sender, RoutedEventArgs e) =>
        ToggleCompleting((TodoItem)((Control)sender!).Tag!); // the circle of an item

    void AddBox_KeyDown(TextBox tb, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0) // Shift+Enter = new line
        {
            var text = Normalize(tb.Text ?? "");
            if (text.Length > 0 && tb.Tag is Category cat)
            {
                SetActive(Actions.AddItem(cat, text)); // last added = active (Delete removes it)
                A11y.Announce(this, $"Added {text.Split('\n')[0]}");
            }
            tb.Text = ""; // focus stays, so the next item can be typed right away
            e.Handled = true;
        }
        else if ((e.Key == Key.Up || e.Key == Key.Down) && string.IsNullOrEmpty(tb.Text) && e.KeyModifiers == KeyModifiers.None)
        {
            // an empty box lets Up/Down move between rows, like everywhere else in the list
            MoveFocus(tb, e.Key == Key.Up ? NavigationDirection.Previous : NavigationDirection.Next);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tb.Text = "";
            ClearFocus();
            e.Handled = true;
        }
    }

    // ================= Text editing (item, category, title) =================

    void EditBox_Loaded(object? sender, RoutedEventArgs e)
    {
        var tb = (TextBox)sender!;
        // A box that is created already in edit mode (the name of a just added category) starts editing now;
        // the others start when they become visible.
        tb.PropertyChanged -= EditBox_PropertyChanged;
        tb.PropertyChanged += EditBox_PropertyChanged;
        if (tb.DataContext is TodoItem { IsEditing: true } or Category { IsEditing: true } or Board { IsEditingTitle: true })
            BeginEdit(tb);
    }

    void EditBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } tb) BeginEdit(tb);
    }

    /// <summary>Fills the edit box with the current text and puts the caret in it.</summary>
    void BeginEdit(TextBox tb)
    {
        // every edit box belongs to an item, a category or a widget title
        tb.Text = tb.DataContext is TodoItem i ? i.Text : tb.DataContext is Category c ? c.Name : ((Board)tb.DataContext!).Title;
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            tb.Focus(NavigationMethod.Tab);
            // Caret at the end; only the default name of a new category is selected so it can be typed over
            if (tb.DataContext is Category { Name: "New category" }) tb.SelectAll();
            else tb.CaretIndex = tb.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    void EditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox tb) return;
        if (tb.Classes.Contains("addbox")) { AddBox_KeyDown(tb, e); return; }
        if (!tb.Classes.Contains("edit")) return;
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0) { CommitEdit(tb); FocusAfterEdit(tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(tb); FocusAfterEdit(tb); e.Handled = true; }
    }

    /// <summary>After Enter/Esc, keyboard focus goes back to the edited row instead of getting lost.</summary>
    void FocusAfterEdit(TextBox tb)
    {
        switch (tb.DataContext)
        {
            case TodoItem i when Store.Locate(i) != null: FocusRow(i); break;
            case Category c when Store.BoardOf(c) != null: FocusRow(c); break;
            default: Dispatcher.UIThread.Post(FocusContent, DispatcherPriority.Loaded); break;
        }
    }

    void EditBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb && tb.Classes.Contains("edit")) CommitEdit(tb);
    }

    /// <summary>Unified line breaks, no blank lines at the start/end; indentation inside the text is kept.</summary>
    static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    static void CommitEdit(TextBox tb)
    {
        var text = Normalize(tb.Text ?? "");
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
