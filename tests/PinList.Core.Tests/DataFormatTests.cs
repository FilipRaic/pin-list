using System.Text.Json.Nodes;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The data file format (docs/data-format.md) must not change by accident.</summary>
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
