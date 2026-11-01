using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>Permanent deletes ask first (spec 4.5); Ctrl+Z still brings things back.</summary>
public class DeleteConfirmTests : UiTest
{
    static void FocusOn(AccessibleRow row)
    {
        row.Focus(NavigationMethod.Tab);
        Pump();
    }

    [AvaloniaFact]
    public void Delete_item_asks_and_cancel_keeps_it()
    {
        var w = OpenNew(("A", new[] { "Send the invoice\nto Anna", "b" }));
        var cat = w.Board.Categories[0];
        FocusOn(Row(w, cat.Items[0]));
        Press(w, Key.Delete); // Esc
        Assert.Contains("Delete “Send the invoice”?: Ctrl+Z brings it back.", Dialogs.Shown);
        Assert.Equal(2, cat.Items.Count);

        Dialogs.ChooseAnswer = _ => 1;
        Press(w, Key.Delete);
        Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Delete_category_asks_with_item_count()
    {
        var w = OpenNew(("Work", new[] { "a", "b" }), ("Empty", new string[0]), ("One", new[] { "x" }));
        var (work, empty, one) = (w.Board.Categories[0], w.Board.Categories[1], w.Board.Categories[2]);
        FocusOn(Row(w, work));
        Press(w, Key.Delete);
        Assert.Contains("Delete category “Work”?: Its 2 items are deleted too. Ctrl+Z brings them back.", Dialogs.Shown);
        Assert.Equal(3, w.Board.Categories.Count);

        Dialogs.ChooseAnswer = _ => 1;
        FocusOn(Row(w, one));
        Press(w, Key.Delete);
        Assert.Contains("Delete category “One”?: Its 1 item is deleted too. Ctrl+Z brings it back.", Dialogs.Shown);
        FocusOn(Row(w, empty));
        Press(w, Key.Delete);
        Assert.Contains("Delete category “Empty”?: Ctrl+Z brings it back.", Dialogs.Shown);
        Assert.Equal(new[] { work }, w.Board.Categories);
    }

    [AvaloniaFact]
    public void Delete_from_the_menu_asks_too()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        RightClick(Row(w, item));
        ChooseFromMenu(w.LastMenu!, "Delete");
        Assert.Contains("Delete “a”?: Ctrl+Z brings it back.", Dialogs.Shown);
        Assert.Single(w.Board.Categories[0].Items);
    }

    [AvaloniaFact]
    public void Delete_archived_asks_with_count()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0];
        foreach (var i in cat.Items.ToList()) Actions.Complete(i);
        App.ShowArchive();
        var a = App.Archive!;
        Pump();
        var list = Find<ListBox>(a)!;
        list.SelectAll();
        Press(list, Key.Delete);
        Assert.Contains("Delete 3 items permanently?: Ctrl+Z brings them back while PinList is running.", Dialogs.Shown);
        Assert.Equal(3, Store.Data.Archive.Count);

        Dialogs.ChooseAnswer = _ => 1;
        list.SelectedIndex = 0;
        Press(list, Key.Delete);
        Assert.Contains("Delete 1 item permanently?: Ctrl+Z brings it back while PinList is running.", Dialogs.Shown);
        Assert.Equal(2, Store.Data.Archive.Count);
    }

    [AvaloniaFact]
    public void Clearing_text_asks_and_cancel_restores()
    {
        var w = OpenNew(("A", new[] { "keep me" }));
        var item = w.Board.Categories[0].Items[0];
        item.IsEditing = true;
        Pump();
        EditBox(w, item).Text = "  ";
        Press(EditBox(w, item), Key.Enter); // Esc in the question
        Assert.Contains("Delete “keep me”?: Its text is empty. Ctrl+Z brings it back.", Dialogs.Shown);
        Assert.Equal("keep me", item.Text);
        Assert.False(item.IsEditing);
        Assert.Single(w.Board.Categories[0].Items);

        Dialogs.ChooseAnswer = _ => 1;
        item.IsEditing = true;
        Pump();
        EditBox(w, item).Text = "";
        Press(EditBox(w, item), Key.Enter);
        Assert.Empty(w.Board.Categories[0].Items);
    }

    [AvaloniaFact]
    public void Delete_synced_window_mentions_other_devices()
    {
        var w = OpenNew(("A", new[] { "a" }));
        w.Board.Title = "Groceries";
        w.Board.Synced = true;
        Click(Named<Button>(w, "MenuBtn"));
        ChooseFromMenu(w.LastMenu!, "Delete window");
        Assert.Contains(Dialogs.Shown, d => d.StartsWith("Delete \"Groceries\"?: ") && d.EndsWith(" It will also be deleted from your other devices."));
    }

    [AvaloniaFact]
    public void Undo_restores_a_confirmed_delete()
    {
        var w = OpenNew(("A", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        Dialogs.ChooseAnswer = _ => 1;
        FocusOn(Row(w, cat.Items[0]));
        Press(w, Key.Delete);
        Assert.Equal(new[] { "b" }, cat.Items.Select(i => i.Text));
        Press(w, Key.Z, KeyModifiers.Control);
        Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text));
    }

    [AvaloniaFact]
    public void Completing_does_not_ask()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var item = w.Board.Categories[0].Items[0];
        FocusOn(Row(w, item));
        Press(w, Key.Space);
        UiHost.WaitUntil(() => Store.Data.Archive.Count == 1);
        Assert.Empty(Dialogs.Shown);
    }
}
