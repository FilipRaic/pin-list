using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The timeline of completing an item: crossed out, sliding away, archived.</summary>
public class CompleteFlowTests : StoreTestBase
{
    readonly TimeSpan _leaveAt = CompleteFlow.LeaveAt, _total = CompleteFlow.Total;
    readonly List<TodoItem> _archived = new();
    readonly CompleteFlow _flow;

    public CompleteFlowTests() => _flow = new CompleteFlow(item => { _archived.Add(item); Actions.Complete(item); });

    public override void Dispose()
    {
        CompleteFlow.LeaveAt = _leaveAt;
        CompleteFlow.Total = _total;
        base.Dispose();
    }

    /// <summary>The two timers a Toggle created: the one for the slide-out and the one for the archive.</summary>
    static (ManualTimer Leave, ManualTimer End) LastTimers() => (ManualTimer.Created[^2], ManualTimer.Created[^1]);

    [Fact]
    public void Start_then_leave_then_archive()
    {
        var item = MakeBoard(("A", new[] { "a", "b" })).Categories[0].Items[0];

        Assert.Equal(CompleteResult.Started, _flow.Toggle(item));
        Assert.True(item.IsCompleting);
        Assert.False(item.IsLeaving);
        var (leave, end) = LastTimers();
        Assert.Equal(CompleteFlow.LeaveAt, leave.Interval);
        Assert.Equal(CompleteFlow.Total, end.Interval);

        leave.Fire();
        Assert.True(item.IsLeaving);
        Assert.Empty(_archived);

        end.Fire();
        Assert.Equal(new[] { item }, _archived);
        Assert.False(item.IsCompleting);
        Assert.False(item.IsLeaving);
        Assert.Equal("a", Store.Data.Archive[0].Text);
        Assert.False(leave.IsEnabled || end.IsEnabled);
    }

    [Fact]
    public void Second_toggle_before_leaving_cancels()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        var item = cat.Items[0];
        _flow.Toggle(item);
        var (leave, end) = LastTimers();

        Assert.Equal(CompleteResult.Cancelled, _flow.Toggle(item));
        Assert.False(item.IsCompleting);
        Assert.False(leave.IsEnabled || end.IsEnabled);
        leave.Fire(); // a tick that was already on its way does nothing
        Assert.False(item.IsLeaving);
        end.Fire();
        Assert.Empty(_archived);
        Assert.Contains(item, cat.Items);
    }

    [Fact]
    public void Toggle_while_leaving_is_ignored()
    {
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        _flow.Toggle(item);
        LastTimers().Leave.Fire();

        Assert.Equal(CompleteResult.Ignored, _flow.Toggle(item));
        Assert.True(item.IsCompleting);
        Assert.True(item.IsLeaving);
        LastTimers().End.Fire();
        Assert.Single(_archived);
    }

    [Fact]
    public void Several_items_complete_independently()
    {
        var items = MakeBoard(("A", new[] { "a", "b", "c" })).Categories[0].Items.ToList();
        _flow.Toggle(items[0]);
        var first = LastTimers();
        _flow.Toggle(items[1]);
        var second = LastTimers();
        _flow.Toggle(items[2]);
        var third = LastTimers();

        second.End.Fire();
        Assert.Equal(new[] { items[1] }, _archived);
        Assert.True(items[0].IsCompleting && items[2].IsCompleting);
        _flow.Toggle(items[2]); // cancelling one does not touch the others
        first.End.Fire();
        third.End.Fire();
        Assert.Equal(new[] { items[1], items[0] }, _archived);
    }

    [Fact]
    public void Zero_total_archives_at_once()
    {
        CompleteFlow.Total = TimeSpan.Zero;
        var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
        ManualTimer.Created.Clear();

        Assert.Equal(CompleteResult.Started, _flow.Toggle(item));
        Assert.Equal(new[] { item }, _archived);
        Assert.Empty(ManualTimer.Created);
        Assert.False(item.IsCompleting);
    }

    [Fact]
    public void Item_removed_while_completing_is_not_archived()
    {
        var cat = MakeBoard(("A", new[] { "a", "b" })).Categories[0];
        var item = cat.Items[0];
        _flow.Toggle(item);
        var (leave, end) = LastTimers();
        leave.Fire();

        cat.Items.Remove(item); // sync deleted it
        end.Fire();

        Assert.Empty(_archived);
        Assert.Empty(Store.Data.Archive);
        Assert.False(item.IsCompleting);
        Assert.False(item.IsLeaving);
    }

    [Fact]
    public void An_item_that_was_put_back_by_undo_is_not_leaving()
    {
        var cat = MakeBoard(("A", new[] { "a" })).Categories[0];
        var item = cat.Items[0];
        item.IsLeaving = true;
        item.IsCompleting = true;
        Actions.Complete(item);
        Assert.False(item.IsLeaving);
        Assert.True(Actions.Undo(out _));
        Assert.Contains(cat.Items, i => i.Text == "a" && !i.IsLeaving && !i.IsCompleting);
    }
}
