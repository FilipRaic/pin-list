using System;
using System.Collections.Generic;

namespace PinList;

/// <summary>What <see cref="CompleteFlow.Toggle"/> did.</summary>
public enum CompleteResult
{
    /// <summary>Completing started (or, with no delay, the item was archived at once).</summary>
    Started,
    /// <summary>The item was still completing and the user changed their mind.</summary>
    Cancelled,
    /// <summary>The row is already leaving, it is too late to cancel.</summary>
    Ignored,
}

/// <summary>
/// The timeline of completing an item, the same on the desktop and the phone: the circle fills and the text is
/// crossed out (<see cref="TodoItem.IsCompleting"/>), after <see cref="LeaveAt"/> the row slides away
/// (<see cref="TodoItem.IsLeaving"/>), and after <see cref="Total"/> the item is archived. Cancelling is possible
/// until the row starts to leave. Every item runs on its own timers, so several can complete at the same time.
/// </summary>
public sealed class CompleteFlow
{
    /// <summary>Click until the row starts to leave. Cancelling works until then.</summary>
    public static TimeSpan LeaveAt { get; set; } = TimeSpan.FromMilliseconds(650);

    /// <summary>Click until the item is archived (the exit animation has ended by then). Zero archives at once.</summary>
    public static TimeSpan Total { get; set; } = TimeSpan.FromMilliseconds(930);

    readonly Action<TodoItem> _archive;
    readonly Dictionary<TodoItem, (IUiTimer Leave, IUiTimer End)> _running = new();

    /// <param name="archive">Archives the item (the view also moves focus and announces it).</param>
    public CompleteFlow(Action<TodoItem> archive) => _archive = archive;

    public CompleteResult Toggle(TodoItem item)
    {
        if (item.IsLeaving) return CompleteResult.Ignored;
        if (item.IsCompleting)
        {
            Stop(item);
            item.IsCompleting = false;
            return CompleteResult.Cancelled;
        }

        if (Total <= TimeSpan.Zero)
        {
            _archive(item);
            return CompleteResult.Started;
        }

        item.IsCompleting = true;
        var leave = UiTimers.Create();
        leave.Interval = LeaveAt;
        var end = UiTimers.Create();
        end.Interval = Total;
        _running[item] = (leave, end);
        leave.Tick += () =>
        {
            leave.Stop();
            if (item.IsCompleting) item.IsLeaving = true;
        };
        end.Tick += () => Finish(item);
        leave.Start();
        end.Start();
        return CompleteResult.Started;
    }

    void Finish(TodoItem item)
    {
        if (!_running.ContainsKey(item)) return; // cancelled, a tick that was already on its way does nothing
        Stop(item);
        item.IsLeaving = false;
        item.IsCompleting = false;
        // sync may have removed or moved the item meanwhile: then there is nothing to archive
        if (Store.Locate(item) != null) _archive(item);
    }

    void Stop(TodoItem item)
    {
        if (!_running.Remove(item, out var timers)) return;
        timers.Leave.Stop();
        timers.End.Stop();
    }
}
