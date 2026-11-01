using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PinList.Sync;

/// <summary>
/// Finds local changes by fingerprint (spec 9.4): every board, category, item and archived item keeps a hash of its
/// synced fields. Objects whose hash no longer matches get a new last-change stamp (deviceId, ++counter) and
/// ModifiedAt = now. Runs before every save and every sync, so changes the UI makes directly are caught too.
/// </summary>
public static class ChangeTracker
{
    /// <summary>Stamps every object whose synced fields changed. Returns how many objects got a new stamp.</summary>
    public static int StampChanges(AppData data, DateTime utcNow)
    {
        var device = data.SyncInfo ??= new DeviceInfo { DeviceId = Guid.NewGuid().ToString("N") };
        int stamped = 0;

        void Check(SyncMeta? meta, string hash, Action<SyncMeta> set, SeenList? seen)
        {
            if (meta?.Hash == hash) return;
            meta ??= new SyncMeta();
            var stamp = new Stamp(device.DeviceId, ++device.Counter);
            meta.Born ??= stamp;
            meta.Changed = stamp;
            meta.ModifiedAt = utcNow;
            meta.Hash = hash;
            set(meta);
            seen?.Include(stamp);
            stamped++;
        }

        foreach (var b in data.Boards)
        {
            var seen = b.Seen ??= new SeenList();
            Check(b.Sync, Fingerprint(b), m => b.Sync = m, seen);
            Check(b.OrderSync, OrderFingerprint(b), m => b.OrderSync = m, seen);
            // The on/off flag is only worth a stamp once the list was ever synced
            if (b.Synced || b.SyncedChanged != null)
                Check(b.SyncedChanged, FlagHash(b.Synced), m => b.SyncedChanged = m, seen);
            foreach (var c in b.Categories)
            {
                Check(c.Sync, Fingerprint(c, b.Id), m => c.Sync = m, seen);
                Check(c.OrderSync, OrderFingerprint(c), m => c.OrderSync = m, seen);
                foreach (var i in c.Items) Check(i.Sync, Fingerprint(i, c.Id, b.Id), m => i.Sync = m, seen);
            }
        }
        foreach (var a in data.Archive)
        {
            var seen = data.Boards.FirstOrDefault(b => b.Id == a.BoardId)?.Seen;
            Check(a.Sync, Fingerprint(a), m => a.Sync = m, seen);
        }
        return stamped;
    }

    // The order of a list's categories and of a category's items has its own stamp: a reorder (also adding or
    // removing a child) on one device and a rename on another are then two changes, and both stay.

    public static string Fingerprint(Board b) => Hash("board", b.Title, b.Style, b.Transparent);

    public static string OrderFingerprint(Board b) => Hash("board order", b.Categories.Select(c => c.Id).ToArray());

    public static string OrderFingerprint(Category c) => Hash("category order", c.Items.Select(i => i.Id).ToArray());

    // The list a category or item is in is part of it: moving a category to another list must look like a change of
    // it and its items on every device, or the other devices would take it for deleted from both lists.

    public static string Fingerprint(Category c, string boardId) => Hash("category", c.Name, c.Color, c.Collapsed, boardId);

    public static string Fingerprint(TodoItem i, string categoryId, string boardId) =>
        Hash("item", i.Text, i.CreatedAt, i.DueDate, i.ReminderAt, i.ReminderShown, categoryId, boardId);

    public static string Fingerprint(ArchivedItem a) =>
        Hash("archived", a.Text, a.CreatedAt, a.CompletedAt, a.DueDate, a.CategoryId, a.CategoryName, a.CategoryColor,
            a.BoardId, a.BoardTitle);

    static string FlagHash(bool on) => Hash("synced", on);

    // ---- After a merge wrote values from another device: they aren't a local change ----

    public static void Accept(Board b)
    {
        (b.Sync ??= new SyncMeta()).Hash = Fingerprint(b);
        (b.OrderSync ??= new SyncMeta()).Hash = OrderFingerprint(b);
        if (b.Synced || b.SyncedChanged != null) (b.SyncedChanged ??= new SyncMeta()).Hash = FlagHash(b.Synced);
    }

    // A merge always writes the stamps before it accepts, so Sync is set here.

    public static void Accept(Category c, string boardId)
    {
        c.Sync!.Hash = Fingerprint(c, boardId);
        c.OrderSync!.Hash = OrderFingerprint(c);
    }

    public static void Accept(TodoItem i, string categoryId, string boardId) => i.Sync!.Hash = Fingerprint(i, categoryId, boardId);

    public static void Accept(ArchivedItem a) => a.Sync!.Hash = Fingerprint(a);

    /// <summary>
    /// The object gets a new stamp at the next StampChanges even if its fields are unchanged. Undo uses it when it
    /// puts back something deleted or completed: other devices have seen it go, so its old stamp would make them
    /// remove it again (spec 5.9: undo is a new local change).
    /// </summary>
    public static void Touch(SyncMeta? meta)
    {
        if (meta != null) meta.Hash = null;
    }

    /// <summary>SHA-256 of the values as JSON, first 16 bytes as hex.</summary>
    static string Hash(params object?[] values)
    {
        // Times as wall-clock text: a time read back from data.json has lost its Kind and must hash the same
        for (int i = 0; i < values.Length; i++)
            if (values[i] is DateTime t) values[i] = t.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        var bytes = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values));
        return Convert.ToHexStringLower(bytes.AsSpan(0, 16));
    }
}
