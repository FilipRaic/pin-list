using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;

namespace PinList.Tests;

/// <summary>The archive window: list, search, restore, delete, undo.</summary>
public class ArchiveWindowTests : UiTest
{
    static ListBox List(ArchiveWindow a) => a.Items;
    static TextBox Search(ArchiveWindow a) => a.Search;
    static TextBlock Status(ArchiveWindow a) => Named<TextBlock>(a, "StatusText");
    static Button ButtonWith(ArchiveWindow a, string text) => FindAll<Button>(a).First(b => b.Content as string == text);

    /// <summary>A widget whose items are all completed (archive: newest first).</summary>
    static Board Completed(params string[] texts)
    {
        var board = MakeBoard(("Work", texts));
        foreach (var i in board.Categories[0].Items.ToList()) Actions.Complete(i);
        Actions.ClearUndo();
        return board;
    }

    static ArchiveWindow OpenArchive()
    {
        App.ShowArchive();
        Pump();
        return App.Archive!;
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
    public void Lists_completed_items_and_starts_on_the_first()
    {
        Completed("a", "b");
        var a = OpenArchive();
        Assert.Equal("2 items", Named<TextBlock>(a, "CountText").Text);
        Assert.False(Named<TextBlock>(a, "EmptyText").IsVisible);
        Assert.Equal(0, List(a).SelectedIndex);
        Assert.IsType<ListBoxItem>(Focused(a));
        Assert.Equal("Completed items", NameOf(List(a)));
        Assert.StartsWith("b, completed", NameOf((Control)List(a).ContainerFromIndex(0)!)); // read as text, not a type name
    }

    [AvaloniaFact]
    public void An_empty_archive_starts_in_the_search_box()
    {
        var a = OpenArchive();
        Assert.Equal("0 items", Named<TextBlock>(a, "CountText").Text);
        Assert.True(Named<TextBlock>(a, "EmptyText").IsVisible);
        Assert.Same(Search(a), Focused(a));

        Completed("only");
        Assert.Equal("1 item", Named<TextBlock>(a, "CountText").Text); // follows changes
    }

    [AvaloniaFact]
    public void Opening_it_again_brings_back_the_same_window()
    {
        var first = OpenArchive();
        first.WindowState = WindowState.Minimized;
        Assert.Same(first, OpenArchive());
        Assert.Equal(WindowState.Normal, first.WindowState);
        first.Close();
        Pump();
        Assert.Null(App.Archive);
    }

    [AvaloniaFact]
    public void Opening_it_again_while_it_is_open_keeps_its_size()
    {
        var first = OpenArchive();
        first.WindowState = WindowState.Maximized;
        Assert.Same(first, OpenArchive());
        Assert.Equal(WindowState.Maximized, first.WindowState); // only a minimized one is restored
    }

    [AvaloniaFact]
    public void Search_filters_by_text_category_and_widget()
    {
        var board = Completed("Buy milk", "Call mum");
        var a = OpenArchive();
        Search(a).Text = "milk";
        Assert.Single(List(a).Items);
        Search(a).Text = "WORK"; // category, any case
        Assert.Equal(2, List(a).Items.Count);
        Search(a).Text = board.Title;
        Assert.Equal(2, List(a).Items.Count);
        Search(a).Text = "nothing like this";
        Assert.Empty(List(a).Items);
    }

    [AvaloniaFact]
    public void Keyboard_shortcuts()
    {
        Completed("a");
        var a = OpenArchive();
        Press(a, Key.F, KeyModifiers.Control);
        Assert.Same(Search(a), Focused(a));
        Search(a).Text = "zzz";
        Press(Search(a), Key.Escape); // clears the search first…
        Assert.Equal("", Search(a).Text);
        Press(Search(a), Key.Escape); // …then closes
        Assert.Null(App.Archive);
    }

    [AvaloniaFact]
    public void Restore_with_Enter_double_click_or_button_and_undo()
    {
        var board = Completed("a", "b", "c"); // archive: c, b, a
        var cat = board.Categories[0];
        var a = OpenArchive();

        Press(List(a), Key.Enter); // c
        Assert.Equal(new[] { "c" }, cat.Items.Select(i => i.Text));
        Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text);
        Assert.Equal("Restored. Ctrl+Z undoes.", A11y.LastAnnouncement);
        Assert.Equal(0, List(a).SelectedIndex); // the next row is selected, so Enter can be pressed again
        Pump();
        Assert.IsType<ListBoxItem>(Focused(a));

        DoubleClick(a, (Control)List(a).ContainerFromIndex(0)!);
        Assert.Equal(2, cat.Items.Count); // b

        List(a).SelectAll();
        Click(ButtonWith(a, "Restore"));
        Assert.Equal(3, cat.Items.Count);
        Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text); // the last one (a)
        Assert.Same(Search(a), Focused(a)); // the archive is empty now

        Press(a, Key.Z, KeyModifiers.Control);
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Restoring_several_items_says_how_many()
    {
        Completed("a", "b");
        var a = OpenArchive();
        List(a).SelectAll();
        Click(ButtonWith(a, "Restore"));
        Assert.Equal("Restored 2 items. Ctrl+Z undoes.", Status(a).Text);
    }

    [AvaloniaFact]
    public void Double_click_outside_a_row_does_nothing()
    {
        Completed("a");
        var a = OpenArchive();
        DoubleClick(a, Named<TextBlock>(a, "CountText"));
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void Restoring_into_a_deleted_category_says_where_it_went()
    {
        var gone = Completed("x", "y", "z");
        var keep = MakeBoard(("Home", new string[0]));
        Store.Data.Boards.Remove(gone);
        var a = OpenArchive();
        List(a).SelectedIndex = 0;
        Click(ButtonWith(a, "Restore"));
        Assert.Equal("\"Work\" no longer exists, so the item went to \"Home\".", Status(a).Text);

        List(a).SelectAll(); // the other two
        Click(ButtonWith(a, "Restore"));
        Assert.Contains("items went to \"Home\" because their category no longer exists.", Status(a).Text);
        Assert.Equal(3, keep.Categories[0].Items.Count);
    }

    [AvaloniaFact]
    public void Restoring_with_no_category_anywhere_explains_it()
    {
        var board = Completed("x");
        Store.Data.Boards.Remove(board);
        var a = OpenArchive();
        Click(ButtonWith(a, "Restore"));
        Assert.Equal("There is no category to restore into. Add a category to a widget first.", Status(a).Text);

        List(a).SelectedItems!.Clear();
        Click(ButtonWith(a, "Restore")); // nothing selected: nothing happens
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
        Completed("a", "b", "c");
        var a = OpenArchive();
        List(a).SelectedIndex = 2;
        Press(List(a), Key.Delete);
        Assert.Equal(2, Store.Data.Archive.Count);
        Assert.Equal(1, List(a).SelectedIndex); // the next row (now the last one)
        Assert.Equal("Deleted 1 item permanently. Ctrl+Z undoes.", A11y.LastAnnouncement);

        List(a).SelectAll();
        Click(ButtonWith(a, "Delete permanently"));
        Assert.Empty(Store.Data.Archive);
        Assert.Same(Search(a), Focused(a));
        Click(ButtonWith(a, "Delete permanently")); // nothing selected: nothing happens

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
    public void Follows_the_theme_and_the_text_size()
    {
        A11y.TextScaleSource = () => 1.5;
        A11y.RefreshTextScale();
        var a = OpenArchive();
        Assert.Equal(480 * 1.5, a.MinWidth);
        Assert.IsType<ScaleTransform>(Named<LayoutTransformControl>(a, "Scaler").LayoutTransform);

        App.SetTheme("Light"); // the title bar is restyled through ThemeChanged
        Assert.Equal("Light", Store.Data.Settings.Theme);
        A11y.TextScaleSource = () => 1.0;
        App.RefreshAllWindows();
        Assert.Null(Named<LayoutTransformControl>(a, "Scaler").LayoutTransform);
        a.Close();
        Pump();
        App.SetTheme("Dark"); // a closed archive no longer listens
    }

    [AvaloniaFact]
    public void Other_keys_in_the_list_and_undo_with_nothing_to_undo_change_nothing()
    {
        Completed("a");
        var a = OpenArchive();
        Press(List(a), Key.A);
        Press(a, Key.Z, KeyModifiers.Control); // nothing to undo: just says so
        Assert.Equal("Nothing to undo", A11y.LastAnnouncement);
        Assert.Equal(new[] { "a" }, Store.Data.Archive.Select(x => x.Text));
        Assert.NotNull(App.Archive);
    }
}
