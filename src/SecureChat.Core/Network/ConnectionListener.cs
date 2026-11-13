using System.Net;
using System.Net.Sockets;

namespace SecureChat.Core.Network;
// Strana koja slusa, 0 - samo bira slobodan port
public sealed class ConnectionListener : IDisposable
{
    private readonly TcpListener _listener;
    // Port na kom se slusa
    public int Port { get; }

    private ConnectionListener(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static ConnectionListener Start(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, NetworkValidation.MaxPort);

        var listener = new TcpListener(IPAddress.Any, port) { ExclusiveAddressUse = true};
        try{ listener.Start(); }
        catch (SocketException ex) { throw NetworkErrors.ForListen(ex, port); }
        return new ConnectionListener(listener);
    }
    // Ceka jednu dolaznu vezu. Dispose ili otkazivanje tokena prekida cekanje izuzetkom 
    public async Task<TcpClient> AcceptAsync(CancellationToken ct = default) => await _listener.AcceptTcpClientAsync(ct);
    public void Dispose() => _listener.Stop();
}