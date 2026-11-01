using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

/// <summary>Export, import and backups with synced lists (spec 5.8, 6.2).</summary>
public class StoreSyncTests : StoreTestBase
{
    Board Synced(string title, params string[] items)
    {
        var b = MakeBoard(("Cat", items));
        b.Title = title;
        b.Synced = true;
        return b;
    }

    [Fact]
    public void Export_clears_sync_flags()
    {
        var synced = Synced("Shared", "a");
        MakeBoard(("Local", new[] { "b" }));
        Store.SaveNow();
        Assert.NotNull(Store.Data.SyncInfo);

        var path = Path.Combine(Dir, "export.json");
        Store.Export(path);
        var exported = Store.ReadFile(path, out _);
        Assert.All(exported.Boards, b => { Assert.False(b.Synced); Assert.Null(b.SyncedChanged); });
        Assert.Null(exported.SyncInfo);
        Assert.Equal(File.ReadAllText(path), Store.ExportJson());
        Assert.True(synced.Synced); // the app's own data is unchanged
        Assert.NotNull(Store.Data.SyncInfo);
    }

    [Fact]
    public void Replace_keeps_synced_lists()
    {
        var synced = Synced("Shared", "a");
        var local = MakeBoard(("Local", new[] { "b" }));
        Store.Data.Archive.Add(new ArchivedItem { Text = "synced done", BoardId = synced.Id });
        Store.Data.Archive.Add(new ArchivedItem { Text = "local done", BoardId = local.Id });
        Store.SaveNow();
        var device = Store.Data.SyncInfo!.DeviceId;
        var exportedEarlier = Store.Parse(Store.ExportJson(), out _); // contains the synced list too

        var imported = new AppData();
        imported.Boards.Add(new Board { Title = "Imported" });
        foreach (var b in exportedEarlier.Boards.Where(b => b.Id == synced.Id)) imported.Boards.Add(b);
        imported.Archive.Add(new ArchivedItem { Text = "imported done" });
        Assert.True(Store.HasSyncedBoards);
        Store.ReplaceAll(imported);

        Assert.Equal(new[] { "Shared", "Imported" }, Store.Data.Boards.Select(b => b.Title).OrderByDescending(t => t));
        Assert.Same(synced, Store.Data.Boards.Single(b => b.Title == "Shared"));
        Assert.Equal(new[] { "imported done", "synced done" }, Store.Data.Archive.Select(a => a.Text).OrderBy(t => t));
        Assert.Equal(device, Store.Data.SyncInfo!.DeviceId); // still the same device
    }

    [Fact]
    public void Replace_without_synced_lists_replaces_everything()
    {
        MakeBoard(("Local", new[] { "b" }));
        Assert.False(Store.HasSyncedBoards);
        var imported = new AppData();
        imported.Boards.Add(new Board { Title = "Imported" });
        Store.ReplaceAll(imported);
        Assert.Equal("Imported", Store.Data.Boards.Single().Title);
    }

    [Fact]
    public void Add_imports_as_local_lists_with_fresh_stamps()
    {
        var source = Synced("Shared", "a");
        Store.Data.Archive.Add(new ArchivedItem { Text = "done", BoardId = source.Id });
        Store.SaveNow();
        var imported = Store.Parse(Store.Serialize(Store.Data), out _);
        imported.Boards[0].Synced = true; // as if the file still said so

        var added = Store.Merge(imported).Single();
        Assert.False(added.Synced);
        Assert.Null(added.Sync);
        Assert.Null(added.SyncedChanged);
        Assert.Null(added.Seen);
        Assert.Null(added.Categories[0].Sync);
        Assert.Null(added.OrderSync);
        Assert.Null(added.Categories[0].OrderSync);
        Assert.Null(added.Categories[0].Items[0].Sync);
        Assert.All(Store.Data.Archive.Where(a => a.BoardId == added.Id), a => Assert.Null(a.Sync));

        var stamped = ChangeTracker.StampChanges(Store.Data, DateTime.UtcNow);
        Assert.Equal(6, stamped); // list and its order, category and its order, item, archived item: all new
        Assert.NotEqual(source.Categories[0].Items[0].Id, added.Categories[0].Items[0].Id);
    }

    [Fact]
    public void Backup_exists_before_a_removing_merge()
    {
        MakeBoard(("Cat", new[] { "a" }));
        Store.SaveNow();
        foreach (var f in Directory.GetFiles(Store.BackupDir)) File.Delete(f);

        Store.BackupBeforeSync();
        Assert.True(File.Exists(Path.Combine(Store.BackupDir, $"data-{DateTime.Now:yyyy-MM-dd}.json")));
    }

    [Fact]
    public void Replacing_with_a_raw_backup_does_not_turn_sync_on()
    {
        MakeBoard(("Local", new[] { "b" }));
        var backup = new AppData();
        backup.Boards.Add(new Board { Title = "Was synced", Synced = true, SyncedChanged = new SyncMeta() });
        Store.ReplaceAll(backup);
        var b = Store.Data.Boards.Single();
        Assert.False(b.Synced);
        Assert.Null(b.SyncedChanged);
    }
}
