using System.Collections.Concurrent;
using System.Threading.Channels;
using SecureChat.Core.Configuration;

namespace SecureChat.Core.Files;

public enum WatcherActivityKind { Detected, Sent, Failed }
public sealed record WatcherActivity(WatcherActivityKind Kind, string FilePath, Exception? Error = null);
internal readonly record struct FileSignature(long Length, long LastWriteTicks);

public sealed class FileWatcherService: IAsyncDisposable
{
    private const FileAttributes IgnoredAttributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory;
    private const int WatcherBufferBytes = 64 * 1024;

    private readonly string _folder;
    private readonly FswSettings _settings;
    private readonly Func<string, Task> _send;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSignature> _sent = new(StringComparer.OrdinalIgnoreCase); // from loop
    private readonly CancellationTokenSource _cts = new();
    private Task _loop = Task.CompletedTask;
    private int _started;
    private int _disposed;

    public event Action<WatcherActivity>? Activity;

    public string Folder => _folder;

    public FileWatcherService(string folder, FswSettings settings, Func<string, Task> send)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = folder;
        _settings = settings;
        _send = send;

        _watcher = new FileSystemWatcher
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false, // Should it follow everything ?
            InternalBufferSize = WatcherBufferBytes
        };

        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        _watcher.Changed += (_, e) => Enqueue(e.FullPath);
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
        _watcher.Error += (_, e) => Report(WatcherActivityKind.Failed, _folder, e.GetException());
    }

    // Pravi folder ako ga nema
    public void Start()
    {
        if(Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("Pracenje je vec pokrenuto.");

        Directory.CreateDirectory(_folder);
        _watcher.Path = _folder;
        _loop = Task.Run(RunAsync);
        _watcher.EnableRaisingEvents = true;
    }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _watcher.Dispose();
        _queue.Writer.TryComplete();
        _cts.Cancel();
        await _loop;
        _cts.Dispose();
    }

    private void Enqueue(string path)
    {
        if(_pending.TryAdd(path, 0))
            _queue.Writer.TryWrite(path);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach(string path in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try { await ProcessAsync(path); }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested) { return; }
                catch (Exception ex) { Report(WatcherActivityKind.Failed, path, ex); }
            }
        }
        catch(OperationCanceledException) { /* stopped */}
    }

    private async Task ProcessAsync(string path)
    {
        bool released = false;
        void Release()
        {
            if(released) return;
            released = true;
            _pending.TryRemove(path, out _);
        }

        try
        {
            if(_settings.DebounceMilliseconds > 0)
                await Task.Delay(_settings.Debounce, _cts.Token);

            FileSignature? signature = await WaitUntilReadyAsync(path);
            Release(); // Ako se nesto promeni kod file'a, on ponovo u red
            if(signature is null) return;
            if(_sent.TryGetValue(path, out FileSignature last) && last == signature.Value) return;

            Report(WatcherActivityKind.Detected, path);
            await _send(path);
            _sent[path] = signature.Value;
            Report(WatcherActivityKind.Sent, path);
        }
        finally
        {
            Release();
        }
    }

    private async Task<FileSignature?> WaitUntilReadyAsync(string path)
    {
        DateTime deadline = DateTime.UtcNow + _settings.ReadyTimeout;
        while(true)
        {
            _cts.Token.ThrowIfCancellationRequested();
            try
            {
                if((File.GetAttributes(path) & IgnoredAttributes) != 0) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new FileSignature(stream.Length, File.GetLastWriteTimeUtc(path).Ticks);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if(DateTime.UtcNow >= deadline)
                    throw new FileTransferException(
                        $"File: '{Path.GetFileName(path)}' je i dalje zauzet posle {_settings.ReadyTimeoutSeconds}s i preskocen je.", ex);
                await Task.Delay(_settings.ReadyRetryInterval, _cts.Token);
            }
        }
    }

    private void Report(WatcherActivityKind kind, string path, Exception? error = null) 
        => Activity?.Invoke(new WatcherActivity(kind, path, error));
}