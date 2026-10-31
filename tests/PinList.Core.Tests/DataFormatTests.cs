using System.Text.Json.Nodes;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The data file format (docs/data-format.md) must not change by accident.</summary>
public class DataFormatTests
{
    [Fact]
    public void Version_2_fixture_round_trips_unchanged()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-v2.json"));
        var data = Store.Parse(original, out int version);
        Assert.Equal(2, version);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(Store.Serialize(data))));
    }
}

/// <summary>Times are local wall-clock time: a 09:00 reminder means 09:00 on whichever device has the file.</summary>
public class WallClockTimeTests
{
    // Offsets that differ from any local zone of the test machine, so a conversion would show
    const string WithOffsets = """
        { "version": 2,
          "boards": [ { "categories": [ { "items": [ { "text": "t",
            "createdAt": "2026-07-01T10:00:00+09:00",
            "dueDate": "2026-07-02T00:00:00-05:00",
            "reminderAt": "2026-07-02T09:30:00.5+13:00" } ] } ] } ],
          "archive": [ { "text": "done", "createdAt": "2026-06-01T08:00:00Z", "completedAt": "2026-07-03T08:00:00-11:00" } ] }
        """;

    [Fact]
    public void Offsets_in_older_files_are_ignored_on_load()
    {
        var data = Store.Parse(WithOffsets, out _);
        var i = data.Boards[0].Categories[0].Items[0];
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), i.CreatedAt);
        Assert.Equal(new DateTime(2026, 7, 2), i.DueDate);
        Assert.Equal(new DateTime(2026, 7, 2, 9, 30, 0, 500), i.ReminderAt);
        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0), data.Archive[0].CreatedAt);
        Assert.Equal(new DateTime(2026, 7, 3, 8, 0, 0), data.Archive[0].CompletedAt);
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00")]          // a time that was never set (C#'s DateTime.MinValue)
    [InlineData("9999-12-31T23:59:59.9999999")]  // the largest time
    [InlineData("0001-01-01T00:00:00+05:00")]
    public void The_smallest_and_largest_times_load_in_every_time_zone(string time)
    {
        var root = JsonNode.Parse(Store.Serialize(new AppData()))!;
        root["archive"] = new JsonArray(new JsonObject { ["text"] = "x", ["createdAt"] = time, ["completedAt"] = time });
        var data = Store.Parse(root.ToJsonString(), out _);
        Assert.Equal(int.Parse(time[..4]), data.Archive.Single().CreatedAt.Year);
        Assert.Equal(time[11..19], data.Archive.Single().CompletedAt.ToString("HH:mm:ss"));
        Store.Parse(Store.Serialize(data), out _); // and again after saving
    }

    [Fact]
    public void Times_are_written_without_an_offset()
    {
        var data = Store.Parse(WithOffsets, out _);
        data.Boards[0].Categories[0].Items.Add(new TodoItem { Text = "now", CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local) });
        var json = Store.Serialize(data);
        Assert.Contains("\"createdAt\": \"2026-07-01T10:00:00\"", json);
        Assert.Contains("\"reminderAt\": \"2026-07-02T09:30:00.5\"", json);
        Assert.Contains("\"createdAt\": \"2026-01-02T03:04:05\"", json);
        Assert.Contains("\"completedAt\": \"2026-07-03T08:00:00\"", json);
        Assert.DoesNotContain("+", json);
        Assert.DoesNotContain("Z\"", json);
    }

    [Theory]
    [InlineData("\"noon\"")]
    [InlineData("null")]
    public void A_time_that_isnt_one_makes_the_file_invalid(string value) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Store.Parse(
            "{ \"boards\": [ { \"categories\": [ { \"items\": [ { \"createdAt\": " + value + " } ] } ] } ] }", out _));
}
