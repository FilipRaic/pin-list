using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace PinList.Tests;

/// <summary>The release notes as the update popup shows them (ReleaseNotesView).</summary>
public class ReleaseNotesViewTests : UiTest
{
    [AvaloniaFact]
    public void No_notes_show_nothing()
    {
        Assert.Null(ReleaseNotesView.Build(null, 14));
        Assert.Null(ReleaseNotesView.Build("  \n ", 14));
    }

    [AvaloniaFact]
    public void Headings_bullets_and_paragraphs_each_get_their_own_look()
    {
        var panel = Assert.IsType<StackPanel>(ReleaseNotesView.Build("""
            ### Added
            - **New** *thing*

            A paragraph.
            ### Fixed
            """, 14));
        Assert.Contains("releasenotes", panel.Classes);
        Assert.Equal(4, panel.Children.Count);

        // the first heading has no space above it, a later one does
        var first = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.Contains("noteheading", first.Classes);
        Assert.Equal(new Thickness(0), first.Margin);
        var second = Assert.IsType<TextBlock>(panel.Children[3]);
        Assert.Equal(new Thickness(0, 6, 0, 0), second.Margin);

        // a bullet is a dot next to its text, with bold and italic runs
        var bullet = Assert.IsType<Grid>(panel.Children[1]);
        Assert.Equal("•", ((TextBlock)bullet.Children[0]).Text);
        var text = (TextBlock)bullet.Children[1];
        Assert.Equal(1, Grid.GetColumn(text));
        var runs = text.Inlines!.OfType<Run>().ToList();
        Assert.Equal(new[] { "New", " ", "thing" }, runs.Select(r => r.Text));
        Assert.Equal(FontWeight.SemiBold, runs[0].FontWeight);
        Assert.Equal(FontStyle.Italic, runs[2].FontStyle);

        // a paragraph is plain text
        var paragraph = Assert.IsType<TextBlock>(panel.Children[2]);
        Assert.Equal("A paragraph.", paragraph.Inlines!.OfType<Run>().Single().Text);
        Assert.Equal(14, paragraph.FontSize);
        Assert.Equal(14 * 1.4, paragraph.LineHeight, 6);
    }
}
