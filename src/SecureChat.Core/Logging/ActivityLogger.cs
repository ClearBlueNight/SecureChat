using System.Globalization;
using System.Text;

namespace SecureChat.Core.Logging;

public enum LogLevel { Info, Warning, Error }

public sealed class ActivityLogger
{
    public const string FilePrefix = "activity-";
    public const string FileExtension = ".log";

    private const string DateFormat = "yyyy-MM-d";
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    private static readonly UTF8Encoding Utf8 = new (encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();

    public event Action<Exception>? WriteFailed;

    public ActivityLogger(string folder, Func<DateTime>? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = Path.GetFullPath(folder);
        _now = now ?? (() => DateTime.Now);
    }

    public string Folder => _folder;

    public string FileFor(DateTime time) => Path.Combine(_folder, FilePrefix + time.ToString(DateFormat, CultureInfo.InvariantCulture) + FileExtension);

    public void Info(string eventName, string description) => Write(LogLevel.Info, eventName, description);
    public void Warning(string eventName, string description) => Write(LogLevel.Warning, eventName, description);
    public void Error(string eventName, string description) => Write(LogLevel.Error, eventName, description);
    
    public void Write(LogLevel level, string eventName, string description)
    {
        DateTime now = _now();
        string line = string.Concat(
            now.ToString(TimestampFormat, CultureInfo.InvariantCulture),
            "[", LevelName(level), "]", Sanitize(eventName), ": ", Sanitize(description), Environment.NewLine);

        try
        {
            lock(_lock)
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(FileFor(now), line, Utf8);
            }
        }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteFailed?.Invoke(ex);
        }
    }

    public int DeleteOldLogs(int retentionDays)
    {
        if(!Directory.Exists(_folder)) return 0;

        DateOnly cutoff = DateOnly.FromDateTime(_now()).AddDays(-retentionDays);
        int deleted = 0;
        foreach(string file in Directory.EnumerateFiles(_folder, FilePrefix + "*" + FileExtension))
        {
            string stamp = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];
            if(!DateOnly.TryParseExact(stamp, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) || date >= cutoff)
                continue;

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch(Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteFailed?.Invoke(ex);
            }
        }
        return deleted;
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        _ => "ERROR"
    };

    private static string Sanitize(string text) => new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}