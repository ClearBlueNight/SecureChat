using System.Net.Sockets;
using SecureChat.Core.Crypto;
using SecureChat.Core.Protocol;

namespace SecureChat.Core.Network;
// 1 veza. vise podataka mogu da se salju u isto vreme. Pretplata na dogadjaj pre poziva start (Subscribe - rwa)
public sealed class Connection : IDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _sendLock = new (1,1);
    private readonly CancellationTokenSource _cts = new();
    private int _started;
    private int _disconnectedRaised;

    public string LocalName { get; }
    public string RemoteEndpoint { get; }

    public event Action<ReceivedMessage>? MessageReceived;
    public event Action<ReceiveFailure>? ReceiveFailed;
    public event Action<DisconnectInfo>? Disconnected;

    public event Action<ReceivedFile>? FileReceived;

    public event Action<SentMessage>? MessageSent;

    // Dok je null - file'ove preskace (ReceiveFailed)
    public FileReceiveHandler? FileHandler { get; set; }


    public Connection(Stream stream, string localName, byte[] key, string remoteEndpoint = "", IDisposable? owner = null)
    {
        _stream = stream;
        _key = key;
        _owner = owner;
        LocalName = localName;
        RemoteEndpoint = remoteEndpoint;
    }

    public static Connection FromClient(TcpClient client, string localName, byte[] key)
    {
        client.NoDelay = true;
        return new Connection(client.GetStream(), localName, key, client.Client.RemoteEndPoint?.ToString() ?? "", client);
    }

    public void Start()
    {
        if(Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("Veza je vec pokrenuta.");
        _ = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendTextAsync(ContentType type, string text, CipherAlgorithm algorithm, CancellationToken ct = default)
    {
        // Create & Validate pre locka
        var ( header, payload ) = TextMessageCodec.Create(type, LocalName, text, algorithm, _key);
        await SendAsync( async (stream, token) =>
        {
            await Framing.WriteHeaderAsync(stream, header, token);
            await stream.WriteAsync(payload, token);
        }, ct);
        MessageSent?.Invoke(new SentMessage(header, text));
    }

    public async Task SendAsync(Func<Stream, CancellationToken, Task> write, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await write(_stream, ct);
            await _stream.FlushAsync(ct);
        }
        catch
        {
            Close();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose() => Close();

    public void Close()
    {
        _cts.Cancel();
        _stream.Dispose();
        _owner?.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        var info = new DisconnectInfo("Veza je zatvorena.", null);
        try
        {
            while(true)
            {
                MessageHeader? header = await Framing.ReadHeaderAsync(_stream, _cts.Token);
                if(header is null)
                {
                    info = new DisconnectInfo("Druga strana je zatvorila vezu.", null);
                    break;
                }
                await HandleAsync(header);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            info = _cts.IsCancellationRequested 
                ? new DisconnectInfo("Veza je zatvorena.", null)
                : new DisconnectInfo("Veza je prekinuta.", ex);
        }
        catch (ProtocolException ex)
        {
            info = new DisconnectInfo($"Veza je prekinuta zbog narusenog protokola: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            info = new DisconnectInfo($"Neocekivana greska: {ex.Message}", ex);
        }

        Close();
        if(Interlocked.Exchange(ref _disconnectedRaised, 1) == 0) 
            Disconnected?.Invoke(info);
    }

    private async Task HandleAsync(MessageHeader header)
    {
        if(header.ContentType == ContentType.File)
        {
            await HandleFileAsync(header);
            return;
        }

        byte[] payload = await Framing.ReadPayloadAsync(_stream, header, _cts.Token);
        string text;
        try
        {
            text = TextMessageCodec.Open(header, payload, _key);
        }
        catch (Exception ex) when (ex is ProtocolException or CryptoException)
        {
            ReceiveFailed?.Invoke(new ReceiveFailure(header, ex));
            return;
        }
        MessageReceived?.Invoke(new ReceivedMessage(header, text));
    }

    private async Task HandleFileAsync(MessageHeader header)
    {
        FileReceiveHandler? handler = FileHandler;
        if(handler == null)
        {
            await Framing.CopyExactlyAsync(_stream, Stream.Null, header.EncryptedSize, ct: _cts.Token);
            ReceiveFailed?.Invoke(new ReceiveFailure(header, new ProtocolException("Primanje fajlova jos nije podrzano.")));
            return;
        }

        string SavedPath;
        try
        {
            SavedPath = await handler(header, _stream, _cts.Token);
        }
        catch (RecoverableReceiveException ex)
        {
            ReceiveFailed?.Invoke(new ReceiveFailure(header, ex));
            return;
        }
        FileReceived?.Invoke(new ReceivedFile(header, SavedPath));
    }
}