using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;

namespace PinList.Tests;

/// <summary>The phone app's screen (PhoneView), in a phone-sized headless window.</summary>
public partial class PhoneTests : UiTest
{
    Window _window = null!;
    PhoneView _view = null!;

    public PhoneTests()
    {
        PhoneDrag.HoldTime = TimeSpan.FromMilliseconds(80);
        PhoneView.CompleteDelay = TimeSpan.FromMilliseconds(50);
        PhoneView.Clock = () => new DateTime(2030, 6, 12, 10, 15, 0);
    }

    public override void Dispose()
    {
        _window?.Close();
        PhoneDrag.HoldTime = TimeSpan.FromMilliseconds(450);
        PhoneView.CompleteDelay = TimeSpan.FromMilliseconds(900);
        PhoneView.Clock = () => DateTime.Now;
        base.Dispose();
    }

    PhoneView Show()
    {
        _view = new PhoneView();
        _window = new Window { Content = _view, Width = 400, Height = 760 };
        _window.Show();
        _window.Activate();
        _view.Reload();
        UiHost.Wait(TimeSpan.FromMilliseconds(100)); // the window's activation runs late
        return _view;
    }

    Point At(Control c, double y = 0.5) => c.TranslatePoint(new Point(c.Bounds.Width * 0.5, c.Bounds.Height * y), _window)!.Value;

    void Tap(Control c)
    {
        var p = At(c);
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseUp(p, MouseButton.Left);
        Pump();
    }

    /// <summary>Press and hold without moving (a long-press), then let go.</summary>
    void LongPress(Control c)
    {
        var p = At(c);
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        _window.MouseUp(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500)); // past the "no tap after a long-press" time
    }

    /// <summary>Press and hold, then drag onto another element (at a height within it) and let go.</summary>
    void Drag(Control from, Control to, double y = 0.75)
    {
        var p = At(from);
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        var q = At(to, y);
        _window.MouseMove(new Point(q.X, (p.Y + q.Y) / 2), RawInputModifiers.LeftMouseButton);
        Pump();
        _window.MouseMove(At(to, y), RawInputModifiers.LeftMouseButton);
        Pump();
        _window.MouseUp(At(to, y), MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
    }

    Border Card(Board b) => Find<Border>(_view, x => x.Classes.Contains("card") && ReferenceEquals(x.Tag, b))!;
    AccessibleRow PhoneRow(object tag)
    {
        Pump(); // rows of items that just came back (undo) are made on the next layout
        return Row(_view, tag);
    }

    void Choose(string option)
    {
        Assert.True(_view.SheetOpen, "no menu is open");
        var b = _view.SheetPanel!.Children.OfType<Button>().FirstOrDefault(x => x.Content as string == option)
                ?? throw new InvalidOperationException($"No option '{option}' in: " +
                       string.Join(", ", _view.SheetPanel.Children.OfType<Button>().Select(x => x.Content)));
        Click(b);
    }

    string[] Options() => _view.SheetPanel!.Children.OfType<Button>().Select(x => (string)x.Content!).ToArray();

    // ================= Cards =================

    [AvaloniaFact]
    public void Cards_show_title_counts_overdue_and_a_preview()
    {
        var work = MakeBoard(("This week", new[] { "Review PR", "Release", "Invoice", "Call" }), ("Later", new[] { "Plan" }));
        work.Title = "Work";
        work.Categories[0].Items[0].DueDate = DateTime.Today.AddDays(-1);
        var empty = MakeBoard();
        empty.Title = "";
        Show();

        var card = _view.CardList[0];
        Assert.Equal("Work", card.DisplayTitle);
        Assert.Equal(5, card.OpenCount);
        Assert.Equal("1 overdue", card.OverdueText);
        Assert.Equal(new[] { "This week", "Review PR", "Release", "Invoice" }, card.Preview.Select(l => l.Text));
        Assert.True(card.Preview[0].IsCategory); // a dot for the category, circles for the items
        Assert.False(card.Preview[1].IsCategory);
        Assert.Equal("+2 more", card.MoreText);
        Assert.Equal("Work, 5 items, 1 overdue", card.AccessibleName);
        Assert.Equal("No title", _view.CardList[1].DisplayTitle);
        Assert.False(_view.CardList[1].HasMore);

        // the card follows changes
        Actions.AddItem(work.Categories[1], "new");
        Assert.Equal(6, card.OpenCount);
        work.Categories[0].Items[0].Text = "Renamed";
        Assert.Equal("Renamed", card.Preview[1].Text);
        work.Title = "Job";
        Assert.Equal("Job", card.DisplayTitle);
        Actions.NewBoard(new WorkArea(0, 0, 800, 600));
        Assert.Equal(3, _view.CardList.Count);
        Store.Data.Boards.Remove(empty);
        Assert.Equal(2, _view.CardList.Count);
    }

    [AvaloniaFact]
    public void Tapping_a_card_opens_the_board_and_back_returns()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        b.Style = "Yellow";
        Show();
        Tap(Card(b));
        Assert.Same(b, _view.OpenBoard);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Find<ThemeVariantScope>(_view)!.RequestedThemeVariant);

        Assert.True(_view.GoBack());
        Assert.Null(_view.OpenBoard);
        Assert.False(_view.GoBack()); // on the cards: Android closes the app
    }

    [AvaloniaFact]
    public void The_system_back_button_goes_back_until_the_cards()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        var back = new Avalonia.Interactivity.RoutedEventArgs(TopLevel.BackRequestedEvent);
        _window.RaiseEvent(back);
        Assert.True(back.Handled);
        Assert.Null(_view.OpenBoard);
        back = new Avalonia.Interactivity.RoutedEventArgs(TopLevel.BackRequestedEvent);
        _window.RaiseEvent(back);
        Assert.False(back.Handled); // the system closes the app

        _window.Content = null; // taken off the screen: no longer listens
        _view.ShowBoard(b);
        _window.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(TopLevel.BackRequestedEvent));
        Assert.Same(b, _view.OpenBoard);
    }

    [AvaloniaFact]
    public void The_plus_button_starts_a_new_list_with_its_title_being_typed()
    {
        Show();
        // No pumping: the desktop app (also running in tests) would open a widget for the new list and take the focus
        Named<Button>(_view, "AddBoardButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var b = _view.OpenBoard!;
        Assert.Equal("", b.Title);
        var title = Named<TextBox>(_view, "BoardTitleBox");
        Assert.True(title.IsVisible);
        title.Text = "Groceries";
        Press(title, Key.Enter);
        Assert.Equal("Groceries", b.Title);
        Assert.Equal("Groceries", Named<TextBlock>(_view, "BoardTitle").Text);
    }

    [AvaloniaFact]
    public void Dragging_a_card_reorders_the_lists_in_one_undoable_step()
    {
        var a = MakeBoard(("A", new[] { "a" })); a.Title = "A";
        var b = MakeBoard(("B", new[] { "b" })); b.Title = "B";
        var c = MakeBoard(("C", new[] { "c" })); c.Title = "C";
        Show();
        Drag(Card(a), Card(c));
        Assert.Equal(new[] { "B", "C", "A" }, Store.Data.Boards.Select(x => x.Title));
        Assert.Equal(new[] { "B", "C", "A" }, _view.CardList.Select(x => x.Board.Title));
        Actions.Undo();
        Assert.Equal(new[] { "A", "B", "C" }, Store.Data.Boards.Select(x => x.Title));
    }

    [AvaloniaFact]
    public void A_swipe_that_starts_on_a_card_does_not_pick_it_up()
    {
        var a = MakeBoard(("A", new[] { "a" }));
        Show();
        var p = At(Card(a));
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseMove(p + new Point(0, 40), RawInputModifiers.LeftMouseButton); // moving right away = scrolling
        UiHost.Wait(TimeSpan.FromMilliseconds(200));
        Assert.Null(_view.HomeDrag.Lifted);
        Assert.DoesNotContain("lifted", Card(a).Classes); // nothing stays highlighted
        _window.MouseUp(p + new Point(0, 40), MouseButton.Left);
        Pump();
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Long_press_on_a_card_opens_its_menu()
    {
        var a = MakeBoard(("A", new[] { "a" }));
        a.Title = "Work";
        Show();
        LongPress(Card(a));
        Assert.Equal("Work", _view.SheetTitleText);
        Assert.Equal(new[] { "Open", "Color", "Delete list" }, Options());
        Assert.DoesNotContain("lifted", Card(a).Classes);

        Choose("Color");
        Choose("Green");
        Assert.Equal("Green", a.Style);

        LongPress(Card(a));
        Choose("Delete list");
        Assert.Contains("1 items", _view.SheetTitleText);
        Choose("Cancel");
        Assert.Contains(a, Store.Data.Boards);
        LongPress(Card(a));
        Choose("Open");
        Assert.Same(a, _view.OpenBoard);
    }

    // ================= One board =================

    [AvaloniaFact]
    public void Adding_editing_completing_and_undoing_items()
    {
        var b = MakeBoard(("A", new[] { "old" }));
        var cat = b.Categories[0];
        Show();
        _view.ShowBoard(b);
        Pump();

        var add = Find<TextBox>(_view, t => t.Classes.Contains("phoneadd"))!;
        add.Text = "Milk";
        Press(add, Key.Enter);
        Assert.Equal(new[] { "old", "Milk" }, cat.Items.Select(i => i.Text));
        Assert.Equal("", add.Text);

        Tap(Find<TextBlock>(PhoneRow(cat.Items[0]), t => t.Text == "old")!);
        Assert.True(cat.Items[0].IsEditing);
        Pump();
        var edit = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && t.IsEffectivelyVisible)!;
        Assert.Equal("old", edit.Text);
        edit.Text = "new";
        Press(edit, Key.Enter);
        Assert.Equal("new", cat.Items[0].Text);

        var check = Find<Button>(PhoneRow(cat.Items[1]), x => x.Classes.Contains("check"))!;
        Click(check);
        Click(check); // tapped again quickly: cancelled
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        Assert.Equal(2, cat.Items.Count);
        Click(check);
        Assert.True(UiHost.WaitUntil(() => cat.Items.Count == 1));
        Assert.True(_view.SnackbarShown);
        Assert.Equal("Completed Milk", _view.SnackbarMessage);

        Click(Named<Button>(_view, "UndoButton"));
        Assert.Equal(2, cat.Items.Count);
        Assert.False(_view.SnackbarShown);
    }

    [AvaloniaFact]
    public void Tapping_a_category_collapses_it_and_new_categories_are_named_right_away()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        Pump();
        Tap(PhoneRow(b.Categories[0]));
        Assert.True(b.Categories[0].Collapsed);
        Tap(PhoneRow(b.Categories[0]));
        Assert.False(b.Categories[0].Collapsed);

        Click(Find<Button>(_view, x => NameOf(x) == "New category")!);
        Pump();
        var cat = b.Categories[1];
        Assert.True(cat.IsEditing);
        var box = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, cat))!;
        Assert.Equal("New category", box.SelectedText);
        box.Text = "Home";
        Press(box, Key.Enter);
        Assert.Equal("Home", cat.Name);
    }

    [AvaloniaFact]
    public void Long_press_on_an_item_opens_its_menu()
    {
        var b = MakeBoard(("A", new[] { "a", "b" }));
        var other = MakeBoard(("Elsewhere", new string[0]));
        other.Title = "Other";
        var cat = b.Categories[0];
        Show();
        _view.ShowBoard(b);
        Pump();

        LongPress(PhoneRow(cat.Items[0]));
        Assert.Equal(new[] { "Edit", "Add due date", "Add reminder", "Move to list", "Delete" }, Options());
        Choose("Add due date");
        Assert.Equal("Add due date", _view.SheetTitleText);
        Choose_Chip("Tomorrow");
        Assert.Equal(new DateTime(2030, 6, 13), cat.Items[0].DueDate);

        LongPress(PhoneRow(cat.Items[0]));
        Choose("Remove due date");
        Assert.Null(cat.Items[0].DueDate);

        LongPress(PhoneRow(cat.Items[0]));
        Choose("Move to list");
        Choose("Other");
        Assert.Equal("a", other.Categories[0].Items.Single().Text);
        Assert.Equal("Moved to Other", _view.SnackbarMessage);

        LongPress(PhoneRow(cat.Items[0])); // b
        Choose("Delete");
        Assert.Empty(cat.Items);
        Assert.Equal("Deleted b", _view.SnackbarMessage);

        Actions.Undo();
        LongPress(PhoneRow(cat.Items[0]));
        Choose("Edit");
        Assert.True(cat.Items[0].IsEditing);
    }

    [AvaloniaFact]
    public void With_one_list_there_is_nowhere_to_move_an_item()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        Tap(Card(b));
        LongPress(PhoneRow(b.Categories[0].Items[0]));
        Assert.DoesNotContain("Move to list", Options());
    }

    [AvaloniaFact]
    public void The_on_screen_keyboard_pushes_the_screen_up_and_long_menus_scroll()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        Pump();
        LongPress(PhoneRow(b.Categories[0].Items[0]));
        Choose("Add reminder");

        _view.KeyboardChanged(400); // the keyboard opened: 400 of the 760 high screen are covered
        Pump();
        var sheet = Named<Border>(_view, "Sheet");
        var bottom = sheet.TranslatePoint(new Point(0, sheet.Bounds.Height), _window)!.Value.Y;
        Assert.True(bottom <= 360.5, $"the menu ends at {bottom}, under the keyboard");
        var scroller = Find<ScrollViewer>(sheet)!;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the picker doesn't fit, so it scrolls");

        _view.KeyboardChanged(0);
        Pump();
        bottom = sheet.TranslatePoint(new Point(0, sheet.Bounds.Height), _window)!.Value.Y;
        Assert.Equal(760, bottom, 0.5);
    }

    [AvaloniaFact]
    public void What_is_being_typed_is_kept_when_the_app_goes_to_the_background()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        var item = b.Categories[0].Items[0];
        Show();
        _view.ShowBoard(b);
        Pump();
        item.IsEditing = true;
        Pump();
        Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, item))!.Text = "typed";
        _view.SaveTyping(); // the app goes to the background (the system may end it next)
        Assert.Equal("typed", item.Text);
        Assert.False(item.IsEditing);
    }

    void Choose_Chip(string text)
    {
        var chip = FindAll<Button>(_view.SheetPanel!).First(x => x.Content as string == text);
        Click(chip);
    }

    [AvaloniaFact]
    public void The_reminder_picker_checks_the_time()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        var item = b.Categories[0].Items[0];
        Show();
        _view.ShowBoard(b);
        Pump();
        LongPress(PhoneRow(item));
        Choose("Add reminder");
        var buttons = FindAll<Button>(_view.SheetPanel!).Select(x => x.Content as string).ToList();
        Assert.Equal(new[] { "In 1 hour", "This evening", "Tomorrow 9:00" }, buttons.Take(3));
        Assert.Contains("Save", buttons);

        var time = FindAll<TextBox>(_view.SheetPanel!).Single();
        var save = FindAll<Button>(_view.SheetPanel!).Single(x => x.Content as string == "Save");
        time.Text = "25:00";
        Click(save);
        Assert.Contains("Enter a time like 9:30", A11y.LastAnnouncement);
        time.Text = "9:00"; // today 9:00 is before 10:15
        Click(save);
        Assert.Equal("That time has already passed.", A11y.LastAnnouncement);
        time.Text = "11:30";
        Press(time, Key.D0); // typing: nothing saved yet
        Assert.True(_view.SheetOpen);
        Press(time, Key.Enter); // the keyboard's Enter (✓) saves, as Save may be under the keyboard
        Assert.Equal(new DateTime(2030, 6, 12, 11, 30, 0), item.ReminderAt);
        Assert.False(_view.SheetOpen);

        LongPress(PhoneRow(item));
        Assert.Contains("Change reminder", Options());
        Choose("Remove reminder");
        Assert.Null(item.ReminderAt);

        LongPress(PhoneRow(item));
        Choose("Add reminder");
        Choose_Chip("In 1 hour");
        Assert.Equal(new DateTime(2030, 6, 12, 11, 15, 0), item.ReminderAt);

        LongPress(PhoneRow(item));
        Choose("Add due date");
        FindAll<Calendar>(_view.SheetPanel!).Single().SelectedDate = new DateTime(2030, 7, 1);
        Click(FindAll<Button>(_view.SheetPanel!).Single(x => x.Content as string == "Save"));
        Assert.Equal(new DateTime(2030, 7, 1), item.DueDate);

        LongPress(PhoneRow(item));
        Choose("Change due date");
        FindAll<Calendar>(_view.SheetPanel!).Single().SelectedDate = null;
        Click(FindAll<Button>(_view.SheetPanel!).Single(x => x.Content as string == "Save"));
        Assert.Equal("Pick a day.", A11y.LastAnnouncement);
        Assert.True(_view.GoBack()); // Back closes the picker
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Dragging_items_and_categories_is_one_undoable_step()
    {
        var b = MakeBoard(("A", new[] { "a1", "a2", "a3" }), ("B", new[] { "b1" }));
        var (a, bb) = (b.Categories[0], b.Categories[1]);
        Show();
        _view.ShowBoard(b);
        Pump();

        Drag(PhoneRow(a.Items[0]), PhoneRow(a.Items[2]));
        Assert.Equal(new[] { "a2", "a3", "a1" }, a.Items.Select(i => i.Text));
        Actions.Undo();
        Assert.Equal(new[] { "a1", "a2", "a3" }, a.Items.Select(i => i.Text));

        Drag(PhoneRow(a.Items[0]), PhoneRow(bb.Items[0]));
        Assert.Equal(new[] { "b1", "a1" }, bb.Items.Select(i => i.Text));
        Actions.Undo();
        Assert.Equal(new[] { "b1" }, bb.Items.Select(i => i.Text));

        Drag(PhoneRow(a.Items[1]), PhoneRow(bb), 0.5); // onto a category header: to its top
        Assert.Equal(new[] { "a2", "b1" }, bb.Items.Select(i => i.Text));

        Drag(PhoneRow(a), PhoneRow(bb));
        Assert.Equal(new[] { "B", "A" }, b.Categories.Select(c => c.Name));
        Actions.Undo();
        Assert.Equal(new[] { "A", "B" }, b.Categories.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Category_and_board_menus()
    {
        var b = MakeBoard(("Work", new[] { "a" }), ("Home", new string[0]));
        b.Title = "Mine";
        Show();
        _view.ShowBoard(b);
        Pump();

        LongPress(PhoneRow(b.Categories[0]));
        Assert.Equal(new[] { "Rename", "Color", "Delete category" }, Options());
        Choose("Color");
        Choose("Red");
        Assert.Equal("#F1707A", b.Categories[0].Color);
        LongPress(PhoneRow(b.Categories[1]));
        Choose("Delete category");
        Assert.Single(b.Categories);
        Assert.Equal("Deleted category Home", _view.SnackbarMessage);
        LongPress(PhoneRow(b.Categories[0]));
        Choose("Rename");
        Assert.True(b.Categories[0].IsEditing);
        Assert.True(_view.GoBack()); // saves what's typed and goes back to the cards
        Assert.False(b.Categories[0].IsEditing);

        _view.ShowBoard(b);
        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Assert.Equal(new[] { "Rename", "Color", "Delete list" }, Options());
        Choose("Color");
        Choose("Dark");
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Find<ThemeVariantScope>(_view)!.RequestedThemeVariant);
        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Choose("Rename");
        Assert.True(Named<TextBox>(_view, "BoardTitleBox").IsVisible);
        Named<TextBox>(_view, "BoardTitleBox").Text = "";
        Press(Named<TextBox>(_view, "BoardTitleBox"), Key.Enter);
        Assert.Equal("No title", Named<TextBlock>(_view, "BoardTitle").Text);
        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Choose("Delete list");
        Choose("Delete");
        Assert.DoesNotContain(b, Store.Data.Boards);
        Assert.Null(_view.OpenBoard);
    }

    // ================= Archive, menu, export and import =================

    [AvaloniaFact]
    public void The_archive_restores_deletes_and_searches()
    {
        var b = MakeBoard(("Work", new[] { "milk", "bread" }));
        foreach (var i in b.Categories[0].Items.ToList()) Actions.Complete(i);
        Show();
        _view.ShowArchive();
        Pump();
        Assert.Equal(new[] { "bread", "milk" }, _view.ArchiveShown.Select(a => a.Text));
        Named<TextBox>(_view, "ArchiveSearch").Text = "mil";
        Assert.Single(_view.ArchiveShown);
        Named<TextBox>(_view, "ArchiveSearch").Text = "";

        _view.ShowArchivedMenu(Store.Data.Archive[0]);
        Choose("Restore");
        Assert.Equal("Restored", _view.SnackbarMessage);
        Assert.Single(Store.Data.Archive);

        Tap(Find<ListBoxItem>(_view)!);
        Choose("Delete permanently");
        Assert.Empty(Store.Data.Archive);
        Assert.True(Named<TextBlock>(_view, "ArchiveEmpty").IsVisible);
        Actions.Undo();
        Assert.Single(Store.Data.Archive);

        Store.Data.Boards.Clear(); // nowhere to restore to
        _view.ShowArchivedMenu(Store.Data.Archive[0]);
        Choose("Restore");
        Assert.Contains("no category", _view.SnackbarMessage);
        Assert.True(_view.GoBack());
    }

    [AvaloniaFact]
    public void The_archive_shows_completion_times_without_changing_them()
    {
        // A phone set to US English reads "01.11.2026" as January 11. The archive only shows the time and must
        // never parse it back into the item (that changed the date and looped until the app crashed).
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US", false);
            var b = MakeBoard(("Work", new[] { "milk" }));
            Actions.Complete(b.Categories[0].Items[0]);
            var completed = new DateTime(2026, 11, 1, 22, 5, 0);
            Store.Data.Archive[0].CompletedAt = completed;

            Show();
            _view.ShowArchive();
            Pump();

            Assert.Equal(completed, Store.Data.Archive[0].CompletedAt);
            Assert.Contains(FindAll<TextBlock>(_view).SelectMany(t => t.Inlines ?? new()).OfType<Avalonia.Controls.Documents.Run>(),
                r => r.Text == "01.11.2026 22:05");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [AvaloniaFact]
    public void Restoring_into_a_deleted_category_says_where_it_went()
    {
        var gone = MakeBoard(("Old", new[] { "x" }));
        Actions.Complete(gone.Categories[0].Items[0]);
        Store.Data.Boards.Remove(gone);
        MakeBoard(("Home", new string[0]));
        Show();
        _view.ShowArchivedMenu(Store.Data.Archive[0]);
        Choose("Restore");
        Assert.Equal("\"Old\" no longer exists, so it went to \"Home\"", _view.SnackbarMessage);
    }

    [AvaloniaFact]
    public void The_app_menu_switches_the_theme_and_opens_the_archive()
    {
        string? theme = null;
        var real = PhoneView.SetTheme;
        PhoneView.SetTheme = k => theme = k;
        try
        {
            Show();
            Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);
            Assert.Equal(new[] { "Archive", "App theme", "Export", "Import" }, Options());
            Choose("App theme");
            Choose("Light");
            Assert.Equal("Light", theme);
            Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);
            Choose("Archive");
            Assert.True(Named<DockPanel>(_view, "ArchiveScreen").IsVisible);
        }
        finally { PhoneView.SetTheme = real; }
    }

    [AvaloniaFact]
    public void Import_says_that_synced_lists_stay()
    {
        MakeBoard(("Mine", new[] { "mine" })).Synced = true;
        Show();
        var realImport = PhoneView.PickImportFile;
        try
        {
            var json = Store.ExportJson();
            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Await(_view.Import());
            Assert.Equal("Import 1 list with 1 item? Synced lists aren't replaced.", _view.SheetTitleText);
        }
        finally { PhoneView.PickImportFile = realImport; }
    }

    [AvaloniaFact]
    public void Export_writes_the_data_and_import_adds_or_replaces()
    {
        MakeBoard(("Mine", new[] { "mine" })).Title = "Mine";
        Show();
        var exported = new MemoryStream();
        var realExport = PhoneView.PickExportFile;
        var realImport = PhoneView.PickImportFile;
        try
        {
            PhoneView.PickExportFile = _ => Task.FromResult<Stream?>(new KeepOpen(exported));
            Await(_view.Export());
            var json = Encoding.UTF8.GetString(exported.ToArray());
            Assert.Contains("\"mine\"", json);
            Assert.Equal("Exported", _view.SnackbarMessage);

            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Await(_view.Import());
            Assert.Equal("Import 1 list with 1 item?", _view.SheetTitleText);
            Choose("Add next to mine");
            Assert.Equal(2, Store.Data.Boards.Count);
            Assert.Equal(2, _view.CardList.Count);

            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Await(_view.Import());
            Choose("Replace everything (a copy of mine is kept)");
            Assert.Single(Store.Data.Boards);
            Assert.Single(_view.CardList);

            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("not json")));
            Await(_view.Import());
            Assert.Contains("can't be imported", _view.SnackbarMessage);

            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(null); // cancelled
            Await(_view.Import());
            PhoneView.PickExportFile = _ => Task.FromResult<Stream?>(null);
            Await(_view.Export());
            PhoneView.PickExportFile = _ => throw new IOException("no space");
            Await(_view.Export());
            Assert.Equal("Export failed: no space", _view.SnackbarMessage);
        }
        finally
        {
            PhoneView.PickExportFile = realExport;
            PhoneView.PickImportFile = realImport;
        }
    }

    [AvaloniaFact]
    public void Import_counts_plural_and_replacing_closes_the_open_board_or_reports_a_failure()
    {
        var mine = MakeBoard(("A", new[] { "a", "b" }));
        MakeBoard(("B", new string[0]));
        var json = Store.Serialize(Store.Data);
        var realImport = PhoneView.PickImportFile;
        try
        {
            Show();
            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            _view.ShowBoard(mine);
            Await(_view.Import());
            Assert.Equal("Import 2 lists with 2 items?", _view.SheetTitleText);
            Choose("Replace everything (a copy of mine is kept)");
            Assert.Null(_view.OpenBoard); // the open board is gone: back to the cards
            Assert.Equal("Imported", _view.SnackbarMessage);

            Directory.CreateDirectory(Store.Dir);
            if (Directory.Exists(Store.BackupDir)) Directory.Delete(Store.BackupDir, true);
            File.WriteAllText(Store.BackupDir, "a file where the backups folder goes");
            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Await(_view.Import());
            Choose("Replace everything (a copy of mine is kept)");
            Assert.StartsWith("Import failed:", _view.SnackbarMessage);
            File.Delete(Store.BackupDir);

            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Await(_view.Import());
            Choose("Cancel");
            Assert.False(_view.SheetOpen);
        }
        finally { PhoneView.PickImportFile = realImport; }
    }

    [AvaloniaFact]
    public void Export_and_import_need_the_screen()
    {
        var view = new PhoneView(); // not shown anywhere
        bool asked = false;
        var real = PhoneView.PickExportFile;
        PhoneView.PickExportFile = _ => { asked = true; return Task.FromResult<Stream?>(null); };
        try
        {
            Await(view.Export());
            Await(view.Import());
            Assert.False(asked);
        }
        finally { PhoneView.PickExportFile = real; }
    }

    [AvaloniaFact]
    public void Bar_buttons_title_tap_and_tapping_outside_a_menu()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        b.Title = "Mine";
        Show();
        Click(Find<Button>(_view, x => NameOf(x) == "Archive")!);
        Assert.True(Named<DockPanel>(_view, "ArchiveScreen").IsVisible);
        Click(Find<Button>(Named<DockPanel>(_view, "ArchiveScreen"), x => NameOf(x) == "Back")!);
        Assert.True(Named<DockPanel>(_view, "HomeScreen").IsVisible);

        Click(Named<Button>(_view, "UndoButton")); // nothing done yet
        Assert.Equal("Nothing to undo", A11y.LastAnnouncement);

        _view.ShowBoard(b);
        Pump();
        Tap(Named<TextBlock>(_view, "BoardTitle"));
        Assert.True(Named<TextBox>(_view, "BoardTitleBox").IsVisible);
        Named<TextBox>(_view, "BoardTitleBox").Text = null;
        Press(Named<TextBox>(_view, "BoardTitleBox"), Key.Enter);
        Assert.Equal("", b.Title);

        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Pump();
        var shade = Named<Panel>(_view, "SheetLayer").Children.OfType<Border>().First();
        _window.MouseDown(new Point(200, 20), MouseButton.Left); // the dimmed part above the menu
        _window.MouseUp(new Point(200, 20), MouseButton.Left);
        Pump();
        Assert.False(_view.SheetOpen, "a tap outside closes the menu");
        Assert.NotNull(shade);

        Click(Find<Button>(Named<Border>(_view, "BoardScreen"), x => NameOf(x) == "Back")!);
        Assert.Null(_view.OpenBoard);
        Press((Interactive)_view, Key.A); // a key outside any text box: nothing happens

        bool exported = false;
        var real = PhoneView.PickExportFile;
        PhoneView.PickExportFile = _ => { exported = true; return Task.FromResult<Stream?>(null); };
        try
        {
            Click(Find<Button>(_view, x => NameOf(x) == "Menu")!);
            Choose("Export");
            Assert.True(UiHost.WaitUntil(() => exported));
        }
        finally { PhoneView.PickExportFile = real; }

        _view.ShowArchive();
        Pump();
        Tap(Named<ListBox>(_view, "ArchiveList")); // the empty list itself, not an entry
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Escape_and_emptied_text_while_editing()
    {
        var b = MakeBoard(("Work", new[] { "first line\nsecond line", "gone" }));
        var cat = b.Categories[0];
        Show();
        Assert.Equal("first line", _view.CardList[0].Preview[1].Text);
        _view.ShowBoard(b);
        Pump();

        LongPress(PhoneRow(cat.Items[0]));
        Assert.Equal("first line", _view.SheetTitleText);
        Choose("Edit");
        Pump();
        var box = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, cat.Items[0]))!;
        box.Text = "changed";
        Press(box, Key.Escape);
        Assert.False(cat.Items[0].IsEditing);
        Assert.Equal("first line\nsecond line", cat.Items[0].Text);

        LongPress(PhoneRow(cat));
        Choose("Rename");
        Pump();
        box = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, cat))!;
        Press(box, Key.Escape);
        Assert.False(cat.IsEditing);
        cat.IsEditing = true;
        Pump();
        box.Text = "  ";
        Press(box, Key.Enter);
        Assert.Equal("Work", cat.Name); // an empty name keeps the old one

        LongPress(PhoneRow(cat.Items[1]));
        Choose("Edit");
        Pump();
        box = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, cat.Items[1]))!;
        box.Text = null;
        Press(box, Key.Enter);
        Assert.Single(cat.Items); // an emptied item is deleted

        var add = Find<TextBox>(_view, t => t.Classes.Contains("phoneadd"))!;
        Press(add, Key.Escape); // only Enter does something in the add box
        cat.Items[0].IsEditing = true;
        Pump();
        box = Find<TextBox>(_view, t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, cat.Items[0]))!;
        Press(box, Key.A); // typing
        Assert.True(cat.Items[0].IsEditing);
        Press(box, Key.Escape);
        add.Text = null;
        Press(add, Key.Enter);
        add.Text = "   ";
        Press(add, Key.Enter);
        Assert.Single(cat.Items);
    }

    [AvaloniaFact]
    public void Presses_that_are_not_a_long_press_on_a_row()
    {
        var b = MakeBoard(("A", new[] { "a", "b" }));
        var cat = b.Categories[0];
        Show();
        _view.ShowBoard(b);
        Pump();

        // holding the check button doesn't lift the row (it's a tap on the button: the item starts completing)
        PhoneView.CompleteDelay = TimeSpan.FromSeconds(30);
        var check = Find<Button>(PhoneRow(cat.Items[0]), x => x.Classes.Contains("check"))!;
        LongPress(check);
        Assert.False(_view.SheetOpen);
        _view.ToggleCompleting(cat.Items[0]); // changed my mind
        // nor does holding the add box
        LongPress(Find<TextBox>(_view, t => t.Classes.Contains("phoneadd"))!);
        Assert.False(_view.SheetOpen);
        // holding where there is no row
        _window.MouseDown(new Point(200, 740), MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        _window.MouseUp(new Point(200, 740), MouseButton.Left);
        Assert.False(_view.SheetOpen);
        // moving the pointer around without pressing
        _window.MouseMove(At(PhoneRow(cat.Items[1])));

        // a little wobble while holding is still a long-press (opens the menu, nothing moves)
        var p = At(PhoneRow(cat.Items[0]));
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        _window.MouseMove(p + new Point(3, 3), RawInputModifiers.LeftMouseButton);
        _window.MouseUp(p + new Point(3, 3), MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(100));
        Assert.True(_view.SheetOpen);
        Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Dragging_up_and_dropping_back_in_place()
    {
        var b = MakeBoard(("A", new[] { "a1", "a2", "a3" }));
        var a = b.Categories[0];
        var c1 = MakeBoard(("X", new[] { "x" })); c1.Title = "One";
        var c2 = MakeBoard(("Y", new[] { "y" })); c2.Title = "Two";
        Show();

        // a card held and moved, but only over itself: nothing changes and nothing is undoable
        var card = Card(c1);
        var p = At(card);
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        Assert.Same(c1, _view.HomeDrag.Lifted);
        _window.MouseMove(p + new Point(0, 20), RawInputModifiers.LeftMouseButton);
        Pump();
        _window.MouseUp(p + new Point(0, 20), MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
        Assert.Equal(new[] { "Test", "One", "Two" }, Store.Data.Boards.Select(x => x.Title));
        Assert.False(_view.SheetOpen);

        _view.ShowBoard(b);
        Pump();
        Drag(PhoneRow(a.Items[2]), PhoneRow(a.Items[0]), 0.25); // up, onto the top half of the first
        Assert.Equal(new[] { "a3", "a1", "a2" }, a.Items.Select(i => i.Text));
        Actions.Undo();
        Assert.Equal(new[] { "a1", "a2", "a3" }, a.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Holding_a_dragged_row_near_the_bottom_scrolls_the_list()
    {
        var b = MakeBoard(("A", Enumerable.Range(1, 40).Select(i => $"item {i}").ToArray()));
        Show();
        _view.ShowBoard(b);
        Pump();
        var scroller = Named<ScrollViewer>(_view, "BoardScroller");
        var p = At(PhoneRow(b.Categories[0].Items[0]));
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        var bottom = scroller.TranslatePoint(new Point(200, scroller.Bounds.Height - 10), _window)!.Value;
        _window.MouseMove(new Point(p.X, (p.Y + bottom.Y) / 2), RawInputModifiers.LeftMouseButton);
        _window.MouseMove(bottom, RawInputModifiers.LeftMouseButton);
        Assert.True(UiHost.WaitUntil(() => scroller.Offset.Y > 50), "the list didn't scroll");
        var top = scroller.TranslatePoint(new Point(200, 10), _window)!.Value;
        _window.MouseMove(top, RawInputModifiers.LeftMouseButton);
        Assert.True(UiHost.WaitUntil(() => scroller.Offset.Y == 0), "the list didn't scroll back up");
        UiHost.Wait(TimeSpan.FromMilliseconds(100)); // at the top already: nothing more to scroll
        _window.MouseUp(top, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
        Assert.Equal(40, b.Categories[0].Items.Count);
        Assert.Null(_view.HomeDrag.Lifted);
    }

    [AvaloniaFact]
    public void Board_menus_from_the_cards_and_colors_while_open()
    {
        var empty = MakeBoard(("A", new string[0]));
        empty.Title = "Empty";
        var b = MakeBoard(("B", new[] { "x" }));
        var untitled = MakeBoard(("C", new string[0]));
        untitled.Title = "";
        Show();

        LongPress(Card(empty));
        Choose("Delete list");
        Assert.Equal("Delete \"Empty\"?", _view.SheetTitleText);
        Choose("Delete");
        Assert.DoesNotContain(empty, Store.Data.Boards);

        _view.ShowBoard(b);
        Pump();
        LongPress(PhoneRow(b.Categories[0].Items[0]));
        Choose("Move to list");
        Choose("No title");
        Assert.Single(untitled.Categories[0].Items);

        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Choose("Color");
        Choose("Light");
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Find<ThemeVariantScope>(_view)!.RequestedThemeVariant);
        _view.App_ThemeOrDataChanged(); // the app theme changed while a board is open: it keeps its colors
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Find<ThemeVariantScope>(_view)!.RequestedThemeVariant);

        Store.Data.Boards.Clear();
        Pump();
        Assert.Null(_view.OpenBoard);
        Click(Named<Button>(_view, "AddBoardButton")); // with no lists at all
        Assert.Single(Store.Data.Boards);
    }

    [AvaloniaFact]
    public void Date_picker_quick_choices_depend_on_the_day_and_time()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        var item = b.Categories[0].Items[0];
        item.DueDate = new DateTime(2030, 6, 1); // in the past
        PhoneView.Clock = () => new DateTime(2030, 6, 17, 18, 30, 0); // a Monday evening
        Show();
        PhoneView.Clock = () => new DateTime(2030, 6, 17, 18, 30, 0);
        _view.ShowBoard(b);
        Pump();

        LongPress(PhoneRow(item));
        Choose("Change due date");
        Assert.Equal(new DateTime(2030, 6, 17), FindAll<Calendar>(_view.SheetPanel!).Single().SelectedDate); // not in the past
        Choose_Chip("Next week");
        Assert.Equal(new DateTime(2030, 6, 24), item.DueDate);

        LongPress(PhoneRow(item));
        Choose("Add reminder");
        Assert.DoesNotContain(FindAll<Button>(_view.SheetPanel!), x => x.Content as string == "This evening");
        Assert.Equal(new DateTime(2030, 6, 24), FindAll<Calendar>(_view.SheetPanel!).Single().SelectedDate); // starts on the due day
        Choose_Chip("Tomorrow 9:00");
        Assert.Equal(new DateTime(2030, 6, 18, 9, 0, 0), item.ReminderAt);

        LongPress(PhoneRow(item));
        Choose("Change reminder");
        Assert.Equal("Change reminder", _view.SheetTitleText);
        Assert.Equal("09:00", FindAll<TextBox>(_view.SheetPanel!).Single().Text);
        Assert.True(_view.GoBack());

        PhoneView.Clock = () => new DateTime(2030, 6, 12, 10, 15, 0); // Wednesday morning
        LongPress(PhoneRow(item));
        Choose("Change due date");
        Choose_Chip("Next week");
        Assert.Equal(new DateTime(2030, 6, 17), item.DueDate);
        LongPress(PhoneRow(item));
        Choose("Change due date");
        Choose_Chip("Today");
        Assert.Equal(new DateTime(2030, 6, 12), item.DueDate);
        LongPress(PhoneRow(item));
        Choose("Change reminder");
        Choose_Chip("This evening");
        Assert.Equal(new DateTime(2030, 6, 12, 18, 0, 0), item.ReminderAt);
    }

    [AvaloniaFact]
    public void Store_messages_show_in_the_bar()
    {
        Show();
        _view.ShowMessageForStore("The backup was loaded");
        Assert.True(_view.SnackbarShown);
        Assert.False(Named<Button>(_view, "UndoButton").IsVisible);
    }

    static void Await(Task t)
    {
        Assert.True(UiHost.WaitUntil(() => t.IsCompleted));
        t.GetAwaiter().GetResult();
    }

    /// <summary>A stream the test can read after the app closed it.</summary>
    sealed class KeepOpen(MemoryStream inner) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }
}

/// <summary>The phone app class itself (loading, first start, theme, reminders).</summary>
public class PhoneAppTests : UiTest
{
    [AvaloniaFact]
    public void Start_loads_the_data_and_makes_a_first_list()
    {
        var dir = Path.Combine(Dir, "phone");
        PhoneApp.DataDir = dir;
        var shown = new List<(string, string)>();
        PhoneApp.ShowNotification = (t, x) => shown.Add((t, x));
        int saved = 0;
        PhoneApp.Saved = () => saved++;
        var app = new PhoneApp();
        try
        {
            var view = app.Start();
            Assert.NotNull(view);
            Assert.Single(Store.Data.Boards); // first start: one list
            Assert.True(File.Exists(Path.Combine(dir, "data.json")));

            app.SetTheme("Light");
            Assert.Equal("Light", Store.Data.Settings.Theme);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.RequestedThemeVariant);

            var item = Store.Data.Boards[0].Categories[0].Items;
            Actions.AddItem(Store.Data.Boards[0].Categories[0], "Call");
            item[0].ReminderAt = DateTime.Now.AddMinutes(-1);
            app.ReminderService!.Check();
            Assert.Equal(("Reminder", "Call"), shown.Single());

            Assert.True(UiHost.WaitUntil(() => saved > 0)); // saving tells the platform (alarms)
            Store.Notify("damaged file", true);
            Assert.True(view.SnackbarShown);
            app.SetTheme("Dark");
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, app.RequestedThemeVariant);

            // a second start (the activity was made again), here without a folder or notifications set
            PhoneApp.DataDir = null;
            PhoneApp.ShowNotification = null;
            app.Start();
            Assert.Single(Store.Data.Boards);
            var later = Actions.AddItem(Store.Data.Boards[0].Categories[0], "Later");
            later.ReminderAt = DateTime.Now.AddMinutes(-1);
            app.ReminderService!.Check(); // nothing to show it with: no crash
            Assert.Single(shown);
        }
        finally
        {
            app.ReminderService?.Stop();
            PhoneApp.DataDir = null;
            PhoneApp.ShowNotification = null;
            PhoneApp.Saved = null;
            App.ConfigureCore(); // the desktop app's settings back
            Store.UseDirectory(Dir);
            Store.Saver = Saver;
        }
    }
}
