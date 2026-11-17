namespace LumenMarkdown.Core;

public static class MarkdownFileOperations
{
    private const int BufferSize = 1024 * 1024;

    public static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(input, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: BufferSize, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
