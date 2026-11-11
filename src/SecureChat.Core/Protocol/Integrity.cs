using SecureChat.Core.Crypto;

namespace SecureChat.Core.Protocol;

public static class Integrity
{
    public static string HashHex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(Sha256.ComputeHash(data));

    // Poredi ocekivani hash iz headera sa izracunatim
    public static void VerifyHash(string expectedHex, byte[] actualHex)
    {
        if(!Convert.ToHexStringLower(actualHex).Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException("Provera integriteta nije uspela, hash primljenih podataka se ne poklapa");
    }

    // Izracunava hash nad podacima i poredi ga sa ocekivanim (no sht sherlock)
    public static void VerifyData(string expectedHex, ReadOnlySpan<byte> data) => VerifyHash(expectedHex, Sha256.ComputeHash(data));
}