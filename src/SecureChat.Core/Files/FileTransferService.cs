using System.Security;
using SecureChat.Core.Crypto;
using SecureChat.Core.Network;
using SecureChat.Core.Protocol;

namespace SecureChat.Core.Files;

public sealed class FileTransferService
{
    private const int ProgressIntervalMs = 100;
    private const int FileBufferSize = 81920;

    private readonly Connection _connection;
    private readonly byte[] _key;
    private volatile string _receivedFolder = "";

    public event Action<FileProgress>? Progress;

    // Gde smestam primljene file'ove. Moze promena i pri aktivnoj vezi
    public string ReceivedFolder
    {
        get => _receivedFolder;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _receivedFolder = Path.GetFullPath(value);
        }
    }

    public FileTransferService(Connection connection, byte[] key, string receivedFolder)
    {
        _connection = connection;
        _key = key;
        _receivedFolder = receivedFolder;
        connection.FileHandler = ReceiveAsync;
    }

    public async Task SendFileAsync(string path, CipherAlgorithm algorithm, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if(!info.Exists) throw new FileTransferException($"File '{path}' ne postoji.");

        await using FileStream file = OpenForRead(info);
        string name = info.Name;
        long plainLength = file.Length;
        byte[] nonce = CipherFactory.GenerateNonce(algorithm);

        // Pass 1
        var sha = new Sha256();
        long encryptedSize = 0;
        Action<long> hashReport = Reporter(name, TransferPhase.Hashing, plainLength);
        long hashed = await PumpAsync(file, CipherFactory.CreateEncryptor(algorithm, _key, nonce), 
            chunk => { 
                sha.Append(chunk);
                encryptedSize += chunk.Length; 
                return Task.CompletedTask;
                },
            plainLength, hashReport, ct);
        hashReport(hashed);
        if(hashed != plainLength) throw ChangedDuringSend(name);

        var header = new MessageHeader
        {
            ContentType = ContentType.File,
            Sender = _connection.LocalName,
            EncryptedSize = encryptedSize,
            Timestamp = DateTimeOffset.UtcNow,
            Algorithm = algorithm,
            FileName = name,
            Hash = Convert.ToHexStringLower(sha.Finish()),
            Nonce = Convert.ToBase64String(nonce),
        };
        HeaderValidator.Validate(header); // Ne sme da se desi

        // Pass 2
        file.Seek(0, SeekOrigin.Begin);
        Action<long> sendReport = Reporter(name, TransferPhase.Sending, plainLength);
        await _connection.SendAsync(async (stream, token) =>
        {
            await Framing.WriteHeaderAsync(stream, header, token);
            long sent = await PumpAsync(file, CipherFactory.CreateEncryptor(algorithm, _key, nonce),
                chunk => stream.WriteAsync(chunk, token).AsTask(), plainLength, sendReport, token);
            if(sent != plainLength) throw ChangedDuringSend(name); // header vec poslat, veza bye bye
        }, ct);
        sendReport(plainLength);
    }

    private async Task<string> ReceiveAsync(MessageHeader header, Stream network, CancellationToken ct)
    {
        string folder = ReceivedFolder;
        string partPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            var sha = new Sha256();
            Action<long> receiveReport = Reporter(header.FileName!, TransferPhase.Receiving, header.EncryptedSize);
            Exception? diskError = await ReceiveToPartAsync(network, partPath, header.EncryptedSize, sha, receiveReport, ct);
            if(diskError is not null) throw DiskFailure(diskError);
            receiveReport(header.EncryptedSize);

            try
            {
                Integrity.VerifyHash(header.Hash, sha.Finish());
            }
            catch( IntegrityException ex )
            {
                throw new RecoverableReceiveException(ex.Message, ex);
            }

            return await DecryptPartAsync(partPath, folder, header, ct);
        }
        finally
        {
            TryDelete(partPath);
        }
    }

    private static async Task<Exception?> ReceiveToPartAsync(Stream network, string partPath, long size, Sha256 sha, Action<long> report, CancellationToken ct)
    {
        Exception? diskError = null;
        FileStream? part = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
            part = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileBufferSize, useAsync: true);
        }
        catch(Exception ex) when (IsDiskError(ex)) { diskError = ex; }

        try
        {
            byte[] buffer = new byte[ProtocolLimits.ChunkSize];
            long done = 0;
            while(done < size)
            {
                int want = (int)Math.Min(buffer.Length, size - done);
                int read = await network.ReadAsync(buffer.AsMemory(0, want), ct);
                if (read == 0) throw new ProtocolException("Veza je prekinuta usred poruke.");
                done += read;  

                if(diskError is null)
                {
                    try
                    {
                        sha.Append(buffer.AsSpan(0, read));
                        await part!.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    catch (Exception ex) when (IsDiskError(ex)) { diskError = ex; }
                } 
                report(done);
            }
        }
        finally
        {
            if (part is not null)
            {
                try { await part.DisposeAsync();}
                catch (Exception ex) when (IsDiskError(ex)) { diskError ??= ex; }
            }
        }

        return diskError;
    }

    private async Task<string> DecryptPartAsync(string partPath, string folder, MessageHeader header, CancellationToken ct)
    {
        FileStream output;
        try { output = CreateUniqueFile(folder, header.FileName!); }
        catch (Exception  ex) when (IsDiskError(ex)) { throw DiskFailure(ex); }

        string outputPath = output.Name;
        bool completed = false;

        try
        {
            IChunkTransform decryptor = CipherFactory.CreateDecryptor(header.Algorithm, _key, header.NonceBytes());
            Action<long> report = Reporter(header.FileName!, TransferPhase.Decrypting, header.EncryptedSize);

            await using (output)
            await using (var part = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.None, FileBufferSize, useAsync: true))
            {
                await PumpAsync(part, decryptor, chunk => output.WriteAsync(chunk, ct).AsTask(), long.MaxValue, report, ct);
            }
            report(header.EncryptedSize);
            completed = true;
            return outputPath;
        }
        catch(CryptoException ex) { throw new RecoverableReceiveException(ex.Message, ex); }
        catch(Exception ex) when (IsDiskError(ex)) { throw DiskFailure(ex); }
        finally
        {
            if(!completed) TryDelete(outputPath);
        }
    }

    // Pumpa do granice, sve u sink po chunkovima.
    private static async Task<long> PumpAsync(Stream source, IChunkTransform transform, Func<byte[], Task> sink, long limit, Action<long> report, CancellationToken ct)
    {
        byte[] buffer = new byte[ProtocolLimits.ChunkSize];
        long total = 0;
        while(total < limit)
        {
            int want = (int)Math.Min(buffer.Length, limit - total);
            int read = await source.ReadAsync(buffer.AsMemory(0, want), ct);
            if(read == 0) break;
            total += read;

            byte[] chunk = transform.Update(buffer.AsSpan(0, read));
            await sink(chunk);
            report(total);
        }

        await sink(transform.Final());
        return total;
    }

    private static FileStream CreateUniqueFile(string folder, string fileName)
    {
        if(Path.GetFileName(fileName) != fileName)
            throw new IOException("Neispravan naziv file'a");
        
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for(int i = 0; ; i++)
        {
            string candidate = Path.Combine(folder, i == 0 ? fileName : $"{stem} ({i}){extension}");
            try
            {
                return new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileBufferSize, useAsync: true);
            }
            catch(IOException) when (File.Exists(candidate)) {}
        }
    }
    // Akko proces za upis traje, otvaranje ne uspeva
    private static FileStream OpenForRead(FileInfo info)
    {
        try
        {
            return new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);            
        }
        catch( Exception ex ) when(IsDiskError(ex))
        {
            throw new FileTransferException($"File '{info}' nije moguce otvoriti: {ex.Message}.", ex);
        }
    }

    private Action<long> Reporter(string fileName, TransferPhase phase, long total)
    {
        long lastTick = 0;
        return done =>
        {
            long now = Environment.TickCount64;
            if(done < total && now - lastTick < ProgressIntervalMs) return; // poslednji event
            lastTick = now;
            Progress?.Invoke(new FileProgress(fileName, phase, done, total));
        };
    }
    private static bool IsDiskError(Exception ex) => ex is IOException or UnauthorizedAccessException or NotSupportedException;

    private static RecoverableReceiveException DiskFailure(Exception ex) => new ($"Cuvanje primljneog file'a nije uspelo {ex.Message}", ex);

    private static FileTransferException ChangedDuringSend(string name) => new ($"File '{name} je izmenjen tokom slanja'");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch(Exception ex) when(IsDiskError(ex)) {}
    }
}