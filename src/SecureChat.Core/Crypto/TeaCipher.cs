using System.Buffers.Binary;

namespace SecureChat.Core.Crypto;

public sealed class TeaCipher : IBlockCipher
{
    public const int BlockBytes = 8;
    public const int KeyBytes = 16;
    private const uint Delta = 0x9E3779B9;
    private const int Rounds = 32;

    private readonly uint[] _k = new uint[4];

    public int BlockSize => BlockBytes;

    public TeaCipher(ReadOnlySpan<byte> key)
    {
        if(key.Length != KeyBytes)
            throw new CryptoException($"TEA zahteva kljuc od {KeyBytes} bajtova.");
        for(int i = 0; i < 4; i++)
            _k[i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i*4)..]);
    }

    public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        uint v0 = BinaryPrimitives.ReadUInt32LittleEndian(input);
        uint v1 = BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);
        uint sum = 0;

        for(int i = 0; i < Rounds; i++)
        {
            sum += Delta;
            v0 += ((v1 << 4) + _k[0] ^ (v1 + sum) ^ ((v1 >> 5) + _k[1]));
            v1 += ((v0 << 4) + _k[2] ^ (v0 + sum) ^ ((v0 >> 5) + _k[3]));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output, v0);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], v1);
    }

    public void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        uint v0 = BinaryPrimitives.ReadUInt32LittleEndian(input);
        uint v1 = BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);
        uint sum = unchecked(Delta * Rounds);
        for(int i = 0; i < Rounds; i++)
        {
            v1 -= ((v0 << 4) + _k[2] ^ (v0 + sum) ^ ((v0 >> 5) + _k[3]));
            v0 -= ((v1 << 4) + _k[0] ^ (v1 + sum) ^ ((v1 >> 5) + _k[1]));
            sum -= Delta;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output, v0);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], v1);
    }
}