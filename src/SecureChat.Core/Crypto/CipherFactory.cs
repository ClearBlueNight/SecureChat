using System.Security.Cryptography; // samo za slucajnost
using System.Text;

namespace SecureChat.Core.Crypto;

public enum CipherAlgorithm  { Tea, Lea, LeaCtr}

public static class CipherFactory
{
    public const int KeySize = 16;

    public static int NonceSize(CipherAlgorithm algorithm) => algorithm == CipherAlgorithm.LeaCtr ? LeaCipher.BlockBytes / 2 : 0;

    // Nasumican Nonce ?
    public static byte[] GenerateNonce(CipherAlgorithm algorithm) => RandomNumberGenerator.GetBytes(NonceSize(algorithm));

    public static IChunkTransform CreateEncryptor(CipherAlgorithm algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
        => algorithm == CipherAlgorithm.LeaCtr
        ? new CtrTransform(CreateBlockCipher(algorithm, key), nonce)
        : new EcbPkcs7Encryptor(CreateBlockCipher(algorithm, key));

    public static IChunkTransform CreateDecryptor(CipherAlgorithm algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
        => algorithm == CipherAlgorithm.LeaCtr
        ? new CtrTransform(CreateBlockCipher(algorithm, key), nonce)
        : new EcbPkcs7Decryptor(CreateBlockCipher(algorithm, key));

    // Velicina sifrata za dati otvoreni sadrzaj(potrebna za metadata header pre slanja)
    public static long EncryptedSize(CipherAlgorithm algorithm, long plainLength) 
        => algorithm switch
        {
            CipherAlgorithm.LeaCtr => plainLength,
            CipherAlgorithm.Tea => (plainLength / TeaCipher.BlockBytes + 1) * TeaCipher.BlockBytes,
            CipherAlgorithm.Lea => (plainLength / LeaCipher.BlockBytes + 1) * LeaCipher.BlockBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };


    // KLjuc = prvih 16 byte'ova SHA-256(UTF-8 lozinka?)
    public static byte[] DeriveKey(string password)
    {
        byte[] hash = Sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return hash[..KeySize];
    }

    private static IBlockCipher CreateBlockCipher(CipherAlgorithm algorithm, ReadOnlySpan<byte> key) 
        => algorithm switch
        {
            CipherAlgorithm.Tea => new TeaCipher(key),
            CipherAlgorithm.Lea or CipherAlgorithm.LeaCtr => new LeaCipher(key),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

    public static int BlockSize(CipherAlgorithm algorithm) 
        => algorithm == CipherAlgorithm.Tea ? TeaCipher.BlockBytes : LeaCipher.BlockBytes; 
}