using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>Everything in a widget done with the keyboard.</summary>
public class BoardKeyboardTests : UiTest
{
    static void FocusOn(Control c) => c.Focus(NavigationMethod.Tab);

    [AvaloniaFact]
    public void Focus_starts_on_the_first_category_and_arrows_walk_the_list()
    {
        var w = OpenNew(("Work", new[] { "a", "b" }), ("Home", new string[0]));
        var (work, home) = (w.Board.Categories[0], w.Board.Categories[1]);
        w.FocusContent();
        Pump();
        Assert.Same(Row(w, work), Focused(w));
        Assert.True(work.IsActive); // the highlight follows focus, so Delete acts on it

        Press(w, Key.Down); Assert.Same(Row(w, work.Items[0]), Focused(w));
        Press(w, Key.Down); Assert.Same(Row(w, work.Items[1]), Focused(w));
        Press(w, Key.Down); Assert.Same(AddBox(w, work), Focused(w));
        Press(w, Key.Down); Assert.Same(Row(w, home), Focused(w));
        Press(w, Key.Up); Assert.Same(AddBox(w, work), Focused(w));
        Press(w, Key.Up); Assert.Same(Row(w, work.Items[1]), Focused(w));

        Press(w, Key.A); // other keys are left alone
        Assert.Same(Row(w, work.Items[1]), Focused(w));
    }

    [AvaloniaFact]
    public void Tab_and_Shift_Tab_go_through_every_category()
    {
        var w = OpenNew(("Work", new[] { "a" }), ("Home", new string[0]));
        var (work, home) = (w.Board.Categories[0], w.Board.Categories[1]);
        FocusOn(AddBox(w, work));
        Press(w, Key.Tab);
        Assert.Same(Row(w, home), Focused(w));
        Press(w, Key.Tab, KeyModifiers.Shift);
        Assert.Same(AddBox(w, work), Focused(w));

        FocusOn(AddBox(w, home)); // the last one: Tab goes round to the header buttons
        Press(w, Key.Tab);
        Assert.Equal("New category", NameOf((Control)Focused(w)!));
        Press(w, Key.Tab, KeyModifiers.Shift);
        Assert.Same(AddBox(w, home), Focused(w));
    }

    [AvaloniaFact]
    public void An_empty_widget_focuses_its_first_button()
    {
        var w = OpenNew();
        w.FocusContent();
        Pump();
        Assert.Equal("New category", NameOf((Control)Focused(w)!));
    }

    [AvaloniaFact]
    public void Space_completes_and_focus_moves_to_the_next_item()
    {
        BoardWindow.CompleteDelay = TimeSpan.FromMilliseconds(100);
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];
        FocusOn(Row(w, a));

        Press(w, Key.Space);
        Assert.True(a.IsCompleting);
        Press(w, Key.Space); // again within the moment: cancel
        Assert.False(a.IsCompleting);

        Press(w, Key.Space);
        Assert.True(UiHost.WaitUntil(() => !cat.Items.Contains(a)));
        Pump();
        Assert.Same(Row(w, cat.Items[0]), Focused(w));
    }

    [AvaloniaFact]
    public void Enter_and_F2_edit_an_item()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var a = w.Board.Categories[0].Items[0];
        FocusOn(Row(w, a));
        Press(w, Key.Enter);
        Assert.True(a.IsEditing);
        Pump();
        Press(EditBox(w, a), Key.Escape);
        Pump();
        Press(w, Key.F2);
        Assert.True(a.IsEditing);
    }

    [AvaloniaFact]
    public void Delete_moves_focus_to_the_next_then_previous_then_the_category()
    {
        var w = OpenNew(("A", new[] { "a", "b", "c" }));
        var cat = w.Board.Categories[0];
        FocusOn(Row(w, cat.Items[1]));

        Press(w, Key.Delete); // b → focus on c
        Assert.Equal(new[] { "a", "c" }, cat.Items.Select(i => i.Text));
        Assert.Same(Row(w, cat.Items[1]), Focused(w));
        Assert.Equal("Deleted b. Ctrl+Z undoes.", A11y.LastAnnouncement);

        Press(w, Key.Delete); // c (last) → focus on a
        Assert.Same(Row(w, cat.Items[0]), Focused(w));

        Press(w, Key.Delete); // a (only one) → focus on the category
        Assert.Same(Row(w, cat), Focused(w));

        Press(w, Key.Z, KeyModifiers.Control); // undo brings a back
        Assert.Single(cat.Items);
        Assert.Equal("Undone: deleting “a”", A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void Deleting_categories_moves_focus_too()
    {
        var w = OpenNew(("A", new string[0]), ("B", new string[0]));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
        FocusOn(Row(w, a));
        Press(w, Key.Delete);
        Assert.Same(Row(w, b), Focused(w)); // the next one

        Press(w, Key.Delete); // the last one: focus goes to the widget's first control
        Assert.Empty(w.Board.Categories);
        Assert.Equal("New category", NameOf((Control)Focused(w)!));
    }

    [AvaloniaFact]
    public void Delete_does_nothing_while_typing_or_without_a_highlight()
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
    }

    [AvaloniaFact]
    public void Escape_clears_the_highlight()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var a = w.Board.Categories[0].Items[0];
        FocusOn(Row(w, a));
        Press(w, Key.Escape);
        Assert.False(a.IsActive);
    }

    [AvaloniaFact]
    public void Ctrl_Z_undoes_but_not_while_typing()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];
        Actions.SetItemText(cat.Items[0], "changed");

        var box = AddBox(w, cat);
        box.Focus();
        box.Text = "typing";
        Press(box, Key.Z, KeyModifiers.Control);
        Assert.Equal("changed", cat.Items[0].Text);

        FocusOn(Row(w, cat.Items[0]));
        Press(w, Key.Z, KeyModifiers.Control);
        Assert.Equal("a", cat.Items[0].Text);
        Press(w, Key.Z, KeyModifiers.Control); // nothing left
        Assert.Equal("Nothing to undo", A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void Ctrl_Z_while_editing_an_item_is_left_to_the_text_box()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        Actions.SetItemText(item, "changed");
        item.IsEditing = true;
        Pump();
        var box = EditBox(w, item);
        box.Focus();
        Press(box, Key.Z, KeyModifiers.Control);
        Assert.True(Actions.CanUndo); // the widget's undo didn't run
        Press(box, Key.Escape);
    }

    [AvaloniaFact]
    public void Alt_arrows_move_items_within_and_between_categories()
    {
        var w = OpenNew(("A", new[] { "a1", "a2" }), ("B", new[] { "b1" }));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
        var a1 = a.Items[0];
        FocusOn(Row(w, a1));

        Press(w, Key.Up, KeyModifiers.Alt); // already at the very top
        Assert.Same(a1, a.Items[0]);
        Assert.Equal("Already at the top", A11y.LastAnnouncement);

        Press(w, Key.Left, KeyModifiers.Alt); // only up and down move
        Assert.Equal(new[] { "a1", "a2" }, a.Items.Select(i => i.Text));

        Press(w, Key.Down, KeyModifiers.Alt);
        Assert.Equal(new[] { "a2", "a1" }, a.Items.Select(i => i.Text));
        Pump();
        Assert.Same(Row(w, a1), Focused(w)); // focus stays with it
        Assert.Equal("Moved to position 2 of 2 in A", A11y.LastAnnouncement);

        b.Collapsed = true;
        Press(w, Key.Down, KeyModifiers.Alt); // bottom of A → top of B, which opens
        Assert.Equal(new[] { "a1", "b1" }, b.Items.Select(i => i.Text));
        Assert.False(b.Collapsed);
        Pump();

        a.Collapsed = true;
        Press(w, Key.Up, KeyModifiers.Alt); // top of B → end of A, which opens
        Assert.Equal(new[] { "a2", "a1" }, a.Items.Select(i => i.Text));
        Assert.False(a.Collapsed);
        Pump();

        var b1 = b.Items[0];
        FocusOn(Row(w, b1));
        Press(w, Key.Down, KeyModifiers.Alt); // already at the very bottom
        Assert.Same(b1, b.Items[0]);
        Assert.Equal("Already at the bottom", A11y.LastAnnouncement);

        Press(w, Key.Up, KeyModifiers.Alt); // within B there's nothing above, so to A
        Assert.Equal(3, a.Items.Count);
    }

    [AvaloniaFact]
    public void Category_keys_collapse_rename_and_move()
    {
        var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);
        FocusOn(Row(w, a));

        Press(w, Key.Enter);
        Assert.True(a.Collapsed);
        Assert.Equal("A, 1 item, collapsed", NameOf(Row(w, a)));
        Pump();
        Press(w, Key.Space);
        Assert.False(a.Collapsed);
        Pump();

        Press(w, Key.Up, KeyModifiers.Alt); // already first
        Press(w, Key.Down, KeyModifiers.Alt);
        Assert.Equal(new[] { "B", "A" }, w.Board.Categories.Select(c => c.Name));
        Pump();
        Assert.Same(Row(w, a), Focused(w));
        Press(w, Key.Down, KeyModifiers.Alt); // already last
        Press(w, Key.Up, KeyModifiers.Alt);
        Assert.Equal(new[] { "A", "B" }, w.Board.Categories.Select(c => c.Name));
        Pump();

        Press(w, Key.F2);
        Assert.True(a.IsEditing);
    }

    [AvaloniaFact]
    public void Context_menu_key_and_Shift_F10_open_the_menus()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];

        FocusOn(Row(w, cat.Items[0]));
        Press(w, Key.Apps);
        Assert.Equal("Edit", Header(MenuItem(w.LastMenu!, "Edit")));
        Assert.Equal(PlacementMode.Bottom, w.LastMenu!.Placement); // below the row, not at the mouse
        w.LastMenu.Close();

        FocusOn(Row(w, cat));
        Press(w, Key.F10, KeyModifiers.Shift);
        Assert.Equal("Rename", Header(MenuItem(w.LastMenu!, "Rename")));
        w.LastMenu!.Close();
    }

    [AvaloniaFact]
    public void Odd_keys_and_clicks_are_left_alone()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];
        var item = cat.Items[0];

        item.IsEditing = true; // Esc in a text box belongs to the box, the highlight stays
        Pump();
        var box = EditBox(w, item);
        box.Text = "edited";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = box });
        Pump();
        Assert.Equal("a", item.Text);

        var header = Named<Grid>(w, "Header"); // a right-button press on the header neither moves nor renames
        var props = new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed);
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
        header.RaiseEvent(new PointerPressedEventArgs(header, pointer, w, new Avalonia.Point(50, 10), 0, props, KeyModifiers.None, 2));
        Assert.False(w.Board.IsEditingTitle);
        Row(w, item).RaiseEvent(new PointerPressedEventArgs(Row(w, item), pointer, w, new Avalonia.Point(50, 50), 0, props, KeyModifiers.None, 2));
        Assert.False(item.IsEditing); // nor on a row
    }

    [AvaloniaFact]
    public void Keys_from_inside_a_row_are_left_to_that_element()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        // a key event that started on the text (not the row itself) is ignored by the row
        Press(Find<TextBlock>(Row(w, item), t => t.Text == "a")!, Key.Space);
        Assert.False(item.IsCompleting);

        Press(Find<TextBlock>(Row(w, w.Board.Categories[0]), t => t.Text == "A")!, Key.Enter);
        Assert.False(w.Board.Categories[0].Collapsed);
    }

    [AvaloniaFact]
    public void Ctrl_arrows_move_and_Ctrl_Shift_arrows_resize_the_widget()
    {
        var w = OpenNew(("A", new[] { "a" }));
        FocusOn(Row(w, w.Board.Categories[0]));
        var (left, top, width, height) = (w.Left, w.Top, w.Width, w.Height);

        Press(w, Key.Right, KeyModifiers.Control);
        Press(w, Key.Down, KeyModifiers.Control);
        Assert.Equal(left + 10, w.Left, 0);
        Assert.Equal(top + 10, w.Top, 0);
        Assert.Equal(Math.Round(w.Left), w.Board.X); // saved right away
        Press(w, Key.Left, KeyModifiers.Control);
        Press(w, Key.Up, KeyModifiers.Control);
        Assert.Equal(left, w.Left, 0);
        Assert.Equal(top, w.Top, 0);

        Press(w, Key.Right, KeyModifiers.Control | KeyModifiers.Shift);
        Press(w, Key.Down, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal((width + 10, height + 10), (w.Width, w.Height));

        Press(w, Key.A, KeyModifiers.Control); // not an arrow
        Press(w, Key.Right, KeyModifiers.Alt | KeyModifiers.Control); // other modifiers: not handled here

        w.Board.Locked = true;
        Press(w, Key.Right, KeyModifiers.Control);
        Assert.Equal(left, w.Left, 0); // locked: "unlock it first"
        Assert.Equal("The window is locked. Unlock it to move or resize.", A11y.LastAnnouncement);

        w.Board.Locked = false;
        var box = AddBox(w, w.Board.Categories[0]);
        box.Focus();
        box.Text = "typing";
        Press(box, Key.Right, KeyModifiers.Control); // moves the caret by a word instead
        Assert.Equal(left, w.Left, 0);
    }
}

/// <summary>The widget's ⋯ menu and the right-click menus of categories and items.</summary>
public class BoardMenuTests : UiTest
{
    static ContextMenu WidgetMenu(BoardWindow w)
    {
        Click(Named<Button>(w, "MenuBtn"));
        return w.LastMenu!;
    }

    static ContextMenu ItemMenu(BoardWindow w, TodoItem item)
    {
        RightClick(Row(w, item));
        return w.LastMenu!;
    }

    static ContextMenu CategoryMenu(BoardWindow w, Category cat)
    {
        RightClick(Row(w, cat));
        return w.LastMenu!;
    }

    [AvaloniaFact]
    public void Widget_menu_changes_color_transparency_and_theme()
    {
        var w = OpenNew(("A", new string[0]));
        var menu = WidgetMenu(w);
        Assert.True(MenuItem(menu, "Window color", "Dark").IsChecked);
        Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(MenuItem(menu, "Window color", "Yellow").Icon);
        ChooseFromMenu(menu, "Window color", "Yellow");
        Assert.Equal("Yellow", w.Board.Style);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, w.RequestedThemeVariant);

        menu = WidgetMenu(w);
        Assert.False(MenuItem(menu, "Transparent background").IsChecked);
        ChooseFromMenu(menu, "Transparent background");
        Assert.True(w.Board.Transparent);

        Store.Data.Settings.Theme = "System";
        menu = WidgetMenu(w);
        Assert.True(MenuItem(menu, "App theme", "Use Windows setting").IsChecked); // the default for new data
        ChooseFromMenu(menu, "App theme", "Light");
        Assert.Equal("Light", Store.Data.Settings.Theme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, App.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void Widget_menu_opens_windows_and_export_import()
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
    }

    [AvaloniaFact]
    public void Category_menu()
    {
        var w = OpenNew(("A", new[] { "a" }), ("B", new string[0]));
        var (a, b) = (w.Board.Categories[0], w.Board.Categories[1]);

        var menu = CategoryMenu(w, a);
        Assert.True(a.IsActive);
        Assert.Equal(PlacementMode.Pointer, menu.Placement); // at the mouse
        Assert.Equal("Double-click", AutomationProperties.GetAcceleratorKey(MenuItem(menu, "Rename")));
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
    }

    [AvaloniaFact]
    public void Item_menu_edit_complete_move_delete()
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
    }

    [AvaloniaFact]
    public void Item_menu_due_date()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var a = w.Board.Categories[0].Items[0];

        var menu = ItemMenu(w, a);
        Assert.IsType<PathIcon>(MenuItem(menu, "Add due date…").Icon);
        Assert.Throws<InvalidOperationException>(() => MenuItem(menu, "Remove due date"));
        ChooseFromMenu(menu, "Add due date…"); // cancelled
        Assert.Null(a.DueDate);

        Dialogs.DueDateAnswer = new DateTime(2031, 3, 4);
        ChooseFromMenu(ItemMenu(w, a), "Add due date…");
        Assert.Equal(new DateTime(2031, 3, 4), a.DueDate);
        Pump();
        Assert.Same(Row(w, a), Focused(w)); // focus back on the item

        ChooseFromMenu(ItemMenu(w, a), "Change due date…");
        ChooseFromMenu(ItemMenu(w, a), "Remove due date");
        Assert.Null(a.DueDate);
        Assert.Equal("Due date removed", A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void Item_menu_reminder()
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
        var menu = ItemMenu(w, a);
        Assert.Equal("Add reminder…", Header(MenuItem(menu, "Add reminder…")));
        ChooseFromMenu(menu, "Remove reminder");
        Assert.Null(a.ReminderAt);
    }

    [AvaloniaFact]
    public void Due_dates_show_on_the_item()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var a = w.Board.Categories[0].Items[0];
        var chip = Find<StackPanel>(Row(w, a), s => s.Classes.Contains("chip"))!;
        Assert.False(chip.IsVisible);

        a.DueDate = DateTime.Today;
        Pump();
        Assert.True(chip.IsVisible);
        Assert.Equal("Due today", ToolTip.GetTip(chip));
        Assert.Contains("today", chip.Classes); // accent color

        a.DueDate = DateTime.Today.AddDays(-2);
        Pump();
        Assert.Equal("Overdue", ToolTip.GetTip(chip));
        Assert.Contains("overdue", chip.Classes); // red
        Assert.Contains("overdue", NameOf(Row(w, a)));
    }
}
