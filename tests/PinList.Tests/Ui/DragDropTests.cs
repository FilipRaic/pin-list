using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>
/// Drag and drop of items and categories, with the real mouse input of the headless platform: press on a row,
/// move, release. Rows can be dropped within a widget and into another widget.
/// </summary>
public class DragDropTests : UiTest
{
    /// <summary>A point in the element (in the window), on its text: left of the middle, clear of its buttons.</summary>
    static Point At(Control c, Visual w, double y = 0.5) =>
        c.TranslatePoint(new Point(c.Bounds.Width * 0.45, c.Bounds.Height * y), w)!.Value;

    static Border AddRow(BoardWindow w, Category cat) =>
        Find<Border>(w, b => b.Classes.Contains("addrow") && ReferenceEquals(b.Tag, cat))!;

    static Border EndArea(BoardWindow w) => Named<Border>(w, "EndZone");

    static Border Line(BoardWindow w) => Named<Border>(w, "DropLine");

    BoardWindow? _source;
    Point _last;

    /// <summary>Presses on a row and moves far enough to start dragging it.</summary>
    void Pick(BoardWindow w, Control row)
    {
        // a press where the last one was would count as a double-click (= edit); click elsewhere first
        var away = At(EndArea(w), w);
        w.MouseDown(away, MouseButton.Left);
        w.MouseUp(away, MouseButton.Left);
        _source = w;
        _last = At(row, w);
        w.MouseDown(_last, MouseButton.Left);
        _last += new Point(0, 10);
        w.MouseMove(_last, RawInputModifiers.LeftMouseButton);
        Pump();
    }

    /// <summary>Moves the dragged row over an element (in any widget), at a height within it (0 = top, 1 = bottom).</summary>
    void Over(BoardWindow target, Control element, double y = 0.5)
    {
        var screen = target.PointToScreen(At(element, target, y));
        _last = _source!.PointToClient(screen);
        _source!.MouseMove(_last, RawInputModifiers.LeftMouseButton);
        Pump();
    }

    void Release()
    {
        _source!.MouseUp(_last, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(250)); // rows slide to their new places
    }

    static double LineY(BoardWindow w)
    {
        UiHost.Wait(TimeSpan.FromMilliseconds(150)); // the line glides to its place
        return Canvas.GetTop(Line(w));
    }

    [AvaloniaFact]
    public void Dragging_an_item_below_another()
    {
        var w = OpenNew(("A", new[] { "a", "b", "c" }));
        var cat = w.Board.Categories[0];
        var (a, c) = (cat.Items[0], cat.Items[2]);

        Pick(w, Row(w, a));
        Assert.True(BoardWindow.IsDragging);
        Assert.Contains("lifted", Row(w, a).Classes); // the dragged row lifts (fades a little)

        Over(w, Row(w, c), 0.75);
        Assert.True(Line(w).IsVisible);
        var cBottom = At(Row(w, c), w, 1).Y;
        Assert.InRange(LineY(w), cBottom - 2, cBottom + 1); // the line is below c

        Release();
        Assert.Equal(new[] { "b", "c", "a" }, cat.Items.Select(i => i.Text));
        Assert.False(BoardWindow.IsDragging);
        Assert.False(Line(w).IsVisible);
        Assert.DoesNotContain("lifted", Row(w, a).Classes);
        Actions.Undo(); // one step undoes the whole drag
        Assert.Equal(new[] { "a", "b", "c" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Dragging_an_item_into_another_widget()
    {
        var w1 = OpenNew(("A", new[] { "a" }));
        var w2 = OpenNew(("B", new[] { "b" }));
        w2.Left = w1.Left + 400; // side by side
        Pump();
        var a = w1.Board.Categories[0].Items[0];
        var b = w2.Board.Categories[0].Items[0];

        Pick(w1, Row(w1, a));
        Over(w2, Row(w2, b), 0.25);
        Assert.True(Line(w2).IsVisible); // the line shows in the widget under the mouse
        Assert.False(Line(w1).IsVisible);
        var bTop = At(Row(w2, b), w2, 0).Y;
        Assert.InRange(LineY(w2), bTop - 2, bTop + 1); // above b

        Release();
        Assert.Equal(new[] { "a", "b" }, w2.Board.Categories[0].Items.Select(i => i.Text));
        Assert.Empty(w1.Board.Categories[0].Items);
    }

    [AvaloniaFact]
    public void An_item_onto_itself_or_onto_empty_space_is_refused()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];

        Pick(w, Row(w, a));
        Over(w, Row(w, cat.Items[1]));
        Assert.True(Line(w).IsVisible);
        Over(w, Row(w, a)); // itself: the line goes away
        Assert.False(Line(w).IsVisible);
        Over(w, EndArea(w)); // the end area takes categories only
        Assert.False(Line(w).IsVisible);
        Over(w, Named<Grid>(w, "Header")); // anywhere else
        Assert.False(Line(w).IsVisible);
        Release();
        Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Dropping_an_item_on_a_category_header_or_add_row()
    {
        var w = OpenNew(("A", new[] { "a" }), ("B", new[] { "b1", "b2" }));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);

        Pick(w, Row(w, a.Items[0]));
        Over(w, Row(w, b), 0.25);
        var headerBottom = At(Row(w, b), w, 1).Y;
        Assert.InRange(LineY(w), headerBottom - 2, headerBottom + 1); // always "below the header"
        Release();
        Assert.Equal(new[] { "a", "b1", "b2" }, b.Items.Select(i => i.Text)); // to the top

        Pick(w, Row(w, b.Items[0]));
        Assert.True(BoardWindow.IsDragging, "second drag started");
        Over(w, AddRow(w, b));
        Assert.True(Line(w).IsVisible, "line over add row; target=" + BoardWindow.HasDropTarget);
        Release();
        Assert.Equal(new[] { "b1", "b2", "a" }, b.Items.Select(i => i.Text)); // to the end
    }

    [AvaloniaFact]
    public void Dragging_categories()
    {
        var w = OpenNew(("A", new string[0]), ("B", new string[0]), ("C", new[] { "c" }));
        var (a, b, c) = (w.Board.Categories[0], w.Board.Categories[1], w.Board.Categories[2]);

        Pick(w, Row(w, a));
        Over(w, Row(w, a)); // itself
        Assert.False(Line(w).IsVisible);
        Over(w, AddRow(w, b)); // add rows take items only
        Assert.False(Line(w).IsVisible);
        Over(w, Row(w, c.Items[0])); // items don't take categories
        Assert.False(Line(w).IsVisible);
        Over(w, Row(w, b), 0.75);
        Assert.True(Line(w).IsVisible);
        Assert.True(BoardWindow.HasDropTarget);
        Release();
        Assert.Equal(new[] { "B", "A", "C" }, w.Board.Categories.Select(x => x.Name));

        Pick(w, Row(w, b));
        Over(w, EndArea(w));
        Assert.True(Line(w).IsVisible);
        Release();
        Assert.Equal(new[] { "A", "C", "B" }, w.Board.Categories.Select(x => x.Name));

        Pick(w, Row(w, b));
        Assert.True(BoardWindow.IsDragging, "third drag started");
        Over(w, Row(w, a), 0.25); // upper half: above
        Assert.True(BoardWindow.HasDropTarget, "third drag target");
        Release();
        Assert.Equal(new[] { "B", "A", "C" }, w.Board.Categories.Select(x => x.Name));
    }

    [AvaloniaFact]
    public void Small_moves_buttons_and_text_boxes_dont_start_a_drag()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        var row = Row(w, item);

        var p = At(row, w);
        w.MouseMove(p, RawInputModifiers.LeftMouseButton); // no press before
        Assert.False(BoardWindow.IsDragging);

        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(p + new Point(1, 1), RawInputModifiers.LeftMouseButton); // a tiny move
        Assert.False(BoardWindow.IsDragging);
        w.MouseUp(p + new Point(1, 1), MouseButton.Left);
        w.MouseMove(p + new Point(0, 20)); // button released
        Assert.False(BoardWindow.IsDragging);

        // pressing on the complete circle (a button) is a click, not a drag
        var check = Find<Button>(row, x => x.Classes.Contains("check"))!;
        var cp = At(check, w);
        w.MouseDown(cp, MouseButton.Left);
        w.MouseMove(cp + new Point(0, 30), RawInputModifiers.LeftMouseButton);
        Assert.False(BoardWindow.IsDragging);
        w.MouseUp(cp + new Point(0, 30), MouseButton.Left);

        // nor pressing in a text box
        var box = AddBox(w, w.Board.Categories[0]);
        var bp = At(box, w);
        w.MouseDown(bp, MouseButton.Left);
        w.MouseMove(bp + new Point(0, 30), RawInputModifiers.LeftMouseButton);
        Assert.False(BoardWindow.IsDragging);
        w.MouseUp(bp + new Point(0, 30), MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void A_sideways_move_also_starts_a_drag()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var row = Row(w, w.Board.Categories[0].Items[0]);
        var p = At(row, w);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(p + new Point(30, 0), RawInputModifiers.LeftMouseButton); // only to the side
        Assert.True(BoardWindow.IsDragging);
        w.MouseUp(p + new Point(30, 0), MouseButton.Left);
        Pump();
        Assert.False(BoardWindow.IsDragging);
    }

    [AvaloniaFact]
    public void Escape_cancels_a_drag()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        Pick(w, Row(w, cat.Items[0]));
        Over(w, Row(w, cat.Items[1]), 0.75);
        Press(w, Key.Escape);
        Assert.False(BoardWindow.IsDragging);
        Assert.False(Line(w).IsVisible);
        Release(); // the release after a cancelled drag drops nothing
        Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void A_click_on_a_row_ends_editing_elsewhere()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var (a, b) = (w.Board.Categories[0].Items[0], w.Board.Categories[0].Items[1]);
        a.IsEditing = true;
        Pump();
        EditBox(w, a).Text = "a2";
        var p = At(Row(w, b), w);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.Equal("a2", a.Text);
        Assert.Same(Row(w, b), Focused(w));
        Assert.True(b.IsActive);
    }

    [AvaloniaFact]
    public void Dragging_near_the_edges_scrolls_the_list()
    {
        var w = OpenNew(("A", Enumerable.Range(1, 40).Select(i => "item " + i).ToArray()));
        var scroller = Named<ScrollViewer>(w, "Scroller");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        var cat = w.Board.Categories[0];

        Pick(w, Row(w, cat.Items[0]));
        var bottom = scroller.TranslatePoint(new Point(20, scroller.Bounds.Height - 5), w)!.Value;
        w.MouseMove(bottom, RawInputModifiers.LeftMouseButton);
        UiHost.Wait(TimeSpan.FromMilliseconds(200));
        double down = scroller.Offset.Y;
        Assert.True(down > 0, $"dragging={BoardWindow.IsDragging} extent={scroller.Extent} viewport={scroller.Viewport} bottom={bottom}");

        var middle = scroller.TranslatePoint(new Point(20, scroller.Bounds.Height / 2), w)!.Value;
        w.MouseMove(middle, RawInputModifiers.LeftMouseButton); // middle: stays
        UiHost.Wait(TimeSpan.FromMilliseconds(50));
        down = scroller.Offset.Y;
        UiHost.Wait(TimeSpan.FromMilliseconds(100));
        Assert.InRange(scroller.Offset.Y, down - 12, down); // doesn't keep scrolling down

        var top = scroller.TranslatePoint(new Point(20, 5), w)!.Value;
        w.MouseMove(top, RawInputModifiers.LeftMouseButton); // top edge
        UiHost.Wait(TimeSpan.FromMilliseconds(100));
        Assert.True(scroller.Offset.Y < down);

        w.MouseMove(new Point(-5000, -5000), RawInputModifiers.LeftMouseButton); // outside the widget: stays
        Pump(); // the move is handled (a scroll step that was already due may run first)
        double now = scroller.Offset.Y;
        UiHost.Wait(TimeSpan.FromMilliseconds(100));
        Assert.Equal(now, scroller.Offset.Y);

        w.MouseUp(new Point(-5000, -5000), MouseButton.Left);
        Pump();
        BoardWindow.AutoScrollTick(); // after the drag the timer stops on its own
        Assert.False(BoardWindow.IsDragging);
    }

    [AvaloniaFact]
    public void Widgets_that_dont_scroll_are_left_alone()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        Pick(w, Row(w, cat.Items[0]));
        var scroller = Named<ScrollViewer>(w, "Scroller");
        w.MouseMove(scroller.TranslatePoint(new Point(20, scroller.Bounds.Height - 5), w)!.Value, RawInputModifiers.LeftMouseButton);
        UiHost.Wait(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, scroller.Offset.Y);
        Release();
    }

    [AvaloniaFact]
    public void Dropping_a_category_on_a_widget_deleted_meanwhile_changes_nothing()
    {
        var w = OpenNew(("A", new string[0]), ("B", new string[0]));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
        Pick(w, Row(w, a));
        Over(w, Row(w, b), 0.75);
        Store.Data.Boards.Remove(w.Board); // e.g. by an import while dragging
        Release();
        Assert.Equal(new[] { "A", "B" }, w.Board.Categories.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Rows_slide_to_their_new_places()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var b = cat.Items[1];
        Pick(w, Row(w, cat.Items[0]));
        Over(w, Row(w, b), 0.75);
        _source!.MouseUp(_last, MouseButton.Left);
        Dispatcher().RunJobs(Avalonia.Threading.DispatcherPriority.Loaded);
        // right after the drop, b starts where it was (one row lower) and slides up
        Assert.NotNull(Row(w, b).RenderTransform);
        UiHost.Wait(TimeSpan.FromMilliseconds(300));
        Assert.Null(Row(w, b).RenderTransform);
    }

    static Avalonia.Threading.Dispatcher Dispatcher() => Avalonia.Threading.Dispatcher.UIThread;
}
