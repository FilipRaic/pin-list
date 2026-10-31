using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The less common paths of Actions: archive deletion, categories, widgets, and undo when things are gone.</summary>
public class MoreActionsTests : StoreTestBase
{
    [Fact]
    public void Deleting_archived_items_and_undo_restores_their_places()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0];
        foreach (var i in cat.Items.ToList()) Actions.Complete(i); // archive: c, b, a
        var archive = Store.Data.Archive;

        Actions.DeleteArchived(new[] { archive[2], archive[0] });
        Assert.Equal(new[] { "b" }, archive.Select(a => a.Text));

        Assert.True(Actions.Undo(out var what));
        Assert.Equal("deleting 2 archived items", what);
        Assert.Equal(new[] { "c", "b", "a" }, archive.Select(a => a.Text));

        Actions.DeleteArchived(new[] { archive[1] });
        Actions.Undo(out what);
        Assert.Equal("deleting 1 archived item", what);
    }

    [Fact]
    public void Deleting_archived_items_that_are_not_there_does_nothing()
    {
        Actions.DeleteArchived(new[] { new ArchivedItem { Text = "stranger" } });
        Assert.False(Actions.CanUndo);
        Assert.Equal(0, Saver.Requests);
    }

    [Theory]
    [InlineData(0, 2, new[] { "B", "A", "C" })]
    [InlineData(2, 0, new[] { "C", "A", "B" })]
    [InlineData(1, 1, new[] { "A", "B", "C" })]
    [InlineData(1, 2, new[] { "A", "B", "C" })]
    [InlineData(0, 3, new[] { "B", "C", "A" })]
    public void MoveCategory_within_a_board(int from, int dropIndex, string[] expected)
    {
        var board = MakeBoard(("A", new string[0]), ("B", new string[0]), ("C", new string[0]));
        Actions.MoveCategory(board.Categories[from], board, dropIndex);
        Assert.Equal(expected, board.Categories.Select(c => c.Name));
    }

    [Fact]
    public void Things_that_are_no_longer_in_the_data_are_ignored()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        var strayItem = new TodoItem { Text = "stray" };
        var strayCat = new Category { Name = "stray" };

        Actions.Complete(strayItem);
        Actions.Delete(strayItem);
        Actions.MoveItem(strayItem, board.Categories[0], 0);
        Actions.DeleteCategory(strayCat);
        Actions.MoveCategory(strayCat, board, 0);

        Assert.False(Actions.CanUndo);
        Assert.Empty(Store.Data.Archive);
        Assert.Equal(new[] { "a" }, Texts(board.Categories[0]));
    }

    [Fact]
    public void Unchanged_values_are_not_recorded()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        var cat = board.Categories[0];
        var item = cat.Items[0];

        Actions.SetItemText(item, "a");
        Actions.RenameCategory(cat, "A");
        Actions.SetCategoryColor(cat, cat.Color);
        Actions.RenameBoard(board, board.Title);
        Actions.SetDueDate(item, null);

        Assert.False(Actions.CanUndo);
        Assert.Equal(0, Saver.Requests);
    }

    [Fact]
    public void Undo_of_adding_removes_the_item_and_category_again()
    {
        var board = MakeBoard(("A", new string[0]));
        var item = Actions.AddItem(board.Categories[0], "new");
        item.IsActive = true;
        var cat = Actions.AddCategory(board);
        cat.IsActive = true;

        Actions.Undo(out var what);
        Assert.Equal("adding a category", what);
        Assert.Single(board.Categories);
        Assert.False(cat.IsActive);

        Actions.Undo(out what);
        Assert.Equal("adding “new”", what);
        Assert.Empty(board.Categories[0].Items);
        Assert.False(item.IsActive);
    }

    [Fact]
    public void New_categories_get_an_unused_color_and_cycle_when_all_are_used()
    {
        var board = MakeBoard();
        var colors = Enumerable.Range(0, Actions.Palette.Length + 1).Select(_ => Actions.AddCategory(board).Color).ToList();
        Assert.Equal(Actions.Palette, colors.Take(Actions.Palette.Length));
        Assert.Equal(Actions.Palette[Actions.Palette.Length % Actions.Palette.Length], colors[^1]);
        Assert.All(board.Categories, c => Assert.Equal("New category", c.Name));
    }

    [Fact]
    public void Undo_puts_an_item_into_the_first_category_when_its_own_is_gone()
    {
        var board = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }));
        var b = board.Categories[1];
        Actions.Delete(b.Items[0]);
        board.Categories.Remove(b); // gone without undo history

        Actions.Undo();
        Assert.Equal(new[] { "b", "a" }, Texts(board.Categories[0])); // at its old position (the top)
    }

    [Fact]
    public void Undo_does_nothing_when_there_is_no_category_left()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        var item = board.Categories[0].Items[0];
        Actions.Delete(item);
        board.Categories.Clear();

        Assert.True(Actions.Undo());
        Assert.Null(Store.Locate(item));
    }

    [Fact]
    public void Undo_puts_a_category_on_the_first_board_when_its_own_is_gone()
    {
        var first = MakeBoard(("A", new string[0]));
        var second = MakeBoard(("B", new string[0]));
        var b = second.Categories[0];
        Actions.DeleteCategory(b);
        Store.Data.Boards.Remove(second);

        Actions.Undo();
        Assert.Same(first, Store.BoardOf(b));
    }

    [Fact]
    public void Undo_of_a_category_does_nothing_without_boards()
    {
        var board = MakeBoard(("A", new string[0]));
        var cat = board.Categories[0];
        Actions.DeleteCategory(cat);
        Store.Data.Boards.Clear();

        Assert.True(Actions.Undo());
        Assert.Null(Store.BoardOf(cat));
    }

    [Fact]
    public void Restore_skips_items_that_left_the_archive_and_needs_a_category()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        Actions.Complete(cat.Items[0]);
        var archived = Store.Data.Archive[0];

        Assert.Empty(Actions.Restore(new[] { new ArchivedItem { Text = "not in the archive" } }));

        Store.Data.Boards.Clear();
        Assert.Empty(Actions.Restore(new[] { archived }));
        Assert.Single(Store.Data.Archive);
    }

    [Fact]
    public void Reminder_can_be_set_and_cleared()
    {
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        item.ReminderShown = true;

        Actions.SetReminder(item, new DateTime(2031, 1, 1, 8, 0, 0));
        Assert.False(item.ReminderShown);
        Assert.True(item.HasPendingReminder);

        Actions.Undo();
        Assert.Null(item.ReminderAt);
        Assert.True(item.ReminderShown);
    }

    [Fact]
    public void New_board_is_centered_cascades_and_inherits_the_look()
    {
        var added = new List<Board>();
        Actions.BoardAdded += added.Add;
        try
        {
            var area = new WorkArea(100, 50, 1000, 800);
            var template = new Board { Style = "Yellow", Transparent = false };

            var first = Actions.NewBoard(area, template);
            var second = Actions.NewBoard(area);

            Assert.Equal(new[] { first, second }, added);
            Assert.Equal((100 + 500 - 160, 50 + 400 - 200), (first.X, first.Y));
            Assert.Equal((first.X + 30, first.Y + 30), (second.X, second.Y));
            Assert.Equal(("Yellow", false), (first.Style, first.Transparent));
            Assert.Equal(("Dark", true), (second.Style, second.Transparent));
            Assert.Equal("General", Assert.Single(first.Categories).Name);
            Assert.Equal(2, Store.Data.Boards.Count);
        }
        finally { Actions.BoardAdded -= added.Add; }
    }

    [Fact]
    public void Deleting_a_board_removes_it()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        Actions.DeleteBoard(board);
        Assert.Empty(Store.Data.Boards);
        Assert.Equal(1, Saver.Requests);
    }
}
