using System.Net;
using SecureChat.Core.Crypto;
using SecureChat.Core.Files;
using SecureChat.Core.Logging;
using SecureChat.Core.Network;
using SecureChat.Core.Protocol;
using SecureChat.Core.Storage;

namespace SecureChat.Core.Session;

public sealed class SessionRecorder : IDisposable
{
    private const string UnknownPeer = "nepoznat";

    private readonly Connection _connection;
    private readonly FileTransferService _files;
    private readonly FswCoordinator _fsw;
    private readonly HistoryStore _history;
    private readonly ActivityLogger _log;
    private volatile string _peer;
    private int _disposed;

    // For every write
    public event Action<HistoryEntry>? EntryRecorded;

    public string Peer => _peer;

    public SessionRecorder(Connection connection, FileTransferService files, FswCoordinator fsw, HistoryStore history, ActivityLogger log)
    {
        _connection = connection;
        _files = files;
        _fsw = fsw;
        _history = history;
        _log = log;
        _peer = HostOf(connection.RemoteEndpoint);

        connection.MessageSent += OnMessageSent;
        connection.MessageReceived += OnMessageReceived;
        connection.ReceiveFailed += OnReceiveFailed;
        connection.FileReceived += OnFileReceived;
        connection.Disconnected += OnDisconnected;
        files.FileSent += OnFileSent;
        fsw.Activity += OnFswActivity;

        _log.Info("CONNECT", $"Veza uspostavljena: lokalno ime={connection.LocalName}, adresa={connection.RemoteEndpoint}.");
    }

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) == 1) return;


        _connection.MessageSent -= OnMessageSent;
        _connection.MessageReceived -= OnMessageReceived;
        _connection.ReceiveFailed -= OnReceiveFailed;
        _connection.FileReceived -= OnFileReceived;
        _connection.Disconnected -= OnDisconnected;
        _files.FileSent -= OnFileSent;
        _fsw.Activity -= OnFswActivity;
    }

    private void OnMessageSent(SentMessage message)
    {
        if(message.Header.ContentType == ContentType.Control) return;

        _log.Info("ENCRYPT", Describe(message.Header));
        _log.Info("SEND", $"{Describe(message.Header)} primalac={_peer}");
        Record(NewEntry(HistoryDirection.Sent, message.Header) with { Text = message.Text });
    }

    private void OnFileSent(SentFile file)
    {
        _log.Info("ENCRYPT", $"{Describe(file.Header)} original={file.PlainSize}B");
        _log.Info("SEND", $"{Describe(file.Header)} primalac={_peer}");
        Record(NewEntry(HistoryDirection.Sent, file.Header) with
        {
            FileSize = file.PlainSize,
            FilePath = file.SourcePath
        });
    }

    private void OnMessageReceived(ReceivedMessage message)
    {
        UpdatePeer(message.Header);
        if(message.Header.ContentType == ContentType.Control) return ;

        LogReceived(message.Header);
        Record(NewEntry(HistoryDirection.Received, message.Header) with
        {
            Text = message.Text
        });
    }

    private void OnFileReceived(ReceivedFile file)
    {
        UpdatePeer(file.Header);
        LogReceived(file.Header, $" sacuvano='{file.SavedPath}'");
        Record(NewEntry(HistoryDirection.Received, file.Header) with
        {
            FileSize = TryGetLength(file.SavedPath),
            FilePath = file.SavedPath 
        });
    }

    private void OnReceiveFailed(ReceiveFailure failure)
    {
        MessageHeader header = failure.Header;
        UpdatePeer(header);

        bool integrity = IsCausedBy<IntegrityException>(failure.Error);
        string eventName = integrity ? "INTEGRITY" : IsCausedBy<CryptoException>(failure.Error) ? "DECRYPT" : "RECEIVE";
        _log.Error(eventName, $"rezultat=NEUSPEH {Describe(header)} posiljalac={header.Sender}: {failure.Error.Message}");

        if(header.ContentType == ContentType.Control) return;
        HistoryStatus status = integrity ? HistoryStatus.IntegrityFailed : HistoryStatus.Failed;
        Record(NewEntry(HistoryDirection.Received, header, status) with
        {
            Note = failure.Error.Message
        });
    }

    private void OnDisconnected(DisconnectInfo info) =>
        _log.Write(info.Error is null ? LogLevel.Info : LogLevel.Warning, "DISCONNECT", info.Reason);

    private void OnFswActivity(FswEvent e)
    {
        LogLevel level = e.Kind switch
        {
            FswEventKind.Error or FswEventKind.FileFailed => LogLevel.Error,
            FswEventKind.RemoteDeclined or FswEventKind.LocalDeclined or FswEventKind.ConnectionLost => LogLevel.Warning,
            _ => LogLevel.Info
        };
        _log.Write(level, "FSW", e.Message);
    }

    // Helper methods
    private void LogReceived(MessageHeader header, string extra = "")
    {
        _log.Info("INTEGRITY", $"rezultat=OK {Describe(header)}");
        _log.Info("DECRYPT", $"rezultat=OK {Describe(header)}");
        _log.Info("RECEIVE", $"{Describe(header)} Posiljalac={header.Sender}{extra}");
    }
    private static string Describe(MessageHeader h) => 
        $"tip={h.ContentType} algoritam={h.Algorithm} sifrat={h.EncryptedSize}B" +
        (h.FileName is null ? "" : $" file='{h.FileName}'");
    private HistoryEntry NewEntry(HistoryDirection direction, MessageHeader header, HistoryStatus status = HistoryStatus.Ok) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Direction = direction,
        Peer = _peer,
        Kind = header.ContentType == ContentType.File ? HistoryKind.File : HistoryKind.Text,
        Algorithm = header.Algorithm,
        Status = status,
        FileName = header.FileName
    };
    private void Record(HistoryEntry entry)
    {
        try { _history.Append(entry); }
        catch(Exception ex) when (ex is HistoryException or ObjectDisposedException)
        {
            _log.Error("HISTORY", $"Upis u istoriju nije uspeo: {ex.Message}");
        }
        EntryRecorded?.Invoke(entry);
    }
    private void UpdatePeer(MessageHeader header) => _peer = header.Sender;
    private static bool IsCausedBy<T>(Exception ex) where T : Exception => ex is T || ex.InnerException is T;
    private static long? TryGetLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private static string HostOf(string endpoint)
    {
        if(IPEndPoint.TryParse(endpoint, out IPEndPoint? parsed))
        {
            IPAddress address = parsed.Address.IsIPv4MappedToIPv6 ? parsed.Address.MapToIPv4() : parsed.Address;
            return address.ToString();
        }
        return string.IsNullOrWhiteSpace(endpoint) ? UnknownPeer : endpoint;
    }
}