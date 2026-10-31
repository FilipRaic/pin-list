using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Xunit;

namespace PinList.Tests;

/// <summary>The widget window: position, size, lock, look, editing, adding, completing and deleting.</summary>
public class BoardWindowTests : UiTest
{
    static Button LockButton(BoardWindow w) => Named<Button>(w, "LockBtn");
    static Button NamedButton(Visual w, string name) => Find<Button>(w, b => NameOf(b) == name)!;
    static Grid Header(BoardWindow w) => Named<Grid>(w, "Header");
    static Button CheckButton(BoardWindow w, TodoItem item) => Find<Button>(Row(w, item), b => b.Classes.Contains("check"))!;
    static Button CollapseButton(BoardWindow w, Category cat) => Find<Button>(Row(w, cat), b => b.Classes.Contains("collapse"))!;

    [AvaloniaFact]
    public void Opens_with_the_saved_size_position_and_title()
    {
        var board = MakeBoard(("Work", new[] { "a" }));
        board.Title = "Groceries";
        board.Width = 350; board.Height = 420; board.X = 120; board.Y = 130;
        var w = Open(board);

        Assert.Equal((350.0, 420.0), (w.Width, w.Height));
        Assert.Equal(120, w.Left, 0);
        Assert.Equal(130, w.Top, 0);
        Assert.Equal("Groceries – PinList", w.Title);
        Assert.Equal(0, Saver.Requests); // opening doesn't count as a change
    }

    [AvaloniaFact]
    public void Too_small_saved_sizes_are_raised_to_the_minimum()
    {
        var board = MakeBoard();
        board.Width = 10; board.Height = 10;
        var w = Open(board);
        Assert.Equal((w.MinWidth, w.MinHeight), (w.Width, w.Height));
    }

    [AvaloniaFact]
    public void A_widget_off_every_screen_is_shown_on_the_main_screen_without_losing_its_place()
    {
        var board = MakeBoard();
        board.X = -100000; board.Y = -100000;
        var w = Open(board);

        var wa = Displays.Primary;
        Assert.InRange(w.Left, wa.Left - 1, wa.Right - w.Width + 1);
        Assert.InRange(w.Top, wa.Top - 1, wa.Bottom);
        Assert.Equal(-100000, board.X); // the saved place is kept for when the monitor comes back

        // a second one is placed a little further down, so they don't hide each other
        var other = MakeBoard();
        other.X = -100000;
        var w2 = Open(other);
        Assert.Equal(w.Top + 30, w2.Top, 0);
    }

    [AvaloniaFact]
    public void Moving_and_resizing_is_saved_but_not_while_maximized()
    {
        var w = OpenNew(("A", new string[0]));
        w.Left = 210.4;
        w.Width = 333;
        Pump();
        int saves = Saver.Requests;
        w.SaveBounds(); // what the end of a drag or resize does (WM_EXITSIZEMOVE)
        Assert.Equal((210.0, 333.0), (w.Board.X, w.Board.Width));
        Assert.True(Saver.Requests > saves);

        saves = Saver.Requests;
        w.SaveBounds(); // a click on the header without moving
        Assert.Equal(saves, Saver.Requests);

        w.WindowState = WindowState.Maximized;
        Pump();
        w.Left = 500; // not a real move while maximized
        w.SaveBounds();
        Assert.Equal(saves, Saver.Requests);
        w.WindowState = WindowState.Normal;
    }

    [AvaloniaFact]
    public void Moves_of_an_inactive_widget_are_not_saved()
    {
        var old = BoardWindow.KeyMoveSaveDelay;
        BoardWindow.KeyMoveSaveDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            Deactivate(w); // another window is active
            Assert.False(w.IsActive);
            var (x, y, width) = (w.Board.X, w.Board.Y, w.Board.Width);
            int saves = Saver.Requests;

            // e.g. the monitor went to sleep and Windows moved the widget to another one
            w.Left += 300;
            w.Top += 50;
            w.Width += 40;
            UiHost.Wait(TimeSpan.FromMilliseconds(300));

            Assert.Equal((x, y, width), (w.Board.X, w.Board.Y, w.Board.Width));
            Assert.Equal(saves, Saver.Requests);
        }
        finally { BoardWindow.KeyMoveSaveDelay = old; }
    }

    [AvaloniaFact]
    public void Moves_of_the_active_widget_are_saved_unless_monitors_change()
    {
        var old = BoardWindow.KeyMoveSaveDelay;
        BoardWindow.KeyMoveSaveDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            w.Activate();
            Pump();
            Assert.True(w.IsActive);

            // Win+Arrow: no drag, so no WM_EXITSIZEMOVE; saved after a short wait
            w.Left += 100;
            Assert.NotEqual(Math.Round(w.Left), w.Board.X);
            Assert.True(UiHost.WaitUntil(() => Math.Round(w.Left) == w.Board.X));
            // also when only the top, the width or the height changed (Win+Shift+Arrow, macOS window tiling)
            w.Top += 30;
            Assert.True(UiHost.WaitUntil(() => Math.Round(w.Top) == w.Board.Y));
            w.Width += 30;
            Assert.True(UiHost.WaitUntil(() => w.Width == w.Board.Width));
            w.Height += 30;
            Assert.True(UiHost.WaitUntil(() => w.Height == w.Board.Height));

            // nor while the monitors are still settling
            double x = w.Board.X;
            BoardWindow.DisplayChanging = true;
            w.Left += 100;
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            BoardWindow.DisplayChanging = false;
            Assert.Equal(x, w.Board.X);

            // nor where PinList itself put the widget (e.g. on the primary monitor while its own is gone)
            w.Board.X = -100000;
            w.EnsureOnScreen();
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            Assert.Equal(-100000, w.Board.X);
        }
        finally { BoardWindow.KeyMoveSaveDelay = old; BoardWindow.DisplayChanging = false; }
    }

    [AvaloniaFact]
    public void Window_messages_save_a_finished_move_and_cancel_one_caused_by_a_monitor_change()
    {
        var old = BoardWindow.KeyMoveSaveDelay;
        BoardWindow.KeyMoveSaveDelay = TimeSpan.FromMilliseconds(50);
        try
        {
            var w = OpenNew(("A", new string[0]));
            bool handled = false;
            w.Left = 250;
            w.WndProc(IntPtr.Zero, 0x0232, IntPtr.Zero, IntPtr.Zero, ref handled); // WM_EXITSIZEMOVE: the user let go
            Assert.Equal(250, w.Board.X);

            w.Left = 350; // Win+Arrow…
            w.WndProc(IntPtr.Zero, 0x007E, IntPtr.Zero, IntPtr.Zero, ref handled); // …then WM_DISPLAYCHANGE: Windows did it
            UiHost.Wait(TimeSpan.FromMilliseconds(200));
            Assert.Equal(250, w.Board.X);
            w.WndProc(IntPtr.Zero, 0x0001, IntPtr.Zero, IntPtr.Zero, ref handled); // anything else: ignored
            Assert.False(handled);
        }
        finally { BoardWindow.KeyMoveSaveDelay = old; }
    }

    [AvaloniaFact]
    public void Lock_button_locks_position_and_size()
    {
        var w = OpenNew(("A", new string[0]));
        var grips = Named<Panel>(w, "Grips");
        Assert.Equal("Lock position and size", NameOf(LockButton(w)));
        Assert.True(grips.IsVisible);

        Click(LockButton(w));
        Assert.True(w.Board.Locked);
        Assert.Equal("Unlock position and size", NameOf(LockButton(w)));
        Assert.False(grips.IsVisible); // no resizing at the edges
        Assert.Equal("Position and size locked", A11y.LastAnnouncement);

        Click(LockButton(w));
        Assert.False(w.Board.Locked);
        Assert.True(grips.IsVisible);
    }

    [AvaloniaFact]
    public void Dragging_the_header_moves_unless_locked_and_double_click_renames()
    {
        var w = OpenNew(("A", new string[0]));
        MouseDown(Header(w)); // the system move needs a real mouse; the app copes without
        w.Board.Locked = true;
        MouseDown(Header(w));
        Assert.False(w.Board.IsEditingTitle);

        MouseDown(Header(w), clicks: 2);
        Assert.True(w.Board.IsEditingTitle);
    }

    [AvaloniaFact]
    public void Edge_grips_resize_unless_locked()
    {
        var w = OpenNew(("A", new string[0]));
        foreach (var grip in Named<Panel>(w, "Grips").Children.OfType<Border>())
            MouseDown(grip); // the system resize needs a real mouse; every edge is wired up
        w.Board.Locked = true;
        MouseDown(Named<Panel>(w, "Grips").Children.OfType<Border>().First());
        Assert.Equal((320.0, 480.0), (w.Width, w.Height));
    }

    [AvaloniaFact]
    public void Without_a_system_resize_dragging_a_grip_resizes_the_widget_itself()
    {
        // macOS can't resize a window without a frame for the app, so PinList follows the mouse itself
        BoardWindow.SystemResize = false;
        var w = OpenNew(("A", new string[0]));
        var (left, top) = (w.Left, w.Top);

        // bottom-right corner: bigger, same place
        var p = new Point(w.Width - 3, w.Height - 3);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(p + new Point(40, 30), RawInputModifiers.LeftMouseButton);
        w.MouseUp(p + new Point(40, 30), MouseButton.Left);
        Pump();
        Assert.Equal((360.0, 510.0), (w.Width, w.Height));
        Assert.Equal((left, top), (w.Left, w.Top));
        Assert.Equal((360.0, 510.0), (w.Board.Width, w.Board.Height)); // saved

        // top-left corner: the opposite corner stays put
        p = new Point(3, 3);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(p + new Point(-20, -10), RawInputModifiers.LeftMouseButton);
        w.MouseUp(p + new Point(-20, -10), MouseButton.Left);
        Pump();
        Assert.Equal((380.0, 520.0), (w.Width, w.Height));
        Assert.Equal((left - 20, top - 10), (w.Left, w.Top));

        // not smaller than the smallest size; the left edge only changes the width
        p = new Point(3, w.Height / 2);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(p + new Point(1000, 50), RawInputModifiers.LeftMouseButton);
        w.MouseUp(p + new Point(1000, 50), MouseButton.Left);
        Pump();
        Assert.Equal((w.MinWidth, 520.0), (w.Width, w.Height));
        Assert.Equal(left - 20 + 380 - w.MinWidth, w.Left, 0);

        // every grip: dragged 10 right and 20 down
        foreach (var grip in Named<Panel>(w, "Grips").Children.OfType<Border>())
        {
            w.Width = 320; w.Height = 480; w.Left = left; w.Top = top;
            Pump();
            var tag = (string)grip.Tag!;
            var at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), w)!.Value;
            w.MouseDown(at, MouseButton.Left);
            w.MouseMove(at + new Point(10, 20), RawInputModifiers.LeftMouseButton);
            w.MouseUp(at + new Point(10, 20), MouseButton.Left);
            Pump();
            double width = tag.Contains('W') ? 310 : tag.Contains('E') ? 330 : 320;
            double height = tag.Contains('N') ? 460 : tag.Contains('S') ? 500 : 480;
            Assert.Equal((tag, width, height), (tag, w.Width, w.Height));
            Assert.Equal((tag, tag.Contains('W') ? left + 10 : left, tag.Contains('N') ? top + 20 : top), (tag, w.Left, w.Top));
        }

        // moving the mouse without pressing a grip first does nothing
        var size = (w.Width, w.Height);
        w.MouseMove(new Point(3, 3), RawInputModifiers.LeftMouseButton);
        Assert.Equal(size, (w.Width, w.Height));
    }

    [AvaloniaFact]
    public void Alt_F4_does_not_close_a_widget_but_shutting_down_does()
    {
        var w = OpenNew(("A", new string[0]));
        w.Close();
        Pump();
        Assert.True(w.IsVisible);

        App.ShuttingDown = true;
        w.Close();
        Pump();
        Assert.False(w.IsVisible);
        Assert.DoesNotContain(w, App.OpenWindows);
    }

    [AvaloniaFact]
    public void Window_title_follows_the_widget_title()
    {
        var w = OpenNew(("A", new string[0]));
        w.Board.Title = "";
        Assert.Equal("PinList", w.Title);
    }

    [AvaloniaFact]
    public void Look_follows_style_transparency_and_contrast_theme()
    {
        var w = OpenNew(("A", new string[0]));
        Color Bg() => ((ImmutableSolidColorBrush)w.Resources["BoardBg"]!).Color;
        Assert.Equal(0xFF, Bg().A);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, w.RequestedThemeVariant);

        w.Board.Transparent = true;
        w.RefreshAppearance();
        Assert.Equal(0xE6, Bg().A);

        var contrast = new ContrastColors(Colors.Black, Colors.Yellow, Colors.Cyan, Colors.Black, Colors.Lime);
        Themes.ContrastSource = () => contrast;
        w.RefreshAppearance();
        Assert.Equal(Colors.Black, Bg());
        Assert.Equal(Colors.Lime, ((ImmutableSolidColorBrush)w.Resources["Overdue"]!).Color);
    }

    [AvaloniaFact]
    public void Text_size_scales_the_content()
    {
        A11y.TextScaleSource = () => 1.25;
        A11y.RefreshTextScale();
        var w = OpenNew(("A", new string[0]));
        var t = (ScaleTransform)Named<LayoutTransformControl>(w, "Scaler").LayoutTransform!;
        Assert.Equal(1.25, t.ScaleX);
    }

    // ================= Highlight =================

    [AvaloniaFact]
    public void Clicking_empty_space_or_leaving_the_widget_clears_the_highlight()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        MouseDown(Row(w, item));
        Assert.True(item.IsActive);

        MouseDown(Header(w));
        Assert.False(item.IsActive);

        MouseDown(Row(w, item));
        Deactivate(w); // e.g. a click on the desktop
        Assert.False(item.IsActive);
    }

    [AvaloniaFact]
    public void Leaving_another_widget_keeps_this_highlight()
    {
        var w1 = OpenNew(("A", new[] { "a" }));
        var w2 = OpenNew(("B", new[] { "b" }));
        var cat = w1.Board.Categories[0];
        MouseDown(Row(w1, cat));
        Deactivate(w2);
        Assert.True(cat.IsActive);
    }

    [AvaloniaFact]
    public void Highlight_shows_an_item_and_opens_its_category()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        cat.Collapsed = true;
        Pump();

        BoardWindow.Highlight(cat.Items[1]);
        Pump();

        Assert.False(cat.Collapsed);
        Assert.True(cat.Items[1].IsActive);
        Assert.Same(Row(w, cat.Items[1]), Focused(w));

        BoardWindow.Highlight(new TodoItem()); // not in any list: nothing happens
        var orphan = MakeBoard(("Z", new[] { "z" })); // in the data but without a window
        BoardWindow.Highlight(orphan.Categories[0].Items[0]);
    }

    // ================= Categories =================

    [AvaloniaFact]
    public void New_category_button_adds_one_ready_to_be_named()
    {
        var w = OpenNew(("A", new string[0]));
        Click(NamedButton(w, "New category"));
        Pump();

        var cat = w.Board.Categories[1];
        Assert.True(cat.IsEditing);
        var box = EditBox(w, cat);
        Assert.True(box.IsFocused);
        Assert.Equal("New category", box.SelectedText); // the default name is selected, so typing replaces it
    }

    [AvaloniaFact]
    public void Collapse_button_hides_and_shows_items()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];
        var button = CollapseButton(w, cat);
        Assert.Equal("Collapse or expand", NameOf(button));

        Click(button);
        Assert.True(cat.Collapsed);
        Assert.Contains("collapsed", button.Classes); // the arrow points right
        Assert.False(Row(w, cat.Items[0]).IsEffectivelyVisible);
        Assert.True(Row(w, cat).IsEffectivelyVisible); // header stays
        Assert.Equal("A collapsed", A11y.LastAnnouncement);

        Click(button);
        Assert.False(cat.Collapsed);
    }

    // ================= Adding items =================

    [AvaloniaFact]
    public void Enter_adds_an_item_and_keeps_the_box_ready()
    {
        var w = OpenNew(("A", new string[0]));
        var cat = w.Board.Categories[0];
        var box = AddBox(w, cat);
        Assert.Equal("Add item to A", NameOf(box));
        box.Focus();

        box.Text = "  Buy milk  ";
        Press(box, Key.Enter);
        Assert.Equal("Buy milk", cat.Items.Single().Text);
        Assert.True(cat.Items[0].IsActive); // Delete would remove it
        Assert.Equal("", box.Text);
        Assert.True(box.IsFocused);
        Assert.Equal("Added Buy milk", A11y.LastAnnouncement);

        Press(box, Key.Enter); // empty: nothing
        Assert.Single(cat.Items);
    }

    [AvaloniaFact]
    public void Shift_Enter_does_not_add_and_Esc_clears()
    {
        var w = OpenNew(("A", new string[0]));
        var cat = w.Board.Categories[0];
        var box = AddBox(w, cat);
        box.Focus();
        box.Text = "first line";

        Press(box, Key.Enter, KeyModifiers.Shift);
        Assert.Empty(cat.Items);

        Press(box, Key.Escape);
        Assert.Equal("", box.Text);
        Assert.False(box.IsFocused);
    }

    [AvaloniaFact]
    public void Arrow_keys_leave_an_empty_add_box()
    {
        var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
        var a = w.Board.Categories[0];
        var box = AddBox(w, a);
        box.Focus();

        Press(box, Key.Up);
        Assert.True(ReferenceEquals(Row(w, a.Items[0]), Focused(w)), "focused: " + (Focused(w) as Control)?.GetType().Name + " " + NameOf((Control)Focused(w)!) + " classes " + string.Join(",", ((Control)Focused(w)!).Classes));

        box.Focus();
        Press(box, Key.Down);
        Assert.True(ReferenceEquals(Row(w, w.Board.Categories[1]), Focused(w)), "focused: " + NameOf((Control)Focused(w)!) + " tip " + ToolTip.GetTip((Control)Focused(w)!) + " classes " + string.Join(",", ((Control)Focused(w)!).Classes));

        box.Focus();
        box.Text = "typing";
        Press(box, Key.Up); // with text, arrows move the caret instead
        Assert.Same(box, Focused(w));
    }

    // ================= Completing =================

    [AvaloniaFact]
    public void The_circle_completes_after_a_moment_and_a_second_click_cancels()
    {
        BoardWindow.CompleteDelay = TimeSpan.FromMilliseconds(100);
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];
        var check = CheckButton(w, a);
        Assert.Equal("Complete a", NameOf(check));

        Click(check);
        Assert.True(a.IsCompleting);
        Assert.Contains("completing", Row(w, a).Classes); // crossed out, the circle filled
        Click(check);
        Assert.False(a.IsCompleting);
        Assert.Equal("Cancelled", A11y.LastAnnouncement);
        UiHost.Wait(TimeSpan.FromMilliseconds(300));
        Assert.Contains(a, cat.Items); // cancelled

        Click(check);
        Assert.True(a.IsCompleting, "restarted");
        var ok = UiHost.WaitUntil(() => !cat.Items.Contains(a));
        Assert.True(ok, "errors: " + string.Join(" | ", UiHost.UnhandledErrors.Select(e => e.Message)) + " completing=" + a.IsCompleting);
        Assert.Equal("a", Store.Data.Archive[0].Text);
        Assert.Equal("Completed a. Ctrl+Z undoes.", A11y.LastAnnouncement);
    }

    // ================= Editing =================

    [AvaloniaFact]
    public void Double_click_edits_and_Enter_saves()
    {
        var w = OpenNew(("A", new[] { "old" }));
        var item = w.Board.Categories[0].Items[0];
        MouseDown(Row(w, item), clicks: 2);
        Assert.True(item.IsEditing);
        Pump();

        var box = EditBox(w, item);
        Assert.Equal("old", box.Text);
        Assert.True(box.IsFocused);
        Assert.Equal(3, box.CaretIndex); // caret at the end
        Assert.Equal("Item text", NameOf(box));

        Press(box, Key.Enter, KeyModifiers.Shift); // Shift+Enter is a new line, not save
        Assert.True(item.IsEditing);
        box.Text = "new\r\nsecond line";

        Press(box, Key.Enter);
        Assert.False(item.IsEditing);
        Assert.Equal("new\nsecond line", item.Text);
        Pump();
        Assert.Same(Row(w, item), Focused(w)); // focus back on the row

        Actions.Undo();
        Assert.Equal("old", item.Text);
    }

    [AvaloniaFact]
    public void Esc_cancels_editing_and_clicking_elsewhere_saves()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var (a, b) = (w.Board.Categories[0].Items[0], w.Board.Categories[0].Items[1]);

        a.IsEditing = true;
        Pump();
        EditBox(w, a).Text = "changed";
        Press(EditBox(w, a), Key.Escape);
        Assert.Equal("a", a.Text);
        Pump();
        Assert.Same(Row(w, a), Focused(w));

        a.IsEditing = true;
        Pump();
        EditBox(w, a).Text = "changed";
        MouseDown(Row(w, b)); // focus moves away: saved
        Assert.Equal("changed", a.Text);
        Assert.False(a.IsEditing);
    }

    [AvaloniaFact]
    public void Emptying_an_item_deletes_it()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];
        a.IsEditing = true;
        Pump();
        EditBox(w, a).Text = "   ";
        Press(EditBox(w, a), Key.Enter);
        Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Renaming_a_category_ignores_an_empty_name()
    {
        var w = OpenNew(("Work", new string[0]));
        var cat = w.Board.Categories[0];
        MouseDown(Row(w, cat), clicks: 2);
        Pump();
        var box = EditBox(w, cat);
        Assert.Equal("Category name", NameOf(box));
        Assert.Equal(4, box.CaretIndex); // not selected: it isn't the default name

        box.Text = "";
        Press(box, Key.Enter);
        Assert.Equal("Work", cat.Name);

        cat.IsEditing = true;
        Pump();
        EditBox(w, cat).Text = "Home";
        Press(EditBox(w, cat), Key.Enter);
        Assert.Equal("Home", cat.Name);
        Pump();
        Assert.Same(Row(w, cat), Focused(w));

        cat.IsEditing = true;
        Pump();
        Press(EditBox(w, cat), Key.Escape);
        Assert.False(cat.IsEditing);
    }

    [AvaloniaFact]
    public void Renaming_the_widget_and_an_empty_title()
    {
        var w = OpenNew(("A", new string[0]));
        w.Board.IsEditingTitle = true;
        Pump();
        var box = EditBox(w, w.Board);
        Assert.Equal("Window title", NameOf(box));
        box.Text = "  ";
        Press(box, Key.Enter);
        Assert.Equal("", w.Board.Title); // allowed: a widget without a title
        Pump();
        Assert.Same(Row(w, w.Board.Categories[0]), Focused(w)); // focus goes into the list

        w.Board.IsEditingTitle = true;
        Pump();
        Press(EditBox(w, w.Board), Key.Escape);
        Assert.False(w.Board.IsEditingTitle);
    }

    [AvaloniaFact]
    public void Edit_boxes_of_deleted_things_close_quietly()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];
        var item = cat.Items[0];
        item.IsEditing = true;
        Pump();
        var box = EditBox(w, item);
        box.Text = "";
        Press(box, Key.Enter); // deletes it; focus can't return to its row
        Assert.Empty(cat.Items);
    }

    // ================= Deleting the widget =================

    [AvaloniaFact]
    public void Delete_window_asks_first()
    {
        var w = OpenNew(("A", new[] { "a", "b" }), ("B", new string[0]));
        w.Board.Title = "Work";
        Click(Named<Button>(w, "MenuBtn"));
        ChooseFromMenu(w.LastMenu!, "Delete window"); // Esc
        Assert.Contains("Delete \"Work\"?: This removes the window with 2 categories and 2 items. Items are not moved to the archive.", Dialogs.Shown);
        Assert.True(w.IsVisible);

        Dialogs.ChooseAnswer = _ => 1;
        Click(Named<Button>(w, "MenuBtn"));
        ChooseFromMenu(w.LastMenu!, "Delete window");
        Assert.False(w.IsVisible);
        Assert.DoesNotContain(w.Board, Store.Data.Boards);
        UiHost.Wait(TimeSpan.FromMilliseconds(300));
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board); // not reopened
    }

    [AvaloniaFact]
    public void Deleting_an_empty_untitled_window_says_so()
    {
        var board = MakeBoard();
        board.Title = "";
        var w = Open(board);
        Click(Named<Button>(w, "MenuBtn"));
        ChooseFromMenu(w.LastMenu!, "Delete window");
        Assert.Contains("Delete this window?: This removes the window from your desktop.", Dialogs.Shown);
    }
}
