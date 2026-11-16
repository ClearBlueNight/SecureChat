using System.Globalization;
using System.Text;
using SecureChat.Core.Crypto;
using SecureChat.Core.Storage;

namespace SecureChat.Core.Session;

public enum EntryTone { Sent, Received, Failed }

public static class EntryPresentation
{
    public const int SummaryMaxLength = 200;
    public const string NewLineMark = "↵";

    public static EntryTone ToneOf(HistoryEntry e) => 
        e.Status != HistoryStatus.Ok ? EntryTone.Failed
        : e.Direction == HistoryDirection.Sent ? EntryTone.Sent
        : EntryTone.Received;

    public static string Time(HistoryEntry e) => 
        e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DirectionLabel(HistoryEntry e) => e.Direction == HistoryDirection.Sent ? "Poslato" : "Primljeno";

    public static string KindLabel(HistoryEntry e) => e.Kind == HistoryKind.Text ? "Poruka" : "File";

    public static string Participant(HistoryEntry e, string localName) =>
        e.Direction == HistoryDirection.Sent ? localName : PeerNames.Normalize(e.Peer);

    public static string AlgorithmLabel(CipherAlgorithm algorithm) => algorithm switch
    {
        CipherAlgorithm.Tea => "TEA",
        CipherAlgorithm.Lea => "LEA",
        CipherAlgorithm.LeaCtr => "LEA+CTR",
        _ => algorithm.ToString()
    };

    public static string StatusLabel(HistoryEntry e) => e.Status switch
    {
        HistoryStatus.Ok => "U redu",
        HistoryStatus.IntegrityFailed => "Integritet narusen",
        _ => "Neuspeh"
    };

    public static string Summary(HistoryEntry e)
    {
        if(e.Kind == HistoryKind.File)
        {
            string name = e.FileName ?? "(bez naziva)";
            string size = e.FileSize is {} bytes ? $" ({FormatSize(bytes)})" : "";
            string note = e.Status == HistoryStatus.Ok || string.IsNullOrWhiteSpace(e.Note)
                ? ""
                : " - " + OneLine(e.Note);
            return Truncate(OneLine(name) + size + note);
        }

        if(e.Text is null)
            return Truncate(string.IsNullOrWhiteSpace(e.Note) ? "(sadrzaj nije dostupan)" : OneLine(e.Note));
        if(e.Text.Length == 0) return "(prazna poruka)";
        return Truncate(OneLine(e.Text));
    }

    public static string Detail(HistoryEntry e)
    {
        if(e.Kind == HistoryKind.Text && e.Text is not null)
            return e.Text.ReplaceLineEndings().Replace('\0', '␀');

        var lines = new List<string>();
        if(e.Kind == HistoryKind.File)
        {
            lines.Add($"File: {e.FileName ?? "(bez naziva)"}");
            if(e.FileSize is { } size) lines.Add($"Velicina: {FormatSize(size)} ({size} B)");
            if(!string.IsNullOrWhiteSpace(e.FilePath)) lines.Add($"Putanja: {e.FilePath}");
        }
        if(!string.IsNullOrWhiteSpace(e.Note)) lines.Add($"Napomena: {e.Note}");
        return string.Join(Environment.NewLine, lines);
    }
    public static string FormatSize(long bytes)
    {
        if(bytes < 1024) return $"{bytes} B";

        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (value >= 1024 && unit < units.Length - 1);

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {units[unit]}");
    }

    private static string OneLine(string text)
    {
        var sb = new StringBuilder(text.Length);
        for(int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if(c == '\r')
            {
                sb.Append(NewLineMark);
                if(i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if(c == '\n') sb.Append(NewLineMark);
            else sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    private static string Truncate(string text)
    {
        if(text.Length <= SummaryMaxLength) return text;
        int cut = SummaryMaxLength;
        if(char.IsHighSurrogate(text[cut - 1])) cut--; 
        return text[..cut] + "…";
    }
}