namespace LumenMarkdown.Core;

public enum MarkdownKind : byte
{
    Blank,
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    Quote,
    Bullet,
    Numbered,
    Task,
    Rule,
    CodeFence,
    Code,
    Table,
    TableHeader,
    TableDelimiter,
    CodeFenceEnd,
    IndentedCode
}

public readonly record struct MarkdownLine(MarkdownKind Kind, string Text);
