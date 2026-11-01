using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// Long-press on the phone. Pressing and holding a row (≈0.45 s without moving) lifts it:
///  - letting go without moving opens its menu,
///  - moving drags it: the row stays under the finger, the other rows slide out of the way,
///    and holding near the top or bottom edge scrolls.
/// A swipe that starts on a row is a scroll: moving before the hold, or the list scrolling, cancels the long-press,
/// and nothing stays highlighted.
/// Rows are controls whose Tag is accepted by <c>canDrag</c>.
/// Which row the finger is over is decided by where the layout puts the rows (not where they are drawn while
/// sliding), so rows that are still moving can't be picked up again and swapped back and forth.
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

    /// <summary>How much bigger the lifted row is drawn.</summary>
    public double LiftScale { get; set; } = 1.02;

    const double MoveTolerance = 10;    // a finger wobbles a little while holding
    const double ScrollEdge = 72;       // holding this close to the top or bottom scrolls
    const double MaxScrollSpeed = 1100; // pixels per second, with the finger at the very edge
    static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(200); // a bit longer than the row slide

    readonly DispatcherTimer _holdTimer = new();
    readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Stopwatch _scrollClock = new();

    IPointer? _pointer;
    Point _start, _last;
    object? _candidate;
    Control? _candidateRow;
    bool _held, _dragging;
    double _grabY;                        // where the finger holds the row, from its top
    double _offsetY;                      // how far the lifted row is drawn from its place in the list
    List<Control> _raised = new();        // the lifted row and its containers, drawn above the other rows
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
            if (x is Control c && IsRow(c)) { _candidate = c.Tag; _candidateRow = c; break; }
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

        // from now on the row moves with the finger directly (no easing, which would make it lag behind)
        if (_candidateRow!.Transitions != null) _candidateRow.Transitions = null;
        if (_dragging) Follow();
        else Place();
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
        _grabY = _start.Y - LayoutTop(_candidateRow!);
        Lift(_candidateRow!);
        Place(); // the row style's transition makes it grow smoothly
        _pointer!.Capture(_root);
        _scrollClock.Restart();
        _scrollTimer.Start();
    }

    /// <summary>
    /// Moves the dragged thing over the row under the finger, if it isn't over itself, then draws it under the finger.
    /// </summary>
    void Follow()
    {
        var over = Rows().FirstOrDefault(c => LayoutRect(c) is { } r && _last.Y >= r.Top && _last.Y < r.Bottom);
        if (over != null && !ReferenceEquals(over.Tag, _candidate) && LayoutRect(over) is { } rect)
        {
            double topBefore = LayoutTop(_candidateRow!);
            var before = Reorder.Capture(_root, IsRow);
            if (MoveOver(_candidate!, over.Tag!, _last - rect.Position, rect.Size))
            {
                _root.UpdateLayout(); // place the rows now, so nothing is drawn in between
                Relift();
                // the others slide out of the way (only when something moved, so running slides aren't restarted)
                if (Math.Abs(LayoutTop(_candidateRow!) - topBefore) > 0.5)
                    Reorder.SlideNow(_root, before, IsRow, _candidate);
            }
        }
        Place();
    }

    /// <summary>Draws the lifted row under the finger, at the height where it was picked up.</summary>
    void Place()
    {
        var row = _candidateRow!;
        _offsetY = _last.Y - _grabY - LayoutTop(row);
        var t = TransformOperations.CreateBuilder(2);
        t.AppendScale(LiftScale, LiftScale); // scale first, so the offset itself isn't scaled
        t.AppendTranslate(0, _offsetY);
        row.RenderTransform = t.Build();
    }

    /// <summary>The list may have made the dragged row again in its new place: keep that one lifted.</summary>
    void Relift()
    {
        if (!_held) return;
        var row = Rows().FirstOrDefault(c => ReferenceEquals(c.Tag, _candidate));
        if (row == null || row == _candidateRow) return;
        Unlift(_candidateRow!);
        _candidateRow = row;
        Lift(row);
        row.Transitions = null;
    }

    void Lift(Control row)
    {
        row.Classes.Add("lifted");
        Raise(row);
    }

    void Unlift(Control row)
    {
        row.Classes.Remove("lifted");
        row.ClearValue(Animatable.TransitionsProperty);
        row.ClearValue(Visual.RenderTransformProperty);
    }

    /// <summary>
    /// Draws the row above the other rows. ZIndex only counts among siblings, so the containers the row is in
    /// (for an item: its place in the category, and the category in the list) are raised too.
    /// </summary>
    void Raise(Control row)
    {
        Lower(_raised);
        _raised = new List<Control>();
        for (Visual? x = row; x != null && x != _scroller && x != _root; x = x.GetVisualParent())
            if (x is Control c) { c.ZIndex = 1; _raised.Add(c); }
    }

    void Lower(List<Control> raised)
    {
        foreach (var c in raised)
            if (!_raised.Contains(c) || raised == _raised) c.ClearValue(Visual.ZIndexProperty);
        raised.Clear();
    }

    void AutoScroll()
    {
        double dt = Math.Min(_scrollClock.Elapsed.TotalSeconds, 0.05); // the same speed however often this runs
        _scrollClock.Restart();
        if (!_dragging) return;
        var p = _root.TranslatePoint(_last, _scroller) ?? default;
        double h = _scroller.Bounds.Height, depth = 0;
        if (p.Y < ScrollEdge) depth = p.Y - ScrollEdge;
        else if (p.Y > h - ScrollEdge) depth = p.Y - (h - ScrollEdge);
        if (depth == 0) return;
        double dy = Math.Clamp(depth / ScrollEdge, -1.5, 1.5) * MaxScrollSpeed * dt;
        double max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        double y = Math.Clamp(_scroller.Offset.Y + dy, 0, max);
        if (y == _scroller.Offset.Y) return;
        _scroller.Offset = new Vector(_scroller.Offset.X, y);
        _root.UpdateLayout(); // so the lifted row is placed against the scrolled list, not the old one
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
        double shownTop = held ? LayoutTop(row!) + _offsetY : 0;
        _candidate = null;
        _candidateRow = null;
        _held = _dragging = false;
        if (!held) { _pointer = null; return; }

        if (_pointer!.Captured == _root) _pointer.Capture(null);
        _pointer = null;
        _suppressTapUntil = DateTime.UtcNow.AddMilliseconds(400);

        if (!dragging)
        {
            Unlift(row!); // shrinks back smoothly (the row style's transition)
            LowerSoon();
            LongPressed(target!, row!);
            return;
        }

        Dropped(target!);
        _root.UpdateLayout();
        // the dragged row (or the one the list made again for it) slides from under the finger to its place
        var home = Rows().FirstOrDefault(c => ReferenceEquals(c.Tag, target)) ?? row!;
        if (home != row) Unlift(row!);
        home.Classes.Remove("lifted");
        Reorder.SlideFrom(home, 0, shownTop - LayoutTop(home), LiftScale);
        LowerSoon();
    }

    /// <summary>Keeps the dropped row above the others until it has slid home.</summary>
    void LowerSoon()
    {
        var raised = _raised;
        _raised = new List<Control>();
        DispatcherTimer.RunOnce(() => Lower(raised), SlideTime);
    }

    bool IsRow(Control c) => c.Tag != null && c is not Button && c is not TextBox && _canDrag(c.Tag);

    IEnumerable<Control> Rows() =>
        _root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && IsRow(c));

    Rect? LayoutRect(Control c) =>
        Reorder.LayoutPosition(c, _root) is { } p ? new Rect(p, c.Bounds.Size) : null;

    double LayoutTop(Control c) => Reorder.LayoutPosition(c, _root)?.Y ?? 0;

    static bool IsInside<T>(Visual v)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x is T) return true;
        return false;
    }

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
