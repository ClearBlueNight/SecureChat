namespace SecureChat.Core.Protocol;

public class ProtocolException(string message, Exception? inner = null) : Exception(message, inner);

// Hash primljenih podataka se ne poklapa sa hashom is headera
public sealed class IntegrityException(string message) : ProtocolException(message);