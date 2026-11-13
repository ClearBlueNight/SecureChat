using System.Buffers.Binary;

namespace SecureChat.Core.Protocol;

// Frame: [4 B duzina headera, big-endian][JSON header][Sifrat].
public static class Framing
{
    // Pise prefix i header jednim pozivom. Payload se pise posebno. Istovremeno slanje iz vise taskova mora da se serijalizuje
    public static async Task WriteHeaderAsync(Stream stream, MessageHeader header, CancellationToken ct = default)
    {
        byte[] json = HeaderSerializer.Serialize(header);
        if(json.Length > ProtocolLimits.MaxHeaderBytes)
            throw new ProtocolException("Header je prevelik.");
        
        byte[] frame = new byte[ProtocolLimits.LengthPrefixBytes + json.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, ProtocolLimits.LengthPrefixBytes);
        await stream.WriteAsync(frame, ct);
    }

    // Vraca null ako je druga strana uredno zatvorila vezu pre pocetka nove poruke
    public static async Task<MessageHeader?> ReadHeaderAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] prefix = new byte[ProtocolLimits.LengthPrefixBytes];
        int read = await ReadUpToAsync(stream, prefix, ct);
        if( read == 0) return null;
        if(read < prefix.Length) throw Truncated();

        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if(length <= 0 || length > ProtocolLimits.MaxHeaderBytes)
            throw new ProtocolException($"Neuspravna duzina headera: {length}.");
        
        byte[] json = new byte[length];
        if(await ReadUpToAsync(stream, json, ct) < length) throw Truncated();
        return HeaderSerializer.Deserialize(json);
    }

    // Cita ceo sifrat u memoriju (samo za text i Control)
    public static async Task<byte[]> ReadPayloadAsync(Stream stream, MessageHeader header, CancellationToken ct = default)
    {
        if(header.EncryptedSize > ProtocolLimits.MaxMessagePayloadBytes)
            throw new ProtocolException("Payload je prevelik za citanje u memoriju.");
        
        byte[] payload = new byte[header.EncryptedSize];
        if(await ReadUpToAsync(stream, payload, ct) < payload.Length) throw Truncated();
        return payload;
    }

    // Prebacuje tacno count bajtova u blokovima
    public static async Task CopyExactlyAsync(Stream source, Stream destination, long count, 
        Action<long>? progress = null, CancellationToken ct = default)
    {
        byte[] buffer = new byte[ProtocolLimits.ChunkSize];
        long done = 0;
        while(done < count)
        {
            int want = (int)Math.Min(buffer.Length, count - done);
            int read = await source.ReadAsync(buffer.AsMemory(0, want), ct);
            if(read == 0) throw Truncated();
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Invoke(done);
        }
    } 

    private static async Task<int> ReadUpToAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while(total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if(read == 0) break;
            total += read;
        }
        return total;
    }

    private static ProtocolException Truncated() => new("Veza je prekinuta usred poruke.");
}