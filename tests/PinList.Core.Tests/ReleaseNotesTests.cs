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

    [Fact]
    public void Italic_inside_bold_is_its_own_span()
    {
        Assert.Equal(new[]
        {
            new NoteSpan("a", Bold: true),
            new NoteSpan("b", Bold: true, Italic: true),
            new NoteSpan("c", Bold: true),
        }, ReleaseNotes.Spans("**a*b*c**"));
    }

    [Fact]
    public void Brackets_without_an_address_stay_as_they_are() =>
        Assert.Equal("[just brackets]", string.Concat(ReleaseNotes.Spans("[just brackets]").Select(s => s.Text)));

    [Fact]
    public void A_dash_on_its_own_is_text_not_a_bullet()
    {
        var block = Assert.Single(ReleaseNotes.Parse("-"));
        Assert.Equal((NoteKind.Text, "-"), (block.Kind, block.Plain));
    }

    const string Changelog = """
        # Changelog

        Intro that belongs to no version.

        ## [2.10.0] - 2026-11-22

        Release notes in Settings.

        ### Added
        - **Release notes** in Settings

        ## [2.9.1] - 2026-11-17

        ### Fixed
        - Safer loading

        ## [2.9.0] - 2026-11-11

        ## [2.8.0] - 2026-11-09
        - Last one
        """;

    [Fact]
    public void A_section_is_the_lines_under_its_version_up_to_the_next_one()
    {
        Assert.Equal("Release notes in Settings.\n\n### Added\n- **Release notes** in Settings",
            ReleaseNotes.Section(Changelog.ReplaceLineEndings("\n"), new Version(2, 10, 0)));
        // a changelog checked out with Windows line endings gives the same notes
        Assert.Equal("### Fixed\n- Safer loading", ReleaseNotes.Section(Changelog.ReplaceLineEndings("\r\n"), new Version(2, 9, 1)));
        Assert.Equal("- Last one", ReleaseNotes.Section(Changelog, new Version(2, 8, 0))); // the last one ends with the file
        Assert.Equal("- Last one", ReleaseNotes.Section(Changelog, new Version(2, 8, 0, 0))); // the assembly's four parts
        Assert.Equal("- Last one", ReleaseNotes.Section(Changelog, new Version(2, 8))); // no patch number is .0
    }

    [Fact]
    public void A_missing_or_empty_section_is_null()
    {
        Assert.Null(ReleaseNotes.Section(Changelog, new Version(2, 9, 0))); // the heading has nothing under it
        Assert.Null(ReleaseNotes.Section(Changelog, new Version(2, 1, 0))); // not in the changelog
        Assert.Null(ReleaseNotes.Section(Changelog, new Version(2, 10, 1))); // 2.10.1 isn't 2.10.0
        Assert.Null(ReleaseNotes.Section(null, new Version(2, 9, 1)));
        Assert.Null(ReleaseNotes.Section("", new Version(2, 9, 1)));
    }

    [Fact]
    public void The_changelog_is_built_into_the_app()
    {
        // an old version, so the test keeps passing when new versions are added
        Assert.StartsWith("Fixes from a pass over the whole code", ReleaseNotes.ForVersion(new Version(2, 9, 1)));
        Assert.Null(ReleaseNotes.ForVersion(new Version(0, 0, 1)));
    }
}
