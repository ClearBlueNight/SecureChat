using System.Net.Sockets;

namespace SecureChat.Core.Network;

// Strana koja se povezuje
public static class TcpConnector
{
    public static async Task<TcpClient> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        if(string.IsNullOrWhiteSpace(host)) throw new NetworkException("IP adresa nije uneta.");
        host = host.Trim();

        var client = new TcpClient();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new NetworkException($"Isteklo je vreme za povezivanje sa {host}:{port} ({timeout.TotalSeconds:0} s).");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw NetworkErrors.ForConnect(ex, host, port);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}