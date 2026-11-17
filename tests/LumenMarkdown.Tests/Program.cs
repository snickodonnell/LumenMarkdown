using System.Diagnostics;
using System.Text;
using LumenMarkdown.Core;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var inCode = false;
Check(MarkdownClassifier.Classify("# Heading", ref inCode) == MarkdownKind.Heading1, "h1 classification");
Check(MarkdownClassifier.Classify("### Heading", ref inCode) == MarkdownKind.Heading3, "h3 classification");
Check(MarkdownClassifier.Classify("- [x] done", ref inCode) == MarkdownKind.Task, "task classification");
Check(MarkdownClassifier.Classify("42. item", ref inCode) == MarkdownKind.Numbered, "numbered classification");
Check(MarkdownClassifier.Classify("---", ref inCode) == MarkdownKind.Rule, "rule classification");
Check(MarkdownClassifier.Classify("```csharp", ref inCode) == MarkdownKind.CodeFence && inCode, "open fence");
Check(MarkdownClassifier.Classify("# not a heading", ref inCode) == MarkdownKind.Code, "fenced code");
Check(MarkdownClassifier.Classify("    indented inside fence", ref inCode) == MarkdownKind.Code, "fenced code keeps indentation kind");
Check(MarkdownClassifier.Classify("```", ref inCode) == MarkdownKind.CodeFenceEnd && !inCode, "close fence");
Check(MarkdownClassifier.Classify("    indented code", ref inCode) == MarkdownKind.IndentedCode, "indented code");
Check(MarkdownClassifier.Classify("\tindented by tab", ref inCode) == MarkdownKind.IndentedCode, "tab-indented code");
Check(MarkdownClassifier.Classify("    - nested bullet", ref inCode) == MarkdownKind.Bullet, "nested bullet");
Check(MarkdownClassifier.Classify("    2. nested number", ref inCode) == MarkdownKind.Numbered, "nested numbered");
Check(MarkdownClassifier.Classify("      ", ref inCode) == MarkdownKind.Blank, "whitespace-only line is blank");
Check(MarkdownClassifier.Classify("Name | Value", ref inCode) == MarkdownKind.Table, "table without outer pipes");
Check(MarkdownClassifier.Classify("| :--- | ---: |", ref inCode) == MarkdownKind.TableDelimiter, "table delimiter classification");
Check(MarkdownTableParser.ParseRow("| one \\| two | `a|b` |").SequenceEqual(["one | two", "`a|b`"]), "table escaped and code pipes");
Check(MarkdownTableParser.GetAlignments("| :--- | ---: | :---: |").SequenceEqual([
    MarkdownColumnAlignment.Left, MarkdownColumnAlignment.Right, MarkdownColumnAlignment.Center]), "table alignments");
Check(MarkdownNavigation.HeadingText(new MarkdownLine(MarkdownKind.Heading2, "## Fast reader ##")) == "Fast reader", "heading display text");
Check(MarkdownNavigation.HeadingText(new MarkdownLine(MarkdownKind.Heading2, "## Learn C#")) == "Learn C#", "heading keeps trailing hash in text");
Check(MarkdownTableLayout.ToPlainText("**Bold** [link](https://x.y) `code` snake_case \\| ~~old~~") == "Bold link code snake_case | old",
    "table cell plain text");
Check(MarkdownTableLayout.ResolveColumnWidths([50, 80], [30, 40], 500).SequenceEqual([50d, 80d]), "compact table keeps natural widths");
var shared = MarkdownTableLayout.ResolveColumnWidths([100, 900], [60, 100], 500);
Check(shared.Sum() <= 500 && shared[0] >= 60 && shared[1] > shared[0], "wide table shares available space");
Check(MarkdownTableLayout.ResolveColumnWidths([400, 400], [300, 300], 500).SequenceEqual([300d, 300d]), "overflowing table keeps minimum widths");
Check(MarkdownNavigation.HeadingSlug("Fast & Friendly Reader!") == "fast-friendly-reader", "heading anchor slug");
Check(MarkdownNavigation.HeadingSlug("Café déjà vu") == "café-déjà-vu", "unicode heading anchor slug");

var temp = Path.Combine(Path.GetTempPath(), $"lumen-{Guid.NewGuid():N}.md");
var tempCopy = temp + ".copy.md";
var tableTemp = temp + ".table.md";
var rawTextTemp = temp + ".raw.md";
try
{
    const string rawMarkdown = "# Raw title\r\n\r\n| **Name** | [Link](https://example.com) |\r\n| --- | --- |\r\n| `code` | value |\r\n";
    await File.WriteAllTextAsync(rawTextTemp, rawMarkdown, new UTF8Encoding(false));
    Check(await MarkdownFileOperations.ReadAllTextAsync(rawTextTemp) == rawMarkdown, "raw Markdown clipboard source fidelity");

    await File.WriteAllTextAsync(tableTemp, "Name | Value\n--- | ---:\nAlpha | **42**\n\nThis | remains prose\n");
    using (var tableSource = await MarkdownDocumentSource.OpenAsync(tableTemp))
    {
        Check(tableSource.GetLine(0).Kind == MarkdownKind.TableHeader, "table header metadata");
        Check(tableSource.GetLine(1).Kind == MarkdownKind.TableDelimiter, "table delimiter metadata");
        Check(tableSource.GetLine(2).Kind == MarkdownKind.Table, "table body metadata");
        Check(tableSource.GetLine(4).Kind == MarkdownKind.Paragraph, "standalone pipe remains paragraph");
        await using var concurrentWriter = new FileStream(tableTemp, FileMode.Open, FileAccess.Write,
            FileShare.None);
        Check(concurrentWriter.CanWrite, "open document permits exclusive writer");
        await concurrentWriter.DisposeAsync();
        var replacement = tableTemp + ".replacement";
        await File.WriteAllTextAsync(replacement, "Updated | Value\n--- | ---\nYes | 2\n");
        File.Move(replacement, tableTemp, overwrite: true);
        Check(File.ReadAllText(tableTemp).StartsWith("Updated | Value"), "open document permits atomic replacement");
    }

    const int lineCount = 1_000_000;
    await using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false), 1024 * 1024))
        for (var i = 0; i < lineCount; i++) await writer.WriteLineAsync(i % 100 == 0 ? $"## Heading {i}" : $"Line {i}: **fast** markdown content for indexing.");

    var timer = Stopwatch.StartNew();
    using var source = await MarkdownDocumentSource.OpenAsync(temp);
    timer.Stop();
    Check(source.Count == lineCount, "large file line count");
    Check(source.GetLine(0).Kind == MarkdownKind.Heading2, "first indexed line");
    Check(source.GetLine(lineCount - 1).Text.StartsWith($"Line {lineCount - 1}"), "random line access");
    Check(timer.Elapsed < TimeSpan.FromSeconds(15), "large file indexing under 15s");
    Console.WriteLine($"Indexed {source.SizeBytes / 1_048_576d:0.0} MB / {source.Count:N0} lines in {timer.ElapsedMilliseconds:N0} ms");

    var scrollTimer = Stopwatch.StartNew();
    for (var i = 0; i < 250_000; i++) _ = source.GetLine(i);
    scrollTimer.Stop();
    Check(scrollTimer.Elapsed < TimeSpan.FromSeconds(10), "sustained scrolling read under 10s");
    Check(source.CachedLineCount <= 2048, "scroll cache line bound");
    Check(source.CachedTextCharacters <= 4 * 1024 * 1024, "scroll cache memory bound");
    Console.WriteLine($"Read 250,000 scrolling lines in {scrollTimer.ElapsedMilliseconds:N0} ms; cache {source.CachedLineCount:N0} lines / {source.CachedTextCharacters * 2 / 1_048_576d:0.00} MB UTF-16 text");

    await MarkdownFileOperations.CopyAsync(temp, tempCopy);
    Check(new FileInfo(tempCopy).Length == new FileInfo(temp).Length, "Save As byte length");
    using var copied = await MarkdownDocumentSource.OpenAsync(tempCopy);
    Check(copied.Count == lineCount, "Save As copied document line count");
}
finally
{
    try { File.Delete(temp); } catch { }
    try { File.Delete(tempCopy); } catch { }
    try { File.Delete(tableTemp); } catch { }
    try { File.Delete(rawTextTemp); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILED: " + string.Join(", ", failures));
    return 1;
}
Console.WriteLine("All Lumen Markdown tests passed.");
return 0;
