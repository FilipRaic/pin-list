using PinList;
using Xunit;

namespace PinList.Tests;

public class ActionsTests : StoreTestBase
{
    [Theory]
    // The drop index means "insert before this position" in the list as it was before the move
    [InlineData(0, 2, new[] { "b", "a", "c", "d" })]
    [InlineData(0, 4, new[] { "b", "c", "d", "a" })]
    [InlineData(3, 0, new[] { "d", "a", "b", "c" })]
    [InlineData(2, 1, new[] { "a", "c", "b", "d" })]
    [InlineData(1, 1, new[] { "a", "b", "c", "d" })]
    [InlineData(1, 2, new[] { "a", "b", "c", "d" })]
    public void MoveItem_within_a_category(int from, int dropIndex, string[] expected)
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c", "d" })).Categories[0];
        Actions.MoveItem(cat.Items[from], cat, dropIndex);
        Assert.Equal(expected, Texts(cat));
    }

    [Fact]
    public void MoveItem_to_another_category_and_undo()
    {
        var board = MakeBoard(("A", new[] { "a", "b" }), ("B", new[] { "x", "y" }));
        var (a, b) = (board.Categories[0], board.Categories[1]);

        Actions.MoveItem(a.Items[0], b, 1);
        Assert.Equal(new[] { "b" }, Texts(a));
        Assert.Equal(new[] { "x", "a", "y" }, Texts(b));

        Assert.True(Actions.Undo());
        Assert.Equal(new[] { "a", "b" }, Texts(a));
        Assert.Equal(new[] { "x", "y" }, Texts(b));
    }

    [Fact]
    public void MoveCategory_between_boards_and_undo()
    {
        var first = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }));
        var second = MakeBoard(("C", new[] { "c" }));

        Actions.MoveCategory(first.Categories[0], second, 0);
        Assert.Equal(new[] { "A", "C" }, second.Categories.Select(c => c.Name));

        Actions.Undo();
        Assert.Equal(new[] { "A", "B" }, first.Categories.Select(c => c.Name));
        Assert.Equal(new[] { "C" }, second.Categories.Select(c => c.Name));
    }

    [Fact]
    public void Complete_moves_to_archive_and_undo_puts_it_back_in_place()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0];
        var item = cat.Items[1];
        item.DueDate = new DateTime(2030, 1, 2);

        Actions.Complete(item);
        Assert.Equal(new[] { "a", "c" }, Texts(cat));
        var archived = Assert.Single(Store.Data.Archive);
        Assert.Equal("b", archived.Text);
        Assert.Equal(new DateTime(2030, 1, 2), archived.DueDate);

        Actions.Undo();
        Assert.Equal(new[] { "a", "b", "c" }, Texts(cat));
        Assert.Empty(Store.Data.Archive);
    }

    [Fact]
    public void Restore_goes_back_to_its_category_and_can_be_undone()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        Actions.Complete(cat.Items[0]);
        var archived = Store.Data.Archive[0];

        var result = Actions.Restore(new[] { archived });
        Assert.Same(cat, Assert.Single(result).category);
        Assert.Equal(new[] { "b", "a" }, Texts(cat));
        Assert.Empty(Store.Data.Archive);

        Actions.Undo();
        Assert.Equal(new[] { "b" }, Texts(cat));
        Assert.Same(archived, Assert.Single(Store.Data.Archive));
    }

    [Fact]
    public void Restore_falls_back_to_the_first_category_when_its_own_is_gone()
    {
        var board = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }));
        Actions.Complete(board.Categories[1].Items[0]);
        board.Categories.RemoveAt(1);

        var result = Assert.Single(Actions.Restore(Store.Data.Archive.ToList()));
        Assert.Equal("A", result.category.Name);
        Assert.NotEqual(result.archived.CategoryId, result.category.Id);
    }

    [Fact]
    public void Restoring_several_items_keeps_archive_order_on_undo()
    {
        var cat = MakeBoard(("A", new[] { "a", "b", "c", "d" })).Categories[0];
        foreach (var i in cat.Items.ToList()) Actions.Complete(i);
        var before = Store.Data.Archive.Select(a => a.Text).ToArray(); // d, c, b, a

        Actions.Restore(new[] { Store.Data.Archive[0], Store.Data.Archive[2] });
        Actions.Undo();

        Assert.Equal(before, Store.Data.Archive.Select(a => a.Text).ToArray());
        Assert.Empty(cat.Items);
    }

    [Fact]
    public void Edits_and_renames_can_be_undone()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        var cat = board.Categories[0];
        var item = cat.Items[0];
        var color = cat.Color;

        Actions.SetItemText(item, "changed");
        Actions.RenameCategory(cat, "Renamed");
        Actions.RenameBoard(board, "");
        Actions.SetCategoryColor(cat, "#000000");

        Actions.Undo(); Assert.Equal(color, cat.Color);
        Actions.Undo(); Assert.Equal("Test", board.Title);
        Actions.Undo(); Assert.Equal("A", cat.Name);
        Actions.Undo(); Assert.Equal("a", item.Text);
        Assert.False(Actions.Undo());
    }

    [Fact]
    public void Due_date_and_reminder_can_be_undone()
    {
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];

        Actions.SetDueDate(item, new DateTime(2030, 5, 6, 15, 0, 0));
        Assert.Equal(new DateTime(2030, 5, 6), item.DueDate); // the time part is dropped
        Actions.SetReminder(item, new DateTime(2030, 5, 6, 9, 30, 0));
        Assert.True(item.HasPendingReminder);

        Actions.Undo();
        Assert.Null(item.ReminderAt);
        Actions.Undo();
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void Delete_category_and_undo()
    {
        var board = MakeBoard(("A", new[] { "a" }), ("B", new[] { "b" }), ("C", new string[0]));
        Actions.DeleteCategory(board.Categories[1]);
        Assert.Equal(new[] { "A", "C" }, board.Categories.Select(c => c.Name));

        Actions.Undo();
        Assert.Equal(new[] { "A", "B", "C" }, board.Categories.Select(c => c.Name));
        Assert.Equal(new[] { "b" }, Texts(board.Categories[1]));
    }

    [Fact]
    public void Undo_history_is_limited_to_50_steps()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        for (int i = 0; i < 60; i++) Actions.SetItemText(cat.Items[0], "t" + i);

        int undone = 0;
        while (Actions.Undo()) undone++;
        Assert.Equal(50, undone);
        Assert.Equal("t9", cat.Items[0].Text);
    }

    [Fact]
    public void Every_change_requests_a_save()
    {
        var board = MakeBoard(("A", new string[0]));
        Actions.AddItem(board.Categories[0], "x");
        Actions.AddCategory(board);
        Assert.Equal(2, Saver.Requests);
    }

    [Fact]
    public void Unknown_fields_survive_completing_and_restoring()
    {
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        item.Extra = new() { ["priority"] = System.Text.Json.JsonDocument.Parse("3").RootElement };

        Actions.Complete(item);
        var archived = Store.Data.Archive[0];
        Assert.Equal(3, archived.Extra!["priority"].GetInt32());

        Actions.Restore(new[] { archived });
        var back = Store.Data.Boards[0].Categories[0].Items.Single();
        Assert.Equal(3, back.Extra!["priority"].GetInt32());
    }
}
