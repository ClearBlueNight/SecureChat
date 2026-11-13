namespace SecureChat.Core.Files;

public static class FswCommands
{
    public const string Start = "FSW_START";
    public const string Stop = "FSW_STOP";
    public const string Accepted = "FSW_ACCEPTED";
    public const string Declined = "FSW_DECLINED";
    public const string Stopped = "FSW_STOPPED";
}

public enum FswLocalState { Inactive, AwaitingApproval, Active }
public enum FswRemoteState { Idle, Requested, Active, Declined }

public enum FswEventKind
{
    // Uloga A
    RequestSent, RemoteAccepted, RemoteDeclined, RemoteActive, StopRequested, RemoteStopped,
    // Uloga B
    ApprovalRequested, ApprovalCancelled, LocalDeclined, WatchingStarted, WatchingStopped, FileDetected, FileSent, FileFailed,
    // zajednicko
    ConnectionLost, Error
}

public sealed record FswEvent(FswEventKind Kind, string Message, string? FilePath = null, Exception? Error = null);
public sealed record FswApprovalRequest(string Peer, string Folder);