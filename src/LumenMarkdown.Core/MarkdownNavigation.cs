using System.Text;

namespace LumenMarkdown.Core;

public static class MarkdownNavigation
{
    public static string HeadingText(MarkdownLine line)
    {
        if (line.Kind is not (>= MarkdownKind.Heading1 and <= MarkdownKind.Heading6)) return line.Text;
        var level = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
        var text = line.Text.TrimStart();
        if (text.Length <= level || text[level] != ' ') return text;
        text = text[(level + 1)..].Trim();
        // An optional closing sequence of '#' only counts when separated by a space ("## C#" keeps its '#').
        var closing = text.TrimEnd('#');
        if (closing.Length == 0) return string.Empty;
        return closing.Length < text.Length && closing[^1] == ' ' ? closing.TrimEnd() : text;
    }

    public static string HeadingSlug(string text)
    {
        var result = new StringBuilder(text.Length);
        var pendingDash = false;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                if (pendingDash && result.Length > 0 && result[^1] != '-') result.Append('-');
                result.Append(character);
                pendingDash = false;
            }
            else if (char.IsWhiteSpace(character)) pendingDash = true;
        }
        return result.ToString().Trim('-');
    }
}
