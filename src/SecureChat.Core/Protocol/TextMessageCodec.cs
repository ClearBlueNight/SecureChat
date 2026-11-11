using System.Text;
using SecureChat.Core.Crypto;

namespace SecureChat.Core.Protocol;

// pravi i otvara Text/Control poruke
public static class TextMessageCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new (false, true);

    public static (MessageHeader Header, byte[] Payload) Create(
        ContentType type, string sender, string text, CipherAlgorithm algorithm, byte[] key)
    {
        if(type == ContentType.File)
            throw new ArgumentException("Za file'ove se koristi tok za file'ove", nameof(type));

        byte[] nonce = CipherFactory.GenerateNonce(algorithm);
        byte[] payload = CipherFactory.CreateEncryptor(algorithm, key, nonce).TransformAll(Encoding.UTF8.GetBytes(text));
        var header = new MessageHeader
        {
            ContentType = type,
            Sender = sender,
            EncryptedSize = payload.Length,
            Timestamp = DateTimeOffset.UtcNow,
            Algorithm = algorithm,
            Hash = Integrity.HashHex(payload),
            Nonce = Convert.ToBase64String(nonce)
        };
        HeaderValidator.Validate(header);
        return (header, payload);
    } 

    // Redosled: velicina, integritet, desifrovanje
    public static string Open(MessageHeader header, byte[] payload, byte[] key)
    {
        if(payload.Length != header.EncryptedSize)
            throw new ProtocolException("Velicina primljenog sifrata se ne poklapa sa headerom.");
        Integrity.VerifyData(header.Hash, payload);

        byte[] plain = CipherFactory.CreateDecryptor(header.Algorithm, key, header.NonceBytes()).TransformAll(payload);
        try { return StrictUtf8.GetString(plain); }
        catch (DecoderFallbackException)
        {
            throw new CryptoException("Desifrovani sadrzaj nije ispravan tekst (pogresna lozinka?).");
        }
    }
}