using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PinList.Sync;

/// <summary>A list's synced fields in a cloud file.</summary>
public sealed class BoardFields
{
    public string Title { get; set; } = "";
    public string Style { get; set; } = "Dark";
    public bool Transparent { get; set; } = true;
    public List<string> CategoryOrder { get; set; } = new();
    public SyncMeta Sync { get; set; } = new();
    public SyncMeta OrderSync { get; set; } = new();

    public BoardFields Clone() => new()
    {
        Title = Title, Style = Style, Transparent = Transparent, CategoryOrder = new(CategoryOrder), Sync = Sync.Clone(),
        OrderSync = OrderSync.Clone(),
    };
}

/// <summary>A category in a cloud file (its items are in ListCopy.Items).</summary>
public sealed class CategoryCopy
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public bool Collapsed { get; set; }
    public List<string> ItemOrder { get; set; } = new();
    public SyncMeta Sync { get; set; } = new();
    public SyncMeta OrderSync { get; set; } = new();

    public CategoryCopy Clone() => new()
    {
        Id = Id, Name = Name, Color = Color, Collapsed = Collapsed, ItemOrder = new(ItemOrder), Sync = Sync.Clone(),
        OrderSync = OrderSync.Clone(),
    };
}

/// <summary>An item in a cloud file. Its category is a field (moving it is a change like editing its text).</summary>
public sealed class ItemCopy
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReminderAt { get; set; }
    public bool ReminderShown { get; set; }
    public string CategoryId { get; set; } = "";
    public SyncMeta Sync { get; set; } = new();

    /// <summary>
    /// The merge combined both sides (rule 6: a reminder shown on either side): the result is new on this device, so
    /// it gets its own stamp instead of being taken as the other side's version.
    /// </summary>
    [JsonIgnore] public bool Combined { get; set; }

    public ItemCopy Clone()
    {
        var c = (ItemCopy)MemberwiseClone();
        c.Sync = Sync.Clone();
        return c;
    }
}

/// <summary>
/// One device's copy of one synced list: the content of a cloud file <c>w-&lt;boardId&gt;.&lt;deviceId&gt;.json</c>
/// (docs/data-format.html). A marker (Board == null) has only the flags: what's left after sync was turned off or
/// the list was deleted.
/// </summary>
public sealed class ListCopy
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public string BoardId { get; set; } = "";
    public string Device { get; set; } = "";
    public string DeviceName { get; set; } = "";
    [JsonConverter(typeof(UtcConverter))] public DateTime UpdatedAt { get; set; }
    public SeenList Seen { get; set; } = new();

    public bool Synced { get; set; }
    public SyncMeta? SyncedChanged { get; set; }
    public bool Deleted { get; set; }
    public SyncMeta? DeletedChanged { get; set; }

    /// <summary>The list's fields; null in a marker.</summary>
    public BoardFields? Board { get; set; }
    public List<CategoryCopy> Categories { get; set; } = new();
    public List<ItemCopy> Items { get; set; } = new();
    public List<ArchivedItem> Archive { get; set; } = new();

    [JsonIgnore] public bool IsMarker => Board == null;

    // ================= Making a copy =================

    /// <summary>The copy of a list on this device (call ChangeTracker.StampChanges first).</summary>
    public static ListCopy From(Board b, IEnumerable<ArchivedItem> archive, DeviceInfo device, string deviceName)
    {
        var copy = new ListCopy
        {
            BoardId = b.Id,
            Device = device.DeviceId,
            DeviceName = deviceName,
            UpdatedAt = DateTime.UtcNow,
            Seen = new SeenList(b.Seen ?? new SeenList()),
            Synced = b.Synced,
            SyncedChanged = b.SyncedChanged?.Clone(),
            Board = new BoardFields
            {
                Title = b.Title, Style = b.Style, Transparent = b.Transparent,
                CategoryOrder = b.Categories.Select(c => c.Id).ToList(),
                Sync = Meta(b.Sync),
                OrderSync = Meta(b.OrderSync),
            },
        };
        foreach (var c in b.Categories)
        {
            copy.Categories.Add(new CategoryCopy
            {
                Id = c.Id, Name = c.Name, Color = c.Color, Collapsed = c.Collapsed,
                ItemOrder = c.Items.Select(i => i.Id).ToList(), Sync = Meta(c.Sync), OrderSync = Meta(c.OrderSync),
            });
            foreach (var i in c.Items)
                copy.Items.Add(new ItemCopy
                {
                    Id = i.Id, Text = i.Text, CreatedAt = i.CreatedAt, DueDate = i.DueDate, ReminderAt = i.ReminderAt,
                    ReminderShown = i.ReminderShown, CategoryId = c.Id, Sync = Meta(i.Sync),
                });
        }
        foreach (var a in archive.Where(a => a.BoardId == b.Id)) copy.Archive.Add(CloneArchived(a));
        return copy;
    }

    static SyncMeta Meta(SyncMeta? m) => m?.Clone() ?? new SyncMeta();

    internal static ArchivedItem CloneArchived(ArchivedItem a) => new()
    {
        Id = a.Id, Text = a.Text, CreatedAt = a.CreatedAt, CompletedAt = a.CompletedAt, DueDate = a.DueDate,
        CategoryId = a.CategoryId, CategoryName = a.CategoryName, CategoryColor = a.CategoryColor,
        BoardId = a.BoardId, BoardTitle = a.BoardTitle, Extra = a.Extra, Sync = Meta(a.Sync),
    };

    public ListCopy Clone() => new()
    {
        Format = Format, BoardId = BoardId, Device = Device, DeviceName = DeviceName, UpdatedAt = UpdatedAt,
        Seen = new SeenList(Seen), Synced = Synced, SyncedChanged = SyncedChanged?.Clone(),
        Deleted = Deleted, DeletedChanged = DeletedChanged?.Clone(), Board = Board?.Clone(),
        Categories = Categories.Select(c => c.Clone()).ToList(),
        Items = Items.Select(i => i.Clone()).ToList(),
        Archive = Archive.Select(CloneArchived).ToList(),
    };

    public ListCopy WithUpdatedAt(DateTime utc)
    {
        var c = Clone();
        c.UpdatedAt = utc;
        return c;
    }

    /// <summary>The same flags without any content (what stays in the cloud after sync was turned off).</summary>
    public ListCopy ToMarker()
    {
        var m = Clone();
        m.Board = null;
        m.Categories.Clear();
        m.Items.Clear();
        m.Archive.Clear();
        return m;
    }

    /// <summary>The marker of a list deleted on this device: the deletion gets its own stamp.</summary>
    public ListCopy ToDeletedMarker(DeviceInfo device, DateTime utcNow)
    {
        var m = ToMarker();
        var stamp = new Stamp(device.DeviceId, ++device.Counter);
        m.Deleted = true;
        m.DeletedChanged = new SyncMeta { Born = stamp, Changed = stamp, ModifiedAt = utcNow };
        m.Seen.Include(stamp);
        m.UpdatedAt = utcNow;
        return m;
    }

    // ================= JSON =================

    public static string ToJson(ListCopy copy) => JsonSerializer.Serialize(copy, Store.JsonOptions);

    /// <summary>Reads a cloud file. Throws if it isn't one.</summary>
    public static ListCopy Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Not a JSON object.");
        if (root["boardId"] is null) throw new JsonException("This isn't a PinList cloud file.");
        var copy = root.Deserialize<ListCopy>(Store.JsonOptions) ?? throw new JsonException("Empty cloud file.");
        copy.Seen ??= new SeenList();
        copy.Categories ??= new();
        copy.Items ??= new();
        copy.Archive ??= new();
        foreach (var a in copy.Archive) a.Sync ??= new SyncMeta();
        return copy;
    }

    // ================= Applying to the local list =================

    /// <summary>
    /// Makes the local list match this copy. Existing boards, categories and items are changed in place (open
    /// windows, bindings and undo keep working). Everything written is accepted by ChangeTracker, so it isn't
    /// mistaken for a local change. Returns true if anything changed.
    /// </summary>
    public bool ApplyTo(Board b, ObservableCollection<ArchivedItem> archive)
    {
        bool changed = false;
        void Set<T>(T current, T value, Action<T> set)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            set(value);
            changed = true;
        }

        Set(b.Synced, Synced, v => b.Synced = v);
        b.SyncedChanged = SyncedChanged?.Clone();
        b.Seen = new SeenList(Seen);
        if (Board != null)
        {
            Set(b.Title, Board.Title, v => b.Title = v);
            Set(b.Style, Board.Style, v => b.Style = v);
            Set(b.Transparent, Board.Transparent, v => b.Transparent = v);
            b.Sync = Board.Sync.Clone();
            b.OrderSync = Board.OrderSync.Clone();

            var oldCats = b.Categories.ToDictionary(c => c.Id);
            var oldItems = new Dictionary<string, TodoItem>();
            foreach (var i in b.Categories.SelectMany(c => c.Items)) oldItems.TryAdd(i.Id, i);
            var items = Items.ToDictionary(i => i.Id);
            var cats = Categories.ToDictionary(c => c.Id);

            var newCats = new List<Category>();
            var newItems = new List<(Category cat, List<TodoItem> items)>();
            foreach (var id in Board.CategoryOrder)
            {
                var cc = cats[id];
                var cat = oldCats.TryGetValue(id, out var existing) ? existing : new Category { Id = id };
                Set(cat.Name, cc.Name, v => cat.Name = v);
                Set(cat.Color, cc.Color, v => cat.Color = v);
                Set(cat.Collapsed, cc.Collapsed, v => cat.Collapsed = v);
                cat.Sync = cc.Sync.Clone();
                cat.OrderSync = cc.OrderSync.Clone();

                var list = new List<TodoItem>();
                foreach (var itemId in cc.ItemOrder)
                {
                    var ic = items[itemId];
                    var item = oldItems.TryGetValue(itemId, out var old) ? old : new TodoItem { Id = itemId };
                    Set(item.Text, ic.Text, v => item.Text = v);
                    Set(item.CreatedAt, ic.CreatedAt, v => item.CreatedAt = v);
                    Set(item.DueDate, ic.DueDate, v => item.DueDate = v);
                    Set(item.ReminderAt, ic.ReminderAt, v => item.ReminderAt = v);
                    Set(item.ReminderShown, ic.ReminderShown, v => item.ReminderShown = v);
                    item.Sync = ic.Sync.Clone();
                    list.Add(item);
                }
                newCats.Add(cat);
                newItems.Add((cat, list));
            }

            changed |= SyncList(b.Categories, newCats);
            // take moved items out first, so no item is ever in two categories at once
            foreach (var (cat, list) in newItems) changed |= RemoveMissing(cat.Items, new HashSet<TodoItem>(list));
            foreach (var (cat, list) in newItems) changed |= SyncList(cat.Items, list);

            foreach (var (cat, list) in newItems)
            {
                ChangeTracker.Accept(cat, b.Id);
                foreach (var item in list)
                {
                    if (items[item.Id].Combined) item.Sync!.Hash = null; // stamped as a change of this device
                    else ChangeTracker.Accept(item, cat.Id, b.Id);
                }
            }

            changed |= ApplyArchive(b.Id, archive);
        }
        ChangeTracker.Accept(b);
        return changed;
    }

    bool ApplyArchive(string boardId, ObservableCollection<ArchivedItem> archive)
    {
        bool changed = false;
        var wanted = Archive.ToDictionary(a => a.Id);
        for (int i = archive.Count - 1; i >= 0; i--)
            if (archive[i].BoardId == boardId && !wanted.ContainsKey(archive[i].Id))
            {
                archive.RemoveAt(i);
                changed = true;
            }

        foreach (var a in Archive)
        {
            int index = IndexOf(archive, a.Id);
            if (index >= 0)
            {
                var local = archive[index];
                if (ChangeTracker.Fingerprint(local) != ChangeTracker.Fingerprint(a))
                {
                    local.Text = a.Text; local.CreatedAt = a.CreatedAt; local.CompletedAt = a.CompletedAt;
                    local.DueDate = a.DueDate; local.CategoryId = a.CategoryId; local.CategoryName = a.CategoryName;
                    local.CategoryColor = a.CategoryColor; local.BoardId = a.BoardId; local.BoardTitle = a.BoardTitle;
                    archive[index] = local; // tells the archive window to show the new text
                    changed = true;
                }
                local.Sync = a.Sync!.Clone();
                ChangeTracker.Accept(local);
            }
            else
            {
                var added = CloneArchived(a);
                int at = 0;
                while (at < archive.Count && archive[at].CompletedAt >= added.CompletedAt) at++;
                archive.Insert(at, added); // the archive is newest first
                ChangeTracker.Accept(added);
                changed = true;
            }
        }
        return changed;
    }

    static int IndexOf(IList<ArchivedItem> archive, string id)
    {
        for (int i = 0; i < archive.Count; i++) if (archive[i].Id == id) return i;
        return -1;
    }

    static bool RemoveMissing<T>(ObservableCollection<T> list, HashSet<T> keep)
    {
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
            if (!keep.Contains(list[i])) { list.RemoveAt(i); changed = true; }
        return changed;
    }

    /// <summary>Makes the collection equal to the target with as few changes as possible. True if it changed.</summary>
    static bool SyncList<T>(ObservableCollection<T> list, List<T> target) where T : class
    {
        bool changed = RemoveMissing(list, new HashSet<T>(target));
        for (int i = 0; i < target.Count; i++)
        {
            if (i < list.Count && ReferenceEquals(list[i], target[i])) continue;
            int from = list.IndexOf(target[i]);
            if (from > i) list.Move(from, i);
            else list.Insert(i, target[i]);
            changed = true;
        }
        return changed;
    }
}

/// <summary>What a merge removed or overrode on this device (ids only, for log.txt).</summary>
public sealed class MergeReport
{
    public List<string> Removed { get; } = new();
    public List<string> Overridden { get; } = new();

    /// <summary>The list was deleted on another device, but this device changed it since; it stays here as a local list.</summary>
    public bool KeptLocalAfterRemoteDelete { get; set; }
}
