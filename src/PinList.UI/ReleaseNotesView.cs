using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace PinList;

/// <summary>What's new in a version, as the update popup shows it (the phone's sheet and the desktop's dialog).</summary>
public static class ReleaseNotesView
{
    /// <summary>The notes as headings, bullets and paragraphs; null when there are none.</summary>
    public static Control? Build(string? notes, double fontSize)
    {
        var blocks = ReleaseNotes.Parse(notes);
        if (blocks.Count == 0) return null;

        var panel = new StackPanel { Spacing = 6 };
        panel.Classes.Add("releasenotes");
        foreach (var block in blocks)
        {
            var text = new TextBlock { FontSize = fontSize, LineHeight = fontSize * 1.4 };
            foreach (var span in block.Spans)
            {
                var run = new Run(span.Text);
                if (span.Bold) run.FontWeight = FontWeight.SemiBold;
                if (span.Italic) run.FontStyle = FontStyle.Italic;
                text.Inlines!.Add(run);
            }

            if (block.Kind == NoteKind.Heading)
            {
                text.Classes.Add("noteheading");
                if (panel.Children.Count > 0) text.Margin = new Thickness(0, 6, 0, 0);
                panel.Children.Add(text);
            }
            else if (block.Kind == NoteKind.Bullet)
            {
                var dot = new TextBlock
                {
                    Text = "•",
                    FontSize = fontSize,
                    LineHeight = fontSize * 1.4,
                    Margin = new Thickness(2, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                Grid.SetColumn(text, 1);
                panel.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dot, text } });
            }
            else panel.Children.Add(text);
        }
        return panel;
    }
}
