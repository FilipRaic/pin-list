using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinList;

/// <summary>
/// Times in the data file are local wall-clock time without a time zone (see docs/data-format.html), so a 09:00
/// reminder is 09:00 on whichever device has the file. A time with an offset isn't valid.
/// </summary>
sealed class WallClockConverter : JsonConverter<DateTime>
{
    const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("A time can't be empty.");
        if (!DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new JsonException($"Not a time: {text}");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
