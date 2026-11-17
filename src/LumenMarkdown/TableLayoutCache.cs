using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LumenMarkdown.Core;

namespace LumenMarkdown;

/// <summary>Shared column metrics for one Markdown table, so every row renders with identical column widths.</summary>
internal sealed record TableLayout(int Start, int End, double[] Natural, double[] Minimum, MarkdownColumnAlignment[] Alignments)
{
    public int ColumnCount => Natural.Length;
}

/// <summary>Measures tables lazily (on first render of any row) and caches the result per document source.</summary>
internal sealed class TableLayoutCache(MarkdownDocumentSource source)
{
    public const double CellHorizontalPadding = 24;
    public const double CellFontSize = 14;
    private const int MaxMeasuredRows = 1500;
    private const double MaxMinimumColumnWidth = 220;
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SemiBold = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly List<TableLayout> _tables = [];

    public TableLayout Get(int lineIndex)
    {
        foreach (var table in _tables)
            if (lineIndex >= table.Start && lineIndex <= table.End) return table;

        var start = lineIndex;
        while (start > 0 && source.GetLine(start).Kind != MarkdownKind.TableHeader &&
               source.GetLine(start - 1).Kind is MarkdownKind.Table or MarkdownKind.TableDelimiter or MarkdownKind.TableHeader)
            start--;
        var end = start;
        while (end + 1 < source.Count && source.GetLine(end + 1).Kind is MarkdownKind.Table or MarkdownKind.TableDelimiter)
            end++;

        var layout = Measure(start, end);
        _tables.Add(layout);
        return layout;
    }

    private TableLayout Measure(int start, int end)
    {
        var natural = new List<double>();
        var minimum = new List<double>();
        MarkdownColumnAlignment[] alignments = [];
        var measuredRows = 0;
        for (var index = start; index <= end && measuredRows < MaxMeasuredRows; index++)
        {
            var line = source.GetLine(index);
            if (line.Kind == MarkdownKind.TableDelimiter)
            {
                alignments = MarkdownTableParser.GetAlignments(line.Text);
                continue;
            }

            measuredRows++;
            var typeface = line.Kind == MarkdownKind.TableHeader ? SemiBold : Regular;
            var cells = MarkdownTableParser.ParseRow(line.Text);
            // The header defines the column count; body rows are padded or truncated to match.
            var columns = natural.Count == 0 ? cells.Length : natural.Count;
            for (var i = 0; i < columns; i++)
            {
                if (i >= natural.Count) { natural.Add(0); minimum.Add(0); }
                if (i >= cells.Length) continue;
                var text = MarkdownTableLayout.ToPlainText(cells[i]);
                natural[i] = Math.Max(natural[i], MeasureWidth(text, typeface));
                var longestWord = 0d;
                foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    longestWord = Math.Max(longestWord, MeasureWidth(word, typeface));
                minimum[i] = Math.Max(minimum[i], Math.Min(longestWord, MaxMinimumColumnWidth));
            }
        }

        // Padding plus a little slack for code spans (monospace) and rounding.
        var slack = CellHorizontalPadding + 6;
        var naturalWidths = natural.Select(width => Math.Ceiling(Math.Max(width, 24) + slack)).ToArray();
        var minimumWidths = minimum.Select(width => Math.Ceiling(Math.Max(width, 24) + slack)).ToArray();
        return new TableLayout(start, end, naturalWidths, minimumWidths, alignments);
    }

    private static double MeasureWidth(string text, Typeface typeface)
    {
        if (text.Length == 0) return 0;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            CellFontSize, Brushes.Black, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
