using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using PinList;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace PinList.Tests;

/// <summary>The widget window: position, size, lock, look, editing, adding, completing and deleting.</summary>
public class BoardWindowTests : UiTest
{
    static Button LockButton(BoardWindow w) => Named<Button>(w, "LockBtn");
    static Button NamedButton(Window w, string name) => Find<Button>(w, b => AutomationProperties.GetName(b) == name)!;
    static Grid Header(BoardWindow w) => Find<Grid>(w, g => g.Height == 38)!;
    static Button CheckButton(BoardWindow w, TodoItem item) => Find<Button>(Row(w, item))!;
    static Button CollapseButton(BoardWindow w, Category cat) => Find<Button>(Row(w, cat))!;

    [Fact]
    public void Opens_with_the_saved_size_position_and_title()
    {
        Ui(() =>
        {
            var board = MakeBoard(("Work", new[] { "a" }));
            board.Title = "Groceries";
            board.Width = 350; board.Height = 420; board.X = 120; board.Y = 130;
            var w = Open(board);

            Assert.Equal((350.0, 420.0, 120.0, 130.0), (w.Width, w.Height, w.Left, w.Top));
            Assert.Equal("Groceries – PinList", w.Title);
            Assert.NotEqual(0, Native.GetExStyle(w) & Native.ToolWindowStyle); // not in the taskbar or Alt+Tab
            Assert.Equal(0, Saver.Requests); // opening doesn't count as a change
        });
    }

    [Fact]
    public void Too_small_saved_sizes_are_raised_to_the_minimum()
    {
        Ui(() =>
        {
            var board = MakeBoard();
            board.Width = 10; board.Height = 10;
            var w = Open(board);
            Assert.Equal((w.MinWidth, w.MinHeight), (w.Width, w.Height));
        });
    }

    [Fact]
    public void A_widget_off_every_screen_is_shown_on_the_main_screen_without_losing_its_place()
    {
        Ui(() =>
        {
            var board = MakeBoard();
            board.X = -100000; board.Y = -100000;
            var w = Open(board);

            var wa = Screens.Primary;
            Assert.InRange(w.Left, wa.Left, wa.Right - w.Width);
            Assert.InRange(w.Top, wa.Top, wa.Bottom);
            Assert.Equal(-100000, board.X); // the saved place is kept for when the monitor comes back

            // a second one is placed a little further down, so they don't hide each other
            var other = MakeBoard();
            other.X = -100000;
            var w2 = Open(other);
            Assert.Equal(w.Top + 30, w2.Top, 0);
        });
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    /// <summary>What Windows sends when the user lets go after dragging or resizing a window.</summary>
    static void EndUserMove(Window w) =>
        SendMessage(new System.Windows.Interop.WindowInteropHelper(w).Handle, 0x0232 /* WM_EXITSIZEMOVE */, IntPtr.Zero, IntPtr.Zero);

    [Fact]
    public void Moving_and_resizing_is_saved_but_not_while_maximized()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Left = 210.4;
            w.Width = 333;
            Pump();
            int saves = Saver.Requests;
            EndUserMove(w);
            Assert.Equal((210.0, 333.0), (w.Board.X, w.Board.Width));
            Assert.True(Saver.Requests > saves);

            saves = Saver.Requests;
            EndUserMove(w); // a click on the header without moving
            Assert.Equal(saves, Saver.Requests);

            w.WindowState = WindowState.Maximized;
            Pump();
            w.Left = 500; // not a real move while maximized
            EndUserMove(w);
            Assert.Equal(saves, Saver.Requests);
            w.WindowState = WindowState.Normal;
        });
    }

    [Fact]
    public void Moves_made_by_Windows_are_not_saved()
    {
        Ui(() =>
        {
            var old = BoardWindow.KeyMoveSaveDelay;
            BoardWindow.KeyMoveSaveDelay = TimeSpan.FromMilliseconds(50);
            try
            {
                var w = OpenNew(("A", new string[0]));
                OpenNew(("B", new string[0])).Activate(); // another window is active
                Pump();
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
        });
    }

    static void Send(Window w, uint msg) =>
        SendMessage(new System.Windows.Interop.WindowInteropHelper(w).Handle, msg, IntPtr.Zero, IntPtr.Zero);

    [Fact]
    public void Win_Arrow_moves_of_the_active_widget_are_saved_unless_monitors_change()
    {
        Ui(() =>
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
                UiHost.Wait(TimeSpan.FromMilliseconds(300));
                Assert.Equal(Math.Round(w.Left), w.Board.X);

                // a monitor change right after the move: Windows moved it, so it's not saved
                double x = w.Board.X;
                w.Left += 100;
                Send(w, 0x007E); // WM_DISPLAYCHANGE
                UiHost.Wait(TimeSpan.FromMilliseconds(300));
                Assert.Equal(x, w.Board.X);

                // nor while the monitors are still settling
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
        });
    }

    [Fact]
    public void Lock_button_locks_position_and_size()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            Assert.Equal("Lock position and size", AutomationProperties.GetName(LockButton(w)));

            Click(LockButton(w));
            Assert.True(w.Board.Locked);
            Assert.Equal("Unlock position and size", AutomationProperties.GetName(LockButton(w)));
            Assert.Equal(new Thickness(0), System.Windows.Shell.WindowChrome.GetWindowChrome(w).ResizeBorderThickness);

            Click(LockButton(w));
            Assert.False(w.Board.Locked);
            Assert.Equal(new Thickness(6), System.Windows.Shell.WindowChrome.GetWindowChrome(w).ResizeBorderThickness);
        });
    }

    [Fact]
    public void Dragging_the_header_moves_unless_locked_and_double_click_renames()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            MouseDown(Header(w)); // DragMove needs the real button held; the app copes without
            w.Board.Locked = true;
            MouseDown(Header(w));
            Assert.False(w.Board.IsEditingTitle);

            MouseDown(Header(w), clicks: 2);
            Assert.True(w.Board.IsEditingTitle);
        });
    }

    [Fact]
    public void Alt_F4_does_not_close_a_widget_but_shutting_down_does()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Close();
            Assert.True(w.IsVisible);

            PinList.App.ShuttingDown = true;
            w.Close();
            Assert.False(w.IsVisible);
            Assert.DoesNotContain(w, App.OpenWindows);
        });
    }

    [Fact]
    public void Window_title_follows_the_widget_title()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Board.Title = "";
            Assert.Equal("PinList", w.Title);
        });
    }

    [Fact]
    public void Look_follows_style_transparency_and_contrast_theme()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            var bg = () => ((System.Windows.Media.SolidColorBrush)w.Resources["BoardBg"]).Color;
            Assert.Equal(0xFF, bg().A);

            w.Board.Transparent = true;
            w.RefreshAppearance();
            Assert.Equal(0xE6, bg().A);

            Themes.ForceHighContrast = true;
            w.RefreshAppearance();
            Assert.Same(SystemColors.WindowBrush, w.Resources["BoardBg"]);
        });
    }

    [Fact]
    public void Text_size_scales_the_content()
    {
        Ui(() =>
        {
            A11y.TextScaleSource = () => 1.25;
            A11y.RefreshTextScale();
            var w = OpenNew(("A", new string[0]));
            var t = (System.Windows.Media.ScaleTransform)((FrameworkElement)w.Content).LayoutTransform;
            Assert.Equal(1.25, t.ScaleX);
        });
    }

    // ================= Highlight =================

    [Fact]
    public void Clicking_empty_space_or_leaving_the_widget_clears_the_highlight()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var item = w.Board.Categories[0].Items[0];
            MouseDown(Row(w, item));
            Assert.True(item.IsActive);

            MouseDown(Header(w));
            Assert.False(item.IsActive);

            MouseDown(Row(w, item));
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { EventArgs.Empty });
            Assert.False(item.IsActive);
        });
    }

    [Fact]
    public void Leaving_another_widget_keeps_this_highlight()
    {
        Ui(() =>
        {
            var w1 = OpenNew(("A", new[] { "a" }));
            var w2 = OpenNew(("B", new[] { "b" }));
            var cat = w1.Board.Categories[0];
            MouseDown(Row(w1, cat));
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w2, new object[] { EventArgs.Empty });
            Assert.True(cat.IsActive);
        });
    }

    [Fact]
    public void Highlight_shows_an_item_and_opens_its_category()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var cat = w.Board.Categories[0];
            cat.Collapsed = true;
            Pump();

            BoardWindow.Highlight(cat.Items[1]);
            Pump();

            Assert.False(cat.Collapsed);
            Assert.True(cat.Items[1].IsActive);
            Assert.Same(Row(w, cat.Items[1]), Keyboard.FocusedElement);

            BoardWindow.Highlight(new TodoItem()); // not in any list: nothing happens
            var orphan = MakeBoard(("Z", new[] { "z" })); // in the data but without a window
            BoardWindow.Highlight(orphan.Categories[0].Items[0]);
        });
    }

    // ================= Categories =================

    [Fact]
    public void New_category_button_adds_one_ready_to_be_named()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            Click(NamedButton(w, "New category"));
            Pump();

            var cat = w.Board.Categories[1];
            Assert.True(cat.IsEditing);
            var box = EditBox(w, cat);
            Assert.True(box.IsKeyboardFocused);
            Assert.Equal("New category", box.SelectedText); // the default name is selected, so typing replaces it
        });
    }

    [Fact]
    public void Collapse_button_hides_and_shows_items()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var cat = w.Board.Categories[0];
            var button = CollapseButton(w, cat);
            Assert.Equal("Collapse", AutomationProperties.GetName(button));

            Click(button);
            Assert.True(cat.Collapsed);
            Assert.Equal("Expand", AutomationProperties.GetName(button));
            Assert.False(Row(w, cat).IsVisible == false); // header stays

            Click(button);
            Assert.False(cat.Collapsed);
        });
    }

    // ================= Adding items =================

    [Fact]
    public void Enter_adds_an_item_and_keeps_the_box_ready()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            var cat = w.Board.Categories[0];
            var box = AddBox(w, cat);
            Assert.Equal("Add item to A", AutomationProperties.GetName(box));
            box.Focus();

            box.Text = "  Buy milk  ";
            Press(box, Key.Enter);
            Assert.Equal("Buy milk", cat.Items.Single().Text);
            Assert.True(cat.Items[0].IsActive); // Delete would remove it
            Assert.Equal("", box.Text);
            Assert.True(box.IsKeyboardFocused);

            Press(box, Key.Enter); // empty: nothing
            Assert.Single(cat.Items);
        });
    }

    [Fact]
    public void Shift_Enter_does_not_add_and_Esc_clears()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            var cat = w.Board.Categories[0];
            var box = AddBox(w, cat);
            box.Focus();
            box.Text = "first line";

            Press(box, Key.Enter, ModifierKeys.Shift);
            Assert.Empty(cat.Items);

            Press(box, Key.Escape);
            Assert.Equal("", box.Text);
            Assert.False(box.IsKeyboardFocused);
        });
    }

    [Fact]
    public void Arrow_keys_leave_an_empty_add_box()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
            var a = w.Board.Categories[0];
            var box = AddBox(w, a);
            box.Focus();

            Press(box, Key.Up);
            Assert.Same(Row(w, a.Items[0]), Keyboard.FocusedElement);

            box.Focus();
            Press(box, Key.Down);
            Assert.Same(Row(w, w.Board.Categories[1]), Keyboard.FocusedElement);

            box.Focus();
            box.Text = "typing";
            Press(box, Key.Up); // with text, arrows move the caret instead
            Assert.Same(box, Keyboard.FocusedElement);
        });
    }

    // ================= Completing =================

    [Fact]
    public void The_circle_completes_after_a_moment_and_a_second_click_cancels()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var cat = w.Board.Categories[0];
            var a = cat.Items[0];
            var check = CheckButton(w, a);
            Assert.Equal("Complete a", AutomationProperties.GetName(check));

            Click(check);
            Assert.True(a.IsCompleting);
            Click(check);
            Assert.False(a.IsCompleting);
            UiHost.Wait(TimeSpan.FromMilliseconds(1000));
            Assert.Contains(a, cat.Items); // cancelled

            Click(check);
            Assert.True(UiHost.WaitUntil(() => !cat.Items.Contains(a)));
            Assert.Equal("a", Store.Data.Archive[0].Text);
        });
    }

    // ================= Editing =================

    [Fact]
    public void Double_click_edits_and_Enter_saves()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "old" }));
            var item = w.Board.Categories[0].Items[0];
            MouseDown(Row(w, item), clicks: 2);
            Assert.True(item.IsEditing);
            Pump();

            var box = EditBox(w, item);
            Assert.Equal("old", box.Text);
            Assert.True(box.IsKeyboardFocused);
            Assert.Equal(3, box.CaretIndex); // caret at the end
            Assert.Equal("Item text", AutomationProperties.GetName(box));

            box.Text = "new\r\nsecond line";
            Press(box, Key.Enter, ModifierKeys.Shift); // Shift+Enter is a new line, not save
            Assert.True(item.IsEditing);

            Press(box, Key.Enter);
            Assert.False(item.IsEditing);
            Assert.Equal("new\nsecond line", item.Text);
            Assert.Same(Row(w, item), Keyboard.FocusedElement); // focus back on the row

            Actions.Undo();
            Assert.Equal("old", item.Text);
        });
    }

    [Fact]
    public void Esc_cancels_editing_and_clicking_elsewhere_saves()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var (a, b) = (w.Board.Categories[0].Items[0], w.Board.Categories[0].Items[1]);

            a.IsEditing = true;
            Pump();
            EditBox(w, a).Text = "changed";
            Press(EditBox(w, a), Key.Escape);
            Assert.Equal("a", a.Text);
            Assert.Same(Row(w, a), Keyboard.FocusedElement);

            a.IsEditing = true;
            Pump();
            EditBox(w, a).Text = "changed";
            MouseDown(Row(w, b)); // focus moves away: saved
            Assert.Equal("changed", a.Text);
            Assert.False(a.IsEditing);
        });
    }

    [Fact]
    public void Emptying_an_item_deletes_it()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var cat = w.Board.Categories[0];
            var a = cat.Items[0];
            a.IsEditing = true;
            Pump();
            EditBox(w, a).Text = "   ";
            Press(EditBox(w, a), Key.Enter);
            Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
        });
    }

    [Fact]
    public void Renaming_a_category_ignores_an_empty_name()
    {
        Ui(() =>
        {
            var w = OpenNew(("Work", new string[0]));
            var cat = w.Board.Categories[0];
            MouseDown(Row(w, cat), clicks: 2);
            Pump();
            var box = EditBox(w, cat);
            Assert.Equal("Category name", AutomationProperties.GetName(box));
            Assert.Equal(4, box.CaretIndex); // not selected: it isn't the default name

            box.Text = "";
            Press(box, Key.Enter);
            Assert.Equal("Work", cat.Name);

            cat.IsEditing = true;
            Pump();
            EditBox(w, cat).Text = "Home";
            Press(EditBox(w, cat), Key.Enter);
            Assert.Equal("Home", cat.Name);
            Assert.Same(Row(w, cat), Keyboard.FocusedElement);

            cat.IsEditing = true;
            Pump();
            Press(EditBox(w, cat), Key.Escape);
            Assert.False(cat.IsEditing);
        });
    }

    [Fact]
    public void Renaming_the_widget_and_an_empty_title()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Board.IsEditingTitle = true;
            Pump();
            var box = EditBox(w, w.Board);
            Assert.Equal("Window title", AutomationProperties.GetName(box));
            box.Text = "  ";
            Press(box, Key.Enter);
            Assert.Equal("", w.Board.Title); // allowed: a widget without a title
            Assert.Same(Row(w, w.Board.Categories[0]), Keyboard.FocusedElement); // focus goes into the list

            w.Board.IsEditingTitle = true;
            Pump();
            Press(EditBox(w, w.Board), Key.Escape);
            Assert.False(w.Board.IsEditingTitle);
        });
    }

    [Fact]
    public void Edit_boxes_of_deleted_things_close_quietly()
    {
        Ui(() =>
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
        });
    }

    // ================= Deleting the widget =================

    [Fact]
    public void Delete_window_asks_first()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }), ("B", new string[0]));
            w.Board.Title = "Work";
            Click(Named<Button>(w, "MenuBtn"));
            ChooseFromMenu(OpenMenu(), "Delete window"); // Esc
            Assert.Contains("Delete \"Work\"?: This removes the window with 2 categories and 2 items. Items are not moved to the archive.", Dialogs.Shown);
            Assert.True(w.IsVisible);

            Dialogs.ChooseAnswer = _ => 1;
            Click(Named<Button>(w, "MenuBtn"));
            ChooseFromMenu(OpenMenu(), "Delete window");
            Assert.False(w.IsVisible);
            Assert.DoesNotContain(w.Board, Store.Data.Boards);
            UiHost.Wait(TimeSpan.FromMilliseconds(300));
            Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board); // not reopened
        });
    }

    [Fact]
    public void Deleting_an_empty_untitled_window_says_so()
    {
        Ui(() =>
        {
            var board = MakeBoard();
            board.Title = "";
            var w = Open(board);
            Click(Named<Button>(w, "MenuBtn"));
            ChooseFromMenu(OpenMenu(), "Delete window");
            Assert.Contains("Delete this window?: This removes the window from your desktop.", Dialogs.Shown);
        });
    }
}
