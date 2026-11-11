using SecureChat.Core.Crypto;

namespace SecureChat.Core.Protocol;

public static class HeaderValidator
{
    private static readonly char[] ForbiddenFileNameChars = [.. Path.GetInvalidFileNameChars(), '/' , '\\'];
    public static void Validate(MessageHeader mh)
    {
        if(!Enum.IsDefined(mh.ContentType)) throw Invalid("Nepoznat tip sadrzaja");
        if(!Enum.IsDefined(mh.Algorithm)) throw Invalid("Nepoznat algoritham");
        if(string.IsNullOrWhiteSpace(mh.Sender) || mh.Sender.Length > ProtocolLimits.MaxSenderLength)
            throw Invalid("Naziv posiljaoca nedostaje ili je predugacak");
        
        ValidateHash(mh);
        ValidateNonce(mh);
        ValidateSize(mh);
        if(mh.ContentType == ContentType.File) ValidateFileName(mh.FileName);
    }

    private static void ValidateHash(MessageHeader mh)
    {
        if(mh.Hash is not { Length: ProtocolLimits.HashHexLength} || !mh.Hash.All(Uri.IsHexDigit))
            throw Invalid("Hash mora biti sha-256 u hexadecimalnom zapisu (64 znaka)");
    }

    private static void ValidateNonce(MessageHeader mh)
    {
        byte[] nonce; 
        try { nonce = mh.NonceBytes(); }
        catch (FormatException) { throw Invalid("Nonce nije ispravan base64"); }

        if(nonce.Length != CipherFactory.NonceSize(mh.Algorithm))
            throw Invalid($"Nonce za algoritham {mh.Algorithm} mora biti {CipherFactory.NonceSize(mh.Algorithm)} bajtova");
    }

    private static void ValidateSize(MessageHeader mh)
    {
        if(mh.EncryptedSize < 0 ) throw Invalid("Negativna velicina sifrata");
        if(mh.Algorithm != CipherAlgorithm.LeaCtr)
        {
            int block = CipherFactory.BlockSize(mh.Algorithm);
            if(mh.EncryptedSize == 0 || mh.EncryptedSize % block != 0)
                throw Invalid($"Velicina Sifrata mora biti pozitivan umnozak od {block} bajtova");
        }

        if(mh.ContentType != ContentType.File && mh.EncryptedSize > ProtocolLimits.MaxMessagePayloadBytes)
            throw Invalid($"Poruka je veca od dozvoljenih {ProtocolLimits.MaxMessagePayloadBytes} bajtova");
    }

    private static void ValidateFileName(string? name)
    {
        if(string.IsNullOrEmpty(name) || name.Length > ProtocolLimits.MaxFileNameLength)
            throw Invalid("Naziv file'a nedostaje ili je predugacak");
        if(name.IndexOfAny(ForbiddenFileNameChars) >= 0 || name.Trim('.').Length == 0)
            throw Invalid("Naziv file'a sadrzi nedozvoljene znakove ili putanju");

    }

    private static ProtocolException Invalid(string reason) => new ($"Neispravan header: {reason}.");
}