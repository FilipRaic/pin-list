using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PinList;

/// <summary>
/// The order of the lists, kept in <see cref="Board.Position"/> so sync can send it. Moving a list changes only the
/// moved list's position (a number between its new neighbours), so a move on one device and a move of another list
/// on another device both stay. Sync sorts the lists by position when it brought new ones.
/// </summary>
public static class BoardOrder
{
    /// <summary>Two neighbours closer than this get new positions all along (after very many moves between them).</summary>
    const double MinGap = 1e-6;

    /// <summary>
    /// Gives every list without a position one between its neighbours: new lists, moved lists (Actions.MoveBoard
    /// clears the moved list's position), imported lists and lists from before 2.6.0. The other lists keep theirs,
    /// also when sync just changed them and the lists aren't sorted yet. Runs before every save and sync
    /// (ChangeTracker). Returns true if any position changed.
    /// </summary>
    public static bool Fix(IList<Board> boards)
    {
        int n = boards.Count;
        bool changed = false;
        int i = 0;
        while (i < n)
        {
            if (HasPosition(boards[i])) { i++; continue; }
            int start = i;
            while (i < n && !HasPosition(boards[i])) i++;
            // lists start..i-1 go between their neighbours
            double? lo = start > 0 ? boards[start - 1].Position : null;
            double? hi = i < n ? boards[i].Position : null;
            if (lo is { } l && hi is { } h && h <= l) hi = null; // neighbours out of order: after the one before
            int count = i - start;
            if (lo is { } a && hi is { } b && (b - a) / (count + 1) < MinGap)
            {
                Renumber(boards);
                return true;
            }
            for (int k = 0; k < count; k++)
            {
                boards[start + k].Position = (lo, hi) switch
                {
                    ({ } low, { } high) => low + (high - low) * (k + 1) / (count + 1),
                    ({ } low, null) => low + k + 1,
                    (null, { } high) => high - (count - k),
                    _ => k,
                };
            }
            changed = true;
        }
        return changed;
    }

    static bool HasPosition(Board b) => b.Position is { } p && double.IsFinite(p);

    /// <summary>
    /// Sorts the lists by position after sync brought other devices' positions. Lists with the same position are
    /// sorted by id, so every device ends up with the same order. Returns true if anything moved.
    /// </summary>
    public static bool Sort(ObservableCollection<Board> boards)
    {
        // a list without a position (from an older PinList) stays after the list before it
        var keys = new Dictionary<Board, double>();
        double last = double.NegativeInfinity;
        foreach (var b in boards)
        {
            if (HasPosition(b)) last = b.Position!.Value;
            keys[b] = last;
        }
        var order = boards
            .OrderBy(b => keys[b])
            .ThenBy(b => HasPosition(b) ? 0 : 1)
            .ThenBy(b => b.Id, StringComparer.Ordinal)
            .ToList();

        bool moved = false;
        for (int i = 0; i < order.Count; i++)
        {
            int from = boards.IndexOf(order[i]);
            if (from == i) continue;
            boards.Move(from, i);
            moved = true;
        }
        return moved;
    }

    static void Renumber(IList<Board> boards)
    {
        for (int i = 0; i < boards.Count; i++) boards[i].Position = i;
    }
}
