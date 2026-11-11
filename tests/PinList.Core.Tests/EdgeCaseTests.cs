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

    [Fact]
    public void Showing_a_list_as_a_widget_again_can_be_undone()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        board.Widget = false;

        Actions.SetWidget(board, true);
        Assert.True(board.Widget);
        Assert.True(Actions.Undo(out var what));
        Assert.Equal("showing “Test” as a widget", what);
        Assert.False(board.Widget);
    }

    [Fact]
    public void Hiding_a_widget_says_so_when_undone()
    {
        var board = MakeBoard(("A", new[] { "a" }));

        Actions.SetWidget(board, false);
        Assert.True(Actions.Undo(out var what));
        Assert.Equal("hiding the widget of “Test”", what);
        Assert.True(board.Widget);
    }

    [Fact]
    public void Setting_the_same_due_day_again_changes_nothing()
    {
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        Actions.SetDueDate(item, null); // none before, none now
        Assert.False(Actions.CanUndo);

        Actions.SetDueDate(item, new DateTime(2026, 11, 9, 8, 0, 0));
        Actions.SetDueDate(item, new DateTime(2026, 11, 9, 17, 30, 0)); // the same day at another time
        Assert.True(Actions.Undo());
        Assert.Null(item.DueDate);
        Assert.False(Actions.CanUndo);
    }

    [Fact]
    public void Undoing_the_move_of_a_list_that_was_deleted_since_does_nothing()
    {
        var first = MakeBoard(("A", new[] { "a" }));
        var second = MakeBoard(("B", new[] { "b" }));
        Actions.MoveBoard(second, 0);
        Actions.DeleteBoard(second);

        Assert.True(Actions.Undo());
        Assert.Equal(new[] { first }, Store.Data.Boards);
    }

    [Fact]
    public void Undo_names_only_the_first_line_also_with_Windows_line_ends()
    {
        var item = MakeBoard(("A", new[] { "first\r\nsecond" })).Categories[0].Items[0];
        Actions.Delete(item);
        Assert.True(Actions.Undo(out var what));
        Assert.Equal("deleting “first”", what);
    }

    [Fact]
    public void A_save_that_was_never_scheduled_is_not_pending()
    {
        int saves = 0;
        var saver = new DebouncedSaveScheduler(() => saves++);
        Assert.False(saver.CancelPending());

        saver.Schedule();
        Assert.True(saver.CancelPending());
        Assert.False(saver.CancelPending()); // cancelled already
        Assert.Equal(0, saves);
    }

    [Fact]
    public void Missing_settings_and_archive_load_as_empty()
    {
        var data = Store.Parse("""{ "boards": [], "settings": null, "archive": null }""", out _);
        Assert.NotNull(data.Settings);
        Assert.Equal("System", data.Settings.Theme);
        Assert.Empty(data.Archive);
    }

    [Fact]
    public void Import_keeps_the_synced_list_here_instead_of_its_copy_in_the_file()
    {
        var synced = MakeBoard(("A", new[] { "mine" }));
        synced.Synced = true;
        var imported = new AppData();
        imported.Boards.Add(new Board { Id = synced.Id, Title = "Old copy" });
        imported.Boards.Add(new Board { Title = "Other" });

        Store.ReplaceAll(imported);

        Assert.Same(synced, Store.Data.Boards[0]);
        Assert.Equal(new[] { "Test", "Other" }, Store.Data.Boards.Select(b => b.Title));
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

    [Fact]
    public void Windows_line_ends_read_as_one_line()
    {
        Assert.Equal("first, second", new TodoItem { Text = "first\r\nsecond" }.AccessibleName);
        var archived = new ArchivedItem { Text = "first\r\nsecond", CategoryName = "Work" };
        Assert.StartsWith("first, second, completed ", archived.ToString());
    }

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
