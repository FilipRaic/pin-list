using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// Long-press on the phone. Pressing and holding a row (≈0.45 s without moving) lifts it:
///  - letting go without moving opens its menu,
///  - moving drags it; the other rows slide out of the way, and holding near the top or bottom edge scrolls.
/// A swipe that starts on a row is a scroll: moving before the hold, or the list scrolling, cancels the long-press,
/// and nothing stays highlighted.
/// Rows are controls whose Tag is accepted by <c>canDrag</c>.
/// </summary>
public sealed class PhoneDrag
{
    readonly Control _root;
    readonly ScrollViewer _scroller;
    readonly Func<object, bool> _canDrag;

    /// <summary>The dragged thing is over another row: move it there (live). Returns true if it moved.</summary>
    public Func<object, object, Point, Size, bool> MoveOver { get; set; } = (_, _, _, _) => false;

    /// <summary>The drag ended; save the result (as one undoable change).</summary>
    public Action<object> Dropped { get; set; } = _ => { };

    /// <summary>Held without moving: open the menu of this row.</summary>
    public Action<object, Control> LongPressed { get; set; } = (_, _) => { };

    /// <summary>How long a press must be held before it lifts the row (tests shorten it).</summary>
    public static TimeSpan HoldTime { get; set; } = TimeSpan.FromMilliseconds(450);

    const double MoveTolerance = 10; // a finger wobbles a little while holding

    readonly DispatcherTimer _holdTimer = new();
    readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    IPointer? _pointer;
    Point _start, _last;
    object? _candidate;
    Control? _candidateRow;
    bool _held, _dragging;
    DateTime _suppressTapUntil;

    /// <summary>The row being held or dragged (it carries the "lifted" class).</summary>
    public object? Lifted => _held ? _candidate : null;

    /// <summary>True right after a long-press or drag ended, so the release isn't also a tap.</summary>
    public bool SuppressTap => DateTime.UtcNow < _suppressTapUntil;

    public PhoneDrag(Control root, ScrollViewer scroller, Func<object, bool> canDrag)
    {
        _root = root; _scroller = scroller; _canDrag = canDrag;
        root.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Bubble, true);
        scroller.ScrollChanged += (_, _) => { if (!_held) Cancel(); }; // the list scrolls: it's not a long-press
        _holdTimer.Tick += (_, _) => Hold();
        _scrollTimer.Tick += (_, _) => AutoScroll();
    }

    void OnPressed(object? s, PointerPressedEventArgs e)
    {
        Finish();
        var v = (Visual)e.Source!;
        if (IsInside<TextBox>(v) || IsInside<Button>(v)) return;
        for (Visual? x = v; x != null && x != _root; x = x.GetVisualParent())
            if (x is Control { Tag: { } t } c && _canDrag(t)) { _candidate = t; _candidateRow = c; break; }
        if (_candidate == null) return;
        _pointer = e.Pointer;
        _start = _last = e.GetPosition(_root);
        _holdTimer.Interval = HoldTime;
        _holdTimer.Start();
    }

    void OnMoved(object? s, PointerEventArgs e)
    {
        var p = e.GetPosition(_root);
        if (!_held)
        {
            if (_candidate != null && Distance(p, _start) > MoveTolerance) Cancel(); // a swipe: scroll instead
            return;
        }
        e.PreventGestureRecognition();
        e.Handled = true;
        _last = p;
        if (!_dragging && Distance(p, _start) > MoveTolerance) _dragging = true;
        if (_dragging) Follow();
    }

    void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        if (_held) e.Handled = true;
        Finish();
    }

    /// <summary>The press was held long enough: lift the row (a candidate, its row and pointer are set together).</summary>
    void Hold()
    {
        _holdTimer.Stop();
        if (_candidate == null) return;
        _held = true;
        _candidateRow!.Classes.Add("lifted");
        _pointer!.Capture(_root);
        _scrollTimer.Start();
    }

    /// <summary>Moves the dragged thing over whatever row is under the finger (only while held).</summary>
    void Follow()
    {
        if (_root.InputHitTest(_last) is not Visual hit) return;
        for (Visual? v = hit; v != null && v != _root; v = v.GetVisualParent())
        {
            if (v is not Control { Tag: { } tag } c) continue;
            if (ReferenceEquals(tag, _candidate)) return; // over itself
            var local = _root.TranslatePoint(_last, c) ?? default;
            var before = Reorder.Capture(_root, IsRow);
            if (MoveOver(_candidate!, tag, local, c.Bounds.Size))
            {
                Reorder.Animate(_root, before, IsRow);
                // the row may have been made again in its new place; keep it lifted
                Dispatcher.UIThread.Post(Relift, DispatcherPriority.Loaded);
                return;
            }
        }
    }

    bool IsRow(Control c) => c.Tag != null && _canDrag(c.Tag);

    void Relift()
    {
        if (!_held) return;
        _candidateRow!.Classes.Remove("lifted");
        foreach (var c in _root.GetVisualDescendants())
            if (c is Control { Tag: { } t } row && ReferenceEquals(t, _candidate)) { _candidateRow = row; break; }
        _candidateRow.Classes.Add("lifted");
    }

    void AutoScroll()
    {
        if (!_dragging) return;
        var p = _root.TranslatePoint(_last, _scroller) ?? default;
        double edge = 72, h = _scroller.Bounds.Height, dy = 0;
        if (p.Y < edge) dy = -(edge - p.Y) / 4;
        else if (p.Y > h - edge) dy = (p.Y - (h - edge)) / 4;
        if (dy == 0) return;
        double max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        double y = Math.Clamp(_scroller.Offset.Y + dy, 0, max);
        if (y == _scroller.Offset.Y) return;
        _scroller.Offset = new Vector(_scroller.Offset.X, y);
        Follow();
    }

    /// <summary>Forget a press that turned out to be a tap or a scroll.</summary>
    public void Cancel()
    {
        if (_held) return;
        _holdTimer.Stop();
        _candidate = null;
        _candidateRow = null;
    }

    /// <summary>The finger lifted (or the drag was taken away): drop, or open the menu if it never moved.</summary>
    void Finish()
    {
        _holdTimer.Stop();
        _scrollTimer.Stop();
        var target = _candidate;
        var row = _candidateRow;
        bool held = _held, dragging = _dragging;
        _candidate = null;
        _candidateRow = null;
        _held = _dragging = false;
        if (!held) { _pointer = null; return; }

        row!.Classes.Remove("lifted");
        if (_pointer!.Captured == _root) _pointer.Capture(null);
        _pointer = null;
        _suppressTapUntil = DateTime.UtcNow.AddMilliseconds(400);
        if (dragging) Dropped(target!);
        else LongPressed(target!, row);
    }

    static bool IsInside<T>(Visual v)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x is T) return true;
        return false;
    }

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
