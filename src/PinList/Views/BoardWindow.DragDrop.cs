using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PinList;

/// <summary>Drag and drop of items and categories, within a widget and between widgets, with auto-scroll.</summary>
public partial class BoardWindow
{
    const string DragFormat = "PinList.Drag";

    // Drag and drop also works between widgets because they all live in the same process
    static object? _dragPayload;
    static Border? _indicator;
    static Thickness _indicatorPadding;

    // Auto-scroll while dragging near the top/bottom edge of the list
    const double AutoScrollZone = 40;     // px from the edge where scrolling starts
    const double AutoScrollMaxStep = 12;  // px per step (~60 steps per second)
    static DispatcherTimer? _autoScrollTimer;

    Point _dragStart;
    object? _dragCandidate;


    void Drag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        SetActive(el.Tag as Observable);
        if (IsInteractive(e.OriginalSource as DependencyObject, el))
        {
            _dragCandidate = null;
            return;
        }

        // focus the clicked row: Delete then acts on it, and keyboard navigation continues from here
        el.Focus();

        if (e.ClickCount == 2) // double-click = edit
        {
            _dragCandidate = null;
            if (el.Tag is TodoItem item) item.IsEditing = true;
            else if (el.Tag is Category cat) cat.IsEditing = true;
            e.Handled = true;
            return;
        }

        _dragCandidate = el.Tag;
        _dragStart = InputState.Position(e, this);
    }

    void Drag_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || !InputState.IsLeftButtonPressed(e)) return;

        var p = InputState.Position(e, this);
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
            InputState.DoDragDrop(el, new DataObject(DragFormat, "1"), DragDropEffects.Move);
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

    static void StopAutoScroll() => _autoScrollTimer!.Stop(); // only called after StartAutoScroll created it

    static void AutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragPayload == null) { StopAutoScroll(); return; }
        var cursor = InputState.CursorScreen();
        foreach (var w in Application.Current.Windows.OfType<BoardWindow>())
            w.AutoScrollAt(cursor);
    }

    void AutoScrollAt(Point screen)
    {
        // a visible widget is on screen, so converting screen points always works
        if (!IsVisible || Scroller.ScrollableHeight <= 0) return;
        var inWindow = PointFromScreen(screen);
        var inList = Scroller.PointFromScreen(screen);

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
