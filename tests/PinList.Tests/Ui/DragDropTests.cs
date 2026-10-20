using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using PinList;
using Xunit;
using Point = System.Windows.Point;
using DragEventArgs = System.Windows.DragEventArgs;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;

namespace PinList.Tests;

/// <summary>
/// Drag and drop of items and categories. The real drag loop (DoDragDrop) is replaced by the test, which
/// then raises the DragOver / Drop events the system would raise while the mouse moves.
/// </summary>
public class DragDropTests : UiTest
{
    static readonly ConstructorInfo DragArgsCtor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];

    /// <summary>A DragOver or Drop event at a height within the element (0 = top, 1 = bottom).</summary>
    static DragEventArgs DragArgs(FrameworkElement target, RoutedEvent e, double height)
    {
        var args = (DragEventArgs)DragArgsCtor.Invoke(new object[]
        {
            new DataObject("PinList.Drag", "1"), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move,
            target, new Point(5, target.ActualHeight * height),
        });
        args.RoutedEvent = e;
        args.Source = target;
        return args;
    }

    static DragDropEffects Over(FrameworkElement target, double height = 0.25)
    {
        var args = DragArgs(target, DragDrop.DragOverEvent, height);
        target.RaiseEvent(args);
        return args.Effects;
    }

    static void DropOn(FrameworkElement target, double height = 0.25) => target.RaiseEvent(DragArgs(target, DragDrop.DropEvent, height));

    /// <summary>
    /// Presses on <paramref name="source"/>, moves far enough to start a drag, and runs <paramref name="duringDrag"/>
    /// while the drag is going on (that's where DragOver/Drop are raised).
    /// </summary>
    void Drag(FrameworkElement source, Action duringDrag)
    {
        var position = new Point(10, 10);
        InputState.Position = (_, _) => position;
        InputState.IsLeftButtonPressed = _ => true;
        bool ran = false;
        InputState.DoDragDrop = (_, _, _) =>
        {
            Assert.Equal(0.4, source.Opacity); // the dragged element fades while dragging
            duringDrag();
            ran = true;
            return DragDropEffects.Move;
        };
        MouseDown(source);
        position = new Point(10, 40); // far enough to count as a drag
        source.RaiseEvent(MouseMove(source));
        Pump();
        Assert.True(ran);
        Assert.Equal(1, source.Opacity);
    }

    static Border AddRow(BoardWindow w, Category cat) =>
        Find<Border>(w, b => ReferenceEquals(b.Tag, cat) && b is not AccessibleRow)!;

    static Border EndArea(BoardWindow w) => Find<Border>(w, b => b.Height == 40)!;

    [Fact]
    public void Dragging_an_item_below_another()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b", "c" }));
            var cat = w.Board.Categories[0];
            var (a, c) = (cat.Items[0], cat.Items[2]);
            Drag(Row(w, a), () =>
            {
                Assert.Equal(DragDropEffects.Move, Over(Row(w, c), 0.75));
                Assert.Equal(new Thickness(0, 0, 0, 2), Row(w, c).BorderThickness); // line below
                DropOn(Row(w, c), 0.75);
                Assert.Equal(new Thickness(0), Row(w, c).BorderThickness);
            });
            Assert.Equal(new[] { "b", "c", "a" }, cat.Items.Select(i => i.Text));
        });
    }

    [Fact]
    public void Dragging_an_item_into_another_widget()
    {
        Ui(() =>
        {
            var w1 = OpenNew(("A", new[] { "a" }));
            var w2 = OpenNew(("B", new[] { "b" }));
            var a = w1.Board.Categories[0].Items[0];
            var b = w2.Board.Categories[0].Items[0];
            Drag(Row(w1, a), () =>
            {
                Over(Row(w2, b), 0.25);
                Assert.Equal(new Thickness(0, 2, 0, 0), Row(w2, b).BorderThickness); // line above
                Over(Row(w2, b), 0.75); // same row, other half: the line moves without changing the height
                Assert.Equal(new Thickness(0, 0, 0, 2), Row(w2, b).BorderThickness);
                DropOn(Row(w2, b), 0.25);
            });
            Assert.Equal(new[] { "a", "b" }, w2.Board.Categories[0].Items.Select(i => i.Text));
            Assert.Empty(w1.Board.Categories[0].Items);
        });
    }

    [Fact]
    public void An_item_onto_itself_or_onto_empty_space_is_refused()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var cat = w.Board.Categories[0];
            var a = cat.Items[0];
            Drag(Row(w, a), () =>
            {
                Over(Row(w, cat.Items[1]));
                Assert.Equal(DragDropEffects.None, Over(Row(w, a))); // itself: the other row's line goes away
                Assert.Equal(new Thickness(0), Row(w, cat.Items[1]).BorderThickness);
                DropOn(Row(w, a));
                Assert.Equal(DragDropEffects.None, Over(EndArea(w))); // the end area takes categories only
                Assert.Equal(DragDropEffects.None, Over((FrameworkElement)w.Content)); // anywhere else
                DropOn((FrameworkElement)w.Content);
            });
            Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text));
        });
    }

    [Fact]
    public void Dropping_an_item_on_a_category_header_or_add_row()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }), ("B", new[] { "b1", "b2" }));
            var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
            var item = a.Items[0];

            Drag(Row(w, item), () =>
            {
                Assert.Equal(DragDropEffects.Move, Over(Row(w, b), 0.25));
                Assert.Equal(new Thickness(0, 0, 0, 2), Row(w, b).BorderThickness); // always "below the header"
                DropOn(Row(w, b));
            });
            Assert.Equal(new[] { "a", "b1", "b2" }, b.Items.Select(i => i.Text)); // to the top

            Drag(Row(w, item), () =>
            {
                Assert.Equal(DragDropEffects.Move, Over(AddRow(w, b)));
                DropOn(AddRow(w, b));
            });
            Assert.Equal(new[] { "b1", "b2", "a" }, b.Items.Select(i => i.Text)); // to the end
        });
    }

    [Fact]
    public void Dragging_categories()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]), ("B", new string[0]), ("C", new string[0]));
            var (a, b, c) = (w.Board.Categories[0], w.Board.Categories[1], w.Board.Categories[2]);

            Drag(Row(w, a), () =>
            {
                Assert.Equal(DragDropEffects.None, Over(Row(w, a))); // itself
                Assert.Equal(DragDropEffects.None, Over(AddRow(w, b))); // add rows take items only
                Assert.Equal(DragDropEffects.Move, Over(Row(w, b), 0.75));
                DropOn(Row(w, b), 0.75);
            });
            Assert.Equal(new[] { "B", "A", "C" }, w.Board.Categories.Select(x => x.Name));

            Drag(Row(w, b), () =>
            {
                Assert.Equal(DragDropEffects.Move, Over(EndArea(w)));
                DropOn(EndArea(w));
            });
            Assert.Equal(new[] { "A", "C", "B" }, w.Board.Categories.Select(x => x.Name));

            Drag(Row(w, c), () =>
            {
                Assert.Equal(DragDropEffects.None, Over(Row(w, c)));
                DropOn(Row(w, c)); // onto itself: nothing
                var item = OpenNew(("Z", new[] { "z" })).Board.Categories[0].Items[0];
                Assert.Equal(DragDropEffects.None, Over(Row(App.OpenWindows.Last(), item))); // items don't take categories
            });
        });
    }

    [Fact]
    public void Small_moves_buttons_and_text_boxes_dont_start_a_drag()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var row = Row(w, w.Board.Categories[0].Items[0]);
            int drags = 0;
            InputState.DoDragDrop = (_, _, _) => { drags++; return DragDropEffects.None; };
            InputState.IsLeftButtonPressed = _ => true;
            var position = new Point(10, 10);
            InputState.Position = (_, _) => position;

            row.RaiseEvent(MouseMove(row)); // no press before
            MouseDown(row);
            position = new Point(11, 11); // a tiny move
            row.RaiseEvent(MouseMove(row));
            InputState.IsLeftButtonPressed = _ => false;
            position = new Point(100, 100);
            row.RaiseEvent(MouseMove(row)); // button released
            Assert.Equal(0, drags);

            // pressing on the complete circle (a button) is a click, not a drag
            InputState.IsLeftButtonPressed = _ => true;
            var check = Find<System.Windows.Controls.Button>(row)!;
            position = new Point(10, 10);
            MouseDown(check);
            position = new Point(100, 100);
            row.RaiseEvent(MouseMove(row));
            Assert.Equal(0, drags);
        });
    }

    [Fact]
    public void A_click_on_a_row_ends_editing_elsewhere()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var (a, b) = (w.Board.Categories[0].Items[0], w.Board.Categories[0].Items[1]);
            a.IsEditing = true;
            Pump();
            EditBox(w, a).Text = "a2";
            MouseDown(Row(w, b));
            Assert.Equal("a2", a.Text);
            Assert.Same(Row(w, b), Keyboard.FocusedElement);
            Assert.True(b.IsActive);
        });
    }

    [Fact]
    public void Text_inside_a_row_is_not_a_button()
    {
        // walking up from text that isn't a visual (a Run) uses the logical tree
        var isInteractive = typeof(BoardWindow).GetMethod("IsInteractive", BindingFlags.NonPublic | BindingFlags.Static)!;
        Ui(() =>
        {
            var block = new TextBlock();
            var run = new Run("x");
            block.Inlines.Add(run);
            Assert.False((bool)isInteractive.Invoke(null, new object?[] { run, block })!);
            Assert.False((bool)isInteractive.Invoke(null, new object?[] { null, block })!);
        });
    }

    [Fact]
    public void Dragging_near_the_edges_scrolls_the_list()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", Enumerable.Range(1, 40).Select(i => "item " + i).ToArray()));
            w.Height = 250;
            Pump();
            var scroller = Named<ScrollViewer>(w, "Scroller");
            Assert.True(scroller.ScrollableHeight > 0);
            var items = w.Board.Categories[0].Items;

            Point Screen(double y) => scroller.PointToScreen(new Point(20, y));

            Drag(Row(w, items[0]), () =>
            {
                InputState.CursorScreen = () => Screen(scroller.ActualHeight - 5); // bottom edge
                UiHost.Wait(TimeSpan.FromMilliseconds(150));
                Assert.True(scroller.VerticalOffset > 0);

                double down = scroller.VerticalOffset;
                InputState.CursorScreen = () => Screen(scroller.ActualHeight / 2); // middle: stays
                UiHost.Wait(TimeSpan.FromMilliseconds(100));
                Assert.Equal(down, scroller.VerticalOffset);

                InputState.CursorScreen = () => Screen(5); // top edge
                UiHost.Wait(TimeSpan.FromMilliseconds(150));
                Assert.True(scroller.VerticalOffset < down);

                InputState.CursorScreen = () => new Point(-50000, -50000); // outside the widget: stays
                double now = scroller.VerticalOffset;
                UiHost.Wait(TimeSpan.FromMilliseconds(100));
                Assert.Equal(now, scroller.VerticalOffset);
            });

            // after the drag the timer stops on its own
            var tick = typeof(BoardWindow).GetMethod("AutoScrollTick", BindingFlags.NonPublic | BindingFlags.Static)!;
            tick.Invoke(null, new object?[] { null, EventArgs.Empty });
        });
    }

    [Fact]
    public void Widgets_that_dont_scroll_are_left_alone()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var hidden = OpenNew(("B", new[] { "b" }));
            hidden.Hide();
            Drag(Row(w, w.Board.Categories[0].Items[0]), () =>
            {
                InputState.CursorScreen = () => Named<ScrollViewer>(w, "Scroller").PointToScreen(new Point(5, 5));
                UiHost.Wait(TimeSpan.FromMilliseconds(100));
            });
            Assert.Equal(0, Named<ScrollViewer>(w, "Scroller").VerticalOffset);
        });
    }
}
