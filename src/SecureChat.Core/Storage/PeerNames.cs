using System.Net;

namespace SecureChat.Core.Storage;

public static class PeerNames
{
    public const string Unknown = "Nepoznat";

    public static bool IsUnknown(string? peer)
    {
        if(string.IsNullOrWhiteSpace(peer)) return true;

        string trimmed = peer.Trim();
        if(trimmed.Equals(Unknown, StringComparison.OrdinalIgnoreCase)) return true;

        bool looksLikeIp = trimmed.Contains(":") || trimmed.Count(c => c == '.') == 3;
        return looksLikeIp && IPAddress.TryParse(trimmed, out _);
    }

    public static string Normalize(string? peer) => IsUnknown(peer) ? Unknown : peer!.Trim();
}