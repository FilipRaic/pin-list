using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PinList;

/// <summary>The heading above one day's completed items in the archive.</summary>
public sealed record ArchiveDayHeader(string Title, DateTime Day);

/// <summary>The archive as rows grouped by day, the same on the desktop and the phone.</summary>
public static class ArchiveGroups
{
    /// <summary>
    /// The items newest first, with a <see cref="ArchiveDayHeader"/> before each day's items. A day without items
    /// (for example none matches the search) has no header.
    /// </summary>
    public static IReadOnlyList<object> Rows(IEnumerable<ArchivedItem> items, DateTime now)
    {
        var rows = new List<object>();
        foreach (var day in items.OrderByDescending(i => i.CompletedAt).GroupBy(i => i.CompletedAt.Date))
        {
            rows.Add(new ArchiveDayHeader(Title(day.Key, now), day.Key));
            rows.AddRange(day);
        }
        return rows;
    }

    /// <summary>"Today", "Yesterday", otherwise "Mon 27 Oct" (with the year when it isn't this year).</summary>
    public static string Title(DateTime day, DateTime now)
    {
        var today = now.Date;
        if (day.Date == today) return "Today";
        if (day.Date == today.AddDays(-1)) return "Yesterday";
        return day.ToString(day.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>"28 Oct", with the year when it isn't the current year.</summary>
    public static string ShortDate(DateTime date, DateTime now) =>
        date.ToString(date.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
}
