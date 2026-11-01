using System.Text.Json.Nodes;
using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

/// <summary>The cloud file of one list (docs/data-format.html, "Cloud files").</summary>
public class ListCopyTests
{
    readonly SimClock _clock = new();

    ListCopy SampleFile(out SimDevice a)
    {
        a = new SimDevice("A", _clock);
        var b = a.NewList("L", ("Todo", new[] { "x", "y" }));
        b.X = 1234; b.Locked = true;
        a.Item("x").DueDate = new DateTime(2026, 11, 5);
        a.Item("x").ReminderAt = new DateTime(2026, 11, 5, 9, 30, 0);
        a.Data.Archive.Add(new ArchivedItem { Id = "done", Text = "done", BoardId = "L", CompletedAt = new DateTime(2026, 11, 1, 8, 0, 0) });
        a.Data.Archive.Add(new ArchivedItem { Id = "other", Text = "other list", BoardId = "M" });
        return a.File();
    }

    [Fact]
    public void File_has_the_list_and_only_its_archive()
    {
        var f = SampleFile(out var a);
        Assert.Equal(1, f.Format);
        Assert.Equal("L", f.BoardId);
        Assert.Equal("A", f.Device);
        Assert.Equal("Device A", f.DeviceName);
        Assert.True(f.Synced);
        Assert.False(f.IsMarker);
        Assert.Equal(new[] { "Todo" }, f.Board!.CategoryOrder);
        Assert.Equal(new[] { "x", "y" }, f.Categories.Single().ItemOrder);
        Assert.Equal("Todo", f.Items.Single(i => i.Id == "x").CategoryId);
        Assert.Equal(new[] { "done" }, f.Archive.Select(x => x.Id));
        Assert.Equal(a.List().Seen, f.Seen);
    }

    [Fact]
    public void Per_device_fields_are_not_in_the_file()
    {
        var json = ListCopy.ToJson(SampleFile(out _));
        foreach (var name in new[] { "\"width\"", "\"height\"", "\"locked\"", "\"theme\"", "1234" })
            Assert.DoesNotContain(name, json);
    }

    [Fact]
    public void File_round_trips()
    {
        var f = SampleFile(out _);
        var json = ListCopy.ToJson(f);
        var back = ListCopy.Parse(json);
        Assert.Equal(json, ListCopy.ToJson(back));
        Assert.Equal(new DateTime(2026, 11, 5, 9, 30, 0), back.Items.Single(i => i.Id == "x").ReminderAt);
        Assert.Contains("\"reminderAt\": \"2026-11-05T09:30:00\"", json); // wall clock, no zone
    }

    [Fact]
    public void Newer_format_is_read_so_it_can_be_refused()
    {
        var node = JsonNode.Parse(ListCopy.ToJson(SampleFile(out _)))!;
        node["format"] = 2;
        node["somethingNew"] = "x";
        Assert.Equal(2, ListCopy.Parse(node.ToJsonString()).Format);
    }

    [Fact]
    public void Broken_file_throws()
    {
        Assert.ThrowsAny<Exception>(() => ListCopy.Parse("not json"));
        Assert.ThrowsAny<Exception>(() => ListCopy.Parse("{}"));
    }

    [Fact]
    public void Apply_to_a_new_board_creates_everything()
    {
        var f = SampleFile(out _);
        var board = new Board { Id = "L" };
        var archive = new System.Collections.ObjectModel.ObservableCollection<ArchivedItem>();
        Assert.True(f.ApplyTo(board, archive));
        Assert.Equal("L", board.Title);
        Assert.Equal(new[] { "x", "y" }, board.Categories.Single().Items.Select(i => i.Text));
        Assert.Equal(new DateTime(2026, 11, 5), board.Categories[0].Items[0].DueDate);
        Assert.Equal("done", archive.Single().Text);
        Assert.False(f.ApplyTo(board, archive)); // nothing left to change
    }

    [Fact]
    public void Apply_a_marker_changes_only_the_flags()
    {
        SampleFile(out var a);
        a.List().Synced = false;
        a.Stamp();
        var marker = a.File().ToMarker();
        var board = new Board { Id = "L", Title = "mine", Synced = true };
        Assert.True(marker.ApplyTo(board, new()));
        Assert.False(board.Synced);
        Assert.Equal("mine", board.Title);
        Assert.Equal(0, ChangeTracker.StampChanges(new AppData { Boards = { board }, SyncInfo = new DeviceInfo { DeviceId = "Z" } }, DateTime.UtcNow));
    }

    [Fact]
    public void Missing_parts_of_a_file_are_empty()
    {
        var f = ListCopy.Parse("""{ "boardId": "L", "seen": null, "categories": null, "items": null, "archive": [ { "id": "a", "text": "t" } ] }""");
        Assert.Empty(f.Seen);
        Assert.Empty(f.Categories);
        Assert.Empty(f.Items);
        Assert.NotNull(f.Archive.Single().Sync);
        Assert.ThrowsAny<Exception>(() => ListCopy.Parse("""{ "boardId": "L", "updatedAt": "noon" }"""));
        Assert.ThrowsAny<Exception>(() => ListCopy.Parse("""{ "boardId": "L", "updatedAt": null }"""));
    }
}
