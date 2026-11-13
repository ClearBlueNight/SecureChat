namespace SecureChat.Core.Configuration;

public sealed record LoggingSettings
{
    public int RetentionDays { get; init; } = 30;
    internal void Validate()
    {
        if(RetentionDays is < 1 or > 3650)
            throw new SettingsException("Logging.RetentionDays mora biti izmedju 1 i 3650.");
    }
}