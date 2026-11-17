namespace LumenMarkdown.Core;

public enum MarkdownColumnAlignment : byte { Left, Center, Right }

public static class MarkdownTableParser
{
    public static string[] ParseRow(string row)
    {
        var trimmed = row.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !IsEscaped(trimmed, trimmed.Length - 1)) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var escaped = false;
        var inCode = false;
        foreach (var ch in trimmed)
        {
            if (escaped)
            {
                if (ch != '|') current.Append('\\');
                current.Append(ch);
                escaped = false;
                continue;
            }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '`') { inCode = !inCode; current.Append(ch); continue; }
            if (ch == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        if (escaped) current.Append('\\');
        cells.Add(current.ToString().Trim());
        return cells.ToArray();
    }

    public static bool IsDelimiterRow(string row)
    {
        var cells = ParseRow(row);
        return cells.Length >= 2 && cells.All(IsDelimiterCell);
    }

    public static MarkdownColumnAlignment[] GetAlignments(string delimiterRow) =>
        ParseRow(delimiterRow).Select(cell =>
        {
            var value = cell.Trim();
            var left = value.StartsWith(':');
            var right = value.EndsWith(':');
            return left && right ? MarkdownColumnAlignment.Center
                : right ? MarkdownColumnAlignment.Right
                : MarkdownColumnAlignment.Left;
        }).ToArray();

    private static bool IsDelimiterCell(string cell)
    {
        var value = cell.Trim();
        if (value.StartsWith(':')) value = value[1..];
        if (value.EndsWith(':')) value = value[..^1];
        return value.Length >= 3 && value.All(ch => ch == '-');
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashes = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--) slashes++;
        return (slashes & 1) != 0;
    }
}
