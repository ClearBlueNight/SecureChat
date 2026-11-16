using System.Globalization;
using SecureChat.Core.Files;

namespace SecureChat.Core.Session;

public enum ChatPasswordCheck { Ok, Empty, Short }

public static class StatusPresentation
{
    public const int MinChatPasswordLength = 6;

    public static string ConnectionText(ControllerState state, string peer, int? listenPort = null) => state switch
    {
        ControllerState.Idle => "Nije povezano",
        ControllerState.Listening => listenPort is { } port
            ? $"Ceka se dolazna veza (port {port})…"
            : "Ceka se dolazna veza…",
        ControllerState.Connecting => "Povezivanje…",
        ControllerState.Connected => string.IsNullOrWhiteSpace(peer) ? "Povezano" : $"Povezano: {peer}",
        _ => state.ToString()  
    };

    public static string FswRequesterText(FswRemoteState state) => state switch
    {
        FswRemoteState.Idle => "Automatsko slanje nije zatrazeno.",
        FswRemoteState.Requested => "Zahtev je poslat, ceka se odgovor druge strane…",
        FswRemoteState.Active => "Aktivno: druga strana automatski salje nove file'ove.",
        FswRemoteState.Declined => "Druga strana je odbila zahtev.",
        _ => state.ToString()
    };

    public static string FswWatcherText(FswLocalState state) => state switch
    {
        FswLocalState.Inactive => "Pracenje foldera nije aktivno.",
        FswLocalState.AwaitingApproval => "Ceka se tvoje odobrenje zahteva…",
        FswLocalState.Active => "Aktivno: novi file'ovi iz foldera se automatski salju.",
        _ => state.ToString()
    };

    public static ChatPasswordCheck CheckChatPassword(string? password) =>
        string.IsNullOrEmpty(password) ? ChatPasswordCheck.Empty
        : password.Length < MinChatPasswordLength ? ChatPasswordCheck.Short
        : ChatPasswordCheck.Ok;

    public static string PhaseLabel(TransferPhase phase) => phase switch
    {
        TransferPhase.Hashing => "priprema (sifrovanje i hes)",
        TransferPhase.Sending => "slanje",
        TransferPhase.Receiving => "primanje",
        TransferPhase.Decrypting => "desifrovanje",
        _ => phase.ToString()
    };

    public static int ProgressPercent(FileProgress progress) =>
        progress.Total <= 0 ? 100 : (int)Math.Clamp(progress.Done * 100 / progress.Total, 0L, 100L);

    public static string ProgressText(FileProgress progress) =>
        $"{progress.FileName}: {PhaseLabel(progress.Phase)} {ProgressPercent(progress)}%";

    public static string FswLine(FswEvent e, DateTime time) =>
        time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + e.Message;
}