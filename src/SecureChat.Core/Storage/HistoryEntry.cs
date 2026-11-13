using SecureChat.Core.Crypto;

namespace SecureChat.Core.Storage;

public enum HistoryDirection { Sent, Received }
public enum HistoryKind { Text, File }

public enum HistoryStatus { Ok, IntegrityFailed, Failed}

public sealed record HistoryEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required HistoryDirection Direction { get; init; }
    // Peer2
    public required string Peer { get; init; }
    public required HistoryKind Kind { get; init; }
    public required CipherAlgorithm Algorithm { get; init; }
    public HistoryStatus Status { get; init; } = HistoryStatus.Ok;
    // Text only
    public string? Text { get; init; }
    public string? FileName { get; init; }
    // Og file size in byte
    public long? FileSize { get; init; }
    public string? FilePath { get; init; }
    // Reason of failure
    public string? Note { get; init; }
}

// What was actually found whilst history load
public sealed record HistoryLoadReport(int Loaded, int Corrupted, long TruncatedBytes)
{
    public bool HasProblems => Corrupted > 0 || TruncatedBytes > 0;
}