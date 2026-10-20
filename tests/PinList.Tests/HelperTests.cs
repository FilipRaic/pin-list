using System.Windows;
using PinList;
using Xunit;

namespace PinList.Tests;

public class ScreensTests
{
    // A big monitor on the left and a smaller one on the right, aligned at the top
    static readonly Rect[] Monitors = { new(0, 0, 2560, 1400), new(2560, 0, 1920, 1040) };

    [Fact]
    public void A_widget_on_a_monitor_is_reachable() =>
        Assert.True(Screens.IsReachable(new Rect(100, 100, 320, 480), Monitors));

    [Fact]
    public void A_widget_in_the_corner_no_monitor_covers_is_not_reachable() =>
        // inside the rectangle around both monitors, but below the smaller one
        Assert.False(Screens.IsReachable(new Rect(3000, 1200, 320, 480), Monitors));

    [Fact]
    public void A_widget_mostly_off_screen_needs_60_visible_pixels_of_header()
    {
        Assert.True(Screens.IsReachable(new Rect(-250, 10, 320, 480), Monitors));  // 70 px visible
        Assert.False(Screens.IsReachable(new Rect(-280, 10, 320, 480), Monitors)); // 40 px visible
    }

    [Fact]
    public void A_widget_whose_header_is_below_the_work_area_is_not_reachable() =>
        Assert.False(Screens.IsReachable(new Rect(100, 1395, 320, 480), Monitors));
}

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
