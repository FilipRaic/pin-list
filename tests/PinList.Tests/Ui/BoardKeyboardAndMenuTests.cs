using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using PinList;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace PinList.Tests;

/// <summary>Everything in a widget done with the keyboard.</summary>
public class BoardKeyboardTests : UiTest
{
    static IInputElement Focused => Keyboard.FocusedElement;

    [Fact]
    public void Focus_starts_on_the_first_category_and_arrows_walk_the_list()
    {
        Ui(() =>
        {
            var w = OpenNew(("Work", new[] { "a", "b" }), ("Home", new string[0]));
            var (work, home) = (w.Board.Categories[0], w.Board.Categories[1]);
            w.FocusContent();
            Pump();
            Assert.Same(Row(w, work), Focused);
            Assert.True(work.IsActive); // the highlight follows focus, so Delete acts on it

            Press(Key.Down); Assert.Same(Row(w, work.Items[0]), Focused);
            Press(Key.Down); Assert.Same(Row(w, work.Items[1]), Focused);
            Press(Key.Down); Assert.Same(AddBox(w, work), Focused);
            Press(Key.Down); Assert.Same(Row(w, home), Focused);
            Press(Key.Up); Assert.Same(AddBox(w, work), Focused);
            Press(Key.Up); Assert.Same(Row(w, work.Items[1]), Focused);

            Press(Key.A); // other keys are left alone
            Assert.Same(Row(w, work.Items[1]), Focused);
        });
    }

    [Fact]
    public void An_empty_widget_focuses_its_first_button()
    {
        Ui(() =>
        {
            var w = OpenNew();
            w.FocusContent();
            Pump();
            Assert.Equal("New category", AutomationProperties.GetName((DependencyObject)Focused));
        });
    }

    [Fact]
    public void Space_completes_and_focus_moves_to_the_next_item()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b" }));
            var cat = w.Board.Categories[0];
            var a = cat.Items[0];
            Row(w, a).Focus();

            Press(Key.Space);
            Assert.True(a.IsCompleting);
            Press(Key.Space); // again within the moment: cancel
            Assert.False(a.IsCompleting);

            Press(Key.Space);
            Assert.True(UiHost.WaitUntil(() => !cat.Items.Contains(a)));
            Assert.Same(Row(w, cat.Items[0]), Focused);
        });
    }

    [Fact]
    public void Enter_and_F2_edit_an_item()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var a = w.Board.Categories[0].Items[0];
            Row(w, a).Focus();
            Press(Key.Enter);
            Assert.True(a.IsEditing);
            Press(EditBox(w, a), Key.Escape);
            Press(Key.F2);
            Assert.True(a.IsEditing);
        });
    }

    [Fact]
    public void Delete_moves_focus_to_the_next_then_previous_then_the_category()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b", "c" }));
            var cat = w.Board.Categories[0];
            Row(w, cat.Items[1]).Focus();

            Press(Key.Delete); // b → focus on c
            Assert.Equal(new[] { "a", "c" }, cat.Items.Select(i => i.Text));
            Assert.Same(Row(w, cat.Items[1]), Focused);

            Press(Key.Delete); // c (last) → focus on a
            Assert.Same(Row(w, cat.Items[0]), Focused);

            Press(Key.Delete); // a (only one) → focus on the category
            Assert.Same(Row(w, cat), Focused);

            Press(Key.Z, ModifierKeys.Control); // undo brings a back
            Assert.Single(cat.Items);
        });
    }

    [Fact]
    public void Deleting_categories_moves_focus_too()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]), ("B", new string[0]));
            var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
            Row(w, a).Focus();
            Press(Key.Delete);
            Assert.Same(Row(w, b), Focused); // the next one

            Press(Key.Delete); // the last one: focus goes to the widget's first control
            Assert.Empty(w.Board.Categories);
            Assert.Equal("New category", AutomationProperties.GetName((DependencyObject)Focused));
        });
    }

    [Fact]
    public void Delete_does_nothing_while_typing_or_without_a_highlight()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var cat = w.Board.Categories[0];
            var box = AddBox(w, cat);
            box.Focus();
            box.Text = "typing";
            Press(box, Key.Delete);
            Assert.Single(cat.Items);

            box.Text = "";
            Press(box, Key.Delete); // empty box, nothing highlighted
            Assert.Single(cat.Items);
        });
    }

    [Fact]
    public void Escape_clears_the_highlight()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var a = w.Board.Categories[0].Items[0];
            Row(w, a).Focus();
            Press(Key.Escape);
            Assert.False(a.IsActive);
        });
    }

    [Fact]
    public void Ctrl_Z_undoes_but_not_while_typing()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var cat = w.Board.Categories[0];
            Actions.SetItemText(cat.Items[0], "changed");

            var box = AddBox(w, cat);
            box.Focus();
            box.Text = "typing";
            Press(box, Key.Z, ModifierKeys.Control);
            Assert.Equal("changed", cat.Items[0].Text);

            Row(w, cat.Items[0]).Focus();
            Press(Key.Z, ModifierKeys.Control);
            Assert.Equal("a", cat.Items[0].Text);
            Press(Key.Z, ModifierKeys.Control); // nothing left: "Nothing to undo"
        });
    }

    [Fact]
    public void Alt_arrows_move_items_within_and_between_categories()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a1", "a2" }), ("B", new[] { "b1" }));
            var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
            var a1 = a.Items[0];
            Row(w, a1).Focus();

            Press(Key.Up, ModifierKeys.Alt); // already at the very top
            Assert.Same(a1, a.Items[0]);

            Press(Key.Down, ModifierKeys.Alt);
            Assert.Equal(new[] { "a2", "a1" }, a.Items.Select(i => i.Text));
            Assert.Same(Row(w, a1), Focused); // focus stays with it

            b.Collapsed = true;
            Press(Key.Down, ModifierKeys.Alt); // bottom of A → top of B, which opens
            Assert.Equal(new[] { "a1", "b1" }, b.Items.Select(i => i.Text));
            Assert.False(b.Collapsed);

            a.Collapsed = true;
            Press(Key.Up, ModifierKeys.Alt); // top of B → end of A, which opens
            Assert.Equal(new[] { "a2", "a1" }, a.Items.Select(i => i.Text));
            Assert.False(a.Collapsed);

            var b1 = b.Items[0];
            Row(w, b1).Focus();
            Press(Key.Down, ModifierKeys.Alt); // already at the very bottom
            Assert.Same(b1, b.Items[0]);

            Press(Key.Up, ModifierKeys.Alt); // within B there's nothing above, so to A
            Assert.Equal(3, a.Items.Count);
        });
    }

    [Fact]
    public void Category_keys_collapse_rename_and_move()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
            var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
            Row(w, a).Focus();

            Press(Key.Enter);
            Assert.True(a.Collapsed);
            Assert.Equal("A, 1 item, collapsed", AutomationProperties.GetName(Row(w, a)));
            Press(Key.Space);
            Assert.False(a.Collapsed);

            Press(Key.Up, ModifierKeys.Alt); // already first
            Press(Key.Down, ModifierKeys.Alt);
            Assert.Equal(new[] { "B", "A" }, w.Board.Categories.Select(c => c.Name));
            Assert.Same(Row(w, a), Focused);
            Press(Key.Down, ModifierKeys.Alt); // already last
            Press(Key.Up, ModifierKeys.Alt);
            Assert.Equal(new[] { "A", "B" }, w.Board.Categories.Select(c => c.Name));

            Press(Key.F2);
            Assert.True(a.IsEditing);
        });
    }

    [Fact]
    public void Context_menu_key_and_Shift_F10_open_the_menus()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var cat = w.Board.Categories[0];

            Row(w, cat.Items[0]).Focus();
            Press(Key.Apps);
            Assert.Equal("Edit", MenuItem(OpenMenu(), "Edit").Header);
            OpenMenu().IsOpen = false;

            Row(w, cat).Focus();
            Press(Key.F10, ModifierKeys.Shift);
            Assert.Equal("Rename", MenuItem(OpenMenu(), "Rename").Header);
            OpenMenu().IsOpen = false;
        });
    }

    [Fact]
    public void Keys_from_inside_a_row_are_left_to_that_element()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var item = w.Board.Categories[0].Items[0];
            var text = Find<TextBlock>(Row(w, item), t => t.Text == "a")!;
            // a key event that started on the text (not the row itself) is ignored by the row
            text.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, Key.Space)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.False(item.IsCompleting);

            var catText = Find<TextBlock>(Row(w, w.Board.Categories[0]), t => t.Text == "A")!;
            catText.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.False(w.Board.Categories[0].Collapsed);
        });
    }

    [Fact]
    public void Ctrl_arrows_move_and_Ctrl_Shift_arrows_resize_the_widget()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            Row(w, w.Board.Categories[0]).Focus();
            var (left, top, width, height) = (w.Left, w.Top, w.Width, w.Height);

            Press(Key.Right, ModifierKeys.Control);
            Press(Key.Down, ModifierKeys.Control);
            Assert.Equal((left + 10, top + 10), (w.Left, w.Top));
            Press(Key.Left, ModifierKeys.Control);
            Press(Key.Up, ModifierKeys.Control);
            Assert.Equal((left, top), (w.Left, w.Top));

            Press(Key.Right, ModifierKeys.Control | ModifierKeys.Shift);
            Press(Key.Down, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.Equal((width + 10, height + 10), (w.Width, w.Height));

            Press(Key.A, ModifierKeys.Control); // not an arrow
            Press(Key.Right, ModifierKeys.Alt | ModifierKeys.Control); // other modifiers: not handled here

            w.Board.Locked = true;
            Press(Key.Right, ModifierKeys.Control);
            Assert.Equal(left, w.Left); // locked: "unlock it first"

            w.Board.Locked = false;
            var box = AddBox(w, w.Board.Categories[0]);
            box.Focus();
            box.Text = "typing";
            Press(box, Key.Right, ModifierKeys.Control); // moves the caret by a word instead
            Assert.Equal(left, w.Left);
        });
    }
}

/// <summary>The widget's ⋯ menu and the right-click menus of categories and items.</summary>
public class BoardMenuTests : UiTest
{
    static ContextMenu WidgetMenu(BoardWindow w)
    {
        Click(Named<Button>(w, "MenuBtn"));
        return OpenMenu();
    }

    static ContextMenu ItemMenu(BoardWindow w, TodoItem item)
    {
        RightClick(Row(w, item));
        return OpenMenu();
    }

    static ContextMenu CategoryMenu(BoardWindow w, Category cat)
    {
        RightClick(Row(w, cat));
        return OpenMenu();
    }

    [Fact]
    public void Widget_menu_changes_color_transparency_and_theme()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            var menu = WidgetMenu(w);
            Assert.True(MenuItem(menu, "Window color", "Dark").IsChecked);
            Assert.IsType<System.Windows.Shapes.Ellipse>(MenuItem(menu, "Window color", "Yellow").Icon);
            ChooseFromMenu(menu, "Window color", "Yellow");
            Assert.Equal("Yellow", w.Board.Style);

            menu = WidgetMenu(w);
            Assert.False(MenuItem(menu, "Transparent background").IsChecked);
            ChooseFromMenu(menu, "Transparent background");
            Assert.True(w.Board.Transparent);

            menu = WidgetMenu(w);
            Assert.True(MenuItem(menu, "App theme", "Use Windows setting").IsChecked); // the default for new data
            ChooseFromMenu(menu, "App theme", "Light");
            Assert.Equal("Light", Store.Data.Settings.Theme);
        });
    }

    [Fact]
    public void Widget_menu_opens_windows_and_export_import()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            w.Board.Style = "Pink";

            ChooseFromMenu(WidgetMenu(w), "New window");
            var added = Store.Data.Boards.Last();
            Assert.Equal("Pink", added.Style); // like the widget it came from
            Assert.Contains(App.OpenWindows, x => x.Board == added);

            ChooseFromMenu(WidgetMenu(w), "Archive");
            Assert.NotNull(App.Archive);

            ChooseFromMenu(WidgetMenu(w), "Export…");
            ChooseFromMenu(WidgetMenu(w), "Import…");
            Assert.Contains("open", Dialogs.Shown);
            Assert.Contains(Dialogs.Shown, s => s.StartsWith("save: PinList-"));
        });
    }

    [Fact]
    public void Category_menu()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
            var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);

            var menu = CategoryMenu(w, a);
            Assert.True(a.IsActive);
            Assert.Equal("Double-click", MenuItem(menu, "Rename").InputGestureText);
            ChooseFromMenu(menu, "Rename");
            Assert.True(a.IsEditing);
            a.IsEditing = false;

            menu = CategoryMenu(w, a);
            Assert.True(MenuItem(menu, "Color", "Blue").IsChecked);
            ChooseFromMenu(menu, "Color", "Red");
            Assert.Equal("#F1707A", a.Color);

            ChooseFromMenu(CategoryMenu(w, a), "Collapse");
            Assert.True(a.Collapsed);
            ChooseFromMenu(CategoryMenu(w, a), "Expand");
            Assert.False(a.Collapsed);

            ChooseFromMenu(CategoryMenu(w, a), "Move down");
            Assert.Same(a, w.Board.Categories[1]);
            ChooseFromMenu(CategoryMenu(w, a), "Move up");
            Assert.Same(a, w.Board.Categories[0]);

            ChooseFromMenu(CategoryMenu(w, b), "Delete category");
            Assert.Single(w.Board.Categories);
        });
    }

    [Fact]
    public void Item_menu_edit_complete_move_delete()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a", "b", "c" }));
            var cat = w.Board.Categories[0];
            var (a, b, c) = (cat.Items[0], cat.Items[1], cat.Items[2]);

            ChooseFromMenu(ItemMenu(w, a), "Edit");
            Assert.True(a.IsEditing);
            a.IsEditing = false;

            ChooseFromMenu(ItemMenu(w, a), "Move down");
            Assert.Same(a, cat.Items[1]);
            ChooseFromMenu(ItemMenu(w, a), "Move up");
            Assert.Same(a, cat.Items[0]);

            ChooseFromMenu(ItemMenu(w, a), "Complete");
            Assert.DoesNotContain(a, cat.Items);
            Assert.Equal("a", Store.Data.Archive[0].Text);

            ChooseFromMenu(ItemMenu(w, c), "Delete");
            Assert.Equal(new[] { b }, cat.Items);
        });
    }

    [Fact]
    public void Item_menu_due_date()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var a = w.Board.Categories[0].Items[0];

            var menu = ItemMenu(w, a);
            Assert.IsType<TextBlock>(MenuItem(menu, "Add due date…").Icon);
            Assert.Throws<InvalidOperationException>(() => MenuItem(menu, "Remove due date"));
            ChooseFromMenu(menu, "Add due date…"); // cancelled
            Assert.Null(a.DueDate);

            Dialogs.DueDateAnswer = new DateTime(2031, 3, 4);
            ChooseFromMenu(ItemMenu(w, a), "Add due date…");
            Assert.Equal(new DateTime(2031, 3, 4), a.DueDate);
            Assert.Same(Row(w, a), Keyboard.FocusedElement); // focus back on the item

            ChooseFromMenu(ItemMenu(w, a), "Change due date…");
            ChooseFromMenu(ItemMenu(w, a), "Remove due date");
            Assert.Null(a.DueDate);
        });
    }

    [Fact]
    public void Item_menu_reminder()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var a = w.Board.Categories[0].Items[0];

            ChooseFromMenu(ItemMenu(w, a), "Add reminder…"); // cancelled
            Assert.Null(a.ReminderAt);

            Dialogs.ReminderAnswer = new DateTime(2031, 3, 4, 9, 0, 0);
            ChooseFromMenu(ItemMenu(w, a), "Add reminder…");
            Assert.Equal(new DateTime(2031, 3, 4, 9, 0, 0), a.ReminderAt);

            ChooseFromMenu(ItemMenu(w, a), "Change reminder…");
            a.ReminderShown = true; // went off: "Add" again, but it can still be removed
            Assert.Equal("Add reminder…", MenuItem(ItemMenu(w, a), "Add reminder…").Header);
            ChooseFromMenu(OpenMenu(), "Remove reminder");
            Assert.Null(a.ReminderAt);
        });
    }

    [Fact]
    public void Due_dates_show_on_the_item()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var a = w.Board.Categories[0].Items[0];
            var chip = Find<StackPanel>(Row(w, a), s => s.Name == "DueChip")!;
            Assert.False(chip.IsVisible);

            a.DueDate = DateTime.Today;
            Pump();
            Assert.True(chip.IsVisible);
            Assert.Equal("Due today", chip.ToolTip);

            a.DueDate = DateTime.Today.AddDays(-2);
            Pump();
            Assert.Equal("Overdue", chip.ToolTip);
            Assert.Contains("overdue", AutomationProperties.GetName(Row(w, a)));
        });
    }
}
