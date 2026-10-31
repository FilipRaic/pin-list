using PinList;
using Xunit;

namespace PinList.Tests;

public class DateTextTests
{
    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("09", 9, 0)]
    [InlineData("930", 9, 30)]
    [InlineData("0930", 9, 30)]
    [InlineData("9:30", 9, 30)]
    [InlineData("9.30", 9, 30)]
    [InlineData(" 21:05 ", 21, 5)]
    [InlineData("0:00", 0, 0)]
    public void Parses_times(string text, int h, int m)
    {
        Assert.True(DateText.TryParseTime(text, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24")]
    [InlineData("9:60")]
    [InlineData("9:5")]
    [InlineData("12345")]
    [InlineData("noon")]
    [InlineData("1:2:3")]
    public void Rejects_invalid_times(string text) => Assert.False(DateText.TryParseTime(text, out _));

    [Fact]
    public void Day_names_relative_to_today()
    {
        var today = new DateTime(2026, 10, 20);
        Assert.Equal("Today", DateText.Day(today, today));
        Assert.Equal("Tomorrow", DateText.Day(today.AddDays(1), today));
        Assert.Equal("Yesterday", DateText.Day(today.AddDays(-1), today));
        Assert.Contains("2027", DateText.Day(new DateTime(2027, 1, 5), today));
        Assert.DoesNotContain("2026", DateText.Day(new DateTime(2026, 12, 5), today));
    }
}

public class RemindersTests
{
    [Fact]
    public void Takes_due_reminders_once()
    {
        var now = new DateTime(2030, 1, 1, 12, 0, 0);
        var due = new TodoItem { Text = "due", ReminderAt = now.AddMinutes(-5) };
        var later = new TodoItem { Text = "later", ReminderAt = now.AddMinutes(5) };
        var none = new TodoItem { Text = "none" };
        var items = new[] { later, due, none };

        Assert.Equal(new[] { due }, Reminders.TakeDue(items, now));
        Assert.True(due.ReminderShown);
        Assert.False(due.HasPendingReminder);
        Assert.Empty(Reminders.TakeDue(items, now));
        Assert.Equal(new[] { later }, Reminders.TakeDue(items, now.AddMinutes(10)));
    }

    [Fact]
    public void Describes_one_or_many_reminders()
    {
        var one = Reminders.Describe(new[] { new TodoItem { Text = "Call the bank\nabout the card" } });
        Assert.Equal(("Reminder", "Call the bank …"), one);

        var many = Reminders.Describe(Enumerable.Range(1, 5).Select(i => new TodoItem { Text = "t" + i }).ToList());
        Assert.Equal("5 reminders", many.title);
        Assert.Contains("and 2 more", many.text);
    }
}

public class DueHintTests
{
    [Fact]
    public void Due_hint_says_overdue_today_or_due_date()
    {
        Assert.Equal("Overdue", new TodoItem { DueDate = DateTime.Today.AddDays(-1) }.DueHint);
        Assert.Equal("Due today", new TodoItem { DueDate = DateTime.Today }.DueHint);
        Assert.Equal("Due date", new TodoItem { DueDate = DateTime.Today.AddDays(3) }.DueHint);
    }

    [Fact]
    public void Due_hint_is_refreshed_with_the_due_date()
    {
        var item = new TodoItem { DueDate = DateTime.Today };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        item.RefreshDue();
        Assert.Contains(nameof(TodoItem.DueHint), changed);
    }
}

public class DataFolderTests
{
    [Fact]
    public void The_data_folder_is_where_each_system_keeps_app_data()
    {
        var expected = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PinList")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinList");
        Assert.Equal(expected, Store.DataFolderFor(OperatingSystem.IsMacOS()));
        Assert.EndsWith(Path.Combine("Library", "Application Support", "PinList"), Store.DataFolderFor(macOS: true));
    }
}

public class MoveBoardTests : StoreTestBase
{
    [Fact]
    public void Boards_can_be_reordered_and_the_move_undone()
    {
        var a = MakeBoard(); a.Title = "A";
        var b = MakeBoard(); b.Title = "B";
        var c = MakeBoard(); c.Title = "C";

        Actions.MoveBoard(a, 2);
        Assert.Equal(new[] { "B", "C", "A" }, Store.Data.Boards.Select(x => x.Title));
        Assert.True(Saver.Requests > 0);

        Assert.True(Actions.Undo(out var what));
        Assert.Equal("moving the window “A”", what);
        Assert.Equal(new[] { "A", "B", "C" }, Store.Data.Boards.Select(x => x.Title));

        Actions.MoveBoard(a, 0); // already there: nothing to undo
        Assert.False(Actions.CanUndo);
        Actions.MoveBoard(new Board(), 0); // not in the data: nothing happens
        Actions.MoveBoard(c, 99); // past the end: to the end
        Assert.Same(c, Store.Data.Boards[^1]);
    }
}
