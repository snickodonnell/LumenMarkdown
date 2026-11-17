using System.Text;

namespace LumenMarkdown.Core;

public static class MarkdownClassifier
{
    public static MarkdownKind Classify(string line, ref bool inCodeFence)
    {
        var bytes = Encoding.UTF8.GetBytes(line.Length > 256 ? line[..256] : line);
        return Classify(bytes, ref inCodeFence);
    }

    internal static MarkdownKind Classify(ReadOnlySpan<byte> line, ref bool inCodeFence)
    {
        var start = 0;
        var columns = 0;
        while (start < line.Length && (line[start] == (byte)' ' || line[start] == (byte)'\t'))
        {
            columns += line[start] == (byte)'\t' ? 4 - columns % 4 : 1;
            start++;
        }

        var trimmed = line[start..];
        if (IsFence(trimmed))
        {
            inCodeFence = !inCodeFence;
            return inCodeFence ? MarkdownKind.CodeFence : MarkdownKind.CodeFenceEnd;
        }

        if (inCodeFence)
            return MarkdownKind.Code;

        if (IsBlank(trimmed))
            return MarkdownKind.Blank;

        if (columns >= 4)
        {
            // Deeply indented list markers are nested list items rather than indented code.
            if (IsTask(trimmed)) return MarkdownKind.Task;
            if (trimmed.Length >= 2 && IsBulletMarker(trimmed[0]) && trimmed[1] == (byte)' ') return MarkdownKind.Bullet;
            if (IsNumbered(trimmed)) return MarkdownKind.Numbered;
            return MarkdownKind.IndentedCode;
        }

        var heading = 0;
        while (heading < trimmed.Length && heading < 6 && trimmed[heading] == (byte)'#')
            heading++;
        if (heading > 0 && heading < trimmed.Length && trimmed[heading] == (byte)' ')
            return (MarkdownKind)((byte)MarkdownKind.Heading1 + heading - 1);

        if (trimmed[0] == (byte)'>')
            return MarkdownKind.Quote;

        if (IsTask(trimmed))
            return MarkdownKind.Task;

        if (trimmed.Length >= 2 && IsBulletMarker(trimmed[0]) && trimmed[1] == (byte)' ')
            return MarkdownKind.Bullet;

        if (IsNumbered(trimmed))
            return MarkdownKind.Numbered;

        if (IsRule(trimmed))
            return MarkdownKind.Rule;

        if (IsTableRow(trimmed))
            return MarkdownTableParser.IsDelimiterRow(Encoding.UTF8.GetString(trimmed))
                ? MarkdownKind.TableDelimiter
                : MarkdownKind.Table;

        return MarkdownKind.Paragraph;
    }

    private static bool IsBlank(ReadOnlySpan<byte> line) =>
        line.IsEmpty || (line.Length == 1 && line[0] == (byte)'\r');

    private static bool IsFence(ReadOnlySpan<byte> line) =>
        line.Length >= 3 &&
        ((line[0] == (byte)'`' && line[1] == (byte)'`' && line[2] == (byte)'`') ||
         (line[0] == (byte)'~' && line[1] == (byte)'~' && line[2] == (byte)'~'));

    private static bool IsTask(ReadOnlySpan<byte> line) =>
        line.Length >= 6 && IsBulletMarker(line[0]) && line[1] == (byte)' ' &&
        line[2] == (byte)'[' && (line[3] == (byte)' ' || line[3] is (byte)'x' or (byte)'X') &&
        line[4] == (byte)']' && line[5] == (byte)' ';

    private static bool IsBulletMarker(byte value) => value is (byte)'-' or (byte)'*' or (byte)'+';

    private static bool IsNumbered(ReadOnlySpan<byte> line)
    {
        var i = 0;
        while (i < line.Length && line[i] >= (byte)'0' && line[i] <= (byte)'9') i++;
        return i > 0 && i + 1 < line.Length && line[i] == (byte)'.' && line[i + 1] == (byte)' ';
    }

    private static bool IsRule(ReadOnlySpan<byte> line)
    {
        if (line.Length < 3 || line[0] is not ((byte)'-' or (byte)'*' or (byte)'_')) return false;
        var marker = line[0];
        var count = 0;
        foreach (var value in line)
        {
            if (value == marker) count++;
            else if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r')) return false;
        }
        return count >= 3;
    }

    private static bool IsTableRow(ReadOnlySpan<byte> line)
    {
        var pipes = 0;
        var escaped = false;
        var inCode = false;
        foreach (var value in line)
        {
            if (escaped) { escaped = false; continue; }
            if (value == (byte)'\\') { escaped = true; continue; }
            if (value == (byte)'`') { inCode = !inCode; continue; }
            if (value == (byte)'|' && !inCode) pipes++;
        }

        if (pipes == 0) return false;
        var hasLeadingPipe = line.Length > 0 && line[0] == (byte)'|';
        var hasTrailingPipe = line.Length > 0 && line[^1] == (byte)'|';
        return pipes - (hasLeadingPipe ? 1 : 0) - (hasTrailingPipe ? 1 : 0) >= 1;
    }
}
