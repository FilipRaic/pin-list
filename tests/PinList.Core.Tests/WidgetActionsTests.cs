using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>What an Android home screen widget can change, by id (WidgetActions).</summary>
public class WidgetActionsTests : StoreTestBase
{
    [Fact]
    public void Complete_moves_the_item_to_the_archive()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        var a = cat.Items[0];

        Assert.True(WidgetActions.Complete(a.Id));

        Assert.Equal(new[] { "b" }, Texts(cat));
        Assert.Equal("a", Assert.Single(Store.Data.Archive).Text);
    }

    [Fact]
    public void Complete_of_an_item_that_is_gone_does_nothing()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        int saves = Saver.Requests;

        Assert.False(WidgetActions.Complete("gone"));

        Assert.Equal(new[] { "a" }, Texts(cat));
        Assert.Empty(Store.Data.Archive);
        Assert.Equal(saves, Saver.Requests);
    }

    [Fact]
    public void Undo_right_after_puts_the_item_back_in_its_place()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0];
        string id = cat.Items[1].Id;
        WidgetActions.Complete(id);

        Assert.True(WidgetActions.UndoComplete(id));

        Assert.Equal(new[] { "a", "b", "c" }, Texts(cat));
        Assert.Equal(id, cat.Items[1].Id);
        Assert.Empty(Store.Data.Archive);
    }

    [Fact]
    public void Undo_after_another_change_restores_only_that_item()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        string id = cat.Items[0].Id;
        WidgetActions.Complete(id);
        Actions.AddItem(cat, "x");

        Assert.True(WidgetActions.UndoComplete(id));

        Assert.Equal(new[] { "b", "x", "a" }, Texts(cat)); // "x" stays, "a" comes back at the end
        Assert.Empty(Store.Data.Archive);
    }

    [Fact]
    public void Undo_after_a_restart_restores_from_the_archive()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        string id = cat.Items[0].Id;
        WidgetActions.Complete(id);
        Actions.ClearUndo(); // a new process has no undo history

        Assert.True(WidgetActions.UndoComplete(id));

        Assert.Equal(new[] { "b", "a" }, Texts(cat));
        Assert.Empty(Store.Data.Archive);
    }

    [Fact]
    public void Undo_twice_does_nothing_the_second_time()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        string id = cat.Items[0].Id;
        WidgetActions.Complete(id);
        Assert.True(WidgetActions.UndoComplete(id));

        Assert.False(WidgetActions.UndoComplete(id));

        Assert.Equal(new[] { "a" }, Texts(cat));
    }

    [Fact]
    public void Undo_of_an_unknown_id_does_nothing()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        WidgetActions.Complete(cat.Items[0].Id);

        Assert.False(WidgetActions.UndoComplete("gone"));

        Assert.Equal(new[] { "b" }, Texts(cat)); // the other completion is not undone
        Assert.Single(Store.Data.Archive);
    }

    /// <summary>The widget's Undo must never undo a newer completion that happens to be on top of the history.</summary>
    [Fact]
    public void Undo_of_an_older_completion_leaves_the_newer_one_alone()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0];
        string a = cat.Items[0].Id, b = cat.Items[1].Id;
        WidgetActions.Complete(a);
        WidgetActions.Complete(b);

        Assert.True(WidgetActions.UndoComplete(a));

        Assert.Equal(new[] { "c", "a" }, Texts(cat));
        Assert.Equal("b", Assert.Single(Store.Data.Archive).Text);
    }

    [Fact]
    public void Toggle_collapsed_flips_and_saves()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        int saves = Saver.Requests;

        Assert.True(WidgetActions.ToggleCollapsed(cat.Id));
        Assert.True(cat.Collapsed);
        Assert.True(Saver.Requests > saves);

        Assert.True(WidgetActions.ToggleCollapsed(cat.Id));
        Assert.False(cat.Collapsed);
    }

    [Fact]
    public void Toggle_of_a_category_that_is_gone_does_nothing()
    {
        MakeBoard(("A", new[] { "a" }));
        int saves = Saver.Requests;

        Assert.False(WidgetActions.ToggleCollapsed("gone"));
        Assert.Equal(saves, Saver.Requests);
    }

    [Fact]
    public void Add_puts_the_item_at_the_end_of_the_category()
    {
        var board = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }));

        var added = WidgetActions.AddItem(board.Id, board.Categories[1].Id, "  new  ");

        Assert.NotNull(added);
        Assert.Equal(new[] { "b", "new" }, Texts(board.Categories[1]));
        Assert.Equal(new[] { "a" }, Texts(board.Categories[0]));
    }

    [Fact]
    public void Add_falls_back_to_the_first_category()
    {
        var board = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }));
        var other = MakeBoard(("Other", new string[0]));

        WidgetActions.AddItem(board.Id, null, "one");
        WidgetActions.AddItem(board.Id, "gone", "two");
        WidgetActions.AddItem(board.Id, other.Categories[0].Id, "three"); // a category of another list doesn't count

        Assert.Equal(new[] { "a", "one", "two", "three" }, Texts(board.Categories[0]));
        Assert.Empty(other.Categories[0].Items);
    }

    [Fact]
    public void Add_to_a_deleted_list_or_a_list_without_categories_does_nothing()
    {
        var empty = MakeBoard();
        int saves = Saver.Requests;

        Assert.Null(WidgetActions.AddItem("gone", null, "x"));
        Assert.Null(WidgetActions.AddItem(empty.Id, null, "x"));

        Assert.Empty(empty.Categories);
        Assert.Equal(saves, Saver.Requests);
    }

    [Fact]
    public void Add_ignores_empty_text()
    {
        var board = MakeBoard(("A", new[] { "a" }));

        Assert.Null(WidgetActions.AddItem(board.Id, null, ""));
        Assert.Null(WidgetActions.AddItem(board.Id, null, "   "));

        Assert.Equal(new[] { "a" }, Texts(board.Categories[0]));
    }
}
