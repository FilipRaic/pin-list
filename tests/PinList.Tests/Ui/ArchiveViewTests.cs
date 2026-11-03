using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;

namespace PinList.Tests;

/// <summary>The archive section: items grouped by day, search, restore, delete, undo.</summary>
public class ArchiveViewTests : UiTest
{
    static ListBox List(ArchiveView a) => a.Items;
    static TextBox Search(ArchiveView a) => a.Search;
    static TextBlock Status(ArchiveView a) => Named<TextBlock>(a, "StatusText");

    /// <summary>The rows of the list with a restore or delete button (on the row of this item).</summary>
    static Button RowButton(ArchiveView a, ArchivedItem item, string cls) =>
        Find<Button>((Control)List(a).ContainerFromItem(item)!, b => b.Classes.Contains(cls))!;

    /// <summary>A widget whose items are all completed (archive: newest first).</summary>
    static Board Completed(params string[] texts)
    {
        var board = MakeBoard(("Work", texts));
        foreach (var i in board.Categories[0].Items.ToList()) Actions.Complete(i);
        Actions.ClearUndo();
        return board;
    }

    static ArchiveView OpenArchive()
    {
        App.ShowArchive();
        Pump();
        return App.MainWindow!.Archive;
    }

    /// <summary>A real double click with the mouse on an element of the window.</summary>
    static void DoubleClick(Window w, Control at)
    {
        var p = at.TranslatePoint(new Point(at.Bounds.Width / 2, at.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void Lists_completed_items_grouped_by_day_and_starts_on_the_first()
    {
        Completed("a", "b");
        var a = OpenArchive();
        Assert.Equal("2 items", Named<TextBlock>(a, "CountText").Text);
        Assert.False(Named<TextBlock>(a, "EmptyText").IsVisible);
        var rows = List(a).Items.Cast<object>().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Today", Assert.IsType<ArchiveDayHeader>(rows[0]).Title);
        Assert.Same(a.Shown[0], List(a).SelectedItem);
        Assert.IsType<ListBoxItem>(Focused(App.MainWindow!));
        Assert.Equal("Completed items", NameOf(List(a)));
        Assert.StartsWith("b, completed", NameOf((Control)List(a).ContainerFromIndex(1)!)); // read as text, not a type name
        Assert.Equal("Today", NameOf((Control)List(a).ContainerFromIndex(0)!));
    }

    [AvaloniaFact]
    public void A_row_shows_the_category_in_its_color_and_the_meta_line()
    {
        Completed("a");
        var a = OpenArchive();
        var item = a.Shown[0];
        var row = (Control)List(a).ContainerFromItem(item)!;
        var meta = Find<TextBlock>(row, t => t.Classes.Contains("meta"))!;
        Assert.Equal(item.MetaText, string.Concat(meta.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        var name = meta.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Single(r => r.Text == "Work");
        Assert.Equal(Themes.CategoryShade(item.CategoryColor, "Dark"), ((ISolidColorBrush)name.Foreground!).Color);
        var circle = Find<Avalonia.Controls.Shapes.Ellipse>(row)!;
        Assert.Equal(Themes.CategoryColor(item.CategoryColor, "Dark", CategoryPart.Fill), ((ISolidColorBrush)circle.Fill!).Color);
    }

    [AvaloniaFact]
    public void An_empty_archive_starts_in_the_search_box()
    {
        var a = OpenArchive();
        Assert.Equal("0 items", Named<TextBlock>(a, "CountText").Text);
        Assert.True(Named<TextBlock>(a, "EmptyText").IsVisible);
        Assert.Same(Search(a), Focused(App.MainWindow!));

        Completed("only");
        Assert.Equal("1 item", Named<TextBlock>(a, "CountText").Text); // follows changes
    }

    [AvaloniaFact]
    public void Search_filters_by_text_category_and_list()
    {
        var board = Completed("Buy milk", "Call mum");
        var a = OpenArchive();
        Search(a).Text = "milk";
        Assert.Single(a.Shown);
        Search(a).Text = "WORK"; // category, any case
        Assert.Equal(2, a.Shown.Length);
        Search(a).Text = board.Title;
        Assert.Equal(2, a.Shown.Length);
        Search(a).Text = "nothing like this";
        Assert.Empty(List(a).Items);
    }

    [AvaloniaFact]
    public void Search_hides_empty_days()
    {
        var board = MakeBoard(("Work", new[] { "milk", "eggs" }));
        Actions.Complete(board.Categories[0].Items[0]);
        Actions.Complete(board.Categories[0].Items[0]);
        Store.Data.Archive[0].CompletedAt = DateTime.Now.AddDays(-1); // eggs: yesterday, milk: today
        var a = OpenArchive();
        Assert.Equal(new[] { "Today", "Yesterday" }, List(a).Items.OfType<ArchiveDayHeader>().Select(h => h.Title));
        Search(a).Text = "eggs";
        Assert.Equal(new[] { "Yesterday" }, List(a).Items.OfType<ArchiveDayHeader>().Select(h => h.Title));
        Assert.Single(a.Shown);
    }

    [AvaloniaFact]
    public void Headers_are_not_selectable()
    {
        var board = MakeBoard(("Work", new[] { "milk", "eggs", "tea" }));
        foreach (var i in board.Categories[0].Items.ToList()) Actions.Complete(i);
        Store.Data.Archive[2].CompletedAt = DateTime.Now.AddDays(-2); // two days
        var a = OpenArchive();
        var headers = List(a).Items.OfType<ArchiveDayHeader>().ToList();
        Assert.Equal(2, headers.Count);

        List(a).SelectAll();
        Assert.DoesNotContain(List(a).SelectedItems!.Cast<object>(), x => x is ArchiveDayHeader);
        Assert.Equal(3, List(a).SelectedItems!.Count);

        // arrow keys go from entry to entry and skip the heading in between
        List(a).SelectedItem = a.Shown[1];
        Press(List(a), Key.Down);
        Assert.Same(a.Shown[2], List(a).SelectedItem);
        Press(List(a), Key.Down); // the last one stays
        Assert.Same(a.Shown[2], List(a).SelectedItem);
        Press(List(a), Key.Up);
        Press(List(a), Key.Up);
        Press(List(a), Key.Up);
        Assert.Same(a.Shown[0], List(a).SelectedItem);

        // the headings can't be focused or clicked, and Enter restores only entries
        Assert.False(((Control)List(a).ContainerFromItem(headers[0])!).Focusable);
        Assert.False(((Control)List(a).ContainerFromItem(headers[1])!).IsHitTestVisible);
        List(a).SelectAll();
        Press(List(a), Key.Enter);
        Assert.Equal(3, board.Categories[0].Items.Count);
        Assert.Empty(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Keyboard_shortcuts()
    {
        Completed("a");
        var a = OpenArchive();
        var window = App.MainWindow!;
        Press(window, Key.F, KeyModifiers.Control);
        Assert.Same(Search(a), Focused(window));
        Search(a).Text = "zzz";
        Press(Search(a), Key.Escape); // clears the search first...
        Assert.Equal("", Search(a).Text);
        Press(Search(a), Key.Escape); // ...then closes (hides) the window
        Pump();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Restore_with_Enter_double_click_or_button_and_undo()
    {
        var board = Completed("a", "b", "c"); // archive: c, b, a
        var cat = board.Categories[0];
        var a = OpenArchive();
        var window = App.MainWindow!;

        Press(List(a), Key.Enter); // c
        Assert.Equal(new[] { "c" }, cat.Items.Select(i => i.Text));
        Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text);
        Assert.Equal("Restored. Ctrl+Z undoes.", A11y.LastAnnouncement);
        Assert.Same(a.Shown[0], List(a).SelectedItem); // the next row is selected, so Enter can be pressed again
        Pump();
        Assert.IsType<ListBoxItem>(Focused(window));

        DoubleClick(window, (Control)List(a).ContainerFromItem(a.Shown[0])!);
        Assert.Equal(2, cat.Items.Count); // b

        List(a).SelectAll();
        Click(RowButton(a, a.Shown[0], "restore"));
        Assert.Equal(3, cat.Items.Count);
        Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text); // the last one (a)
        Assert.Same(Search(a), Focused(window)); // the archive is empty now

        Press(window, Key.Z, KeyModifiers.Control);
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void The_restore_button_of_one_row_acts_only_on_that_row_and_double_click_on_a_button_does_not_restore()
    {
        var board = Completed("a", "b", "c");
        var a = OpenArchive();
        List(a).SelectedItem = a.Shown[0];
        var other = a.Shown[2]; // not selected
        Click(RowButton(a, other, "restore"));
        Assert.Equal(new[] { "a" }, board.Categories[0].Items.Select(i => i.Text));
        Assert.Equal(2, Store.Data.Archive.Count);

        var button = RowButton(a, a.Shown[0], "restore");
        DoubleClick(App.MainWindow!, button); // a double click on a row button is not a double click on the row
        Assert.True(board.Categories[0].Items.Count <= 2);
    }

    [AvaloniaFact]
    public void The_restore_and_delete_buttons_under_the_list_act_on_the_selection()
    {
        var board = Completed("a", "b", "c"); // archive: c, b, a
        var a = OpenArchive();
        var restore = Named<Button>(a, "RestoreButton");
        var delete = Named<Button>(a, "DeleteButton");
        Assert.True(restore.IsEnabled); // the first entry is selected when the archive opens

        List(a).SelectedItems!.Clear();
        Pump();
        Assert.False(restore.IsEnabled);
        Assert.False(delete.IsEnabled);

        List(a).SelectedItem = a.Shown[1]; // b
        Pump();
        Assert.True(delete.IsEnabled);
        Click(restore);
        Assert.Equal(new[] { "b" }, board.Categories[0].Items.Select(i => i.Text));
        Assert.Equal("Restore selected", NameOf(restore));
        Assert.Equal("Delete selected permanently", NameOf(delete));
    }

    /// <summary>A real mouse click on a row, then a real key press through the window (like a person does it).</summary>
    static void ClickRowThenPress(ArchiveView a, ArchivedItem item, Key key, PhysicalKey physical)
    {
        var w = App.MainWindow!;
        var row = (Control)List(a).ContainerFromItem(item)!;
        var p = row.TranslatePoint(new Point(40, row.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        w.KeyPress(key, RawInputModifiers.None, physical, null);
        w.KeyRelease(key, RawInputModifiers.None, physical, null);
        Pump();
    }

    [AvaloniaFact]
    public void Enter_after_clicking_a_row_restores_it()
    {
        var board = Completed("a", "b"); // archive: b, a
        var a = OpenArchive();
        ClickRowThenPress(a, a.Shown[1], Key.Enter, PhysicalKey.Enter);
        Assert.Equal(new[] { "a" }, board.Categories[0].Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Delete_after_clicking_a_row_deletes_it()
    {
        Dialogs.ChooseAnswer = _ => 1; // "Delete" in the confirmation
        Completed("a", "b");
        var a = OpenArchive();
        ClickRowThenPress(a, a.Shown[1], Key.Delete, PhysicalKey.Delete);
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void On_a_Mac_the_delete_key_deletes_and_Cmd_Z_undoes()
    {
        bool was = OsText.IsMac;
        OsText.IsMac = true;
        try
        {
            Dialogs.ChooseAnswer = _ => 1; // "Delete" in the confirmation
            Completed("a", "b");
            var a = OpenArchive();
            ClickRowThenPress(a, a.Shown[1], Key.Back, PhysicalKey.Backspace);
            Assert.Single(Store.Data.Archive);
            Press(App.MainWindow!, Key.Z, KeyModifiers.Meta);
            Assert.Equal(2, Store.Data.Archive.Count);

            // in the search box the delete key only deletes text
            Search(a).Focus();
            Search(a).Text = "b";
            Press(Search(a), Key.Back);
            Assert.Equal(2, Store.Data.Archive.Count);
        }
        finally { OsText.IsMac = was; }
    }

    [AvaloniaFact]
    public void Enter_on_the_Delete_button_does_not_restore()
    {
        Completed("a");
        var a = OpenArchive();
        var delete = Named<Button>(a, "DeleteButton");
        delete.Focus();
        Press(delete, Key.Enter);
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Restoring_several_items_says_how_many()
    {
        Completed("a", "b");
        var a = OpenArchive();
        List(a).SelectAll();
        Click(RowButton(a, a.Shown[0], "restore"));
        Assert.Equal("Restored 2 items. Ctrl+Z undoes.", Status(a).Text);
    }

    [AvaloniaFact]
    public void Double_click_outside_a_row_does_nothing()
    {
        Completed("a");
        var a = OpenArchive();
        DoubleClick(App.MainWindow!, Named<TextBlock>(a, "CountText"));
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Restoring_into_a_deleted_category_says_where_it_went()
    {
        var gone = Completed("x", "y", "z");
        var keep = MakeBoard(("Home", new string[0]));
        Store.Data.Boards.Remove(gone);
        var a = OpenArchive();
        List(a).SelectedItem = a.Shown[0];
        Click(RowButton(a, a.Shown[0], "restore"));
        Assert.Equal("\"Work\" no longer exists, so the item went to \"Home\".", Status(a).Text);

        List(a).SelectAll(); // the other two
        Click(RowButton(a, a.Shown[0], "restore"));
        Assert.Contains("items went to \"Home\" because their category no longer exists.", Status(a).Text);
        Assert.Equal(3, keep.Categories[0].Items.Count);
    }

    [AvaloniaFact]
    public void Restoring_with_no_category_anywhere_explains_it()
    {
        var board = Completed("x");
        Store.Data.Boards.Remove(board);
        var a = OpenArchive();
        Click(RowButton(a, a.Shown[0], "restore"));
        Assert.Equal("There is no category to restore into. Add a category to a widget first.", Status(a).Text);

        List(a).SelectedItems!.Clear();
        Press(List(a), Key.Enter); // nothing selected: nothing happens
        Press(List(a), Key.Delete);
    }

    [AvaloniaFact]
    public void The_status_message_goes_away_after_a_while()
    {
        Completed("a", "b");
        var a = OpenArchive();
        a.StatusDuration = TimeSpan.FromMilliseconds(50);
        Press(List(a), Key.Enter);
        Assert.True(Status(a).IsVisible);
        Assert.False(Named<TextBlock>(a, "HintText").IsVisible);
        UiHost.Wait(TimeSpan.FromMilliseconds(200));
        Assert.False(Status(a).IsVisible);
        Assert.True(Named<TextBlock>(a, "HintText").IsVisible);
    }

    [AvaloniaFact]
    public void Delete_removes_permanently_and_undo_brings_back()
    {
        Dialogs.ChooseAnswer = _ => 1; // "Delete" in the confirmation
        Completed("a", "b", "c");
        var a = OpenArchive();
        var window = App.MainWindow!;
        List(a).SelectedItem = a.Shown[2];
        Press(List(a), Key.Delete);
        Assert.Equal(2, Store.Data.Archive.Count);
        Assert.Same(a.Shown[1], List(a).SelectedItem); // the next row (now the last one)
        Assert.Equal("Deleted 1 item permanently. Ctrl+Z undoes.", A11y.LastAnnouncement);

        List(a).SelectAll();
        Click(RowButton(a, a.Shown[0], "deleteforever"));
        Assert.Empty(Store.Data.Archive);
        Assert.Same(Search(a), Focused(window));

        Search(a).Focus();
        Press(Search(a), Key.Z, KeyModifiers.Control); // empty search box: undo works
        Assert.Equal(2, Store.Data.Archive.Count);

        Search(a).Text = "typing";
        Press(Search(a), Key.Z, KeyModifiers.Control); // while typing, Ctrl+Z belongs to the text
        Assert.Equal(2, Store.Data.Archive.Count);
        Press(Search(a), Key.A); // other keys are left alone
    }

    [AvaloniaFact]
    public void Selection_survives_a_search_that_still_shows_it()
    {
        Completed("apple", "avocado", "banana");
        var a = OpenArchive();
        List(a).SelectedItem = Store.Data.Archive.First(x => x.Text == "avocado");
        Search(a).Text = "a"; // all three still match
        Assert.Equal("avocado", ((ArchivedItem)List(a).SelectedItem!).Text);
    }

    [AvaloniaFact]
    public void Other_keys_in_the_list_and_undo_with_nothing_to_undo_change_nothing()
    {
        Completed("a");
        var a = OpenArchive();
        Press(List(a), Key.A);
        Press(App.MainWindow!, Key.Z, KeyModifiers.Control); // nothing to undo: just says so
        Assert.Equal("Nothing to undo", A11y.LastAnnouncement);
        Assert.Equal(new[] { "a" }, Store.Data.Archive.Select(x => x.Text));
        Assert.True(App.MainWindow!.IsVisible);
    }

    [AvaloniaFact]
    public void Arrow_keys_in_an_empty_list_do_nothing()
    {
        var a = OpenArchive();
        Press(List(a), Key.Down);
        Press(List(a), Key.Up);
        Assert.Empty(a.Shown);
    }

    [AvaloniaFact]
    public void A_replaced_archive_is_shown_after_reload()
    {
        Completed("old");
        var a = OpenArchive();
        var imported = new AppData();
        imported.Archive.Add(new ArchivedItem { Text = "new", CompletedAt = DateTime.Now, CreatedAt = DateTime.Now });
        Store.ReplaceAll(imported);
        a.Reload();
        Assert.Equal(new[] { "new" }, a.Shown.Select(x => x.Text));
        Assert.Equal("1 item", Named<TextBlock>(a, "CountText").Text);
    }
}
