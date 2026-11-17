using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LumenMarkdown.Core;

namespace LumenMarkdown;

public sealed class MarkdownBlockView : Border
{
    /// <summary>Maximum width of the reading column (text, headings, lists, quotes).</summary>
    private const double ReadingColumnWidth = 920;
    /// <summary>Left/right margin of the block border; block padding adds the rest of the 34px content gutter.</summary>
    private const double BlockMargin = 12;
    private const double ContentGutter = 34;
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    private DocumentLineItem? _item;
    private DocumentTab? _owner;

    public MarkdownBlockView()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        DataContextChanged += (_, args) =>
        {
            _item = args.NewValue as DocumentLineItem;
            Subscribe(_item?.Owner);
            if (_item is not null) Render(_item);
        };
        Unloaded += (_, _) => Subscribe(null);
        Loaded += (_, _) => Subscribe(_item?.Owner);
    }

    private void Subscribe(DocumentTab? owner)
    {
        if (ReferenceEquals(owner, _owner)) return;
        if (_owner is not null) _owner.PropertyChanged -= Owner_PropertyChanged;
        _owner = owner;
        if (_owner is not null) _owner.PropertyChanged += Owner_PropertyChanged;
    }

    private void Owner_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_item is null) return;
        if (e.PropertyName == nameof(DocumentTab.SearchQuery)) Render(_item);
        else if (e.PropertyName == nameof(DocumentTab.ViewportWidth))
        {
            if (_item.Line.Kind is MarkdownKind.Table or MarkdownKind.TableHeader) Render(_item);
            else ApplyColumnWidth(_item);
        }
    }

    private static bool IsFullWidth(MarkdownKind kind) => kind is MarkdownKind.Code or MarkdownKind.IndentedCode
        or MarkdownKind.CodeFence or MarkdownKind.CodeFenceEnd or MarkdownKind.Table or MarkdownKind.TableHeader
        or MarkdownKind.TableDelimiter;

    /// <summary>
    /// Text blocks get an explicit width so they wrap to the window and line up on the same left edge
    /// regardless of how wide other content (tables, long code lines) makes the scrollable area.
    /// </summary>
    private void ApplyColumnWidth(DocumentLineItem item)
    {
        if (IsFullWidth(item.Line.Kind)) return;
        var viewport = item.Owner.ViewportWidth;
        if (viewport > 0)
        {
            MaxWidth = double.PositiveInfinity;
            Width = Math.Clamp(viewport - BlockMargin * 2, 160, ReadingColumnWidth);
        }
        else
        {
            Width = double.NaN;
            MaxWidth = ReadingColumnWidth;
        }
    }

    private void Render(DocumentLineItem item)
    {
        var line = item.Line;
        Width = double.NaN;
        MaxWidth = double.PositiveInfinity;
        HorizontalAlignment = HorizontalAlignment.Left;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(0);
        Padding = new Thickness(ContentGutter - BlockMargin, 2, ContentGutter - BlockMargin, 2);
        Margin = new Thickness(BlockMargin, 0, BlockMargin, 0);
        ApplyColumnWidth(item);
        var highlighted = IsSearchHit(item);
        if (highlighted)
        {
            BorderThickness = new Thickness(2, 0, 0, 0);
            SetResourceReference(BorderBrushProperty, "AccentBrush");
            SetResourceReference(BackgroundProperty, "HoverBrush");
        }

        switch (line.Kind)
        {
            case MarkdownKind.Blank:
                Child = new Border { Height = 10 };
                return;
            case MarkdownKind.Rule:
                var rule = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
                rule.SetResourceReference(BackgroundProperty, "SubtleBrush");
                Child = rule;
                return;
            case MarkdownKind.TableDelimiter:
                Padding = new Thickness(0);
                Margin = new Thickness(0);
                BorderThickness = new Thickness(0);
                Background = Brushes.Transparent;
                Child = null;
                return;
            case MarkdownKind.Table or MarkdownKind.TableHeader:
                RenderTable(item, highlighted);
                return;
            case MarkdownKind.Code or MarkdownKind.IndentedCode or MarkdownKind.CodeFence or MarkdownKind.CodeFenceEnd:
                RenderCode(line, highlighted);
                return;
        }

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 24, FontSize = 15 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var content = Normalize(line);
        var listIndent = Math.Min(LeadingColumns(line.Text), 24) * 6;

        switch (line.Kind)
        {
            case >= MarkdownKind.Heading1 and <= MarkdownKind.Heading6:
                var level = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
                text.FontSize = level switch { 1 => 30, 2 => 25, 3 => 21, 4 => 18, _ => 16 };
                text.FontWeight = FontWeights.SemiBold;
                text.LineHeight = text.FontSize + 9;
                Margin = new Thickness(BlockMargin, level <= 2 ? 18 : 10, BlockMargin, 3);
                break;
            case MarkdownKind.Quote:
                BorderThickness = new Thickness(3, 0, 0, 0);
                SetResourceReference(BorderBrushProperty, "AccentBrush");
                Padding = new Thickness(ContentGutter - BlockMargin - 3, 5, 22, 5);
                text.FontStyle = FontStyles.Italic;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                break;
            case MarkdownKind.Bullet:
                content = (listIndent > 0 ? "◦  " : "•  ") + content;
                Padding = new Thickness(38 + listIndent, 2, 22, 2);
                break;
            case MarkdownKind.Numbered:
                Padding = new Thickness(38 + listIndent, 2, 22, 2);
                break;
            case MarkdownKind.Task:
                var completed = IsCompletedTask(line.Text);
                content = (completed ? "☑  " : "☐  ") + content;
                Padding = new Thickness(38 + listIndent, 2, 22, 2);
                if (completed) { text.TextDecorations = TextDecorations.Strikethrough; text.Opacity = 0.65; }
                break;
        }

        InlineFormatter.Apply(text, content, item.Owner.RequestLink);
        Child = text;
    }

    private void RenderCode(MarkdownLine line, bool highlighted)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        if (!highlighted) SetResourceReference(BackgroundProperty, "CodeBrush");
        Margin = new Thickness(ContentGutter, 0, ContentGutter, 0);
        var text = new TextBlock { FontFamily = MonoFont, TextWrapping = TextWrapping.NoWrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        switch (line.Kind)
        {
            case MarkdownKind.CodeFence:
                Padding = new Thickness(16, 5, 16, 3);
                Margin = new Thickness(ContentGutter, 7, ContentGutter, 0);
                CornerRadius = new CornerRadius(5, 5, 0, 0);
                text.FontSize = 11;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                var language = line.Text.Trim().TrimStart('`', '~').Trim();
                text.Text = language.Length == 0 ? "code" : language;
                break;
            case MarkdownKind.CodeFenceEnd:
                // The closing fence just finishes the block with a little breathing room.
                Padding = new Thickness(0);
                Margin = new Thickness(ContentGutter, 0, ContentGutter, 7);
                CornerRadius = new CornerRadius(0, 0, 5, 5);
                Child = new Border { Height = 8 };
                return;
            default:
                Padding = new Thickness(16, 0, 16, 0);
                text.FontSize = 13;
                text.LineHeight = 20;
                text.Text = line.Kind == MarkdownKind.IndentedCode ? StripCodeIndent(line.Text) : ExpandTabs(line.Text);
                break;
        }
        Child = text;
    }

    private void RenderTable(DocumentLineItem item, bool highlighted)
    {
        var line = item.Line;
        var layout = item.Owner.GetTableLayout(item.Index);
        var header = line.Kind == MarkdownKind.TableHeader;
        var cells = MarkdownTableParser.ParseRow(line.Text);
        var columnCount = layout?.ColumnCount ?? cells.Length;
        if (columnCount == 0) columnCount = 1;

        var available = item.Owner.ViewportWidth > 0 ? item.Owner.ViewportWidth - ContentGutter * 2 - 2 : 900;
        var widths = layout is null
            ? Enumerable.Repeat(Math.Max(120, available / columnCount), columnCount).ToArray()
            : MarkdownTableLayout.ResolveColumnWidths(layout.Natural, layout.Minimum, available);

        var grid = new Grid();
        foreach (var width in widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        for (var i = 0; i < columnCount; i++)
        {
            var alignment = layout is not null && i < layout.Alignments.Length ? layout.Alignments[i] : MarkdownColumnAlignment.Left;
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = TableLayoutCache.CellFontSize,
                LineHeight = 21,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Top,
                TextAlignment = alignment switch
                {
                    MarkdownColumnAlignment.Center => TextAlignment.Center,
                    MarkdownColumnAlignment.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                }
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            if (i < cells.Length) InlineFormatter.Apply(text, cells[i], item.Owner.RequestLink);

            var cell = new Border
            {
                Padding = new Thickness(TableLayoutCache.CellHorizontalPadding / 2, header ? 8 : 7, TableLayoutCache.CellHorizontalPadding / 2, header ? 8 : 7),
                BorderThickness = i < columnCount - 1 ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = text
            };
            cell.SetResourceReference(BorderBrushProperty, "SubtleBrush");
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        var last = layout is null || item.Index >= layout.End;
        var stripe = layout is not null && (item.Index - layout.Start) % 2 == 1;
        Padding = new Thickness(0);
        Margin = new Thickness(ContentGutter, 0, ContentGutter, last ? 6 : 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        BorderThickness = header ? new Thickness(1) : new Thickness(1, 0, 1, 1);
        CornerRadius = new CornerRadius(header ? 5 : 0, header ? 5 : 0, last ? 5 : 0, last ? 5 : 0);
        SetResourceReference(BorderBrushProperty, "SubtleBrush");
        SetResourceReference(BackgroundProperty, highlighted ? "HoverBrush" : header ? "CodeBrush" : stripe ? "PageBrush" : "SurfaceBrush");
        Child = grid;
    }

    private static bool IsSearchHit(DocumentLineItem item) =>
        !string.IsNullOrWhiteSpace(item.Owner.SearchQuery) &&
        item.Line.Text.Contains(item.Owner.SearchQuery, StringComparison.OrdinalIgnoreCase);

    private static int LeadingColumns(string text)
    {
        var columns = 0;
        foreach (var ch in text)
        {
            if (ch == ' ') columns++;
            else if (ch == '\t') columns += 4 - columns % 4;
            else break;
        }
        return columns;
    }

    private static string StripCodeIndent(string text)
    {
        var expanded = ExpandTabs(text);
        var remove = 0;
        while (remove < 4 && remove < expanded.Length && expanded[remove] == ' ') remove++;
        return expanded[remove..];
    }

    private static string ExpandTabs(string text)
    {
        if (!text.Contains('\t')) return text;
        var result = new System.Text.StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            if (ch == '\t') result.Append(' ', 4 - result.Length % 4);
            else result.Append(ch);
        }
        return result.ToString();
    }

    private static string Normalize(MarkdownLine line)
    {
        var text = line.Text;
        if (line.Kind is >= MarkdownKind.Heading1 and <= MarkdownKind.Heading6) return MarkdownNavigation.HeadingText(line);
        var trimmed = text.TrimStart();
        return line.Kind switch
        {
            MarkdownKind.Quote => trimmed.TrimStart('>', ' '),
            MarkdownKind.Bullet => trimmed.Length > 2 ? trimmed[2..] : string.Empty,
            MarkdownKind.Task => trimmed.Length > 6 ? trimmed[6..] : string.Empty,
            _ => trimmed
        };
    }

    private static bool IsCompletedTask(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 4 && trimmed[3] is 'x' or 'X';
    }
}

internal static class InlineFormatter
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    public static void Apply(TextBlock target, string input, Action<string>? navigate = null)
    {
        target.Inlines.Clear();
        var plain = new System.Text.StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            target.Inlines.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        var i = 0;
        while (i < input.Length)
        {
            var ch = input[i];
            if (ch == '\\' && i + 1 < input.Length && MarkdownTableLayout.IsEscapable(input[i + 1]))
            {
                plain.Append(input[i + 1]);
                i += 2; continue;
            }
            if ((TryEmphasis(input, i, "**", out var boldEnd) || TryEmphasis(input, i, "__", out boldEnd)))
            {
                Flush();
                target.Inlines.Add(new Bold(new Run(input[(i + 2)..boldEnd])));
                i = boldEnd + 2; continue;
            }
            if (TryEmphasis(input, i, "~~", out var strikeEnd))
            {
                Flush();
                target.Inlines.Add(new Run(input[(i + 2)..strikeEnd]) { TextDecorations = TextDecorations.Strikethrough });
                i = strikeEnd + 2; continue;
            }
            if (ch == '`' && input.IndexOf('`', i + 1) is var codeEnd && codeEnd > i)
            {
                Flush();
                var run = new Run(input[(i + 1)..codeEnd]) { FontFamily = MonoFont, FontSize = 13 };
                run.SetResourceReference(TextElement.BackgroundProperty, "CodeBrush");
                target.Inlines.Add(run); i = codeEnd + 1; continue;
            }
            if (ch == '[' || (ch == '!' && i + 1 < input.Length && input[i + 1] == '['))
            {
                var open = ch == '!' ? i + 1 : i;
                var middle = input.IndexOf("](", open + 1, StringComparison.Ordinal);
                var close = middle > open ? input.IndexOf(')', middle + 2) : -1;
                if (close > middle && input.IndexOf(']', open + 1) == middle)
                {
                    Flush();
                    var uriText = input[(middle + 2)..close].Trim();
                    var space = uriText.IndexOf(' ');
                    if (space > 0) uriText = uriText[..space]; // drop an optional "title"
                    uriText = uriText.Trim('<', '>');
                    var label = input[(open + 1)..middle];
                    if (label.Length == 0) label = uriText;
                    var link = new Hyperlink(new Run(label)) { ToolTip = uriText };
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    link.Click += (_, _) => navigate?.Invoke(uriText);
                    target.Inlines.Add(link); i = close + 1; continue;
                }
            }
            if (ch == '<' && input.IndexOf('>', i + 1) is var autoEnd && autoEnd > i)
            {
                var uriText = input[(i + 1)..autoEnd];
                if (uriText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    uriText.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    uriText.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    var link = new Hyperlink(new Run(uriText));
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    link.Click += (_, _) => navigate?.Invoke(uriText);
                    target.Inlines.Add(link); i = autoEnd + 1; continue;
                }
            }
            if ((ch == '*' || ch == '_') && TryEmphasis(input, i, ch == '*' ? "*" : "_", out var italicEnd))
            {
                Flush();
                target.Inlines.Add(new Italic(new Run(input[(i + 1)..italicEnd])));
                i = italicEnd + 1; continue;
            }

            plain.Append(ch);
            i++;
        }
        Flush();
    }

    /// <summary>
    /// Finds a closing delimiter for emphasis. Delimiters must hug their content ("* not emphasis *"),
    /// and underscores never open or close inside a word, so snake_case_names stay intact.
    /// </summary>
    private static bool TryEmphasis(string text, int start, string delimiter, out int end)
    {
        end = -1;
        var length = delimiter.Length;
        if (!text.AsSpan(start).StartsWith(delimiter, StringComparison.Ordinal)) return false;
        var contentStart = start + length;
        if (contentStart >= text.Length || char.IsWhiteSpace(text[contentStart])) return false;
        // A single '*' or '_' must not be part of a doubled delimiter.
        if (length == 1 && text[contentStart] == delimiter[0]) return false;
        var underscore = delimiter[0] == '_';
        if (underscore && start > 0 && char.IsLetterOrDigit(text[start - 1])) return false;

        var search = contentStart + 1;
        while (search < text.Length)
        {
            var candidate = text.IndexOf(delimiter, search, StringComparison.Ordinal);
            if (candidate < 0) return false;
            var after = candidate + length;
            var valid = !char.IsWhiteSpace(text[candidate - 1]) &&
                        (!underscore || after >= text.Length || !char.IsLetterOrDigit(text[after])) &&
                        (length > 1 || after >= text.Length || text[after] != delimiter[0]);
            if (valid) { end = candidate; return true; }
            search = candidate + 1;
        }
        return false;
    }
}
