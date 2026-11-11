namespace SecureChat.Core.Crypto;

public class CryptoException(string message) : Exception(message);

// (de)sifriranje jednog bloka
public interface IBlockCipher
{
    int BlockSize { get; }
    void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output);
    void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output);
}

public interface IChunkTransform
{
    byte[] Update(ReadOnlySpan<byte> data);
    byte[] Final();
}

public static class ChunkTransformExtension
{
    public static byte[] TransformAll(this IChunkTransform transform, ReadOnlySpan<byte> data)
    {
        byte[] head = transform.Update(data);
        byte[] tail = transform.Final();
        byte[] result = new byte[head.Length + tail.Length];

        head.CopyTo(result, 0);
        tail.CopyTo(result, head.Length);
        return result;
    }
}