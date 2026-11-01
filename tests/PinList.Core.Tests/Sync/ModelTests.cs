using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

public class ModelTests : StoreTestBase
{
    static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-v1.json"));

    [Fact]
    public void Old_files_load_without_sync_fields()
    {
        var data = Store.Parse(Fixture, out _);

        Assert.Null(data.SyncInfo);
        Assert.All(data.Boards, b =>
        {
            Assert.Null(b.Sync);
            Assert.False(b.Synced);
            Assert.Null(b.SyncedChanged);
            Assert.Null(b.Seen);
            Assert.All(b.Categories, c =>
            {
                Assert.Null(c.Sync);
                Assert.All(c.Items, i => Assert.Null(i.Sync));
            });
        });
        Assert.NotEmpty(data.Archive);
        Assert.All(data.Archive, a => { Assert.Null(a.Sync); Assert.False(string.IsNullOrEmpty(a.Id)); });
        Assert.Equal(data.Archive.Count, data.Archive.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Empty_archive_id_gets_a_new_one()
    {
        var data = Store.Parse("""{ "boards": [], "archive": [ { "id": "", "text": "a" }, { "id": null, "text": "b" } ] }""", out _);
        Assert.All(data.Archive, a => Assert.Equal(32, a.Id.Length));
    }

    [Fact]
    public void Sync_fields_round_trip()
    {
        var modified = new DateTime(2026, 11, 1, 8, 30, 15, 250, DateTimeKind.Utc);
        SyncMeta Meta(long n) => new() { Born = new Stamp("A", 1), Changed = new Stamp("B", n), ModifiedAt = modified, Hash = "h" + n };

        var data = new AppData { SyncInfo = new DeviceInfo { DeviceId = "A", Counter = 42 } };
        var item = new TodoItem { Text = "x", Sync = Meta(2) };
        var cat = new Category { Sync = Meta(3) };
        cat.Items.Add(item);
        var board = new Board { Sync = Meta(4), Synced = true, SyncedChanged = Meta(5), Seen = new SeenList { ["A"] = 7, ["B"] = 9 } };
        board.Categories.Add(cat);
        data.Boards.Add(board);
        data.Archive.Add(new ArchivedItem { Id = "arch", Text = "done", Sync = Meta(6) });

        var json = Store.Serialize(data);
        Assert.Contains("\"modifiedAt\": \"2026-11-01T08:30:15.2500000Z\"", json);
        var back = Store.Parse(json, out _);

        Assert.Equal("A", back.SyncInfo!.DeviceId);
        Assert.Equal(42, back.SyncInfo.Counter);
        var b = back.Boards[0];
        Assert.True(b.Synced);
        Assert.Equal(new Stamp("B", 5), b.SyncedChanged!.Changed);
        Assert.Equal(new SeenList { ["A"] = 7, ["B"] = 9 }, b.Seen);
        Assert.Equal(new Stamp("B", 4), b.Sync!.Changed);
        Assert.Equal(new Stamp("A", 1), b.Sync.Born);
        Assert.Equal("h4", b.Sync.Hash);
        Assert.Equal(modified, b.Sync.ModifiedAt);
        Assert.Equal(DateTimeKind.Utc, b.Sync.ModifiedAt.Kind);
        Assert.Equal(new Stamp("B", 3), b.Categories[0].Sync!.Changed);
        Assert.Equal(new Stamp("B", 2), b.Categories[0].Items[0].Sync!.Changed);
        Assert.Equal("arch", back.Archive[0].Id);
        Assert.Equal(new Stamp("B", 6), back.Archive[0].Sync!.Changed);
    }

    [Fact]
    public void Unsynced_lists_write_no_sync_fields()
    {
        var data = new AppData();
        data.Boards.Add(new Board());
        var json = Store.Serialize(data);
        Assert.DoesNotContain("\"synced\"", json);
        Assert.DoesNotContain("\"seen\"", json);
        Assert.DoesNotContain("\"sync\"", json);
        Assert.DoesNotContain("\"syncInfo\"", json);
    }

    [Fact]
    public void Completed_item_keeps_its_id_in_the_archive()
    {
        var item = MakeBoard(("Work", new[] { "Send the invoice" })).Categories[0].Items[0];
        Actions.Complete(item);
        Assert.Equal(item.Id, Store.Data.Archive[0].Id);
    }

    [Fact]
    public void Seen_list_covers_and_merges()
    {
        var a = new SeenList { ["A"] = 3 };
        var b = new SeenList { ["A"] = 1, ["B"] = 2 };
        Assert.True(a.Covers(new Stamp("A", 3)));
        Assert.False(a.Covers(new Stamp("A", 4)));
        Assert.False(a.Covers(new Stamp("B", 1)));
        Assert.True(a.Covers(null));
        Assert.Equal(new SeenList { ["A"] = 3, ["B"] = 2 }, SeenList.Max(a, b));

        a.Include(new Stamp("A", 2));
        Assert.Equal(3, a["A"]);
        a.Include(new Stamp("C", 5));
        Assert.Equal(5, a["C"]);
    }
}
