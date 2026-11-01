using PinList;
using PinList.Sync;

namespace PinList.Tests.Sync;

/// <summary>A clock shared by simulated devices; every action moves it on by one second.</summary>
sealed class SimClock
{
    public DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    public DateTime Tick() => Now = Now.AddSeconds(1);
}

/// <summary>
/// One simulated device with its own data (no Store, no files): edit its lists, stamp, make its cloud file and merge
/// other devices' files into it, exactly like SyncService does. Ids are readable: a category's id is its name and an
/// item's id is its first text.
/// </summary>
sealed class SimDevice
{
    public readonly AppData Data = new();
    readonly SimClock _clock;

    /// <summary>How far this device's clock is off.</summary>
    public TimeSpan Skew;

    public SimDevice(string id, SimClock clock)
    {
        _clock = clock;
        Data.SyncInfo = new DeviceInfo { DeviceId = id };
    }

    public string Id => Data.SyncInfo!.DeviceId;

    public Board NewList(string id, params (string cat, string[] items)[] categories)
    {
        var b = new Board { Id = id, Title = id, Synced = true };
        foreach (var (name, items) in categories)
        {
            var c = new Category { Id = name, Name = name };
            foreach (var t in items) c.Items.Add(new TodoItem { Id = t, Text = t });
            b.Categories.Add(c);
        }
        Data.Boards.Add(b);
        Stamp();
        return b;
    }

    public Board List(string id = "L") => Data.Boards.Single(b => b.Id == id);
    public bool HasList(string id = "L") => Data.Boards.Any(b => b.Id == id);
    public Category Cat(string id) => Data.Boards.SelectMany(b => b.Categories).Single(c => c.Id == id);
    public TodoItem Item(string id) => Data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items).Single(i => i.Id == id);
    public bool Has(string itemId) => Data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items).Any(i => i.Id == itemId);
    public Category CatOf(string itemId) => Data.Boards.SelectMany(b => b.Categories).Single(c => c.Items.Any(i => i.Id == itemId));

    public void Stamp() => ChangeTracker.StampChanges(Data, _clock.Tick() + Skew);

    // ---- Edits (each is stamped right away, like a save) ----

    public void Edit(string itemId, string text) { Item(itemId).Text = text; Stamp(); }

    public TodoItem Add(string catId, string itemId, int index = -1)
    {
        var item = new TodoItem { Id = itemId, Text = itemId };
        var c = Cat(catId);
        c.Items.Insert(index < 0 ? c.Items.Count : index, item);
        Stamp();
        return item;
    }

    public void Delete(string itemId) { CatOf(itemId).Items.Remove(Item(itemId)); Stamp(); }

    public void Move(string itemId, string toCat, int index = -1)
    {
        var item = Item(itemId);
        CatOf(itemId).Items.Remove(item);
        var c = Cat(toCat);
        c.Items.Insert(index < 0 ? c.Items.Count : index, item);
        Stamp();
    }

    public void Complete(string itemId)
    {
        var item = Item(itemId);
        var c = CatOf(itemId);
        var b = Data.Boards.Single(x => x.Categories.Contains(c));
        c.Items.Remove(item);
        Data.Archive.Insert(0, new ArchivedItem
        {
            Id = item.Id, Text = item.Text, CreatedAt = item.CreatedAt, CompletedAt = _clock.Now,
            CategoryId = c.Id, CategoryName = c.Name, CategoryColor = c.Color, BoardId = b.Id, BoardTitle = b.Title,
        });
        Stamp();
    }

    // ---- Sync ----

    /// <summary>This device's cloud file for a list.</summary>
    public ListCopy File(string listId = "L")
    {
        Stamp();
        return ListCopy.From(List(listId), Data.Archive, Data.SyncInfo!, "Device " + Id);
    }

    /// <summary>Merges another device's file into this device (creating the list when it's new here).</summary>
    public MergeReport Receive(ListCopy theirs)
    {
        Stamp();
        if (!HasList(theirs.BoardId))
        {
            var created = new Board { Id = theirs.BoardId };
            Data.Boards.Add(created);
            theirs.ApplyTo(created, Data.Archive);
            return new MergeReport();
        }
        var result = SyncMerge.Merge(File(theirs.BoardId), theirs, out var report);
        result.ApplyTo(List(theirs.BoardId), Data.Archive);
        return report;
    }

    /// <summary>Gets the other device's latest file for the list.</summary>
    public MergeReport SyncFrom(SimDevice other, string listId = "L") => Receive(other.File(listId));

    /// <summary>Categories and their item texts, e.g. "A: x, y | B: z".</summary>
    public string Content(string listId = "L") =>
        string.Join(" | ", List(listId).Categories.Select(c => $"{c.Name}: {string.Join(", ", c.Items.Select(i => i.Text))}"));

    public string ArchiveTexts() => string.Join(", ", Data.Archive.Select(a => a.Text).OrderBy(t => t));
}
