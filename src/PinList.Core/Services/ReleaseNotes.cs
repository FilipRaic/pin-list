using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PinList;

public enum NoteKind { Heading, Bullet, Text }

/// <summary>A piece of a line of the release notes, bold and / or italic.</summary>
public sealed record NoteSpan(string Text, bool Bold = false, bool Italic = false);

/// <summary>A heading ("Added"), a bullet or a paragraph of the release notes.</summary>
public sealed record NoteBlock(NoteKind Kind, IReadOnlyList<NoteSpan> Spans)
{
    public string Plain => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// Reads the release notes of a version (the Markdown of its CHANGELOG.md section) into headings, bullets and
/// paragraphs, so the app can show them. Only what the changelog uses is understood: headings, "- " bullets,
/// **bold**, *italic*, `code` and [links](...) (their text is kept, the address is dropped).
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<NoteBlock> Parse(string? markdown)
    {
        var blocks = new List<NoteBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        NoteKind? kind = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (kind is { } k && text.Length > 0) blocks.Add(new NoteBlock(k, Spans(text.ToString())));
            kind = null;
            text.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (line.StartsWith('#'))
            {
                Flush();
                kind = NoteKind.Heading;
                text.Append(line.TrimStart('#').Trim());
                Flush();
            }
            else if (line.Length > 1 && line[0] is '-' or '*' or '+' && line[1] == ' ')
            {
                Flush();
                kind = NoteKind.Bullet;
                text.Append(line[2..].Trim());
            }
            else
            {
                // a line that goes on from the one before it (a wrapped bullet or paragraph)
                if (kind == null) kind = NoteKind.Text;
                else text.Append(' ');
                text.Append(line);
            }
        }
        Flush();
        return blocks;
    }

    /// <summary>The text of a line, cut where bold or italic starts or ends.</summary>
    internal static IReadOnlyList<NoteSpan> Spans(string line)
    {
        var spans = new List<NoteSpan>();
        var text = new StringBuilder();
        bool bold = false, italic = false;

        void Cut()
        {
            if (text.Length == 0) return;
            if (spans.Count > 0 && spans[^1].Bold == bold && spans[^1].Italic == italic)
                spans[^1] = spans[^1] with { Text = spans[^1].Text + text };
            else
                spans.Add(new NoteSpan(text.ToString(), bold, italic));
            text.Clear();
        }

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '*' && i + 1 < line.Length && line[i + 1] == '*')
            {
                Cut();
                bold = !bold;
                i++;
            }
            else if (c == '*')
            {
                Cut();
                italic = !italic;
            }
            else if (c == '`') { } // code: only its text
            else if (c == '[' && LinkEnd(line, i) is { } link)
            {
                text.Append(link.label);
                i = link.end;
            }
            else text.Append(c);
        }
        Cut();
        return spans;
    }

    /// <summary>
    /// The notes of <paramref name="version"/> in <paramref name="changelog"/>: the lines under its "## [x.y.z]"
    /// heading, up to the next version. Null when the changelog has no such section or it's empty.
    /// </summary>
    public static string? Section(string? changelog, Version version)
    {
        if (string.IsNullOrEmpty(changelog)) return null;
        string heading = $"## [{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}]";
        var lines = new List<string>();
        bool inside = false;
        foreach (var line in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## [", StringComparison.Ordinal))
            {
                if (inside) break;
                inside = line.StartsWith(heading, StringComparison.Ordinal);
                continue;
            }
            if (inside) lines.Add(line);
        }
        var notes = string.Join("\n", lines).Trim();
        return notes.Length == 0 ? null : notes;
    }

    /// <summary>The CHANGELOG.md that is built into PinList (null when it's missing).</summary>
    static string? Bundled()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("PinList.CHANGELOG.md");
        if (stream == null) return null;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// What's new in <paramref name="version"/>, from the changelog built into the app, so it works offline and
    /// without asking GitHub. Null when the changelog has nothing for that version.
    /// </summary>
    public static string? ForVersion(Version version) => Section(Bundled(), version);

    /// <summary>"[text](address)" at <paramref name="start"/>: its text and where it ends.</summary>
    static (string label, int end)? LinkEnd(string line, int start)
    {
        int close = line.IndexOf("](", start, StringComparison.Ordinal);
        if (close < 0) return null;
        int end = line.IndexOf(')', close + 2);
        if (end < 0) return null;
        return (line[(start + 1)..close], end);
    }
}
