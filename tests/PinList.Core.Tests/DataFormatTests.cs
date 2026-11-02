using System.Text.Json.Nodes;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The data file format (docs/data-format.html) must not change by accident.</summary>
public class DataFormatTests
{
    [Fact]
    public void Version_1_fixture_round_trips_unchanged()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-v1.json"));
        var data = Store.Parse(original, out int version);
        Assert.Equal(1, version);
        Assert.Equal(1, AppData.CurrentVersion);
        // Unchanged except that archive entries from before 2.1.0 get an id (see Sync.ModelTests)
        var saved = JsonNode.Parse(Store.Serialize(data))!;
        foreach (var a in saved["archive"]!.AsArray()) a!.AsObject().Remove("id");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), saved));
    }
}

/// <summary>"widget": whether a list is shown as a widget on this computer (optional, written only when false).</summary>
public class WidgetFieldTests : StoreTestBase
{
    [Fact]
    public void Widget_is_written_only_when_false_and_loads_back()
    {
        var shown = MakeBoard(("A", new[] { "a" }));
        var hidden = MakeBoard(("B", new[] { "b" }));
        Assert.True(hidden.Widget); // every list is a widget unless the user says otherwise
        hidden.Widget = false;
        var json = JsonNode.Parse(Store.Serialize(Store.Data))!;
        var boards = json["boards"]!.AsArray();
        Assert.Null(boards[0]!["widget"]);
        Assert.False(boards[1]!["widget"]!.GetValue<bool>());

        var back = Store.Parse(json.ToJsonString(), out _);
        Assert.True(back.Boards[0].Widget);
        Assert.False(back.Boards[1].Widget);
    }

    [Fact]
    public void Exports_show_every_list_as_a_widget()
    {
        MakeBoard(("A", new[] { "a" })).Widget = false;
        var exported = Store.Parse(Store.ExportJson(), out _);
        Assert.True(exported.Boards.Single().Widget);
        Assert.False(Store.Data.Boards.Single().Widget); // this computer's choice is unchanged
    }

    [Fact]
    public void Imported_lists_are_widgets()
    {
        var file = new AppData();
        file.Boards.Add(new Board { Title = "From a backup", Widget = false });
        var added = Store.Merge(Store.Parse(Store.Serialize(file), out _));
        Assert.True(added.Single().Widget);
    }

    [Fact]
    public void Set_widget_can_be_undone_and_tells_the_app()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        var changed = new List<Board>();
        void OnChanged(Board b) => changed.Add(b);
        Actions.WidgetChanged += OnChanged;
        try
        {
            Actions.SetWidget(board, false);
            Assert.False(board.Widget);
            Assert.True(Actions.Undo(out _));
            Assert.True(board.Widget);
            Assert.Equal(new[] { board, board }, changed);
            Actions.SetWidget(board, true); // no change: nothing to undo, nothing to tell
            Assert.Equal(2, changed.Count);
        }
        finally { Actions.WidgetChanged -= OnChanged; }
    }
}

/// <summary>Times are local wall-clock time: a 09:00 reminder means 09:00 on whichever device has the file.</summary>
public class WallClockTimeTests
{
    static string FileWithTime(string time)
    {
        var root = JsonNode.Parse(Store.Serialize(new AppData()))!;
        root["archive"] = new JsonArray(new JsonObject { ["text"] = "x", ["createdAt"] = time, ["completedAt"] = time });
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00")]          // a time that was never set (C#'s DateTime.MinValue)
    [InlineData("9999-12-31T23:59:59.9999999")]  // the largest time
    [InlineData("2026-07-02T09:30:00.5")]
    public void Times_load_as_written_in_every_time_zone(string time)
    {
        var data = Store.Parse(FileWithTime(time), out _);
        var created = data.Archive.Single().CreatedAt;
        Assert.Equal(DateTimeKind.Unspecified, created.Kind);
        Assert.Equal(time, created.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"));
        Store.Parse(Store.Serialize(data), out _); // and again after saving
    }

    [Fact]
    public void Times_are_written_without_an_offset()
    {
        var data = new AppData();
        data.Archive.Add(new ArchivedItem { Text = "done", CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local),
            CompletedAt = new DateTime(2026, 7, 3, 8, 0, 0, 500, DateTimeKind.Utc) });
        var json = Store.Serialize(data);
        Assert.Contains("\"createdAt\": \"2026-01-02T03:04:05\"", json);
        Assert.Contains("\"completedAt\": \"2026-07-03T08:00:00.5\"", json);
    }

    [Theory]
    [InlineData("\"noon\"")]
    [InlineData("null")]
    [InlineData("\"2026-07-01T10:00:00+09:00\"")] // times have no time zone
    [InlineData("\"2026-07-01T10:00:00Z\"")]
    [InlineData("\"2026-07-01\"")]
    public void A_time_that_isnt_one_makes_the_file_invalid(string value) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Store.Parse(
            "{ \"boards\": [ { \"categories\": [ { \"items\": [ { \"createdAt\": " + value + " } ] } ] } ] }", out _));
}
