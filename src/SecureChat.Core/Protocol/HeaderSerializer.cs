using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureChat.Core.Protocol;

public static class HeaderSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static byte[] Serialize(MessageHeader header)
    {
        HeaderValidator.Validate(header);
        return JsonSerializer.SerializeToUtf8Bytes(header, Options);
    }

    public static MessageHeader Deserialize(ReadOnlySpan<byte> json)
    {
        MessageHeader? header;
        try{ header = JsonSerializer.Deserialize<MessageHeader>(json, Options); }
        catch( JsonException ex ) { throw new ProtocolException("Header nije ispravan JSON", ex); }

        if(header is null) throw new ProtocolException("Header je prazan");
        HeaderValidator.Validate(header);
        return header;
    }

}