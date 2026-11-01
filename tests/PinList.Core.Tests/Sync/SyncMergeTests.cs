using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

/// <summary>The three-way merge (spec 5.3 with 9.4–9.5). Every test simulates devices with their own data.</summary>
public class SyncMergeTests
{
    readonly SimClock _clock = new();
    readonly SimDevice A, B, C;

    public SyncMergeTests()
    {
        A = new SimDevice("A", _clock);
        B = new SimDevice("B", _clock);
        C = new SimDevice("C", _clock);
    }

    /// <summary>A has list L with one category "Todo" (items x, y, z); B has a copy.</summary>
    void Shared(params string[] items)
    {
        A.NewList("L", ("Todo", items.Length > 0 ? items : new[] { "x", "y", "z" }));
        B.SyncFrom(A);
    }

    void Both() { B.SyncFrom(A); A.SyncFrom(B); }

    [Fact]
    public void New_list_arrives_whole()
    {
        Shared();
        Assert.Equal("Todo: x, y, z", B.Content());
        Assert.True(B.List().Synced);
        Assert.Equal("L", B.List().Title);
    }

    [Fact]
    public void Changed_on_one_side_takes_that_side()
    {
        Shared();
        A.Edit("x", "x2");
        B.Edit("y", "y2");
        Both();
        Assert.Equal("Todo: x2, y2, z", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Board_and_category_fields_sync()
    {
        Shared();
        A.List().Title = "Groceries"; A.List().Style = "Yellow"; A.List().Transparent = false;
        A.Cat("Todo").Name = "Shop"; A.Cat("Todo").Color = "#000000"; A.Cat("Todo").Collapsed = true;
        A.List().X = 999;
        A.Stamp();
        B.SyncFrom(A);
        var b = B.List();
        Assert.Equal(("Groceries", "Yellow", false), (b.Title, b.Style, b.Transparent));
        Assert.Equal(("Shop", "#000000", true), (B.Cat("Todo").Name, B.Cat("Todo").Color, B.Cat("Todo").Collapsed));
        Assert.NotEqual(999, b.X); // position is per device
    }

    [Fact]
    public void Same_object_both_sides_newer_wins()
    {
        Shared();
        A.Edit("x", "older");
        B.Edit("x", "newer");
        var report = A.SyncFrom(B);
        B.SyncFrom(A);
        Assert.Equal("newer", A.Item("x").Text);
        Assert.Equal("newer", B.Item("x").Text);
        Assert.Contains("x", report.Overridden);
    }

    [Fact]
    public void Same_time_conflict_is_decided_the_same_way_on_both()
    {
        Shared();
        A.Item("x").Text = "from A";
        B.Item("x").Text = "from B";
        var t = _clock.Now.AddMinutes(1);
        ChangeTracker.StampChanges(A.Data, t);
        ChangeTracker.StampChanges(B.Data, t);
        var fa = ListCopy.From(A.List(), A.Data.Archive, A.Data.SyncInfo!, "A");
        var fb = ListCopy.From(B.List(), B.Data.Archive, B.Data.SyncInfo!, "B");
        var ab = SyncMerge.Merge(fa, fb, out _);
        var ba = SyncMerge.Merge(fb, fa, out _);
        Assert.Equal("from B", ab.Items.Single(i => i.Id == "x").Text);
        Assert.Equal("from B", ba.Items.Single(i => i.Id == "x").Text);
    }

    [Fact]
    public void Added_on_either_side_is_kept()
    {
        Shared();
        A.Add("Todo", "a");
        B.Add("Todo", "b");
        Both();
        Assert.True(A.Has("a") && A.Has("b") && B.Has("a") && B.Has("b"));
    }

    [Fact]
    public void Deleted_on_one_side_is_deleted()
    {
        Shared();
        A.Delete("y");
        var report = B.SyncFrom(A);
        Assert.False(B.Has("y"));
        Assert.Contains("y", report.Removed);
        A.SyncFrom(B);
        Assert.False(A.Has("y"));
    }

    [Fact]
    public void Edit_beats_delete_in_both_orders()
    {
        Shared();
        A.Delete("y");
        B.Edit("y", "edited");
        B.SyncFrom(A);
        Assert.Equal("edited", B.Item("y").Text);
        A.SyncFrom(B);
        Assert.Equal("edited", A.Item("y").Text);

        // and the other way round: the edit happens first, the delete later without seeing it
        A.Edit("z", "edited z");
        B.Delete("z");
        A.SyncFrom(B);
        Assert.Equal("edited z", A.Item("z").Text);
        B.SyncFrom(A);
        Assert.Equal("edited z", B.Item("z").Text);
    }

    [Fact]
    public void Delete_after_seeing_the_edit_wins()
    {
        Shared();
        B.Edit("y", "edited");
        A.SyncFrom(B);
        A.Delete("y");
        B.SyncFrom(A);
        Assert.False(B.Has("y"));
    }

    [Fact]
    public void Completed_stays_completed_and_gets_newer_text()
    {
        Shared();
        A.Complete("x");
        B.Edit("x", "x with newer text");
        Both();
        Assert.False(A.Has("x"));
        Assert.False(B.Has("x"));
        Assert.Equal("x with newer text", A.ArchiveTexts());
        Assert.Equal(A.ArchiveTexts(), B.ArchiveTexts());
        A.SyncFrom(B);
        Assert.Single(A.Data.Archive);
    }

    [Fact]
    public void Completed_keeps_its_text_when_the_edit_is_older()
    {
        Shared();
        B.Edit("x", "older edit");
        A.Edit("x", "final");
        A.Complete("x");
        B.SyncFrom(A);
        Assert.Equal("final", B.ArchiveTexts());
    }

    [Fact]
    public void Deleting_from_the_archive_syncs()
    {
        Shared();
        A.Complete("x");
        Both();
        A.Data.Archive.Clear();
        A.Stamp();
        B.SyncFrom(A);
        Assert.Empty(B.Data.Archive);
    }

    [Fact]
    public void Reminder_shown_on_either_side_stays_shown()
    {
        Shared();
        A.Item("x").ReminderAt = new DateTime(2026, 11, 2, 9, 0, 0);
        A.Stamp();
        B.SyncFrom(A);
        A.Item("x").ReminderShown = true;
        A.Stamp();
        B.Item("x").Text = "x edited";
        B.Stamp();
        Both();
        Assert.True(A.Item("x").ReminderShown);
        Assert.True(B.Item("x").ReminderShown);
    }

    [Fact]
    public void Different_reminder_times_newer_wins_with_its_flag()
    {
        Shared();
        A.Item("x").ReminderAt = new DateTime(2026, 11, 2, 9, 0, 0);
        A.Item("x").ReminderShown = true;
        A.Stamp();
        B.Item("x").ReminderAt = new DateTime(2026, 11, 3, 9, 0, 0);
        B.Stamp();
        Both();
        Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0), A.Item("x").ReminderAt);
        Assert.False(A.Item("x").ReminderShown);
        Assert.False(B.Item("x").ReminderShown);
    }

    [Fact]
    public void Reorder_on_one_side_is_kept()
    {
        Shared();
        A.Move("z", "Todo", 0);
        B.Edit("x", "x2");
        Both();
        Assert.Equal("Todo: z, x2, y", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Reorder_on_both_sides_newer_order_wins()
    {
        Shared();
        A.Move("z", "Todo", 0);  // z, x, y
        B.Move("x", "Todo");     // y, z, x  (newer)
        Both();
        Assert.Equal("Todo: y, z, x", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void New_item_lands_next_to_its_old_neighbour()
    {
        Shared();
        A.Add("Todo", "after-x", 1);   // x, after-x, y, z
        B.Add("Todo", "first", 0);     // first, x, y, z (newer order)
        Both();
        Assert.Equal("Todo: first, x, after-x, y, z", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Category_order_and_new_categories_merge()
    {
        A.NewList("L", ("One", new[] { "x" }), ("Two", new[] { "y" }));
        B.SyncFrom(A);
        A.List().Categories.Move(1, 0);
        A.Stamp();
        B.List().Categories.Add(new Category { Id = "Three", Name = "Three" });
        B.Stamp();
        Both();
        // both changed the order: the newer one (B's) wins
        Assert.Equal(new[] { "One", "Two", "Three" }, A.List().Categories.Select(c => c.Id));
        Assert.Equal(new[] { "One", "Two", "Three" }, B.List().Categories.Select(c => c.Id));
    }

    [Fact]
    public void Deleting_a_category_syncs()
    {
        A.NewList("L", ("One", new[] { "x" }), ("Two", new[] { "y" }));
        B.SyncFrom(A);
        A.List().Categories.RemoveAt(1);
        A.Stamp();
        var report = B.SyncFrom(A);
        Assert.Equal(new[] { "One" }, B.List().Categories.Select(c => c.Id));
        Assert.Contains("Two", report.Removed);
        Assert.Contains("y", report.Removed);
    }

    [Fact]
    public void Move_between_categories_is_a_field()
    {
        A.NewList("L", ("One", new[] { "x", "y" }), ("Two", new[] { "z" }));
        B.SyncFrom(A);
        A.Move("x", "Two");
        B.Edit("y", "y2");
        Both();
        Assert.Equal("One: y2 | Two: z, x", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Item_moved_into_a_deleted_category_keeps_it()
    {
        A.NewList("L", ("One", new[] { "x" }), ("Two", new[] { "y" }));
        B.SyncFrom(A);
        A.Move("x", "Two");
        B.List().Categories.RemoveAt(1); // deletes Two and y without seeing the move
        B.Stamp();
        Both();
        Assert.True(A.Has("x"));
        Assert.True(B.Has("x"));
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Edited_item_keeps_its_deleted_category()
    {
        A.NewList("L", ("One", new[] { "x" }), ("Two", new[] { "y", "z" }));
        B.SyncFrom(A);
        A.Edit("y", "y edited");
        B.List().Categories.RemoveAt(1);
        B.Stamp();
        Both();
        Assert.Equal("One: x | Two: y edited", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Edited_item_keeps_its_deleted_category_in_either_merge_order()
    {
        A.NewList("L", ("One", new[] { "x" }), ("Two", new[] { "y", "z" }));
        B.SyncFrom(A);
        A.Edit("y", "y edited");
        B.List().Categories.RemoveAt(1);
        B.Stamp();
        A.SyncFrom(B); // this time the side with the edit merges first
        B.SyncFrom(A);
        Assert.Equal("One: x | Two: y edited", A.Content());
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Markers_merge_the_same_way_from_either_side()
    {
        Shared();
        var deleted = A.File().ToDeletedMarker(A.Data.SyncInfo!, _clock.Tick());
        var r = SyncMerge.Merge(deleted, B.File(), out var report);
        Assert.True(r.Deleted);
        Assert.True(r.IsMarker);
        Assert.False(report.KeptLocalAfterRemoteDelete);

        A.List().Synced = false; A.Stamp();
        var off = A.File().ToMarker();
        r = SyncMerge.Merge(off, B.File(), out _);
        Assert.False(r.Synced);
        Assert.Equal(3, r.Items.Count);
        Assert.Equal(off.Device, r.Device);
    }

    [Fact]
    public void Turned_off_marker_changes_only_the_flag()
    {
        Shared();
        var marker = A.File().ToMarker();
        A.List().Synced = false; A.Stamp();
        var off = A.File().ToMarker();
        Assert.True(off.IsMarker);

        var r = SyncMerge.Merge(B.File(), off, out var report);
        Assert.False(r.Synced);
        Assert.Equal(3, r.Items.Count);       // the marker has no content, so nothing is deleted
        Assert.Empty(report.Removed);
        Assert.Equal(B.File().Seen, r.Seen);  // and its seen-list isn't taken over

        // B turned sync off too, then on again later: the old marker can't empty the list or turn it off
        B.Receive(off);
        Assert.False(B.List().Synced);
        B.List().Synced = true; B.Stamp();
        B.Receive(off);
        Assert.True(B.List().Synced);
        Assert.Equal("Todo: x, y, z", B.Content());
        Assert.False(marker.Deleted);
    }

    [Fact]
    public void Move_between_lists_leaves_one_copy()
    {
        A.NewList("L", ("One", new[] { "x" }));
        A.NewList("M", ("Two", new[] { "y" }));
        B.SyncFrom(A, "L");
        B.SyncFrom(A, "M");

        var x = A.Item("x");
        A.CatOf("x").Items.Remove(x);
        A.Cat("Two").Items.Add(x);
        A.Stamp();

        B.SyncFrom(A, "M");
        B.SyncFrom(A, "L");
        Assert.Equal(1, B.Data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items).Count(i => i.Id == "x"));
        Assert.Equal("Two", B.CatOf("x").Id);
    }

    [Fact]
    public void Turning_sync_on_again_after_drift_keeps_deletes()
    {
        Shared("x", "y", "z", "w");
        // sync is off for a while: both edit their own copy
        A.List().Synced = false; A.Stamp();
        B.SyncFrom(A);
        Assert.False(B.List().Synced);
        A.Delete("x"); A.Add("Todo", "fromA"); A.Edit("w", "w edited on A");
        B.Delete("y"); B.Add("Todo", "fromB"); B.Edit("z", "z edited on B");
        // turned on again on B
        B.List().Synced = true; B.Stamp();
        Both();
        Assert.Equal("Todo: z edited on B, w edited on A, fromA, fromB", A.Content());
        Assert.Equal(A.Content(), B.Content());
        Assert.True(A.List().Synced);
    }

    [Fact]
    public void Newest_sync_flag_wins()
    {
        Shared();
        A.List().Synced = false; A.Stamp();
        B.Edit("x", "x2"); // a content change doesn't touch the flag
        B.SyncFrom(A);
        Assert.False(B.List().Synced);
    }

    [Fact]
    public void Deleted_list_wins_unless_edited()
    {
        Shared();
        A.Complete("z");
        B.SyncFrom(A);
        var marker = A.File().ToDeletedMarker(A.Data.SyncInfo!, _clock.Tick());
        Assert.True(marker.IsMarker);
        Assert.Empty(marker.Items);
        var r = SyncMerge.Merge(B.File(), marker, out var report);
        Assert.True(r.Deleted);
        Assert.False(report.KeptLocalAfterRemoteDelete);

        B.Edit("y", "edited after the delete was made");
        r = SyncMerge.Merge(B.File(), marker, out report);
        Assert.True(r.Deleted);
        Assert.True(report.KeptLocalAfterRemoteDelete);
    }

    [Fact]
    public void Three_devices_converge()
    {
        Shared();
        C.SyncFrom(A);
        A.Edit("x", "x by A"); B.Delete("y"); C.Add("Todo", "c");
        C.Move("z", "Todo", 0);
        // a ring, then once more
        for (int round = 0; round < 2; round++)
        {
            B.SyncFrom(A); C.SyncFrom(B); A.SyncFrom(C);
        }
        Assert.Equal("Todo: z, x by A, c", A.Content());
        Assert.Equal(A.Content(), B.Content());
        Assert.Equal(A.Content(), C.Content());
    }

    [Fact]
    public void Merge_is_idempotent()
    {
        Shared();
        A.Edit("x", "x2"); A.Add("Todo", "a", 0);
        B.Delete("y"); B.Move("z", "Todo", 0);
        var m = B.File();
        var t = A.File();
        var once = SyncMerge.Merge(m, t, out _);
        var twice = SyncMerge.Merge(once, t, out var report);
        Assert.Equal(ListCopy.ToJson(once), ListCopy.ToJson(twice));
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Offline_both_sides_keeps_all_adds_and_all_deletes()
    {
        A.NewList("L", ("One", new[] { "a1", "a2", "a3", "s1" }), ("Two", new[] { "b1", "b2" }));
        B.SyncFrom(A);
        // days offline: lots of edits on both
        A.Add("One", "newA1"); A.Add("Two", "newA2"); A.Delete("a1"); A.Delete("b1"); A.Edit("a2", "a2 by A"); A.Complete("s1");
        B.Add("One", "newB1"); B.Add("Two", "newB2", 0); B.Delete("a3"); B.Delete("b2"); B.Edit("a2", "a2 by B");
        _clock.Now = _clock.Now.AddDays(3);
        Both();
        B.SyncFrom(A);
        foreach (var d in new[] { A, B })
        {
            foreach (var added in new[] { "newA1", "newA2", "newB1", "newB2" }) Assert.True(d.Has(added), added);
            foreach (var deleted in new[] { "a1", "b1", "a3", "b2", "s1" }) Assert.False(d.Has(deleted), deleted);
            Assert.Equal("a2 by B", d.Item("a2").Text);
            Assert.Equal("s1", d.ArchiveTexts());
        }
        Assert.Equal(A.Content(), B.Content());
    }

    [Fact]
    public void Wrong_clock_never_resurrects_a_delete()
    {
        Shared();
        B.Skew = TimeSpan.FromDays(365);
        B.Edit("x", "edited on B (clock a year ahead)");
        A.SyncFrom(B);
        A.Delete("x"); // A saw B's version, then deleted it
        B.SyncFrom(A);
        A.SyncFrom(B);
        Assert.False(A.Has("x"));
        Assert.False(B.Has("x"));
    }

    [Fact]
    public void Stale_file_from_a_device_offline_for_months()
    {
        Shared();
        var stale = B.File(); // B goes offline for months with this file in the cloud
        A.Delete("x");
        A.Edit("y", "y2");
        _clock.Now = _clock.Now.AddDays(120);
        A.Receive(stale);
        C.SyncFrom(A);
        C.Receive(stale);
        foreach (var d in new[] { A, C })
        {
            Assert.False(d.Has("x"));
            Assert.Equal("y2", d.Item("y").Text);
        }
    }

    [Fact]
    public void Retired_device_file_never_resurrects()
    {
        Shared();
        var retired = B.File(); // B is reinstalled: its old file stays in the cloud forever
        var reinstalled = new SimDevice("B2", _clock);
        reinstalled.SyncFrom(A);
        A.Delete("z");
        reinstalled.SyncFrom(A);
        reinstalled.Receive(retired);
        A.Receive(retired);
        Assert.False(A.Has("z"));
        Assert.False(reinstalled.Has("z"));
    }

    [Fact]
    public void Brand_new_device_joining()
    {
        Shared();
        A.Delete("x");
        B.SyncFrom(A);
        B.Add("Todo", "b");
        // C joins and reads the files in either order
        C.SyncFrom(B);
        C.SyncFrom(A);
        Assert.Equal("Todo: y, z, b", C.Content());
        var D = new SimDevice("D", _clock);
        D.SyncFrom(A);
        D.SyncFrom(B);
        Assert.Equal("Todo: y, z, b", D.Content());
    }

    [Fact]
    public void Restored_backup_does_not_resurrect()
    {
        Shared();
        var backup = Store.Serialize(A.Data); // A's daily backup
        A.Add("Todo", "after backup");
        A.Delete("x");
        B.SyncFrom(A);
        var counter = A.Data.SyncInfo!.Counter;

        // A restores the backup; its change counter must not go back (sync-state.json keeps it)
        var restored = Store.Parse(backup, out _);
        A.Data.Boards.Clear();
        foreach (var b in restored.Boards) A.Data.Boards.Add(b);
        A.Data.SyncInfo!.Counter = counter;

        Both();
        foreach (var d in new[] { A, B })
        {
            Assert.False(d.Has("x"));                // deleted after the backup: stays deleted
            Assert.True(d.Has("after backup"));      // added after the backup: comes back from B
        }
    }

    [Fact]
    public void Apply_keeps_instances_and_accepts_fingerprints()
    {
        Shared();
        var item = B.Item("y");
        var cat = B.Cat("Todo");
        A.Edit("y", "y2");
        A.Move("x", "Todo", 2);
        B.SyncFrom(A);
        Assert.Same(item, B.Item("y"));
        Assert.Same(cat, B.Cat("Todo"));
        Assert.Equal("y2", item.Text);
        Assert.Equal(0, ChangeTracker.StampChanges(B.Data, _clock.Tick()));
    }

    [Fact]
    public void Apply_changes_only_what_changed()
    {
        Shared();
        var changes = 0;
        B.Item("x").PropertyChanged += (_, _) => changes++;
        B.Cat("Todo").Items.CollectionChanged += (_, _) => changes++;
        A.Edit("y", "y2");
        B.SyncFrom(A);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Reminder_shown_stays_shown_in_either_merge_order()
    {
        Shared();
        A.Item("x").ReminderAt = new DateTime(2026, 11, 2, 9, 0, 0);
        A.Stamp();
        B.SyncFrom(A);
        A.Item("x").ReminderShown = true;
        A.Stamp();
        B.Item("x").Text = "x edited"; // newer, but the reminder isn't shown here yet
        B.Stamp();
        A.SyncFrom(B); // A merges first this time
        B.SyncFrom(A);
        Assert.True(A.Item("x").ReminderShown);
        Assert.True(B.Item("x").ReminderShown);
        Assert.Equal("x edited", B.Item("x").Text);
    }
}
