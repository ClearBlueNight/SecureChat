using SecureChat.Core.Protocol;

namespace SecureChat.Core.Files;

public enum TransferPhase { Hashing, Sending, Receiving, Decrypting }

public sealed record FileProgress(string FileName, TransferPhase Phase, long Done, long Total);

public sealed class FileTransferException(string message, Exception? inner = null) : Exception(message, inner);

// Uspesno poslat file, PS - duzina og file'a
public sealed record SentFile(MessageHeader Header, string SourcePath, long PlainSize);