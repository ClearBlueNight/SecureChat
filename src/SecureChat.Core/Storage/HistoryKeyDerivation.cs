using System.Text;
using SecureChat.Core.Crypto;

namespace SecureChat.Core.Storage;

// Sha256 iterated over (previous hash || Salt || password)
public static class HistoryKeyDerivation
{
    public const int SaltByes = 16;
    public const int DefaultIterations = 100_000;

    public static byte[] Derive(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] secret = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(secret);
        passwordBytes.CopyTo(secret, salt.Length);

        byte[] hash = [];
        for(int i = 0; i < iterations; i++)
        {
            var sha = new Sha256();
            sha.Append(hash);
            sha.Append(secret);
            hash = sha.Finish();
        }

        Array.Clear(secret);
        Array.Clear(passwordBytes);
        return hash[..CipherFactory.KeySize];
    }
}