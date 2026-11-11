using System.Buffers.Binary;
using System.Numerics;

namespace SecureChat.Core.Crypto;

public sealed class Sha256
{
    public const int HashSize = 32;
    private const int BlockSize = 64;

    private static readonly uint[] K =
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    ];

    private readonly uint[] _h =
    [
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
        0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
    ];

    private readonly byte[] _buffer = new byte[BlockSize];
    private int _bufferLength;
    private ulong _totalLength;
    private bool _finished;

    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        var sha = new Sha256();
        sha.Append(data);
        return sha.Finish();
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if(_finished) throw new InvalidOperationException("Hesh je vec zavrsen");
        _totalLength += (ulong)data.Length;
        int offset = 0;

        if(_bufferLength > 0)
        {
            int take = Math.Min(BlockSize - _bufferLength, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_bufferLength));
            _bufferLength += take;
            offset = take;
            if(_bufferLength == BlockSize)
            {
                Compress(_buffer);
                _bufferLength = 0;
            }
        }

        while(data.Length - offset >= BlockSize)
        {
            Compress(data.Slice(offset, BlockSize));
            offset += BlockSize;
        }

        if(offset < data.Length)
        {
            data[offset..].CopyTo(_buffer);
            _bufferLength = data.Length - offset;
        }
    }

    public byte[] Finish()
    {
        ulong bitLength = _totalLength * 8;
        int padLength = _bufferLength < 56 ? 56 - _bufferLength : 120 - _bufferLength;

        Span<byte> padding = stackalloc byte[padLength + 8];
        padding.Clear();
        padding[0] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padding[padLength..], bitLength);
        Append(padding);
        _finished = true;

        byte[] hash = new byte[HashSize];
        for(int i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(hash.AsSpan(i * 4), _h[i]);
        
        return hash;
    }

    private void Compress(ReadOnlySpan<byte> block)
    {
        Span<uint> w = stackalloc uint[64];
        for(int i = 0; i < 16; i++)
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i*4)..]);
        
        for(int i = 16; i < 64; i++)
        {
            uint s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            uint s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        uint a = _h[0], b = _h[1], c = _h[2], d = _h[3], e = _h[4], f = _h[5], g = _h[6], h = _h[7];
        for (int i = 0; i < 64; i++)
        {
            uint bigS1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
            uint ch = (e & f) ^ (~e & g);
            uint t1 = h + bigS1 + ch + K[i] + w[i];
            uint bigS0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
            uint maj = (a & b) ^ (a & c) ^ (b & c);
            uint t2 = bigS0 + maj;
            h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }

        _h[0] += a; _h[1] += b; _h[2] += c; _h[3] += d;
        _h[4] += e; _h[5] += f; _h[6] += g; _h[7] += h;
    }
}