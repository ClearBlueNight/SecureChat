using SecureChat.Core.Crypto;

namespace SecureChat.Core.Protocol;

public enum ContentType { Text, File, Control }

// Metadata header
// JSON imena polja: contentType, sender, encryptedSize, timestamp, algorithm, fileName, hash, nonce)
public sealed record MessageHeader
{
    public required ContentType ContentType { get; init; }
    public required string Sender { get; init; }
    public required long EncryptedSize { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required CipherAlgorithm Algorithm { get; init; }
    // Samo za Fileova, izostavlja se iz json'a kada je null
    public string? FileName { get; init; }
    // Sha-256, 64 hex znaka
    public required string Hash { get; init; }
    // Base64: Prazan string ako algoritham nema nonce.
    public string Nonce { get; init; } = "";
    public byte[] NonceBytes() => string.IsNullOrEmpty(Nonce) ? [] : Convert.FromBase64String(Nonce);
}