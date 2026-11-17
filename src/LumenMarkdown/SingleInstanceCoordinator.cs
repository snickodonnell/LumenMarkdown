using System.IO.Pipes;
using System.IO;
using System.Text;

namespace LumenMarkdown;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\LumenMarkdown.SingleInstance.v1";
    private const string PipeName = "LumenMarkdown.FileOpen.v1";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsPrimary = created;
    }

    public bool IsPrimary { get; }

    public void StartListening(Action<string[]> onFilesReceived)
    {
        if (!IsPrimary || _listener is not null) return;
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
                    var count = reader.ReadInt32();
                    if (count is < 0 or > 256) continue;
                    var paths = new string[count];
                    for (var i = 0; i < count; i++) paths[i] = reader.ReadString();
                    onFilesReceived(paths);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) when (!_stop.IsCancellationRequested) { }
            }
        });
    }

    public async Task SendToPrimaryAsync(string[] paths)
    {
        if (IsPrimary) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(1500).ConfigureAwait(false);
                using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
                writer.Write(paths.Length);
                foreach (var path in paths) writer.Write(path);
                writer.Flush();
                await client.FlushAsync().ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) when (attempt < 2) { await Task.Delay(150).ConfigureAwait(false); }
            catch (IOException) when (attempt < 2) { await Task.Delay(150).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
