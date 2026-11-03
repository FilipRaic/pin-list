using System.Globalization;
using PinList;
using Xunit;

namespace PinList.Tests;

public class ArchiveGroupsTests : IDisposable
{
    readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    static readonly DateTime Now = new(2026, 11, 3, 15, 0, 0);

    static ArchivedItem Item(string text, DateTime done, string category = "Work", string board = "Home", DateTime? created = null) =>
        new() { Text = text, CompletedAt = done, CreatedAt = created ?? done.AddDays(-1), CategoryName = category, BoardTitle = board };

    public ArchiveGroupsTests() => CultureInfo.CurrentCulture = new CultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void Rows_are_grouped_by_day_newest_first()
    {
        var a = Item("a", new DateTime(2026, 11, 3, 9, 0, 0));
        var b = Item("b", new DateTime(2026, 11, 3, 14, 0, 0));
        var c = Item("c", new DateTime(2026, 10, 27, 8, 0, 0));
        var rows = ArchiveGroups.Rows(new[] { c, a, b }, Now);

        Assert.Equal(5, rows.Count);
        Assert.Equal(new ArchiveDayHeader("Today", new DateTime(2026, 11, 3)), rows[0]);
        Assert.Same(b, rows[1]);
        Assert.Same(a, rows[2]);
        Assert.Equal("Tue 27 Oct", ((ArchiveDayHeader)rows[3]).Title);
        Assert.Same(c, rows[4]);
    }

    [Fact]
    public void Today_and_yesterday_have_names()
    {
        var rows = ArchiveGroups.Rows(new[] { Item("a", Now), Item("b", Now.AddDays(-1)) }, Now);
        Assert.Equal(new[] { "Today", "Yesterday" }, rows.OfType<ArchiveDayHeader>().Select(h => h.Title));
        // the date on the clock decides, not 24 hours: just after midnight is still "Today"
        Assert.Equal("Yesterday", ArchiveGroups.Title(new DateTime(2026, 11, 2, 23, 59, 0), new DateTime(2026, 11, 3, 0, 1, 0)));
    }

    [Fact]
    public void Other_years_show_the_year()
    {
        var rows = ArchiveGroups.Rows(new[] { Item("a", new DateTime(2025, 10, 27, 8, 0, 0)) }, Now);
        Assert.Equal("Mon 27 Oct 2025", ((ArchiveDayHeader)rows[0]).Title);
    }

    [Fact]
    public void Days_without_matches_have_no_header()
    {
        var items = new[] { Item("milk", new DateTime(2026, 11, 3, 9, 0, 0)), Item("eggs", new DateTime(2026, 11, 2, 9, 0, 0)) };
        var rows = ArchiveGroups.Rows(items.Where(i => i.Text == "eggs"), Now); // a search that only matches one day
        Assert.Single(rows.OfType<ArchiveDayHeader>());
        Assert.Equal("Yesterday", ((ArchiveDayHeader)rows[0]).Title);
        Assert.Empty(ArchiveGroups.Rows(Array.Empty<ArchivedItem>(), Now));
    }

    [Fact]
    public void Meta_text_has_time_list_category_and_added_date()
    {
        var item = Item("a", new DateTime(2026, 11, 3, 14, 32, 0), category: "This week", board: "Work", created: DateTime.Now.Date.AddDays(-400));
        var added = ArchiveGroups.ShortDate(item.CreatedAt, DateTime.Now);
        Assert.Equal($"14:32 · Work · This week · added {added}", item.MetaText);
        Assert.EndsWith(" " + item.CreatedAt.Year, added); // an older year shows its year
    }

    [Fact]
    public void Meta_text_leaves_out_an_empty_list_title()
    {
        var item = Item("a", new DateTime(2026, 11, 3, 14, 32, 0), board: "  ", created: new DateTime(DateTime.Now.Year, 1, 5));
        Assert.Equal("14:32 · Work · added 5 Jan", item.MetaText);
    }

    [Fact]
    public void Screen_readers_hear_the_whole_row()
    {
        var item = Item("a\nb", new DateTime(2026, 11, 3, 14, 32, 0), board: "Home", created: new DateTime(DateTime.Now.Year, 1, 5));
        Assert.Equal($"a, b, completed {item.CompletedAt:g}, list Home, category Work, added 5 Jan", item.ToString());
        item.BoardTitle = "";
        Assert.Equal($"a, b, completed {item.CompletedAt:g}, category Work, added 5 Jan", item.ToString());
    }
}
