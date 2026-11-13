using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SecureChat.Core.Configuration;
using SecureChat.Core.Crypto;
using SecureChat.Core.Network;
using SecureChat.Core.Protocol;

namespace SecureChat.Core.Files;

public sealed class FswCoordinator : IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly FileTransferService _files;
    private readonly FswSettings _settings;
    private readonly string _watchFolder;
    private readonly Func<CipherAlgorithm> _algorithm;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true});
    private readonly Task _loop;

    private FileWatcherService? _watcher; // Samo iz petlje
    private string _peerName = ""; // Samo iz petlje

    private int _localState;
    private int _remoteState;
    private volatile bool _autoAccept;
    private int _disposed;

    public event Action<FswEvent>? Activity;
    public event Action<FswApprovalRequest>? ApprovalRequested;

    public string WatchFolder => _watchFolder;

    public FswLocalState LocalState
    {
        get => (FswLocalState)Volatile.Read(ref _localState);
        private set => Volatile.Write(ref _localState, (int)value);
    }

    public FswRemoteState RemoteState
    {
        get => (FswRemoteState)Volatile.Read(ref _remoteState);
        private set => Volatile.Write(ref _remoteState, (int)value);
    }

    public bool AutoAccept
    {
        get => _autoAccept;
        set => _autoAccept = value;
    }

    public FswCoordinator(Connection connection, FileTransferService files, FswSettings settings, string watchFolder, Func<CipherAlgorithm> algorithm)
    {
        _connection = connection;
        _files = files;
        _settings = settings;
        _watchFolder = watchFolder;
        _algorithm = algorithm;

        connection.MessageReceived += OnMessage;
        connection.FileReceived += OnFileReceived;
        connection.Disconnected += OnDisconnected;
        _loop = Task.Run(RunAsync);
    }

    // Role A seeks
    public Task RequestStartAsync() => Submit(async () =>
    {
        if(RemoteState is FswRemoteState.Requested or FswRemoteState.Active) return;
        await SendCommandAsync(FswCommands.Start);
        RemoteState = FswRemoteState.Requested;
        Raise(FswEventKind.RequestSent, "Zahtev za automatsko slanje je poslat. Ceka se na odgovor sa druge strane.");
    });

    public Task RequestStopAsync() => Submit(async ()=>
    {
        if(RemoteState is FswRemoteState.Idle or FswRemoteState.Declined)
        {
            RemoteState = FswRemoteState.Idle;
            return;
        }
        await SendCommandAsync(FswCommands.Stop);
        RemoteState = FswRemoteState.Idle; 
        Raise (FswEventKind.StopRequested, "Zahtev za prekid automatskog slanja je poslat.");
    });

    // Role B Follows
    public Task AcceptRequestAsync() => Submit(async () =>
    {
        if(LocalState != FswLocalState.AwaitingApproval) return;
        await StartWatchingAsync();
    });

    public Task DeclineRequestAsync() => Submit(async () =>
    {
        if(LocalState != FswLocalState.AwaitingApproval) return;
        LocalState = FswLocalState.Inactive;
        Raise(FswEventKind.LocalDeclined, $"Zahtev za autmatsko slanje ({_peerName}) je odbijen.");
        await SendCommandAsync(FswCommands.Declined);
    });

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _connection.MessageReceived -= OnMessage;
        _connection.FileReceived -= OnFileReceived;
        _connection.Disconnected -= OnDisconnected;
        _work.Writer.Complete();
        await _loop;
        await StopWatchingAsync();
    }

    public static void EnsureWatchFolderAllowed(string watchFolder, string receivedFolder)
    {
        if(WithSeparator(watchFolder).StartsWith(WithSeparator(receivedFolder), StringComparison.OrdinalIgnoreCase))
            throw new FileTransferException("Folder koji se prati ne sme biti folder za primljene fajlove niti unutar njega.");
    }

    private static string WithSeparator(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

    // Commands
    private async Task RunAsync()
    {
        await foreach(Func<Task> work in _work.Reader.ReadAllAsync())
        {
            await work();
        }
    }

    private Task Submit(Func<Task> work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _work.Writer.TryWrite(async () =>
        {
            try
            {
                await work();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        if(!queued) done.SetException(new ObjectDisposedException(nameof(FswCoordinator)));
        return done.Task;
    }
    private void SubmitFromEvent(Func<Task> work) => _ = ReportFailureAsync(Submit(work));
    private async Task ReportFailureAsync(Task task)
    {
        try { await task; }
        catch (ObjectDisposedException) {}
        catch (Exception ex) { Raise(FswEventKind.Error, $"Obrada kontrolne poruke nije uspela: {ex.Message}", Error:ex); }
    }

    // Connection Events
    private void OnMessage(ReceivedMessage message)
    {
        if(message.Header.ContentType != ContentType.Control) return;
        string peer = message.Header.Sender;
        Func<Task>? work = message.Text switch
        {
            FswCommands.Start => () => HandleStartRequestAsync(peer),
            FswCommands.Stop => () => HandleStopRequestAsync(peer),
            FswCommands.Accepted => HandleRemoteAcceptAsync,
            FswCommands.Declined => HandleRemoteDeclinedAsync,
            FswCommands.Stopped => HandleRemoteStoppedAsync,
            _ => null
        };

        if(work is null)
            Raise(FswEventKind.Error, "Primljena je nepoznata kontrolna poruka (ignored).");
        else
            SubmitFromEvent(work);
    }

    private void OnFileReceived(ReceivedFile file)
    {
        if(RemoteState != FswRemoteState.Requested) return;
        SubmitFromEvent(() =>
        {
            if(RemoteState != FswRemoteState.Requested) return Task.CompletedTask;
            RemoteState = FswRemoteState.Active;
            Raise(FswEventKind.RemoteActive, "Stigao je priv file: automatsko slanje je aktivno.");
            return Task.CompletedTask;
        });
    }

    private void OnDisconnected(DisconnectInfo info) => SubmitFromEvent( async() =>
    {
        bool wasInUse = LocalState != FswLocalState.Inactive || RemoteState != FswRemoteState.Idle;
        await StopWatchingAsync();
        LocalState = FswLocalState.Inactive;
        RemoteState = FswRemoteState.Idle;
        if(wasInUse) Raise(FswEventKind.ConnectionLost, "Veza je prekinuta. Autmatsko slanje zaustavljeno.");
    });

    // Role B com
    private async Task HandleStartRequestAsync(string peer)
    {
        switch(LocalState)
        {
            case FswLocalState.Active:
                await SendCommandAsync(FswCommands.Accepted);
                return;
            case FswLocalState.AwaitingApproval:
                return;
        }

        _peerName = peer;
        if(_autoAccept)
        {
            await StartWatchingAsync();
            return;
        }

        LocalState = FswLocalState.AwaitingApproval;
        Raise(FswEventKind.ApprovalRequested, $"{peer} trazi automatsko slanje file'ove iz folder '{_watchFolder}'. Ceka se odobrenje.");
        ApprovalRequested?.Invoke(new FswApprovalRequest(peer, _watchFolder));
    }

    private async Task HandleStopRequestAsync(string peer)
    {
        FswLocalState previous = LocalState;
        await StopWatchingAsync();
        LocalState = FswLocalState.Inactive;

        if(previous == FswLocalState.Active)
            Raise(FswEventKind.WatchingStopped, $"Automatsko slanje je zaustavljeno na zahtev ({peer}).");
        else if(previous == FswLocalState.AwaitingApproval)
            Raise(FswEventKind.ApprovalCancelled, $"{peer} je otkazao zahtev za automatsko slanje.");

        await SendCommandAsync(FswCommands.Stopped);
    }

    private async Task StartWatchingAsync()
    {
        FileWatcherService? watcher = null;
        try
        {
            EnsureWatchFolderAllowed(_watchFolder, _files.ReceivedFolder);
            watcher = new FileWatcherService(_watchFolder, _settings, path => _files.SendFileAsync(path, _algorithm()));
            watcher.Activity += OnWatcherActivity;
            watcher.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FileTransferException)
        {
            if(watcher is not null) await watcher.DisposeAsync();
            LocalState = FswLocalState.Inactive;
            Raise(FswEventKind.Error, $"Pracenje folder nije moguce: {ex.Message}", Error:ex);
            await SendCommandAsync(FswCommands.Declined);
            return;
        }

        _watcher = watcher;
        LocalState = FswLocalState.Active;
        Raise(FswEventKind.WatchingStarted, $"Automatsko slanje je aktivno (zahtev od {_peerName}), folder '{_watchFolder}'. ");
        await SendCommandAsync(FswCommands.Accepted);
    }
    private async Task StopWatchingAsync()
    {
        FileWatcherService? watcher = _watcher;
        _watcher = null;
        if(watcher is not null) await watcher.DisposeAsync();
    }
    private void OnWatcherActivity(WatcherActivity activity)
    {
        string name = Path.GetFileName(activity.FilePath);
        FswEvent evt = activity.Kind switch
        {
            WatcherActivityKind.Detected => new(FswEventKind.FileDetected, $"Detektovan file '{name}'.", activity.FilePath),
            WatcherActivityKind.Sent => new(FswEventKind.FileSent, $"File '{name}' je poslat.", activity.FilePath),
            _ => new (FswEventKind.FileFailed, $"Automatsko slanje '{name}' nije uspelo: {activity.Error?.Message}", activity.FilePath, activity.Error)
        };
        Activity?.Invoke(evt);
    }
    // Role A
    private Task HandleRemoteAcceptAsync()
    {
        if(RemoteState != FswRemoteState.Requested) return Task.CompletedTask;
        RemoteState = FswRemoteState.Active;
        Raise(FswEventKind.RemoteAccepted, "Druga strana je prihvatila zahtev za automatsko slanje.");
        return Task.CompletedTask;
    }
    private Task HandleRemoteDeclinedAsync()
    {
        if(RemoteState != FswRemoteState.Requested) return Task.CompletedTask;
        RemoteState = FswRemoteState.Declined;
        Raise(FswEventKind.RemoteDeclined, "Druga strana je odbila zahtev za automatsko slanje.");
        return Task.CompletedTask;
    }
    private Task HandleRemoteStoppedAsync()
    {
        RemoteState = FswRemoteState.Idle;
        Raise(FswEventKind.RemoteStopped, "Druga strana je zaustavila automatsko slanje.");
        return Task.CompletedTask;
    }
    // Util
    private Task SendCommandAsync(string command) =>
        _connection.SendTextAsync(ContentType.Control, command, _algorithm());
    private void Raise(FswEventKind kind, string message, string? filePath = null, Exception? Error = null) =>
        Activity?.Invoke(new FswEvent(kind, message, filePath, Error));
}