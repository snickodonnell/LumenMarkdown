using System.Buffers;
using System.Text;

namespace LumenMarkdown.Core;

public abstract class MarkdownDocumentSource : IDisposable
{
    public abstract int Count { get; }
    public abstract long SizeBytes { get; }
    public virtual int CachedLineCount => 0;
    public virtual long CachedTextCharacters => 0;
    public abstract MarkdownLine GetLine(int index);
    public abstract void Dispose();

    public static async Task<MarkdownDocumentSource> OpenAsync(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Markdown file not found.", path);

        await using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bom = new byte[4];
        var read = await probe.ReadAsync(bom, cancellationToken).ConfigureAwait(false);

        if (read >= 2 && ((bom[0] == 0xFF && bom[1] == 0xFE) || (bom[0] == 0xFE && bom[1] == 0xFF)))
            return await MemoryMarkdownSource.OpenAsync(path, info.Length, progress, cancellationToken).ConfigureAwait(false);

        var bomLength = read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF ? 3 : 0;
        return await Utf8FileMarkdownSource.OpenAsync(path, info.Length, bomLength, progress, cancellationToken).ConfigureAwait(false);
    }
}

internal readonly record struct LineDescriptor(long Offset, int Length, MarkdownKind Kind);

internal sealed class Utf8FileMarkdownSource : MarkdownDocumentSource
{
    private const int ReadAheadLines = 128;
    private const int MaxReadAheadBytes = 1024 * 1024;
    private const int MaxCachedLines = 2048;
    private const int MaxCachedCharacters = 4 * 1024 * 1024;
    private readonly string _path;
    private readonly LineDescriptor[] _lines;
    private readonly Dictionary<int, string> _lineCache = [];
    private readonly Queue<int> _cacheOrder = [];
    private readonly object _cacheGate = new();
    private long _cachedCharacters;

    private Utf8FileMarkdownSource(string path, long size, LineDescriptor[] lines)
    {
        SizeBytes = size;
        _lines = lines;
        _path = path;
    }

    public override int Count => _lines.Length;
    public override long SizeBytes { get; }
    public override int CachedLineCount { get { lock (_cacheGate) return _lineCache.Count; } }
    public override long CachedTextCharacters { get { lock (_cacheGate) return _cachedCharacters; } }

    public override MarkdownLine GetLine(int index)
    {
        if ((uint)index >= (uint)_lines.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var descriptor = _lines[index];
        if (descriptor.Length == 0) return new MarkdownLine(descriptor.Kind, string.Empty);

        lock (_cacheGate)
        {
            if (_lineCache.TryGetValue(index, out var cached))
                return new MarkdownLine(descriptor.Kind, cached);

            return new MarkdownLine(descriptor.Kind, LoadBatch(index));
        }
    }

    private string LoadBatch(int requestedIndex)
    {
        var first = _lines[requestedIndex];
        var endIndex = requestedIndex + 1;
        while (endIndex < _lines.Length && endIndex - requestedIndex < ReadAheadLines)
        {
            var candidate = _lines[endIndex];
            var candidateEnd = candidate.Offset + candidate.Length;
            if (candidateEnd - first.Offset > MaxReadAheadBytes) break;
            endIndex++;
        }

        var last = _lines[endIndex - 1];
        var byteCount = checked((int)(last.Offset + last.Length - first.Offset));

        var rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
            var total = 0;
            while (total < byteCount)
            {
                var read = RandomAccess.Read(stream.SafeFileHandle,
                    rented.AsSpan(total, byteCount - total), first.Offset + total);
                if (read == 0) break;
                total += read;
            }

            var requestedText = string.Empty;
            for (var lineIndex = requestedIndex; lineIndex < endIndex; lineIndex++)
            {
                var current = _lines[lineIndex];
                var relativeOffset = checked((int)(current.Offset - first.Offset));
                var available = Math.Max(0, total - relativeOffset);
                var length = Math.Min(current.Length, available);
                var text = length == 0 ? string.Empty : Encoding.UTF8.GetString(rented, relativeOffset, length);
                AddToCache(lineIndex, text);
                if (lineIndex == requestedIndex) requestedText = text;
            }
            return requestedText;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void AddToCache(int lineIndex, string text)
    {
        if (_lineCache.ContainsKey(lineIndex)) return;
        _lineCache.Add(lineIndex, text);
        _cacheOrder.Enqueue(lineIndex);
        _cachedCharacters += text.Length;

        while ((_lineCache.Count > MaxCachedLines || _cachedCharacters > MaxCachedCharacters) && _cacheOrder.Count > 1)
        {
            var oldest = _cacheOrder.Dequeue();
            if (!_lineCache.Remove(oldest, out var removed)) continue;
            _cachedCharacters -= removed.Length;
        }
    }

    public override void Dispose()
    {
        lock (_cacheGate)
        {
            _lineCache.Clear();
            _cacheOrder.Clear();
            _cachedCharacters = 0;
        }
    }

    public static async Task<Utf8FileMarkdownSource> OpenAsync(
        string path, long size, int bomLength, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var lines = new List<LineDescriptor>(EstimateLineCapacity(size));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = bomLength;

        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        var prefix = new byte[256];
        var prefixLength = 0;
        var lineStart = (long)bomLength;
        var absolute = (long)bomLength;
        var lastByte = (byte)0;
        var hasBytes = false;
        var inCodeFence = false;
        var inTable = false;
        var nextReport = 8L * 1024 * 1024;

        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                for (var i = 0; i < read; i++, absolute++)
                {
                    var value = buffer[i];
                    if (value == (byte)'\n')
                    {
                        var rawLength = absolute - lineStart;
                        var length = checked((int)(rawLength - (lastByte == (byte)'\r' ? 1 : 0)));
                        var kind = MarkdownClassifier.Classify(prefix.AsSpan(0, prefixLength), ref inCodeFence);
                        AddDescriptor(lines, new LineDescriptor(lineStart, length, kind), ref inTable);
                        lineStart = absolute + 1;
                        prefixLength = 0;
                        hasBytes = false;
                        lastByte = 0;
                    }
                    else
                    {
                        if (prefixLength < prefix.Length) prefix[prefixLength++] = value;
                        lastByte = value;
                        hasBytes = true;
                    }
                }

                if (absolute >= nextReport)
                {
                    progress?.Report(size == 0 ? 1 : Math.Min(1, (double)absolute / size));
                    nextReport = absolute + 8L * 1024 * 1024;
                }
            }

            if (hasBytes || lineStart < size)
            {
                var rawLength = absolute - lineStart;
                var length = checked((int)(rawLength - (lastByte == (byte)'\r' ? 1 : 0)));
                var kind = MarkdownClassifier.Classify(prefix.AsSpan(0, prefixLength), ref inCodeFence);
                AddDescriptor(lines, new LineDescriptor(lineStart, length, kind), ref inTable);
            }
            else if (size == bomLength)
            {
                lines.Add(new LineDescriptor(bomLength, 0, MarkdownKind.Blank));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        FinalizePendingTable(lines, inTable);
        progress?.Report(1);
        return new Utf8FileMarkdownSource(path, size, lines.ToArray());
    }

    private static int EstimateLineCapacity(long size) => (int)Math.Clamp(size / 64, 16, 2_000_000);

    private static void AddDescriptor(List<LineDescriptor> lines, LineDescriptor descriptor, ref bool inTable)
    {
        if (descriptor.Kind == MarkdownKind.TableDelimiter)
        {
            if (!inTable && lines.Count > 0 && lines[^1].Kind == MarkdownKind.Table)
            {
                lines[^1] = lines[^1] with { Kind = MarkdownKind.TableHeader };
                inTable = true;
            }
            else descriptor = descriptor with { Kind = MarkdownKind.Paragraph };
        }
        else if (descriptor.Kind != MarkdownKind.Table)
        {
            FinalizePendingTable(lines, inTable);
            inTable = false;
        }
        else if (!inTable)
        {
            FinalizePendingTable(lines, false);
        }
        lines.Add(descriptor);
    }

    private static void FinalizePendingTable(List<LineDescriptor> lines, bool inTable)
    {
        if (!inTable && lines.Count > 0 && lines[^1].Kind == MarkdownKind.Table)
            lines[^1] = lines[^1] with { Kind = MarkdownKind.Paragraph };
    }
}

internal sealed class MemoryMarkdownSource : MarkdownDocumentSource
{
    private readonly MarkdownLine[] _lines;
    private MemoryMarkdownSource(long size, MarkdownLine[] lines) { SizeBytes = size; _lines = lines; }
    public override int Count => _lines.Length;
    public override long SizeBytes { get; }
    public override MarkdownLine GetLine(int index) => _lines[index];
    public override void Dispose() { }

    public static async Task<MemoryMarkdownSource> OpenAsync(
        string path, long size, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var encoding = await DetectEncodingAsync(path, cancellationToken).ConfigureAwait(false);
        var lines = new List<MarkdownLine>();
        var inCode = false;
        var inTable = false;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(input, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024 * 1024);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var kind = MarkdownClassifier.Classify(line, ref inCode);
            if (kind == MarkdownKind.TableDelimiter)
            {
                if (!inTable && lines.Count > 0 && lines[^1].Kind == MarkdownKind.Table)
                {
                    lines[^1] = lines[^1] with { Kind = MarkdownKind.TableHeader };
                    inTable = true;
                }
                else kind = MarkdownKind.Paragraph;
            }
            else if (kind != MarkdownKind.Table)
            {
                FinalizePendingTable(lines, inTable);
                inTable = false;
            }
            else if (!inTable)
            {
                FinalizePendingTable(lines, false);
            }
            lines.Add(new MarkdownLine(kind, line));
            if ((lines.Count & 0x3FFF) == 0) progress?.Report(size == 0 ? 1 : (double)reader.BaseStream.Position / size);
        }
        FinalizePendingTable(lines, inTable);
        if (lines.Count == 0) lines.Add(new MarkdownLine(MarkdownKind.Blank, string.Empty));
        progress?.Report(1);
        return new MemoryMarkdownSource(size, lines.ToArray());
    }

    private static async Task<Encoding> DetectEncodingAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bom = new byte[2];
        var read = await stream.ReadAsync(bom, cancellationToken).ConfigureAwait(false);
        return read == 2 && bom[0] == 0xFE && bom[1] == 0xFF ? Encoding.BigEndianUnicode : Encoding.Unicode;
    }

    private static void FinalizePendingTable(List<MarkdownLine> lines, bool inTable)
    {
        if (!inTable && lines.Count > 0 && lines[^1].Kind == MarkdownKind.Table)
            lines[^1] = lines[^1] with { Kind = MarkdownKind.Paragraph };
    }
}
