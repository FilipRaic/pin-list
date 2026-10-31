using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PinList;

/// <summary>
/// Times in the data file are local wall-clock time without a time zone (see docs/data-format.md), so a 09:00
/// reminder is 09:00 on whichever device has the file. Older files (1.x) wrote most times with an offset
/// ("…+01:00"); that offset is ignored when reading, which gives the same values those versions showed.
/// </summary>
sealed class WallClockConverter : JsonConverter<DateTime>
{
    const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("A time can't be empty.");
        // The clock time as written: drop any offset first. (Parsing it with an offset would fail for the
        // smallest and largest times, which can't be moved into every time zone.)
        var clock = Offset.Replace(text, "");
        if (!DateTime.TryParse(clock, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new JsonException($"Not a time: {text}");
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    static readonly Regex Offset = new(@"(Z|[+-]\d\d:?\d\d)$", RegexOptions.CultureInvariant);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
