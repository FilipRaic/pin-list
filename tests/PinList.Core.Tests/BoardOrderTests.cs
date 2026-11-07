using System.Collections.ObjectModel;
using PinList;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>The order of the lists, kept as positions so sync can send it (BoardOrder).</summary>
public class BoardOrderTests
{
    static ObservableCollection<Board> Boards(params double?[] positions)
    {
        var list = new ObservableCollection<Board>();
        for (int i = 0; i < positions.Length; i++) list.Add(new Board { Id = ((char)('a' + i)).ToString(), Position = positions[i] });
        return list;
    }

    static double?[] Positions(IEnumerable<Board> boards) => boards.Select(b => b.Position).ToArray();
    static string Ids(IEnumerable<Board> boards) => string.Concat(boards.Select(b => b.Id));

    [Fact]
    public void Lists_from_before_positions_are_numbered_in_their_order()
    {
        var boards = Boards(null, null, null);
        Assert.True(BoardOrder.Fix(boards));
        Assert.Equal(new double?[] { 0, 1, 2 }, Positions(boards));
        Assert.False(BoardOrder.Fix(boards)); // in order now
        Assert.False(BoardOrder.Fix(new List<Board>()));
    }

    [Fact]
    public void A_new_list_goes_after_the_last_one_and_first_before_the_first()
    {
        var boards = Boards(0, 1);
        boards.Add(new Board());
        boards.Insert(0, new Board());
        Assert.True(BoardOrder.Fix(boards));
        Assert.Equal(new double?[] { -1, 0, 1, 2 }, Positions(boards));
    }

    [Fact]
    public void A_moved_list_gets_a_position_between_its_new_neighbours_and_the_others_keep_theirs()
    {
        var boards = Boards(0, 1, 2, 3);
        boards.Move(3, 1); // d between a and b (moving clears its position, see Actions.MoveBoard)
        boards[1].Position = null;
        Assert.True(BoardOrder.Fix(boards));
        Assert.Equal(new double?[] { 0, 0.5, 1, 2 }, Positions(boards));

        // several lists in a row between the same neighbours are spread out evenly
        var more = Boards(0, null, null, 1, 2);
        BoardOrder.Fix(more);
        Assert.Equal(new double?[] { 0, 1.0 / 3, 2.0 / 3, 1, 2 }, Positions(more));

        // positions sync just brought (not sorted yet) are left alone, a new list goes after the one before it
        var unsorted = Boards(3, null, 1);
        BoardOrder.Fix(unsorted);
        Assert.Equal(new double?[] { 3, 4, 1 }, Positions(unsorted));
    }

    [Fact]
    public void Neighbours_too_close_together_renumber_every_list()
    {
        var boards = Boards(0, null, 1e-9, 1); // a new list between 0 and 1e-9
        Assert.True(BoardOrder.Fix(boards));
        Assert.Equal(new double?[] { 0, 1, 2, 3 }, Positions(boards));
    }

    [Fact]
    public void Not_a_number_counts_as_no_position()
    {
        var boards = Boards(0, double.NaN, 2);
        BoardOrder.Fix(boards);
        Assert.Equal(1, boards[1].Position);
    }

    [Fact]
    public void Sort_follows_the_positions_and_ties_go_by_id()
    {
        var boards = Boards(3, 1, 2, 1);
        Assert.True(BoardOrder.Sort(boards));
        Assert.Equal("bdca", Ids(boards));
        Assert.False(BoardOrder.Sort(boards)); // nothing to move
    }

    [Fact]
    public void Sort_keeps_a_list_without_a_position_after_the_one_before_it()
    {
        var boards = Boards(5, null, 1);
        BoardOrder.Sort(boards);
        Assert.Equal("cab", Ids(boards));
        var first = Boards(null, 2, 1);
        BoardOrder.Sort(first);
        Assert.Equal("acb", Ids(first));
    }
}

/// <summary>The list order on two devices: a move on one device reaches the other one.</summary>
public class BoardOrderSyncTests
{
    readonly SimClock _clock = new();

    [Fact]
    public void A_moved_list_keeps_its_place_through_sync_and_older_copies_change_nothing()
    {
        var a = new SimDevice("A", _clock);
        a.NewList("L");
        a.NewList("M");
        var b = new SimDevice("B", _clock);
        b.SyncFrom(a, "L");
        b.SyncFrom(a, "M");
        BoardOrder.Sort(b.Data.Boards);
        Assert.Equal("LM", string.Concat(b.Data.Boards.Select(x => x.Id)));

        // A moves M to the top (like Actions.MoveBoard): only M gets a new position
        double? l = a.List("L").Position;
        a.Data.Boards.Move(1, 0);
        a.List("M").Position = null;
        a.Stamp();
        Assert.True(a.List("M").Position < l);
        Assert.Equal(l, a.List("L").Position);
        b.SyncFrom(a, "M");
        Assert.True(BoardOrder.Sort(b.Data.Boards));
        Assert.Equal("ML", string.Concat(b.Data.Boards.Select(x => x.Id)));

        // a copy from PinList 2.5 or older has no position: the list stays where it is
        var old = a.File("M");
        old.Board!.Position = null;
        old.Board.PositionSync = null;
        b.Receive(old);
        Assert.False(BoardOrder.Sort(b.Data.Boards));

        // the other way round: this device's copy has none yet, so the other device's position is taken
        var mine = b.File("L");
        mine.Board!.PositionSync = null;
        var merged = SyncMerge.Merge(mine, a.File("L"), out _);
        Assert.Equal(a.List("L").Position, merged.Board!.Position);
    }

    [Fact]
    public void Moves_of_different_lists_on_two_devices_both_stay()
    {
        var a = new SimDevice("A", _clock);
        a.NewList("L");
        a.NewList("M");
        a.NewList("N");
        var b = new SimDevice("B", _clock);
        foreach (var id in new[] { "L", "M", "N" }) b.SyncFrom(a, id);
        BoardOrder.Sort(b.Data.Boards);

        a.Data.Boards.Move(2, 0); // N first on A
        a.List("N").Position = null;
        a.Stamp();
        b.Data.Boards.Move(0, 2); // L last on B
        b.List("L").Position = null;
        b.Stamp();
        foreach (var id in new[] { "L", "M", "N" })
        {
            a.SyncFrom(b, id);
            b.SyncFrom(a, id);
        }
        BoardOrder.Sort(a.Data.Boards);
        BoardOrder.Sort(b.Data.Boards);
        Assert.Equal("NML", string.Concat(a.Data.Boards.Select(x => x.Id)));
        Assert.Equal("NML", string.Concat(b.Data.Boards.Select(x => x.Id)));
    }
}
