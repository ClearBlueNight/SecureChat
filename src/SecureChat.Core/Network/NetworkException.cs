using System.Net.Sockets;

namespace SecureChat.Core.Network;

public class NetworkException(string message, Exception? inner = null) : Exception(message, inner);

public static class NetworkErrors
{
    public static NetworkException ForListen(SocketException ex, int port) => new(ex.SocketErrorCode switch
    {
        SocketError.AddressAlreadyInUse => $"Port {port} je vec zauzet. Izaberi drugi port ili zatvori program koji ga koristi.",
        SocketError.AccessDenied => $"Nemas dozvolu da slusas na portu {port}. Probaj port iznad 1024",
        _ => $"Slusanje na portu {port} nije uspelo: {ex.Message}"
    }, ex );

    public static NetworkException ForConnect(SocketException ex, string host, int port) => new(ex.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => $"Veza sa {host}:{port} odbijena. Proveri da se druga strana slusa na portu i da firewall dozvoljava vezu",
        SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => $"Adresa '{host}' nije pornadjena.",
        SocketError.TimedOut => $"Isteklo je vreme za povezivanje sa {host}:{port}.",
        SocketError.NetworkUnreachable or SocketError.HostUnreachable => $"Racunar {host} nije dostupan za ove mreze.",
        _ => $"Povezivanje sa {host}:{port} nije uspelo: {ex.Message}"
    }, ex);
}