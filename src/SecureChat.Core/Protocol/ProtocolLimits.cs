namespace SecureChat.Core.Protocol;

public static class ProtocolLimits
{
    public const int LengthPrefixBytes = 4;
    public const int MaxHeaderBytes = 64 * 1024;

    public const int MaxMessagePayloadBytes = 1024 * 1024;
    public const int ChunkSize = 64 * 1024;
    public const int HashHexLength = 64;
    public const int MaxSenderLength = 64;
    public const int MaxFileNameLength = 255;
}