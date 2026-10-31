using System;
using System.Collections.Generic;
using System.Linq;

namespace PinList;

/// <summary>
/// Checks every 20 seconds for reminders that are due, and refreshes "Today" / "Overdue" labels after midnight.
/// Reminders missed while PinList wasn't running are shown right after start.
/// </summary>
public sealed class Reminders
{
    readonly IUiTimer _timer;
    DateTime _today;

    /// <summary>The current time. Tests replace it to travel past midnight or to a reminder.</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    internal bool IsRunning => _timer.IsEnabled;

    internal TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }

    /// <summary>One or more reminders are due (already marked as shown).</summary>
    public event Action<IReadOnlyList<TodoItem>>? Due;

    public Reminders()
    {
        _timer = UiTimers.Create();
        _timer.Interval = TimeSpan.FromSeconds(20);
        _timer.Tick += Check;
        _today = Clock().Date;
    }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    public void Stop() => _timer.Stop();

    public void Check()
    {
        var now = Clock();
        if (now.Date != _today)
        {
            _today = now.Date;
            foreach (var item in Store.AllItems()) item.RefreshDue();
        }

        var due = TakeDue(Store.AllItems(), now);
        if (due.Count == 0) return;
        Store.MarkDirty();
        Due?.Invoke(due);
    }

    /// <summary>Items whose reminder time has come and that weren't shown yet; marks them as shown.</summary>
    public static List<TodoItem> TakeDue(IEnumerable<TodoItem> items, DateTime now)
    {
        var due = items.Where(i => i.ReminderAt is { } at && at <= now && !i.ReminderShown)
                       .OrderBy(i => i.ReminderAt)
                       .ToList();
        foreach (var i in due) i.ReminderShown = true;
        return due;
    }

    /// <summary>Notification title and text for the due items.</summary>
    public static (string title, string text) Describe(IReadOnlyList<TodoItem> due)
    {
        static string FirstLine(string s)
        {
            int i = s.IndexOfAny(new[] { '\r', '\n' });
            return i < 0 ? s : s[..i] + " …";
        }

        if (due.Count == 1) return ("Reminder", FirstLine(due[0].Text));
        var lines = due.Take(3).Select(i => "• " + FirstLine(i.Text)).ToList();
        if (due.Count > 3) lines.Add($"and {due.Count - 3} more");
        return ($"{due.Count} reminders", string.Join("\n", lines));
    }
}
