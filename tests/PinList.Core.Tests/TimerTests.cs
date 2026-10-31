using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>Saving and reminders run on timers the app provides.</summary>
public class TimerTests : StoreTestBase
{
    [Fact]
    public void A_burst_of_changes_is_saved_once()
    {
        int saves = 0;
        var s = new DebouncedSaveScheduler(() => saves++);
        s.Schedule(); s.Schedule(); s.Schedule();
        var timer = Assert.Single(ManualTimer.Created);
        Assert.Equal(TimeSpan.FromMilliseconds(300), timer.Interval);
        timer.Fire();
        Assert.Equal(1, saves);
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public void Flush_writes_a_pending_save()
    {
        Store.Saver = new DebouncedSaveScheduler(Store.SaveNow);
        MakeBoard(("A", new[] { "a" }));
        Store.MarkDirty();
        Store.Flush();
        Assert.True(File.Exists(Path.Combine(Dir, "data.json")));
        Assert.False(Store.Saver.CancelPending()); // nothing left pending
    }

    [Fact]
    public void Timer_tick_refreshes_after_midnight_and_fires_due_reminders()
    {
        var now = new DateTime(2030, 1, 1, 23, 59, 0);
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        item.DueDate = new DateTime(2030, 1, 2);
        item.ReminderAt = new DateTime(2030, 1, 2, 0, 0, 30);
        var r = new Reminders { Clock = () => now };
        IReadOnlyList<TodoItem>? fired = null;
        r.Due += d => fired = d;
        r.Start();
        Assert.Equal(TimeSpan.FromSeconds(20), ManualTimer.Created.Last().Interval);
        Assert.Null(fired);

        // IsDueToday compares with the real DateTime.Today, so check that the midnight refresh was announced
        int refreshes = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TodoItem.IsDueToday)) refreshes++; };
        now = new DateTime(2030, 1, 2, 0, 1, 0);
        ManualTimer.Created.Last().Fire();
        Assert.Equal(1, refreshes);
        Assert.Equal(new[] { item }, fired);
        Assert.True(item.ReminderShown);
    }
}

/// <summary>Small behaviors the UI tests used to cover, now checked where the code lives.</summary>
public class CoreDetailTests : StoreTestBase
{
    [Fact]
    public void Reminders_start_and_stop_their_timer()
    {
        var r = new Reminders();
        Assert.False(r.IsRunning);
        r.Start();
        Assert.True(r.IsRunning);
        r.Interval = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(5), ManualTimer.Created.Last().Interval);
        r.Stop();
        Assert.False(r.IsRunning);
    }

    [Fact]
    public void Due_reminders_come_in_time_order()
    {
        var now = new DateTime(2030, 1, 1, 12, 0, 0);
        var later = new TodoItem { Text = "later", ReminderAt = now.AddMinutes(-1) };
        var earlier = new TodoItem { Text = "earlier", ReminderAt = now.AddMinutes(-5) };
        Assert.Equal(new[] { earlier, later }, Reminders.TakeDue(new[] { later, earlier }, now));
    }

    [Fact]
    public void Category_reads_as_its_spoken_name() =>
        Assert.Equal("Work, 0 items", new Category { Name = "Work" }.ToString());

    [Fact]
    public void Work_area_knows_its_right_and_bottom_edges() =>
        Assert.Equal((110.0, 70.0), (new WorkArea(10, 20, 100, 50).Right, new WorkArea(10, 20, 100, 50).Bottom));
}
