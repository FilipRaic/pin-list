using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>Edge cases of the data and actions: undo when things were removed behind its back, missing values, odd ids.</summary>
public class EdgeCaseTests : StoreTestBase
{
    [Fact]
    public void Undo_of_adding_an_item_whose_widget_is_gone_does_nothing_harmful()
    {
        var board = MakeBoard(("A", new string[0]));
        var item = Actions.AddItem(board.Categories[0], "new");
        item.IsActive = true;
        Store.Data.Boards.Remove(board); // e.g. replaced by an import

        Assert.True(Actions.Undo(out var what));
        Assert.Equal("adding “new”", what);
        Assert.False(item.IsActive);
    }

    [Fact]
    public void Undo_of_adding_a_category_whose_widget_is_gone_does_nothing_harmful()
    {
        var board = MakeBoard();
        var cat = Actions.AddCategory(board);
        cat.IsActive = true;
        Store.Data.Boards.Remove(board);

        Assert.True(Actions.Undo(out _));
        Assert.Single(board.Categories); // the removed widget isn't touched
        Assert.False(cat.IsActive);
    }

    [Fact]
    public void Undo_of_restoring_still_returns_items_to_the_archive_when_their_widget_is_gone()
    {
        var board = MakeBoard(("A", new[] { "a", "b" }));
        foreach (var i in board.Categories[0].Items.ToList()) Actions.Complete(i);
        Actions.ClearUndo();
        Actions.Restore(Store.Data.Archive.ToList());
        Store.Data.Boards.Remove(board);

        Assert.True(Actions.Undo(out var what));
        Assert.Equal("restoring 2 items", what);
        Assert.Equal(new[] { "b", "a" }, Store.Data.Archive.Select(a => a.Text));
    }

    [Fact]
    public void New_widget_without_anyone_listening()
    {
        // the tests' base removes the app's listener, so nobody is told about the new widget
        var b = Actions.NewBoard(new WorkArea(0, 0, 800, 600));
        Assert.Same(b, Assert.Single(Store.Data.Boards));
        Assert.Equal(1, Saver.Requests);
    }

    [Fact]
    public void Archive_entries_without_a_category_stay_without_one_on_import()
    {
        // ids can be empty in hand-edited files; an empty id must not be mapped to a new one
        var data = new AppData();
        var cat = new Category { Id = "", Name = "A" };
        cat.Items.Add(new TodoItem { Id = "", Text = "a" });
        var board = new Board { Id = "" };
        board.Categories.Add(cat);
        data.Boards.Add(board);
        data.Archive.Add(new ArchivedItem { Text = "old", CategoryId = "", BoardId = "" });

        Store.ReassignIds(data);

        Assert.NotEqual("", board.Id);
        Assert.NotEqual("", cat.Id);
        Assert.NotEqual("", cat.Items[0].Id);
        Assert.Equal(("", ""), (data.Archive[0].CategoryId, data.Archive[0].BoardId));
    }
}

public class EdgeCaseModelTests
{
    [Fact]
    public void Setting_items_to_null_gives_an_empty_list_that_still_updates_the_spoken_name()
    {
        var cat = new Category { Name = "Work" };
        cat.Items = null!; // what the JSON reader does for "items": null
        Assert.Empty(cat.Items);

        var changed = new List<string?>();
        cat.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        cat.Items.Add(new TodoItem { Text = "a" });
        Assert.Contains(nameof(Category.AccessibleName), changed);
        Assert.Equal("Work, 1 item", cat.AccessibleName);
    }

    [Fact]
    public void No_time_text_is_not_a_time() => Assert.False(DateText.TryParseTime(null, out _));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Up_to_three_reminders_are_all_listed(int count)
    {
        var (title, text) = Reminders.Describe(Enumerable.Range(1, count).Select(i => new TodoItem { Text = "t" + i }).ToList());
        Assert.Equal($"{count} reminders", title);
        Assert.Equal(count, text.Split('\n').Length);
        Assert.DoesNotContain("more", text);
    }
}
