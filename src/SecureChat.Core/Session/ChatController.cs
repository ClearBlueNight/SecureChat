using System.Data;
using System.Net.Sockets;
using SecureChat.Core.Configuration;
using SecureChat.Core.Crypto;
using SecureChat.Core.Files;
using SecureChat.Core.Logging;
using SecureChat.Core.Network;
using SecureChat.Core.Storage;

namespace SecureChat.Core.Session;

public enum ControllerState { Idle, Listening, Connecting, Connected }

public sealed class ChatController : IAsyncDisposable
{
    private sealed record ActiveSession(Connection Connection, FileTransferService Files, FswCoordinator Fsw, SessionRecorder Recorder);
    
    private readonly object _gate = new();
    private readonly AppSettings _settings;
    private readonly EventMarshaller _ui;
    private readonly string _userName;
    private ConnectionListener? _listener;
    private CancellationTokenSource? _listenCts;
    private ActiveSession? _session;
    private Task _teardown = Task.CompletedTask;
    private int _state;
    private int _algorithm = (int)CipherAlgorithm.LeaCtr;
    private volatile bool _autoAccept;
    private volatile string _receivedFolder;
    private bool _disposed;

    // All events on UI context
    public event Action<ControllerState>? StateChanged;
    public event Action<HistoryEntry>? EntryRecorded;
    public event Action<string>? LogLine;
    public event Action<FileProgress>? Progress;
    public event Action<FswEvent>? FswActivity;
    public event Action<FswApprovalRequest>? ApprovalRequested;
    public event Action<DisconnectInfo>? Disconnected;
    // If failure/error/exception
    public event Action<string>? Notice;

    public string UserName => _userName;
    public string UserFolder { get; }
    public string WatchFolder { get; }
    public HistoryStore History { get; }
    public ActivityLogger Logger { get; }

    public ControllerState State => (ControllerState)Volatile.Read(ref _state);

    // Other sides name. If no msgs exchanged just empty string
    public string PeerName
    {
        get { lock(_gate) return _session?.Recorder.Peer ?? ""; }
    }

    public string RemoteEndpoint
    {
        get { lock(_gate) return _session?.Connection.RemoteEndpoint ?? ""; }
    }

    public CipherAlgorithm Algorithm
    {
        get => (CipherAlgorithm)Volatile.Read(ref _algorithm);
        set
        {
            if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _algorithm, (int)value);
        }
    }
    
    public bool AutoAccept
    {
        get => _autoAccept;
        set
        {
            lock(_gate)
            {
                _autoAccept = value;
                if(_session is not null) _session.Fsw.AutoAccept = value;
            }
        }
    }

    public string ReceivedFolder
    {
        get => _receivedFolder;
        set
        {
            string full = Path.GetFullPath(value);
            lock(_gate)
            {
                _receivedFolder = full;
                if(_session is not null) _session.Files.ReceivedFolder = full;
            }
        }
    }

    public ChatController(LoginResult login, AppSettings settings, SynchronizationContext? context = null)
    {
        _settings = settings;
        _ui = new EventMarshaller(context);
        _userName = login.UserName;
        UserFolder = login.UserFolder;
        History = login.History;
        _receivedFolder = AppPaths.ReceivedFilesFolder(UserFolder);
        WatchFolder = AppPaths.WatchFolder(UserFolder, settings.Fsw);
        
        Logger = new ActivityLogger(AppPaths.LogsFolder(UserFolder));
        Logger.Written += line => _ui.Post(() => LogLine?.Invoke(line));
        Logger.WriteFailed += ex => Notify($"Upis u evidenciju nije uspeo: {ex.Message}");

        int deleted = Logger.DeleteOldLogs(settings.Logging.RetentionDays);
        Logger.Info("SESSION", $"Aplikacija pokrenuta, korisnik={_userName}, stari fajlovi evidencije obrisani = {deleted}.");

        HistoryLoadReport report = History.LoadReport;
        if(report.HasProblems)
            Logger.Warning("HISTORY", $"Istorija ucitana uz probleme: ucitano = {report.Loaded}, osteceno = {report.Corrupted}, odbaceno bajtova = {report.TruncatedBytes}.");
    }

    public int StartListening(int port, string chatPassword)
    {
        byte[] key = KeyFrom(chatPassword);
        lock (_gate)
        {
            ThrowIfNotIdle();

            ConnectionListener listener;
            try { listener = ConnectionListener.Start(port); }
            catch (NetworkException ex)
            {
                Logger.Error("CONNECT", ex.Message);
                throw;
            }

            _listener = listener;
            _listenCts = new CancellationTokenSource();
            SetState(ControllerState.Listening);
            Logger.Info("CONNECT", $"Slusanje na protu {listener.Port}.");
            _ = AcceptAsync(listener, key, _listenCts.Token);
            return listener.Port;
        }
    }

    public void StopListening()
    {
        lock (_gate)
        {
            if(State != ControllerState.Listening) return;
            _listenCts?.Cancel();
            ReleaseListener();
            SetState(ControllerState.Idle);
            Logger.Info("CONNECT", "Slusanje je prekinuto.");
        }
    }

    public async Task ConnectAsync(string host, int port, string chatPassword, CancellationToken ct = default)
    {
        byte[] key = KeyFrom(chatPassword);
        if(!NetworkValidation.IsValidPort(port))
            throw new ArgumentException($"Port mora biti izmedju {NetworkValidation.MinPort} i {NetworkValidation.MaxPort}.", nameof(port));

        lock (_gate)
        {
            ThrowIfNotIdle();
            SetState(ControllerState.Connecting);
        }

        TcpClient client;
        try
        {
            client = await TcpConnector.ConnectAsync(host, port, _settings.Network.ConnectTimeout, ct);
        }
        catch(Exception ex)
        {
            lock (_gate) SetState(ControllerState.Idle);
            if (ex is NetworkException) Logger.Error("CONNECT", ex.Message);
            throw;
        }

        lock (_gate)
        {
            try
            {
                Attach(client, key);
            }
            catch
            {
                client.Dispose();
                SetState(ControllerState.Idle);
                throw;
            }
        }
    }

    public void Disconnect()
    {
        ActiveSession? session;
        lock (_gate) session = _session;
        session?.Connection.Dispose();
    }

    // Msgs and Files
    public async Task SendTextAsync(string text)
    {
        ActiveSession session = RequireSession();
        try { await session.Connection.SendTextAsync(Protocol.ContentType.Text, text, Algorithm); }
        catch (Exception ex)
        {
            Logger.Error("SEND", $"Slanje poruke nije uspelo: {ex.Message}");
            throw;
        }
    }

    public async Task SendFileAsync(string path)
    {
        ActiveSession session = RequireSession();
        try { await session.Files.SendFileAsync(path, Algorithm); }
        catch (Exception ex)
        {
            Logger.Error("SEND", $"Slanje file'a nije uspelo: {ex.Message}");
            throw;
        }
    }

    // FSW
    public Task RequestAutoSendAsync() => RequireSession().Fsw.RequestStartAsync();
    public Task StopAutoSendAsync() => RequireSession().Fsw.RequestStopAsync();
    public Task AcceptApprovalAsync() => RequireSession().Fsw.AcceptRequestAsync();
    public Task DeclineApprovalAsync() => RequireSession().Fsw.DeclineRequestAsync();

    // History & Records
    public void ClearHistory()
    {
        History.Clear();
        Logger.Info("HISTORY", "Istorija je obrisana na zahtev korisnika.");
    }

    public IReadOnlyList<string> ReadLogLines() => Logger.ReadLines(DateTime.Now);

    // Closing
    public async ValueTask DisposeAsync()
    {
        ActiveSession? session;
        lock (_gate)
        {
            if(_disposed) return;
            _disposed = true;
            if(State == ControllerState.Listening)
            {
                _listenCts?.Cancel();
                ReleaseListener();
            }
            session = _session;
            _session = null;
            Volatile.Write(ref _state, (int)ControllerState.Idle);
        }

        if(session is not null) await TearDownAsync(session);
        await _teardown;
        Logger.Info("SESSION", "Aplikacija zatvorena.");
        History.Dispose();
    }

    // Inner methods
    public async Task AcceptAsync(ConnectionListener listener, byte[] key, CancellationToken ct)
    {
        try
        {
            TcpClient client = await listener.AcceptAsync(ct);
            lock (_gate)
            {
                if(!ReferenceEquals(_listener, listener))
                {
                    client.Dispose();
                    return;
                }
                ReleaseListener();
                try { Attach(client, key); }
                catch
                {
                    client.Dispose();
                    SetState(ControllerState.Idle);
                    throw;
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { /* Slusanje je prekinuto */}
        catch (Exception ex)
        {
            lock (_gate)
            {
                if(ReferenceEquals(_listener, listener))
                {
                    ReleaseListener();
                    SetState(ControllerState.Idle);
                }
            }
            Logger.Error("CONNECT", $"Prihvatanje veze nije uspelo: {ex.Message}");
            Notify($"Prihvatanje veze nije uspelo: {ex.Message}");
        }
    }

    private void Attach(TcpClient client, byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Connection connection = Connection.FromClient(client, _userName, key);
        var files = new FileTransferService(connection, key, _receivedFolder);
        var fsw = new FswCoordinator(connection, files, _settings.Fsw, WatchFolder, () => Algorithm)
        {
            AutoAccept = _autoAccept
        };
        var recorder = new SessionRecorder(connection, files, fsw, History, Logger);
        var session = new ActiveSession(connection, files, fsw, recorder);

        recorder.EntryRecorded += e => _ui.Post(() => EntryRecorded?.Invoke(e));
        files.Progress += p => _ui.Post(() => Progress?.Invoke(p));
        fsw.Activity += e => _ui.Post(() => FswActivity?.Invoke(e));
        fsw.ApprovalRequested += r => _ui.Post(() => ApprovalRequested?.Invoke(r));
        connection.Disconnected += info => OnDisconnected(session, info);

        _session = session;
        SetState(ControllerState.Connected);
        connection.Start();
    }
    private void OnDisconnected(ActiveSession session, DisconnectInfo info)
    {
        lock(_gate)
        {
            if(!ReferenceEquals(_session, session)) return;
            _session = null;
            SetState(ControllerState.Idle);
            _teardown = Task.WhenAll(_teardown, Task.Run(() => TearDownAsync(session)));
        }

        _ui.Post(() => Disconnected?.Invoke(info));
    }
    private async Task TearDownAsync(ActiveSession session)
    {
        try
        {
            session.Connection.Dispose();
            await session.Fsw.DisposeAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("SESSION", $"Zatvaranje veze nije potpuno uspelo: {ex.Message}");
        }
        finally
        {
            session.Recorder.Dispose();
        }
    }
    private void SetState(ControllerState state)
    {
        Volatile.Write(ref _state, (int)state);
        _ui.Post(() => StateChanged?.Invoke(state));
    }

    private void ReleaseListener()
    {
        _listenCts?.Dispose();
        _listenCts = null;
        _listener?.Dispose();
        _listener = null;
    }
    private void ThrowIfNotIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if(State != ControllerState.Idle)
            throw new InvalidOperationException("Vec si povezan ili cekas vezu. Prvo prekini trenutnu.");
    }
    private ActiveSession RequireSession()
    {
        lock(_gate) return _session ?? throw new InvalidOperationException("Nema uspostavljene veze.");
    }
    private void Notify(string message) => _ui.Post(() => Notice?.Invoke(message)); 
    private static byte[] KeyFrom(string chatPassword)
    {
        if(string.IsNullOrEmpty(chatPassword))
            throw new ArgumentException("Chat lozinka ne sme biti prazna.", nameof(chatPassword));
        return CipherFactory.DeriveKey(chatPassword);
    }
}
