using System.Text.Json;
using SecureChat.Core.Network;

namespace SecureChat.Core.Configuration;

public sealed class SettingsException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record NetworkSettings
{
    public int DefaultPort { get; init; } = 9000;
    public int ConnectTimeoutSeconds { get; init; } = 5;

    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

    internal void Validate()
    {
        if(!NetworkValidation.IsValidPort(DefaultPort))
            throw new SettingsException($"Network.DefaultPort mora biti izmedju {NetworkValidation.MinPort} i {NetworkValidation.MaxPort}.");
        if(ConnectTimeoutSeconds is < 1 or > 120)
            throw new SettingsException("Network.ConnectTimeoutSeconds mora biti izmedju 1 i 120.");
    }
}

// Root config manje vise SIXXX SEVEENN AHHHH
public sealed record AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public NetworkSettings Network { get; init; } = new();
    public FswSettings Fsw { get; init; } = new();
    public static AppSettings Load(string path)
    {
        if(!File.Exists(path)) return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            settings = settings with 
            { 
                Network = settings.Network ?? new NetworkSettings(),
                Fsw = settings.Fsw ?? new FswSettings()
            };
            settings.Network.Validate();
            settings.Fsw.Validate();
            return settings;
        }
        catch (JsonException ex)
        {
            throw new SettingsException($"Konfiguracija '{Path.GetFileName(path)}' nije ispravan JSON.", ex);
        }
    }
}