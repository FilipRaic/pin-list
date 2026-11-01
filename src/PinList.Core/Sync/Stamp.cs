using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinList.Sync;

/// <summary>
/// One change made on one device: the device's id and the value of its change counter at that moment.
/// Counters only go up, so (device, counter) never repeats.
/// </summary>
public sealed record Stamp(string Device, long Counter);

/// <summary>
/// The highest counter of each device whose changes a copy of a list contains (a version vector).
/// "This copy has seen version X" means Covers(X's stamp).
/// </summary>
public sealed class SeenList : Dictionary<string, long>
{
    public SeenList() { }
    public SeenList(IDictionary<string, long> other) : base(other) { }

    /// <summary>True if this copy contains the change with that stamp (no stamp counts as seen).</summary>
    public bool Covers(Stamp? s) => s == null || (TryGetValue(s.Device, out var n) && n >= s.Counter);

    public void Include(Stamp s)
    {
        if (!TryGetValue(s.Device, out var n) || n < s.Counter) this[s.Device] = s.Counter;
    }

    /// <summary>Per-device maximum of both lists.</summary>
    public static SeenList Max(SeenList a, SeenList b)
    {
        var result = new SeenList(a);
        foreach (var (device, counter) in b) result.Include(new Stamp(device, counter));
        return result;
    }
}

/// <summary>Sync bookkeeping of one board, category, item or archived item (see docs/data-format.md).</summary>
public sealed class SyncMeta
{
    /// <summary>The change that created the object.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Stamp? Born { get; set; }

    /// <summary>The last change of the object's synced fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Stamp? Changed { get; set; }

    /// <summary>When the last change was made, in UTC (the only UTC time in the data file).</summary>
    [JsonConverter(typeof(UtcConverter))] public DateTime ModifiedAt { get; set; }

    /// <summary>Fingerprint of the synced fields when they were last stamped or merged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Hash { get; set; }

    public SyncMeta Clone() => new() { Born = Born, Changed = Changed, ModifiedAt = ModifiedAt, Hash = Hash };
}

/// <summary>This device's id and change counter (kept in data.json and, as a copy, in sync-state.json).</summary>
public sealed class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public long Counter { get; set; }
}

/// <summary>A UTC time written as ISO 8601 with "Z" (unlike every other time in the file, see WallClockConverter).</summary>
public sealed class UtcConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("A time can't be empty.");
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
            throw new JsonException($"Not a time: {text}");
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture));
}
