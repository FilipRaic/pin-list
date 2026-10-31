using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// Drag and drop of items and categories, within a widget and between widgets, with auto-scroll.
/// The dragged row lifts, a line glides to where it would go, and on drop the rows slide to their new places.
/// All widgets live in the same process, so the drag is followed here instead of in the system drag loop.
/// </summary>
public partial class BoardWindow
{
    // ---- The drag in progress (shared by all widgets: a row can be dropped in another widget) ----
    static object? _dragPayload;
    static Control? _dragRow;
    static BoardWindow? _dragSource;
    static PixelPoint _dragScreen;
    static (BoardWindow window, Action apply)? _drop;

    // Auto-scroll while dragging near the top/bottom edge of the list
    const double AutoScrollZone = 40;     // px from the edge where scrolling starts
    const double AutoScrollMaxStep = 12;  // px per step (~60 steps per second)
    static DispatcherTimer? _autoScrollTimer;

    /// <summary>How far the mouse moves (px) before a press becomes a drag.</summary>
    const double DragThreshold = 4;

    Point _dragStart;
    object? _dragCandidate;
    Control? _candidateRow;

    /// <summary>True while a row is being dragged (tests check it).</summary>
    internal static bool IsDragging => _dragPayload != null;

    /// <summary>True while the dragged row would land somewhere if dropped now (tests check it).</summary>
    internal static bool HasDropTarget => _drop != null;

    void SetupDragDrop()
    {
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => { if (_dragSource == this) CancelDrag(); }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    void Row_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var el = (Control)sender!;
        if (!e.GetCurrentPoint(el).Properties.IsLeftButtonPressed) return;
        // (a press on the complete button or in a text box never gets here: they handle it themselves)
        SetActive(el.Tag as Observable);

        // focus the clicked row: Delete then acts on it, and keyboard navigation continues from here
        el.Focus(NavigationMethod.Pointer);

        if (e.ClickCount == 2) // double-click = edit
        {
            _dragCandidate = null;
            if (el.Tag is TodoItem item) item.IsEditing = true;
            else if (el.Tag is Category cat) cat.IsEditing = true;
            e.Handled = true;
            return;
        }

        _dragCandidate = el.Tag;
        _candidateRow = el;
        _dragStart = e.GetPosition(this);
    }

    void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPayload == null)
        {
            if (_dragCandidate == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < DragThreshold && Math.Abs(p.Y - _dragStart.Y) < DragThreshold) return;
            BeginDrag(e);
        }
        if (_dragSource != this) return;
        _dragScreen = this.PointToScreen(e.GetPosition(this));
        UpdateDropTarget();
        e.Handled = true;
    }

    void BeginDrag(PointerEventArgs e)
    {
        _dragPayload = _dragCandidate;
        _dragRow = _candidateRow;
        _dragSource = this;
        _dragCandidate = null;
        _dragRow?.Classes.Add("lifted");
        e.Pointer.Capture(this);
        StartAutoScroll();
    }

    void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragCandidate = null;
        if (_dragSource != this) return;
        _dragScreen = this.PointToScreen(e.GetPosition(this));
        UpdateDropTarget();
        var drop = _drop;
        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
        if (drop is not { } d) return;

        // rows slide to their new places, in the target widget and (when different) in the source widget
        var source = this;
        var beforeTarget = Reorder.Capture(d.window);
        var beforeSource = source == d.window ? null : Reorder.Capture(source);
        d.apply();
        Reorder.Animate(d.window, beforeTarget);
        if (beforeSource != null) Reorder.Animate(source, beforeSource);
    }

    /// <summary>Esc while dragging: put everything back.</summary>
    void CancelDrag() => EndDrag();

    internal static void EndDrag()
    {
        _dragRow?.Classes.Remove("lifted");
        _dragRow = null;
        _dragPayload = null;
        _dragSource = null;
        _drop = null;
        _autoScrollTimer?.Stop();
        foreach (var w in App.Instance.OpenWindows) w.HideDropLine();
    }

    /// <summary>Finds the widget under the pointer and what would happen if the row were dropped there.</summary>
    static void UpdateDropTarget()
    {
        _drop = null;
        foreach (var w in App.Instance.OpenWindows)
        {
            var client = w.PointToClient(_dragScreen);
            bool inside = w.IsVisible && client.X >= 0 && client.Y >= 0 && client.X <= w.Bounds.Width && client.Y <= w.Bounds.Height;
            if (inside && _drop == null && w.DropAt(client) is { } apply) _drop = (w, apply);
            else if (_drop?.window != w) w.HideDropLine();
        }
    }

    /// <summary>Shows where the dragged row would go at this point, and returns how to put it there (null: not here).</summary>
    Action? DropAt(Point client)
    {
        for (var v = this.InputHitTest(client) as Visual; v != null && v != this; v = v.GetVisualParent())
        {
            if (v is not Control c) continue;
            bool below = IsLowerHalf(client, c);

            // over an item
            if (c is AccessibleRow { Tag: TodoItem target } && c.Classes.Contains("item"))
            {
                if (_dragPayload is not TodoItem dragged || dragged == target) return Deny();
                ShowDropLine(c, below);
                return () =>
                {
                    if (Store.Locate(target) is not { } loc) return;
                    Actions.MoveItem(dragged, loc.category, loc.category.Items.IndexOf(target) + (below ? 1 : 0));
                };
            }

            // over a category header
            if (c is AccessibleRow { Tag: Category header })
            {
                if (_dragPayload is Category draggedCat && draggedCat != header)
                {
                    ShowDropLine(c, below);
                    return () =>
                    {
                        if (Store.BoardOf(header) is { } board)
                            Actions.MoveCategory(draggedCat, board, board.Categories.IndexOf(header) + (below ? 1 : 0));
                    };
                }
                if (_dragPayload is TodoItem item)
                {
                    ShowDropLine(c, true); // the item goes to the top of the category
                    return () => Actions.MoveItem(item, header, 0);
                }
                return Deny();
            }

            // over the "Add item" box = end of the category
            if (c is Border { Tag: Category end } && c.Classes.Contains("addrow"))
            {
                if (_dragPayload is not TodoItem item) return Deny();
                ShowDropLine(c, false);
                return () => Actions.MoveItem(item, end, end.Items.Count);
            }

            // empty space at the bottom of the widget
            if (c == EndZone)
            {
                if (_dragPayload is not Category cat) return Deny();
                ShowDropLine(c, false);
                return () => Actions.MoveCategory(cat, Board, Board.Categories.Count);
            }
        }
        return Deny();
    }

    Action? Deny()
    {
        HideDropLine();
        return null;
    }

    bool IsLowerHalf(Point client, Control el) =>
        el.TranslatePoint(default, this) is { } top && client.Y - top.Y > el.Bounds.Height / 2;

    void ShowDropLine(Control el, bool below)
    {
        if (el.TranslatePoint(default, DropLayer) is not { } p) return;
        double top = below ? p.Y + el.Bounds.Height - 1 : p.Y - 1;
        Canvas.SetLeft(DropLine, p.X);
        DropLine.Width = Math.Max(0, el.Bounds.Width);
        if (DropLine.IsVisible)
        {
            Canvas.SetTop(DropLine, top); // glides there
        }
        else
        {
            // appears in place (gliding from nowhere would start at "no position")
            var glide = DropLine.Transitions;
            DropLine.Transitions = null;
            Canvas.SetTop(DropLine, top);
            DropLine.Transitions = glide;
            DropLine.IsVisible = true;
        }
    }

    void HideDropLine() => DropLine.IsVisible = false;

    // ---- Auto-scroll while dragging ----
    // The timer runs for the whole drag, so scrolling continues while the mouse rests at the edge, and also
    // works in another widget if the row is dragged there.

    static void StartAutoScroll()
    {
        _autoScrollTimer?.Stop();
        _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _autoScrollTimer.Tick += (_, _) => AutoScrollTick();
        _autoScrollTimer.Start();
    }

    internal static void AutoScrollTick()
    {
        if (_dragPayload == null) { _autoScrollTimer?.Stop(); return; }
        bool scrolled = false;
        foreach (var w in App.Instance.OpenWindows) scrolled |= w.AutoScrollAt(_dragScreen);
        if (scrolled) UpdateDropTarget();
    }

    bool AutoScrollAt(PixelPoint screen)
    {
        double scrollable = Scroller.Extent.Height - Scroller.Viewport.Height;
        if (!IsVisible || scrollable <= 0) return false;
        var inWindow = this.PointToClient(screen);
        // the cursor must be inside this widget
        if (inWindow.X < 0 || inWindow.Y < 0 || inWindow.X > Bounds.Width || inWindow.Y > Bounds.Height) return false;
        if (this.TranslatePoint(inWindow, Scroller) is not { } inList) return false;

        double height = Scroller.Bounds.Height;
        double step = 0;
        if (inList.Y < AutoScrollZone)                       // top edge (including the header above the list)
            step = -Step(AutoScrollZone - inList.Y);
        else if (inList.Y > height - AutoScrollZone)         // bottom edge
            step = Step(inList.Y - (height - AutoScrollZone));
        if (step == 0) return false;

        Scroller.Offset = new Vector(Scroller.Offset.X, Math.Clamp(Scroller.Offset.Y + step, 0, scrollable));
        return true;
    }

    /// <summary>The closer to the edge, the faster (2 to AutoScrollMaxStep px per step).</summary>
    static double Step(double depth) =>
        Math.Max(2, AutoScrollMaxStep * Math.Min(1, depth / AutoScrollZone));
}
