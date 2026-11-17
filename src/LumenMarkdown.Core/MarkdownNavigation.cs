using System.Text;

namespace LumenMarkdown.Core;

public static class MarkdownNavigation
{
    public static string HeadingText(MarkdownLine line)
    {
        if (line.Kind is not (>= MarkdownKind.Heading1 and <= MarkdownKind.Heading6)) return line.Text;
        var level = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
        return line.Text.Length > level && line.Text[level] == ' '
            ? line.Text[(level + 1)..].Trim().TrimEnd('#').Trim()
            : line.Text;
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
