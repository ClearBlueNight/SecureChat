using System.Buffers.Binary;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecureChat.Core.Crypto;
using SecureChat.Core.Protocol;

namespace SecureChat.Core.Storage;

public class HistoryException(string message, Exception? inner = null) : Exception (message, inner);

public sealed class WrongHistoryPasswordException() : HistoryException("Pogresna lozinka aplikacije");
// Sifrovana istorija u jednom file'u. Zapisi se samo dodaju na kraju (mozda podela po nalozima?)
// File share otvoren u FileShare.None (niko drugi ne moze da ga otvori)
public sealed class HistoryStore : IDisposable
{
    public const int MinPasswordLength = 6;

    private const CipherAlgorithm RecordAlgorithm = CipherAlgorithm.LeaCtr;
    private const byte FormatVersion = 1;
    private const int NonceBytes = LeaCipher.BlockBytes / 2;
    private const int HashBytes = Sha256.HashSize;
    private const int LengthPrefixBytes = 4;
    private const int IterationsBytes = 4;
    private const int MaxIterations = 10_000_000;
    private const int MinBodyBytes = NonceBytes + 1 + HashBytes;
    private const int MaxBodyBytes = 16 * 1024 * 1024;

    private static readonly byte[] Magic = "SCHD"u8.ToArray();
    private static readonly byte[] VerifierPlain = Encoding.ASCII.GetBytes("SecureChat-HIST1");
    private static readonly int HeaderLength = Magic.Length + 1 + IterationsBytes + HistoryKeyDerivation.SaltByes + NonceBytes + VerifierPlain.Length;

    private static readonly JsonSerializerOptions Json = new ()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, 
      Converters = { new JsonStringEnumConverter(allowIntegerValues: false)}  
    };

    private readonly object _lock = new();
    private readonly FileStream _stream;
    private readonly byte[] _key;
    private readonly List<HistoryEntry> _entries;
    private bool _disposed; 

    public HistoryLoadReport LoadReport { get; }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    private HistoryStore(FileStream stream, byte[] key, List<HistoryEntry> entries, HistoryLoadReport report)
    {
        _stream = stream;
        _key = key;
        _entries = entries;
        LoadReport = report;
    }   

    public static bool Exists(string path) => File.Exists(path);
    // Pravljenje i Otvaranje
    public static HistoryStore Create(string path, string password, int iterations = HistoryKeyDerivation.DefaultIterations)
    {
        if(string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new HistoryException($"Lozinka aplikacije mora imati najmanje {MinPasswordLength} znakova.");
        
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);

        byte[] salt = RandomNumberGenerator.GetBytes(HistoryKeyDerivation.SaltByes);
        byte[] key = HistoryKeyDerivation.Derive(password, salt, iterations);
        byte[] verifierNonce = CipherFactory.GenerateNonce(RecordAlgorithm);
        byte[] verifier = CipherFactory.CreateEncryptor(RecordAlgorithm, key, verifierNonce).TransformAll(VerifierPlain);

        byte[] header = new byte[HeaderLength];
        int pos = 0;
        Magic.CopyTo(header, pos); 
        pos += Magic.Length;
        header[pos++] = FormatVersion;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(pos), (uint)iterations);
        pos += IterationsBytes;
        salt.CopyTo(header, pos);
        pos += salt.Length;
        verifierNonce.CopyTo(header, pos);
        pos += NonceBytes;
        verifier.CopyTo(header, pos);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException($"Folder za istoriju nije moguce napraviti: {ex.Message}", ex);
        }

        FileStream stream = OpenStream(path, FileMode.CreateNew);
        try
        {
            stream.Write(header);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stream.Dispose();
            TryDelete(path);
            throw new HistoryException($"Pravljenje istorije nije uspelo: {ex.Message}", ex);
        }
        return new HistoryStore(stream, key, [], new HistoryLoadReport(0, 0, 0));
    }

    public static HistoryStore Open(string path, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if(!File.Exists(path)) throw new HistoryException("Istorija ne postoji.");

        FileStream stream = OpenStream(path, FileMode.Open);
        try
        {
            byte[] key = ReadHeaderAndDeriveKey(stream, password);
            List<HistoryEntry> entries = ReadRecords(stream, key, out HistoryLoadReport report);
            return new HistoryStore(stream, key, entries, report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stream.Dispose();
            throw new HistoryException($"Citanje istorije nije uspelo: {ex.Message}", ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException($"Brisanje istorije nije uspelo: {ex.Message}", ex);
        }
    }

    // Managing History
    public void Append(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        byte[] record = EncodeRecord(entry);

        lock(_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long before = _stream.Length;
            try
            {
                _stream.Write(record);
                _stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryRollBack(before);
                throw new HistoryException($"Upis u istoriju nije uspeo: {ex.Message}", ex);
            }
            _entries.Add(entry);
        }        
    }

    public IReadOnlyList<HistoryEntry> Query(string? peer = null, string? text = null)
    {
        lock(_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IEnumerable<HistoryEntry> query = _entries;
            if(!string.IsNullOrWhiteSpace(peer))
                query = query.Where(e => string.Equals(e.Peer, peer.Trim(), StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrWhiteSpace(text))
            {
                string needle = text.Trim();
                query = query.Where(e => Contains(e.Text, needle) || Contains(e.FileName, needle));
            }
            return query.ToList();
        }
    }

    public IReadOnlyList<string> Peers()
    {
        lock(_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries.Select(e => e.Peer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public void Clear()
    {
        lock(_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _stream.SetLength(HeaderLength);
                _stream.Position = HeaderLength;
                _stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                throw new HistoryException($"Brisanje istorije nije uspelo: {ex.Message}", ex);
            }
            _entries.Clear();
        }
    }

    public void Dispose()
    {
        lock(_lock)
        {
            if(_disposed) return;
            _disposed = true;
            _stream.Dispose();
            Array.Clear(_key);
            _entries.Clear();
        }
    }

    // Formating
    private byte[] EncodeRecord(HistoryEntry entry)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(entry, Json);
        byte[] nonce = CipherFactory.GenerateNonce(RecordAlgorithm);
        byte[] cipher = CipherFactory.CreateEncryptor(RecordAlgorithm, _key, nonce).TransformAll(plain);

        int bodyLength = NonceBytes + cipher.Length + HashBytes;
        if(bodyLength > MaxBodyBytes) throw new HistoryException("Zapis istorije je prevelik.");
        
        byte[] record = new byte[LengthPrefixBytes + bodyLength];
        BinaryPrimitives.WriteInt32BigEndian(record, bodyLength);
        nonce.CopyTo(record, LengthPrefixBytes);
        cipher.CopyTo(record, LengthPrefixBytes + NonceBytes);
        byte[] hash = Sha256.ComputeHash(record.AsSpan(LengthPrefixBytes, NonceBytes + cipher.Length));
        hash.CopyTo(record, LengthPrefixBytes + NonceBytes + cipher.Length);
        return record;
    }

    private static byte[] ReadHeaderAndDeriveKey(FileStream stream, string password)
    {
        byte[] header = new byte[HeaderLength];
        if(stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            throw new HistoryException("File istorije je prekratak ili ostecen.");
        if(!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new HistoryException("File nije istorija ove aplikacije.");

        int pos = Magic.Length;
        if(header[pos] != FormatVersion)
            throw new HistoryException($"Nepodrzana verzija formata istorije: {header[pos]}.");
        pos++;

        
        uint iterations = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(pos));
        pos += IterationsBytes;
        if(iterations == 0 || iterations > MaxIterations)
            throw new HistoryException("Zaglavlje istorije je osteceno (broj iteracija).");

        ReadOnlySpan<byte> salt = header.AsSpan(pos, HistoryKeyDerivation.SaltByes);
        pos += HistoryKeyDerivation.SaltByes;
        ReadOnlySpan<byte> nonce = header.AsSpan(pos, NonceBytes);
        pos += NonceBytes;
        ReadOnlySpan<byte> verifier = header.AsSpan(pos, VerifierPlain.Length);

        byte[] key = HistoryKeyDerivation.Derive(password, salt, (int)iterations);
        byte[] plain = CipherFactory.CreateDecryptor(RecordAlgorithm, key, nonce).TransformAll(verifier);
        if(!plain.AsSpan().SequenceEqual(VerifierPlain))
        {
            Array.Clear(key);
            throw new WrongHistoryPasswordException();
        }
        return key;
    }

    private static List<HistoryEntry> ReadRecords(FileStream stream, byte[] key, out HistoryLoadReport report)
    {
        var entries = new List<HistoryEntry>();
        int corrupted = 0;
        long fileLength = stream.Length;
        long goodEnd = stream.Position;
        byte[] prefix = new byte[LengthPrefixBytes];

        while(true)
        {
            long recordStart = stream.Position;
            int got = stream.ReadAtLeast(prefix, LengthPrefixBytes, throwOnEndOfStream: false);
            if(got == 0) break;                 // Uredan kraj file'a
            if(got < LengthPrefixBytes) break;  // nepotpun prefix na kraju

            int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
            if(length < MinBodyBytes || length > MaxBodyBytes)
                throw new HistoryException($"File istorije je ostecen na poziciji {recordStart} (nemoguca duzina zapisa).");

            byte[] body = new byte[length];
            if(stream.ReadAtLeast(body, length, throwOnEndOfStream: false) < length) break; // nepotpun zapis na kraju

            goodEnd = stream.Position;
            HistoryEntry? entry = TryDecode(body, key);
            if(entry is null) corrupted++;
            else entries.Add(entry);
        }

        long truncated = fileLength - goodEnd;
        if(truncated > 0) stream.SetLength(goodEnd); // Zapis ide posle prvog dobrog
        stream.Position = goodEnd;

        report = new HistoryLoadReport(entries.Count, corrupted, truncated);
        return entries; 
    }

    private static HistoryEntry? TryDecode(byte[] body, byte[] key)
    {
        int hashStart = body.Length - HashBytes;
        try
        {
            Integrity.VerifyData(Convert.ToHexStringLower(body.AsSpan(hashStart)), body.AsSpan(0, hashStart));
            byte[] plain = CipherFactory.CreateDecryptor(RecordAlgorithm, key, body.AsSpan(0, NonceBytes))
                .TransformAll(body.AsSpan(NonceBytes, hashStart - NonceBytes));
            return JsonSerializer.Deserialize<HistoryEntry>(plain, Json);
        }
        catch(Exception ex) when (ex is IntegrityException or CryptoException or JsonException)
        {
            return null;
        }
    }

    // Helper methods
    private static FileStream OpenStream(string path, FileMode mode)
    {
        try
        {
            return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None, bufferSize: 0);
        }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(
                $"File istroije '{Path.GetFileName(path)}' nije moguce otvoriti ({ex.Message}.) " +
                "Proveri da ga ne koristi druga instanca aplikacije sa istim korisnickim imenom.", ex);
        }
    }

    private void TryRollBack(long length)
    {
        try
        {
            _stream.SetLength(length);
            _stream.Position = length;
        }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* Samo ostaje nepotpun pa se izbacuje pri sledecem otvaranju*/ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) {}
    }

    private static bool Contains(string? value, string needle) => value?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
}
