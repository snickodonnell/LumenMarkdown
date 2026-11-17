using System.Text;

namespace LumenMarkdown.Core;

public static class MarkdownTableLayout
{
    /// <summary>
    /// Resolves final column widths the way an automatic table layout does: columns get their natural
    /// (unwrapped) width when the table fits; otherwise every column keeps its minimum (longest word)
    /// width and the remaining space is shared in proportion to how much each column wants to grow.
    /// </summary>
    public static double[] ResolveColumnWidths(IReadOnlyList<double> natural, IReadOnlyList<double> minimum, double available)
    {
        var count = natural.Count;
        var widths = new double[count];
        double naturalTotal = 0, minimumTotal = 0;
        for (var i = 0; i < count; i++)
        {
            var min = Math.Min(minimum[i], natural[i]);
            naturalTotal += natural[i];
            minimumTotal += min;
            widths[i] = min;
        }

        if (naturalTotal <= available)
        {
            for (var i = 0; i < count; i++) widths[i] = natural[i];
            return widths;
        }
        if (minimumTotal >= available) return widths;

        var extra = available - minimumTotal;
        var desire = naturalTotal - minimumTotal;
        for (var i = 0; i < count; i++)
            widths[i] += desire <= 0 ? 0 : extra * (natural[i] - widths[i]) / desire;
        for (var i = 0; i < count; i++) widths[i] = Math.Floor(widths[i]);
        return widths;
    }

    /// <summary>Removes inline Markdown syntax so cell text can be measured as it will be displayed.</summary>
    public static string ToPlainText(string inline)
    {
        if (inline.IndexOfAny(['*', '_', '`', '[', '~', '\\', '!']) < 0) return inline;
        var result = new StringBuilder(inline.Length);
        for (var i = 0; i < inline.Length; i++)
        {
            var ch = inline[i];
            if (ch == '\\' && i + 1 < inline.Length && IsEscapable(inline[i + 1]))
            {
                result.Append(inline[++i]);
                continue;
            }
            if (ch == '`')
            {
                var end = inline.IndexOf('`', i + 1);
                if (end > i) { result.Append(inline, i + 1, end - i - 1); i = end; continue; }
            }
            if (ch == '[' || (ch == '!' && i + 1 < inline.Length && inline[i + 1] == '['))
            {
                var open = ch == '!' ? i + 1 : i;
                var middle = inline.IndexOf("](", open + 1, StringComparison.Ordinal);
                var close = middle > open ? inline.IndexOf(')', middle + 2) : -1;
                if (close > middle && inline.IndexOf(']', open + 1) == middle)
                {
                    result.Append(inline, open + 1, middle - open - 1);
                    i = close;
                    continue;
                }
            }
            if (ch is '*' or '~') continue;
            if (ch == '_' && (i == 0 || i + 1 >= inline.Length || !char.IsLetterOrDigit(inline[i - 1]) || !char.IsLetterOrDigit(inline[i + 1]))) continue;
            result.Append(ch);
        }
        return result.ToString();
    }

    public static bool IsEscapable(char value) => value < 128 && (char.IsPunctuation(value) || char.IsSymbol(value));
}
