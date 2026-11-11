namespace SecureChat.Core.Crypto;

internal static class ByteUtil
{
    public static byte[] Join(byte[] first, ReadOnlySpan<byte> second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }
}

public sealed class EcbPkcs7Encryptor(IBlockCipher cipher) : IChunkTransform
{
    private byte[] _pending = [];

    public byte[] Update(ReadOnlySpan<byte> data)
    {
        int bs = cipher.BlockSize;
        byte[] all = ByteUtil.Join(_pending, data);
        int usable = all.Length - all.Length % bs;
        byte[] output = new byte[usable];
        for(int i = 0; i < usable; i += bs)
            cipher.EncryptBlock(all.AsSpan(i, bs), output.AsSpan(i, bs));
        _pending = all[usable..];
        return output;
    }

    public byte[] Final()
    {
        int bs = cipher.BlockSize;
        int padLength = bs - _pending.Length;
        byte[] block = new byte[bs];
        _pending.CopyTo(block, 0);
        Array.Fill(block, (byte)padLength, _pending.Length, padLength);
        byte[] output = new byte[bs];
        cipher.EncryptBlock(block, output);
        _pending = [];
        return output;
    }
}

public sealed class EcbPkcs7Decryptor(IBlockCipher cipher) :  IChunkTransform
{
    private byte[] _pending = [];

    public byte[] Update(ReadOnlySpan<byte> data)
    {
        int bs = cipher.BlockSize; 
        byte[] all = ByteUtil.Join(_pending, data);
        // Poslednji blok se zadrzava do Final(), jer sadrzi padding
        int keep = all.Length % bs;
        if(keep == 0 && all.Length > 0) keep = bs;
        int usable = all.Length - keep;
        byte[] output = new byte[usable];
        for(int i = 0; i < usable; i += bs)
            cipher.DecryptBlock(all.AsSpan(i, bs), output.AsSpan(i, bs));
        _pending = all[usable..];
        return output;
    }

    public byte[] Final()
    {
        int bs = cipher.BlockSize;
        if(_pending.Length != bs)
            throw new CryptoException("Sifrat nije validne duzine (Nije umnozak velicine bloka)");
        
        byte[] block = new byte[bs];
        cipher.DecryptBlock(_pending, block);
        _pending = [];

        int pad = block[^1];
        if(pad < 1 || pad > bs)
            throw new CryptoException("Neispravan padding (pogresan kljuc ili osteceni podaci).");

        for(int i = bs - pad; i < bs; i++)
            if(block[i] != pad)
                throw new CryptoException("Neispravan padding (pogresan kljuc ili osteceni podaci).");
        return block[..(bs-pad)];        
    }
}