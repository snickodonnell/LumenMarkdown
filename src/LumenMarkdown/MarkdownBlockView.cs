using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LumenMarkdown.Core;

namespace LumenMarkdown;

public sealed class MarkdownBlockView : Border
{
    private DocumentLineItem? _item;

    public MarkdownBlockView()
    {
        MaxWidth = 920;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        DataContextChanged += (_, args) =>
        {
            if (_item is not null) _item.Owner.PropertyChanged -= Owner_PropertyChanged;
            _item = args.NewValue as DocumentLineItem;
            if (_item is null) return;
            _item.Owner.PropertyChanged += Owner_PropertyChanged;
            Render(_item);
        };
        Unloaded += (_, _) => { if (_item is not null) _item.Owner.PropertyChanged -= Owner_PropertyChanged; };
        Loaded += (_, _) => { if (_item is not null) { _item.Owner.PropertyChanged -= Owner_PropertyChanged; _item.Owner.PropertyChanged += Owner_PropertyChanged; } };
    }

    private void Owner_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_item is not null && e.PropertyName == nameof(DocumentTab.SearchQuery)) Render(_item);
    }

    private void Render(DocumentLineItem item)
    {
        var line = item.Line;
        MaxWidth = 920;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(0);
        Padding = new Thickness(22, 2, 22, 2);
        Margin = new Thickness(12, 0, 12, 0);
        if (!string.IsNullOrWhiteSpace(item.Owner.SearchQuery) &&
            line.Text.Contains(item.Owner.SearchQuery, StringComparison.OrdinalIgnoreCase))
        {
            BorderThickness = new Thickness(2, 0, 0, 0);
            SetResourceReference(BorderBrushProperty, "AccentBrush");
            SetResourceReference(BackgroundProperty, "HoverBrush");
        }

        if (line.Kind == MarkdownKind.Blank) { Child = new Border { Height = 10 }; return; }
        if (line.Kind == MarkdownKind.Rule)
        {
            var rule = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
            rule.SetResourceReference(BackgroundProperty, "SubtleBrush");
            Child = rule;
            return;
        }
        if (line.Kind == MarkdownKind.TableDelimiter)
        {
            Padding = new Thickness(0);
            Margin = new Thickness(0);
            Child = new Border { Height = 0 };
            return;
        }
        if (line.Kind is MarkdownKind.Table or MarkdownKind.TableHeader)
        {
            RenderTable(item);
            return;
        }

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 24, FontSize = 15 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var content = Normalize(line);

        switch (line.Kind)
        {
            case >= MarkdownKind.Heading1 and <= MarkdownKind.Heading6:
                var level = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
                text.FontSize = level switch { 1 => 30, 2 => 25, 3 => 21, 4 => 18, _ => 16 };
                text.FontWeight = FontWeights.SemiBold;
                text.LineHeight = text.FontSize + 9;
                Margin = new Thickness(12, level <= 2 ? 18 : 10, 12, 3);
                break;
            case MarkdownKind.Quote:
                BorderThickness = new Thickness(3, 0, 0, 0);
                SetResourceReference(BorderBrushProperty, "AccentBrush");
                Padding = new Thickness(16, 5, 22, 5);
                text.FontStyle = FontStyles.Italic;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                break;
            case MarkdownKind.Bullet:
                content = "•  " + content;
                Padding = new Thickness(38, 2, 22, 2);
                break;
            case MarkdownKind.Numbered:
                Padding = new Thickness(38, 2, 22, 2);
                break;
            case MarkdownKind.Task:
                var completed = IsCompletedTask(line.Text);
                content = (completed ? "☑  " : "☐  ") + content;
                Padding = new Thickness(38, 2, 22, 2);
                if (completed) { text.TextDecorations = TextDecorations.Strikethrough; text.Opacity = 0.65; }
                break;
            case MarkdownKind.Code:
                MaxWidth = double.PositiveInfinity;
                SetResourceReference(BackgroundProperty, "CodeBrush");
                Padding = new Thickness(16, 3, 16, 3);
                Margin = new Thickness(34, 0, 34, 0);
                text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                text.FontSize = 13;
                text.LineHeight = 20;
                text.TextWrapping = TextWrapping.NoWrap;
                text.Text = content;
                Child = text;
                return;
            case MarkdownKind.CodeFence:
                MaxWidth = double.PositiveInfinity;
                SetResourceReference(BackgroundProperty, "CodeBrush");
                Padding = new Thickness(16, 5, 16, 5);
                Margin = new Thickness(34, 7, 34, 0);
                text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                text.FontSize = 11;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                text.Text = content.Length == 0 ? "code" : content;
                Child = text;
                return;
        }

        InlineFormatter.Apply(text, content, item.Owner.RequestLink);
        Child = text;
    }

    private void RenderTable(DocumentLineItem item)
    {
        var line = item.Line;
        var cells = MarkdownTableParser.ParseRow(line.Text);
        var header = line.Kind == MarkdownKind.TableHeader;
        MaxWidth = double.PositiveInfinity;
        var grid = new Grid { MinWidth = Math.Max(700, cells.Length * 150) };
        for (var i = 0; i < cells.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < cells.Length; i++)
        {
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 21,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            InlineFormatter.Apply(text, cells[i], item.Owner.RequestLink);

            var cell = new Border
            {
                Padding = new Thickness(12, header ? 8 : 7, 12, header ? 8 : 7),
                BorderThickness = i < cells.Length - 1 ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = text
            };
            cell.SetResourceReference(BorderBrushProperty, "SubtleBrush");
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        Padding = new Thickness(0);
        Margin = new Thickness(34, 0, 34, 0);
        BorderThickness = header ? new Thickness(1) : new Thickness(1, 0, 1, 1);
        CornerRadius = header ? new CornerRadius(5, 5, 0, 0) : new CornerRadius(0);
        SetResourceReference(BorderBrushProperty, "SubtleBrush");
        SetResourceReference(BackgroundProperty, header ? "CodeBrush" : "SurfaceBrush");
        Child = grid;
    }

    private static string Normalize(MarkdownLine line)
    {
        var text = line.Text;
        if (line.Kind is >= MarkdownKind.Heading1 and <= MarkdownKind.Heading6)
        {
            var count = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
            return text.Length > count && text[count] == ' ' ? text[(count + 1)..] : text;
        }
        if (line.Kind == MarkdownKind.Quote) return text.TrimStart().TrimStart('>').TrimStart();
        if (line.Kind == MarkdownKind.Bullet) return text.TrimStart().Length > 2 ? text.TrimStart()[2..] : string.Empty;
        if (line.Kind == MarkdownKind.Task)
        {
            var trimmed = text.TrimStart();
            return trimmed.Length > 6 ? trimmed[6..] : string.Empty;
        }
        if (line.Kind == MarkdownKind.Code && text.StartsWith("    ")) return text[4..];
        if (line.Kind == MarkdownKind.CodeFence) return text.Trim().TrimStart('`', '~').Trim();
        return text;
    }

    private static bool IsCompletedTask(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 4 && trimmed[3] is 'x' or 'X';
    }
}

internal static class InlineFormatter
{
    public static void Apply(TextBlock target, string input, Action<string>? navigate = null)
    {
        target.Inlines.Clear();
        var i = 0;
        while (i < input.Length)
        {
            if (TryDelimited(input, i, "**", out var boldEnd) || TryDelimited(input, i, "__", out boldEnd))
            {
                target.Inlines.Add(new Bold(new Run(input[(i + 2)..boldEnd])));
                i = boldEnd + 2; continue;
            }
            if (input[i] == '`' && input.IndexOf('`', i + 1) is var codeEnd && codeEnd > i)
            {
                var run = new Run(input[(i + 1)..codeEnd]) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13 };
                run.SetResourceReference(TextElement.BackgroundProperty, "CodeBrush");
                target.Inlines.Add(run); i = codeEnd + 1; continue;
            }
            if (input[i] == '[' && input.IndexOf("](", i + 1, StringComparison.Ordinal) is var middle && middle > i)
            {
                var close = input.IndexOf(')', middle + 2);
                if (close > middle)
                {
                    var uriText = input[(middle + 2)..close];
                    var link = new Hyperlink(new Run(input[(i + 1)..middle]));
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    link.Click += (_, _) => navigate?.Invoke(uriText);
                    target.Inlines.Add(link); i = close + 1; continue;
                }
            }
            if ((input[i] == '*' || input[i] == '_') && input.IndexOf(input[i], i + 1) is var italicEnd && italicEnd > i + 1)
            {
                target.Inlines.Add(new Italic(new Run(input[(i + 1)..italicEnd])));
                i = italicEnd + 1; continue;
            }

            var next = i + 1;
            while (next < input.Length && input[next] is not ('*' or '_' or '`' or '[')) next++;
            target.Inlines.Add(new Run(input[i..next]));
            i = next;
        }
    }

    private static bool TryDelimited(string text, int start, string delimiter, out int end)
    {
        end = -1;
        if (!text.AsSpan(start).StartsWith(delimiter)) return false;
        end = text.IndexOf(delimiter, start + delimiter.Length, StringComparison.Ordinal);
        return end > start + delimiter.Length;
    }
}
