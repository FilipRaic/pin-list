using System;
using System.Collections.Generic;
using System.Linq;

namespace PinList.Sync;

/// <summary>
/// Three-way merge of two copies of one list (spec 5.3 with 9.4–9.5). Pure: no I/O, no Store.
/// "Base" is never stored: whether a side has seen the other side's version of an object is read from version
/// stamps and seen-lists, which stays correct for stale files, new devices and restored backups.
/// The rules are symmetric, so two devices that merge each other's files end up with the same list.
/// </summary>
public static class SyncMerge
{
    enum Side { Mine, Theirs }

    public static ListCopy Merge(ListCopy mine, ListCopy theirs, out MergeReport report)
    {
        var r = new MergeReport();
        report = r;
        var sm = mine.Seen;
        var st = theirs.Seen;

        var result = new ListCopy
        {
            BoardId = mine.BoardId, Device = mine.Device, DeviceName = mine.DeviceName, UpdatedAt = mine.UpdatedAt,
        };
        (result.Synced, result.SyncedChanged) = Flag(mine.Synced, mine.SyncedChanged, theirs.Synced, theirs.SyncedChanged, sm, st);
        (result.Deleted, result.DeletedChanged) = Flag(mine.Deleted, mine.DeletedChanged, theirs.Deleted, theirs.DeletedChanged, sm, st);

        if (result.Deleted)
        {
            // Deleted on one side. Changes the deleting side never saw beat the delete: they stay here, in a local list.
            var (keeper, deleter) = mine.Deleted ? (theirs, mine) : (mine, theirs);
            if (keeper == mine && !mine.IsMarker)
            {
                r.KeptLocalAfterRemoteDelete = Stamps(mine).Any(s => !deleter.Seen.Covers(s));
                if (!r.KeptLocalAfterRemoteDelete) r.Removed.AddRange(Ids(mine));
            }
            result.Seen = SeenList.Max(sm, st);
            return result; // a marker
        }

        // A marker has no content: only its flags count. It must never delete anything, so its seen-list is ignored.
        if (mine.IsMarker || theirs.IsMarker)
        {
            var content = mine.IsMarker ? theirs : mine;
            var withFlags = content.Clone();
            (withFlags.BoardId, withFlags.Device, withFlags.DeviceName, withFlags.UpdatedAt) =
                (mine.BoardId, mine.Device, mine.DeviceName, mine.UpdatedAt);
            (withFlags.Synced, withFlags.SyncedChanged, withFlags.Deleted, withFlags.DeletedChanged) =
                (result.Synced, result.SyncedChanged, false, result.DeletedChanged);
            withFlags.Format = ListCopy.CurrentFormat;
            return withFlags;
        }

        result.Seen = SeenList.Max(sm, st);

        // ---- Board fields, and separately the order of its categories ----
        var boardWinner = Pick(mine.Board!.Sync, theirs.Board!.Sync, sm, st, out _);
        result.Board = (boardWinner == Side.Mine ? mine.Board : theirs.Board).Clone();
        var orderWinner = Pick(mine.Board.OrderSync, theirs.Board.OrderSync, sm, st, out _);
        var (bw, bl) = orderWinner == Side.Mine ? (mine.Board, theirs.Board) : (theirs.Board, mine.Board);
        result.Board.OrderSync = bw.OrderSync.Clone();

        // ---- Categories, items and archived items: each by id ----
        var catWinners = new Dictionary<string, Side>();
        var cats = MergeById(mine.Categories, theirs.Categories, c => c.Id, c => c.Sync, c => c.Clone(), sm, st, r,
            winner: (id, side) => catWinners[id] = side, alsoChanged: c => c.OrderSync.Changed);

        var itemWinners = new Dictionary<string, Side>();
        var items = MergeById(mine.Items, theirs.Items, i => i.Id, i => i.Sync, i => i.Clone(), sm, st, r,
            winner: (id, side) => itemWinners[id] = side,
            conflict: (side, m, t) =>
            {
                // Rule 6: the same reminder shown on either device stays shown (a conflict otherwise takes it all)
                if (m.ReminderAt != t.ReminderAt) return null;
                var chosen = (side == Side.Mine ? m : t).Clone();
                chosen.ReminderShown = m.ReminderShown || t.ReminderShown;
                chosen.Combined = chosen.ReminderShown != (side == Side.Mine ? m : t).ReminderShown;
                return chosen;
            });

        var archive = MergeById(mine.Archive, theirs.Archive, a => a.Id, a => a.Sync!, ListCopy.CloneArchived,
            sm, st, r);

        // ---- Rule 5: completed on one side stays completed; it gets the newer text ----
        var archived = archive.ToDictionary(a => a.Id);
        foreach (var item in items.ToList())
        {
            if (!archived.TryGetValue(item.Id, out var entry)) continue;
            items.Remove(item);
            if (item.Sync.ModifiedAt > entry.Sync!.ModifiedAt && item.Text != entry.Text)
            {
                entry.Text = item.Text;
                // the entry carries the edit's stamp, so a device that still has the old text takes the new one
                entry.Sync = new SyncMeta { Born = entry.Sync.Born, Changed = item.Sync.Changed, ModifiedAt = item.Sync.ModifiedAt };
            }
        }

        // ---- Rule 8 within the list: an item needs its category. An edit beats the category's delete. ----
        // The category is always in the copy the item came from.
        var catIds = cats.Select(c => c.Id).ToHashSet();
        foreach (var item in items)
        {
            if (catIds.Contains(item.CategoryId)) continue;
            var side = itemWinners[item.Id];
            var revived = (side == Side.Mine ? mine : theirs).Categories.First(c => c.Id == item.CategoryId);
            cats.Add(revived.Clone());
            catIds.Add(revived.Id);
            catWinners[revived.Id] = side;
            r.Removed.Remove(revived.Id);
        }

        // ---- Order (rule 7): the order of the side whose container won, new ids next to their old neighbours ----
        result.Board.CategoryOrder = MergeOrder(bw.CategoryOrder, bl.CategoryOrder, catIds);
        var catById = cats.ToDictionary(c => c.Id);
        foreach (var c in cats)
        {
            var mineCat = mine.Categories.FirstOrDefault(x => x.Id == c.Id);
            var theirCat = theirs.Categories.FirstOrDefault(x => x.Id == c.Id);
            // the item order has its own winner (both have the category), else the side that has it
            var side = mineCat != null && theirCat != null
                ? Pick(mineCat.OrderSync, theirCat.OrderSync, sm, st, out _)
                : catWinners[c.Id];
            var (w, l) = side == Side.Mine ? (mineCat, theirCat) : (theirCat, mineCat);
            var inCat = items.Where(i => i.CategoryId == c.Id).Select(i => i.Id).ToHashSet();
            c.ItemOrder = MergeOrder(w!.ItemOrder, l?.ItemOrder ?? new(), inCat);
            c.OrderSync = w.OrderSync.Clone();
        }

        result.Categories = result.Board.CategoryOrder.Select(id => catById[id]).ToList();
        var itemById = items.ToDictionary(i => i.Id);
        result.Items = result.Categories.SelectMany(c => c.ItemOrder.Select(id => itemById[id])).ToList();
        result.Archive = archive.OrderByDescending(a => a.CompletedAt).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        return result;
    }

    /// <summary>
    /// Merges objects by id. <paramref name="conflict"/> may replace the chosen copy when both sides changed the same
    /// object without seeing each other's change (null keeps the winner); <paramref name="winner"/> learns which side
    /// each kept object came from.
    /// </summary>
    static List<T> MergeById<T>(List<T> mine, List<T> theirs, Func<T, string> id, Func<T, SyncMeta> meta,
        Func<T, T> clone, SeenList sm, SeenList st, MergeReport r,
        Func<Side, T, T, T?>? conflict = null, Action<string, Side>? winner = null, Func<T, Stamp?>? alsoChanged = null)
        where T : class
    {
        var result = new List<T>();
        var theirById = new Dictionary<string, T>();
        foreach (var t in theirs) theirById.TryAdd(id(t), t);
        var seen = new HashSet<string>();

        foreach (var m in mine)
        {
            var key = id(m);
            if (!seen.Add(key)) continue;
            if (theirById.TryGetValue(key, out var t))
            {
                var side = Pick(meta(m), meta(t), sm, st, out bool wasConflict);
                if (wasConflict && side == Side.Theirs) r.Overridden.Add(key);
                var chosen = (wasConflict ? conflict?.Invoke(side, m, t) : null) ?? clone(side == Side.Mine ? m : t);
                winner?.Invoke(key, side);
                result.Add(chosen);
            }
            else if (st.Covers(meta(m).Changed) && st.Covers(alsoChanged?.Invoke(m))) r.Removed.Add(key); // they saw it and removed it
            else { result.Add(clone(m)); winner?.Invoke(key, Side.Mine); } // new here, or changed after their delete
        }
        foreach (var t in theirs)
        {
            var key = id(t);
            if (!seen.Add(key)) continue;
            // new there; otherwise this side has seen it and removed it
            if (!sm.Covers(meta(t).Changed) || !sm.Covers(alsoChanged?.Invoke(t))) { result.Add(clone(t)); winner?.Invoke(key, Side.Theirs); }
        }
        return result;
    }

    /// <summary>Which side's version of an object wins (rules 1 and 2).</summary>
    static Side Pick(SyncMeta m, SyncMeta t, SeenList sm, SeenList st, out bool conflict)
    {
        conflict = false;
        if (Equals(m.Changed, t.Changed)) return Side.Mine;
        if (st.Covers(m.Changed)) return Side.Theirs; // they have seen my version: theirs is newer
        if (sm.Covers(t.Changed)) return Side.Mine;
        conflict = true;
        if (t.ModifiedAt != m.ModifiedAt) return t.ModifiedAt > m.ModifiedAt ? Side.Theirs : Side.Mine;
        // same time: decide the same way on every device (both stamps exist here, or a side would cover the other)
        return string.CompareOrdinal(t.Changed!.Device, m.Changed!.Device) > 0 ? Side.Theirs : Side.Mine;
    }

    /// <summary>A list flag (sync on/off, deleted): the newest change wins.</summary>
    static (bool, SyncMeta?) Flag(bool m, SyncMeta? mMeta, bool t, SyncMeta? tMeta, SeenList sm, SeenList st)
    {
        if (tMeta == null) return (m, mMeta?.Clone());
        if (mMeta == null) return (t, tMeta.Clone());
        return Pick(mMeta, tMeta, sm, st, out _) == Side.Mine ? (m, mMeta.Clone()) : (t, tMeta.Clone());
    }

    /// <summary>
    /// The winner's order of the ids in <paramref name="keep"/>. Ids only the loser has go right after their previous
    /// neighbour there (or first, if they were first); ids in neither order (moved in from elsewhere) go at the end.
    /// Depends only on (winner, loser, keep), so both devices get the same order.
    /// </summary>
    static List<string> MergeOrder(List<string> winner, List<string> loser, HashSet<string> keep)
    {
        var order = new List<string>();
        foreach (var id in winner) if (keep.Contains(id) && !order.Contains(id)) order.Add(id);
        for (int i = 0; i < loser.Count; i++)
        {
            var id = loser[i];
            if (!keep.Contains(id) || order.Contains(id)) continue;
            int at = 0;
            for (int j = i - 1; j >= 0; j--)
            {
                int n = order.IndexOf(loser[j]);
                if (n >= 0) { at = n + 1; break; }
            }
            order.Insert(at, id);
        }
        foreach (var id in keep.Where(id => !order.Contains(id)).OrderBy(id => id, StringComparer.Ordinal)) order.Add(id);
        return order;
    }

    static IEnumerable<Stamp?> Stamps(ListCopy c) =>
        new[] { c.Board?.Sync.Changed, c.Board?.OrderSync.Changed, c.SyncedChanged?.Changed }
            .Concat(c.Categories.Select(x => x.Sync.Changed))
            .Concat(c.Categories.Select(x => x.OrderSync.Changed))
            .Concat(c.Items.Select(x => x.Sync.Changed))
            .Concat(c.Archive.Select(x => x.Sync!.Changed))
            .Where(s => s != null);

    static IEnumerable<string> Ids(ListCopy c) =>
        c.Categories.Select(x => x.Id).Concat(c.Items.Select(x => x.Id)).Concat(c.Archive.Select(x => x.Id));
}
