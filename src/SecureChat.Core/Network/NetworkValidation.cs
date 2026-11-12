using System.Globalization;

namespace SecureChat.Core.Network;

public static class NetworkValidation
{
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    // za polja u formi, prihvata samo cele brojeve u dozvoljenom opsegu
    public static bool TryParsePort(string? text, out int port) => 
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && IsValidPort(port);
}