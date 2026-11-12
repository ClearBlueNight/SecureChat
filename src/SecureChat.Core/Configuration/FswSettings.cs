namespace SecureChat.Core.Configuration;

public sealed record FswSettings
{
    public string WatchFolderName { get; init; } = "AutoSend";
    public int DebounceMilliseconds { get; init; } = 300;
    public int ReadyRetryMilliseconds { get; init; } = 500;
    public int ReadyTimeoutSeconds { get; set; } = 30;

    public TimeSpan Debounce => TimeSpan.FromMilliseconds(DebounceMilliseconds);
    public TimeSpan ReadyRetryInterval => TimeSpan.FromMilliseconds(ReadyRetryMilliseconds);
    public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(ReadyTimeoutSeconds);

    internal void Validate()
    {
        if(string.IsNullOrWhiteSpace(WatchFolderName)
            || WatchFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || WatchFolderName.Trim('.').Length == 0)
            throw new SettingsException("Fsw.WatchFolderName mora biti ime foldera bez putanje.");
        if(DebounceMilliseconds is < 0 or > 5000)
            throw new SettingsException("Fsw.DebounceMilliseconds mora biti imzedju 0 i 5000.");
        if(ReadyRetryMilliseconds is < 50 or > 10_000)
            throw new SettingsException("Fsw.ReadyRetryMilliseconds mora biti imzedju 50 i 10000.");
        if(ReadyTimeoutSeconds is < 1 or > 600)
            throw new SettingsException("Fsw.ReadyTimeoutSeconds mora biti imzedju 0 i 600.");
    }
}