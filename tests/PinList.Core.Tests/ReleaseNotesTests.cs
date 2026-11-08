using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>Reading the release notes (a CHANGELOG.md section) for the update popup.</summary>
public class ReleaseNotesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \r\n")]
    public void No_notes_have_no_blocks(string? markdown) => Assert.Empty(ReleaseNotes.Parse(markdown));

    [Fact]
    public void Headings_bullets_and_paragraphs()
    {
        var blocks = ReleaseNotes.Parse("""
            Drag lists into order,
            and the order syncs.

            ### Added
            - First thing
              goes on here
            * Second thing
            + Third thing

            ## Fixed
            -not a bullet
            """);
        Assert.Equal(new[]
        {
            (NoteKind.Text, "Drag lists into order, and the order syncs."),
            (NoteKind.Heading, "Added"),
            (NoteKind.Bullet, "First thing goes on here"),
            (NoteKind.Bullet, "Second thing"),
            (NoteKind.Bullet, "Third thing"),
            (NoteKind.Heading, "Fixed"),
            (NoteKind.Text, "-not a bullet"),
        }, blocks.Select(b => (b.Kind, b.Plain)));
    }

    [Fact]
    public void Windows_line_ends_and_an_empty_heading()
    {
        var blocks = ReleaseNotes.Parse("###\r\n- One\r\n- Two");
        Assert.Equal(new[] { "One", "Two" }, blocks.Select(b => b.Plain));
    }

    [Fact]
    public void Bold_and_italic_become_spans()
    {
        var spans = ReleaseNotes.Spans("**All lists** on *Windows* and ***macOS***: drag");
        Assert.Equal(new[]
        {
            new NoteSpan("All lists", Bold: true),
            new NoteSpan(" on "),
            new NoteSpan("Windows", Italic: true),
            new NoteSpan(" and "),
            new NoteSpan("macOS", Bold: true, Italic: true),
            new NoteSpan(": drag"),
        }, spans);
    }

    [Fact]
    public void Code_and_links_keep_only_their_text()
    {
        Assert.Equal("See data-format.html and Directory.Build.props",
            string.Concat(ReleaseNotes.Spans("See [data-format.html](docs/data-format.html) and `Directory.Build.props`").Select(s => s.Text)));
        // brackets that aren't a link stay as they are
        Assert.Equal("[2.6.0] and [x](no end", string.Concat(ReleaseNotes.Spans("[2.6.0] and [x](no end").Select(s => s.Text)));
    }

    [Fact]
    public void Text_of_the_same_look_is_one_span()
    {
        // an empty bold part between two plain parts
        Assert.Equal(new[] { new NoteSpan("a b") }, ReleaseNotes.Spans("a ****b"));
        Assert.Empty(ReleaseNotes.Spans("**"));
    }
}
