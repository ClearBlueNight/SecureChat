namespace SecureChat.Core.Files;

public enum TransferPhase { Hashing, Sending, Receiving, Decrypting }

public sealed record FileProgress(string FileName, TransferPhase Phase, long Done, long Total);

public sealed class FileTransferException(string message, Exception? inner = null) : Exception(message, inner);