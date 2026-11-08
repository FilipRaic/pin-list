using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>Widget windows in situations the other widget tests don't reach.</summary>
public class BoardWindowMoreTests : UiTest
{
    static void FocusOn(Control c) => c.Focus(NavigationMethod.Tab);

    [AvaloniaFact]
    public void Enter_in_an_add_box_without_text_adds_nothing()
    {
        var w = OpenNew(("A", new string[0]));
        var cat = w.Board.Categories[0];
        var box = AddBox(w, cat);
        box.Text = null;

        Press(box, Key.Enter);

        Assert.Empty(cat.Items);
        Assert.Equal("", box.Text);
    }

    [AvaloniaFact]
    public void An_item_whose_text_box_was_cleared_to_nothing_is_deleted()
    {
        Dialogs.ChooseAnswer = _ => 1; // "Delete" in the confirmation
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var item = cat.Items[0];
        item.IsEditing = true;
        Pump();

        var box = EditBox(w, item);
        box.Text = null;
        Press(box, Key.Enter);

        Assert.False(item.IsEditing);
        Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void A_category_name_cleared_to_nothing_keeps_the_old_name()
    {
        var w = OpenNew(("Work", new[] { "a" }));
        var cat = w.Board.Categories[0];
        cat.IsEditing = true;
        Pump();

        var box = EditBox(w, cat);
        box.Text = null;
        Press(box, Key.Enter);

        Assert.False(cat.IsEditing);
        Assert.Equal("Work", cat.Name);
        Assert.False(Actions.CanUndo);
    }

    [AvaloniaFact]
    public void Escape_cancels_renaming_a_category()
    {
        var w = OpenNew(("Work", new[] { "a" }));
        var cat = w.Board.Categories[0];
        cat.IsEditing = true;
        Pump();

        EditBox(w, cat).Text = "Home";
        Press(EditBox(w, cat), Key.Escape);

        Assert.False(cat.IsEditing);
        Assert.Equal("Work", cat.Name);
    }

    [AvaloniaFact]
    public void The_undo_pill_follows_undo_changes_from_another_thread()
    {
        CompleteIn(TimeSpan.Zero); // archived at once
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        FocusOn(Row(w, item));
        Press(w, Key.Space);
        var pill = Named<Control>(w, "UndoPill");
        Assert.True(pill.IsVisible);

        Task.Run(Actions.ClearUndo).Wait(); // the history changed off the UI thread
        Pump();

        Assert.False(pill.IsVisible); // its Undo would undo something else now
    }

    [AvaloniaFact]
    public void On_a_Mac_Backspace_deletes_the_highlighted_item_but_not_on_Windows()
    {
        Dialogs.ChooseAnswer = _ => 1; // "Delete" in the confirmation
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];

        FocusOn(Row(w, cat.Items[0]));
        Press(w, Key.Back); // Windows: Backspace isn't the delete key
        Assert.Equal(2, cat.Items.Count);

        bool wasMac = OsText.IsMac;
        OsText.IsMac = true;
        try
        {
            FocusOn(Row(w, cat.Items[0]));
            Press(w, Key.Back);
            Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
        }
        finally { OsText.IsMac = wasMac; }
    }

    [AvaloniaFact]
    public void Complete_in_the_menu_of_an_item_that_is_already_completing_keeps_it_going()
    {
        CompleteIn(TimeSpan.FromSeconds(30)); // stays crossed out while the test runs
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];

        RightClick(Row(w, item));
        ChooseFromMenu(w.LastMenu!, "Complete");
        Assert.True(item.IsCompleting);

        RightClick(Row(w, item));
        ChooseFromMenu(w.LastMenu!, "Complete"); // not a toggle: it doesn't cancel
        Assert.True(item.IsCompleting);
    }

    [AvaloniaFact]
    public void Reopening_PinList_restores_its_minimized_window()
    {
        App.ShowArchive();
        Pump();
        var main = App.MainWindow!;
        main.WindowState = WindowState.Minimized;
        Pump();

        App.OnReopen();
        Pump();

        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.Equal(MainSection.Archive, main.Section); // only brought forward, the section stays
    }

    [AvaloniaFact]
    public void Removing_one_of_two_widgets_from_the_desktop_keeps_the_other_and_no_lists_window_opens()
    {
        var keep = Open(MakeBoard(("A", new string[0])));
        var gone = Open(MakeBoard(("B", new string[0])));

        Actions.SetWidget(gone.Board, false);
        Pump();

        Assert.Equal(new[] { keep }, App.OpenWindows);
        Assert.Null(App.MainWindow);
    }
}
