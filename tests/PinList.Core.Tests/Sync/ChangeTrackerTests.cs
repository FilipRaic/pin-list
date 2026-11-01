using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

public class ChangeTrackerTests : StoreTestBase
{
    static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    AppData Data => Store.Data;

    Board OneBoard() => MakeBoard(("Work", new[] { "a", "b" }));

    [Fact]
    public void First_run_stamps_everything_once()
    {
        OneBoard();
        // list, its category order, the category, its item order, 2 items
        Assert.Equal(6, ChangeTracker.StampChanges(Data, T0));
        Assert.Equal(0, ChangeTracker.StampChanges(Data, T0.AddMinutes(1)));
        Assert.Equal(32, Data.SyncInfo!.DeviceId.Length);
        Assert.Equal(6, Data.SyncInfo.Counter);
    }

    [Fact]
    public void Stamps_belong_to_this_device_and_the_list_has_seen_them()
    {
        var b = OneBoard();
        ChangeTracker.StampChanges(Data, T0);
        var me = Data.SyncInfo!.DeviceId;
        var item = b.Categories[0].Items[0];
        Assert.Equal(me, item.Sync!.Changed!.Device);
        Assert.Equal(item.Sync.Born, item.Sync.Changed);
        Assert.Equal(6, b.Seen![me]);
    }

    [Fact]
    public void A_text_change_gets_a_new_stamp()
    {
        var item = OneBoard().Categories[0].Items[0];
        ChangeTracker.StampChanges(Data, T0);
        var born = item.Sync!.Born;
        var before = item.Sync.Changed!.Counter;

        item.Text = "changed";
        Assert.Equal(1, ChangeTracker.StampChanges(Data, T0.AddMinutes(5)));
        Assert.True(item.Sync.Changed!.Counter > before);
        Assert.Equal(born, item.Sync.Born);
        Assert.Equal(T0.AddMinutes(5), item.Sync.ModifiedAt);
    }

    [Fact]
    public void Moving_a_window_is_not_a_change()
    {
        var b = OneBoard();
        ChangeTracker.StampChanges(Data, T0);
        b.X += 10; b.Y += 10; b.Width += 10; b.Height += 10; b.Locked = true;
        Data.Settings.Theme = "Dark";
        Assert.Equal(0, ChangeTracker.StampChanges(Data, T0));
    }

    [Fact]
    public void Reordering_items_stamps_the_category_only()
    {
        var cat = OneBoard().Categories[0];
        ChangeTracker.StampChanges(Data, T0);
        var (fields, order) = (cat.Sync!.Changed, cat.OrderSync!.Changed);
        cat.Items.Move(0, 1);
        Assert.Equal(1, ChangeTracker.StampChanges(Data, T0));
        Assert.NotEqual(order, cat.OrderSync.Changed);
        Assert.Equal(fields, cat.Sync.Changed); // a rename elsewhere still wins over nothing
    }

    [Fact]
    public void Moving_an_item_stamps_both_categories_and_the_item()
    {
        var b = MakeBoard(("One", new[] { "a" }), ("Two", new[] { "b" }));
        ChangeTracker.StampChanges(Data, T0);
        var item = b.Categories[0].Items[0];
        b.Categories[0].Items.Remove(item);
        b.Categories[1].Items.Add(item);
        Assert.Equal(3, ChangeTracker.StampChanges(Data, T0));
    }

    [Fact]
    public void Direct_ui_changes_are_caught()
    {
        var b = OneBoard();
        ChangeTracker.StampChanges(Data, T0);
        b.Categories[0].Collapsed = true;
        b.Style = "Yellow";
        b.Categories[0].Items[0].ReminderShown = true;
        Assert.Equal(3, ChangeTracker.StampChanges(Data, T0));
    }

    [Fact]
    public void Every_synced_field_counts()
    {
        var b = OneBoard();
        var cat = b.Categories[0];
        var item = cat.Items[0];
        Data.Archive.Add(new ArchivedItem { Text = "done", BoardId = b.Id });
        ChangeTracker.StampChanges(Data, T0);

        var changes = new Action[]
        {
            () => b.Title = "x", () => b.Transparent = false, () => b.Categories.Add(new Category()),
            () => cat.Name = "x", () => cat.Color = "#000000",
            () => item.CreatedAt = item.CreatedAt.AddDays(-1), () => item.DueDate = new DateTime(2030, 1, 1),
            () => item.ReminderAt = new DateTime(2030, 1, 1, 9, 0, 0),
            () => Data.Archive[0].Text = "x", () => Data.Archive[0].CompletedAt = new DateTime(2030, 1, 1),
            () => Data.Archive[0].CategoryName = "x", () => Data.Archive[0].BoardTitle = "x",
        };
        foreach (var change in changes)
        {
            change();
            Assert.True(ChangeTracker.StampChanges(Data, T0) >= 1);
        }
    }

    [Fact]
    public void Archived_item_stamps_count_for_its_list()
    {
        var b = OneBoard();
        Data.Archive.Add(new ArchivedItem { Text = "done", BoardId = b.Id });
        Data.Archive.Add(new ArchivedItem { Text = "orphan", BoardId = "gone" });
        Assert.Equal(8, ChangeTracker.StampChanges(Data, T0));
        Assert.Equal(Data.Archive[0].Sync!.Changed!.Counter, b.Seen![Data.SyncInfo!.DeviceId]);
    }

    [Fact]
    public void Sync_flag_has_its_own_stamp()
    {
        var b = OneBoard();
        ChangeTracker.StampChanges(Data, T0);
        Assert.Null(b.SyncedChanged); // never synced: nothing to say

        b.Synced = true;
        Assert.Equal(1, ChangeTracker.StampChanges(Data, T0));
        var on = b.SyncedChanged!.Changed;
        b.Synced = false;
        Assert.Equal(1, ChangeTracker.StampChanges(Data, T0));
        Assert.True(b.SyncedChanged.Changed!.Counter > on!.Counter);
        Assert.Equal(0, ChangeTracker.StampChanges(Data, T0));
    }

    [Fact]
    public void Counter_only_goes_up()
    {
        var item = OneBoard().Categories[0].Items[0];
        ChangeTracker.StampChanges(Data, T0);
        var before = Data.SyncInfo!.Counter;
        for (int i = 0; i < 5; i++)
        {
            item.Text = "v" + i;
            ChangeTracker.StampChanges(Data, T0);
        }
        Assert.Equal(before + 5, Data.SyncInfo.Counter);
    }

    [Fact]
    public void Accept_marks_merged_values_as_seen()
    {
        var b = OneBoard();
        var cat = b.Categories[0];
        var item = cat.Items[0];
        Data.Archive.Add(new ArchivedItem { Text = "done", BoardId = b.Id });
        ChangeTracker.StampChanges(Data, T0);

        item.Text = "from another device";
        cat.Name = "renamed";
        b.Title = "renamed";
        b.Synced = true;
        Data.Archive[0].Text = "renamed";
        ChangeTracker.Accept(item, cat.Id, b.Id);
        ChangeTracker.Accept(cat, b.Id);
        ChangeTracker.Accept(b);
        ChangeTracker.Accept(Data.Archive[0]);
        Assert.Equal(0, ChangeTracker.StampChanges(Data, T0));
    }

    [Fact]
    public void Fingerprints_differ_by_field_and_ignore_position()
    {
        var b = new Board();
        var before = ChangeTracker.Fingerprint(b);
        Assert.Equal(32, before.Length);
        b.X = 500;
        Assert.Equal(before, ChangeTracker.Fingerprint(b));
        b.Title = "Other";
        Assert.NotEqual(before, ChangeTracker.Fingerprint(b));

        var item = new TodoItem { Text = "a" };
        Assert.NotEqual(ChangeTracker.Fingerprint(item, "c1", "b"), ChangeTracker.Fingerprint(item, "c2", "b"));
        Assert.NotEqual(ChangeTracker.Fingerprint(item, "c1", "b"), ChangeTracker.Fingerprint(item, "c1", "b2"));
    }

    [Fact]
    public void Moving_a_category_to_another_list_stamps_it_and_its_items()
    {
        var from = OneBoard();
        var to = MakeBoard(("Other", Array.Empty<string>()));
        ChangeTracker.StampChanges(Data, T0);
        Actions.MoveCategory(from.Categories[0], to, 1);
        Assert.Equal(5, ChangeTracker.StampChanges(Data, T0)); // 2 lists, the category, its 2 items
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("complete")]
    [InlineData("deleteCategory")]
    [InlineData("deleteArchived")]
    [InlineData("restore")]
    public void Undoing_a_delete_is_a_new_change(string what)
    {
        var b = OneBoard();
        var cat = b.Categories[0];
        var item = cat.Items[0];
        Actions.Complete(cat.Items[1]);
        ChangeTracker.StampChanges(Data, T0);
        var archived = Data.Archive[0];
        var before = (item.Sync!.Changed, cat.Sync!.Changed, archived.Sync!.Changed);

        switch (what)
        {
            case "delete": Actions.Delete(item); break;
            case "complete": Actions.Complete(item); break;
            case "deleteCategory": Actions.DeleteCategory(cat); break;
            case "deleteArchived": Actions.DeleteArchived(new[] { archived }); break;
            case "restore": Actions.Restore(new[] { archived }); break;
        }
        ChangeTracker.StampChanges(Data, T0);
        Assert.True(Actions.Undo());
        ChangeTracker.StampChanges(Data, T0);

        if (what is "deleteArchived" or "restore") Assert.NotEqual(before.Item3, archived.Sync!.Changed);
        else Assert.NotEqual(before.Item1, item.Sync!.Changed);
        if (what == "deleteCategory") Assert.NotEqual(before.Item2, cat.Sync!.Changed);
    }

    [Fact]
    public void Loading_is_not_a_change()
    {
        var b = OneBoard();
        b.Categories[0].Items[0].CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Local);
        b.Categories[0].Items[0].ReminderAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Local);
        Data.Archive.Add(new ArchivedItem { BoardId = b.Id, CompletedAt = DateTime.Now });
        ChangeTracker.StampChanges(Data, T0);
        var loaded = Store.Parse(Store.Serialize(Data), out _); // times come back without a kind
        Assert.Equal(0, ChangeTracker.StampChanges(loaded, T0));
    }

    [Fact]
    public void Saving_stamps_changes()
    {
        var item = OneBoard().Categories[0].Items[0];
        Store.SaveNow();
        Assert.NotNull(item.Sync);
        var json = File.ReadAllText(Path.Combine(Dir, "data.json"));
        Assert.Contains(item.Sync!.Hash!, json);
    }

    [Fact]
    public void Change_saved_before_a_crash_is_stamped_on_load()
    {
        var item = OneBoard().Categories[0].Items[0];
        Store.SaveNow();
        var stamp = item.Sync!.Changed!;

        // The text changed and was written, but the process died before stamping (old hash in the file)
        var json = File.ReadAllText(Path.Combine(Dir, "data.json")).Replace("\"text\": \"a\"", "\"text\": \"edited\"");
        File.WriteAllText(Path.Combine(Dir, "data.json"), json);

        Store.UseDirectory(Dir);
        Assert.True(Store.Load());
        var loaded = Store.AllItems().Single(i => i.Text == "edited");
        Assert.True(loaded.Sync!.Changed!.Counter > stamp.Counter);
    }
}
