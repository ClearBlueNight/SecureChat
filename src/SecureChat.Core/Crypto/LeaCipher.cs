using System.Buffers.Binary;
using System.Dynamic;
using System.Numerics;
using System.Security.Cryptography;

namespace SecureChat.Core.Crypto;

public sealed class LeaCipher : IBlockCipher
{
    public const int BlockBytes = 16;

    private static readonly uint[] Delta = [
        0xc3efe9db, 0x44626b02, 0x79e27c8a, 0x78df30ec,
        0x715ea49e, 0xc785da0a, 0xe04ef22a, 0xe5c40957
    ];

    private static readonly int[] KeyRotations = [1, 3, 6, 11, 13, 17];

    private readonly uint[] _roundKeys; // 6 ruci po rundi
    private readonly int _rounds;

    public int BlockSize => BlockBytes;

    public LeaCipher(ReadOnlySpan<byte> key)
    {
        _rounds = key.Length switch
        {
            16 => 24,
            24 => 28, 
            32 => 32,
            _ => throw new CryptoException("LEA zahteva kljuc od 16, 24 ili 32 bajta"),
        };

        int words = key.Length / 4;
        uint[] t = new uint[words];
        for(int i = 0; i < words; i++)
            t[i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i*4)..]);

        _roundKeys = new uint[_rounds * 6];
        for(int i = 0; i < _rounds; i++)
        {
            uint d = Delta[i % words];
            int o = i * 6;
            if(words == 4)
            {
                for(int j = 0; j < 4; j++)
                    t[j] = BitOperations.RotateLeft(t[j] + BitOperations.RotateLeft(d, i + j), KeyRotations[j]);
                (_roundKeys[o], _roundKeys[o + 1], _roundKeys[o + 2]) = (t[0], t[1], t[2]);
                (_roundKeys[o + 3], _roundKeys[o + 4], _roundKeys[o + 5]) = (t[1], t[3], t[1]);
            }
            else
            {
                for(int j = 0; j < 6; j++)
                {
                    int idx = (6*i + j) % words;
                    t[idx] = BitOperations.RotateLeft(t[idx] + BitOperations.RotateLeft(d, i + j), KeyRotations[j]);
                    _roundKeys[o + j] = t[idx];
                }
            }
        }
    }

    public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        uint x0 = BinaryPrimitives.ReadUInt32LittleEndian(input);
        uint x1 = BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);
        uint x2 = BinaryPrimitives.ReadUInt32LittleEndian(input[8..]);
        uint x3 = BinaryPrimitives.ReadUInt32LittleEndian(input[12..]);
        
        for(int i = 0; i < _rounds; i++)
        {
            int o = i * 6;
            uint n0 = BitOperations.RotateLeft((x0 ^ _roundKeys[o]) + (x1 ^ _roundKeys[o + 1]), 9);
            uint n1 = BitOperations.RotateRight((x1 ^ _roundKeys[o + 2]) + (x2 ^ _roundKeys[o + 3]), 5);
            uint n2 = BitOperations.RotateRight((x2 ^ _roundKeys[o + 4]) + (x3 ^ _roundKeys[o + 5]), 3);
            (x0, x1, x2, x3) = (n0, n1, n2, x0);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(output, x0);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], x1);
        BinaryPrimitives.WriteUInt32LittleEndian(output[8..], x2);
        BinaryPrimitives.WriteUInt32LittleEndian(output[12..], x3);        
    }

    public void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        
        uint x0 = BinaryPrimitives.ReadUInt32LittleEndian(input);
        uint x1 = BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);
        uint x2 = BinaryPrimitives.ReadUInt32LittleEndian(input[8..]);
        uint x3 = BinaryPrimitives.ReadUInt32LittleEndian(input[12..]);
        
        for(int i = _rounds -1; i >= 0; i--)
        {
            int o = i * 6;
            uint p0 = x3;
            uint p1 = (BitOperations.RotateRight(x0, 9) - (p0 ^ _roundKeys[o]) ^_roundKeys[o + 1]);
            uint p2 = (BitOperations.RotateLeft(x1, 5) - (p1 ^ _roundKeys[o + 2]) ^ _roundKeys[o + 3]);
            uint p3 = (BitOperations.RotateLeft(x2, 3) - (p2 ^ _roundKeys[o + 4]) ^ _roundKeys[o + 5]);
            (x0, x1, x2, x3) = (p0, p1, p2, p3);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(output, x0);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], x1);
        BinaryPrimitives.WriteUInt32LittleEndian(output[8..], x2);
        BinaryPrimitives.WriteUInt32LittleEndian(output[12..], x3);   
    }
}