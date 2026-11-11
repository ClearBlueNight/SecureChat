namespace SecureChat.Core.Crypto;

// brojacki blok = nonce (pola bloka) + brojac big-endian (druga polivna). Isto za sifrovanje i desifrovanje

public sealed class CtrTransform : IChunkTransform
{
    private readonly IBlockCipher _cipher;
    private readonly byte[] _counter;
    private readonly byte[] _keystream;
    private readonly int _nonceLength;
    private int _position;

    public CtrTransform(IBlockCipher cipher, ReadOnlySpan<byte> nonce)
    {
        _cipher = cipher;
        _nonceLength = cipher.BlockSize / 2;
        if(nonce.Length != _nonceLength)
            throw new CryptoException($"CTR zahteva nonce od {_nonceLength} bajtova.");

        _counter = new byte[cipher.BlockSize];
        nonce.CopyTo(_counter);
        _keystream = new byte[cipher.BlockSize];
        _position = cipher.BlockSize; // Prisilno generisanje prvog keystream bloka
    }

    public byte[] Update(ReadOnlySpan<byte> data)
    {
        byte[] output = new byte[data.Length];
        for(int i = 0; i < data.Length; i++)
        {
            if(_position == _keystream.Length) NextKeystreamBlock();
            output[i] = (byte)(data[i] ^ _keystream[_position++]);
        }

        return output;
    }

    public byte[] Final() => [];

    private void NextKeystreamBlock()
    {
        _cipher.EncryptBlock(_counter, _keystream);
        _position = 0;
        for(int i = _counter.Length - 1; i >= _nonceLength; i--)
            if(++_counter[i] != 0) return ;
        throw new CryptoException("Brojac u CTR modu je prekoracen: podatak je prevelik za ovaj nonce.");
    }
}