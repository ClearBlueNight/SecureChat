using SecureChat.Core.Protocol;

namespace SecureChat.Core.Configuration;

public static class AppPaths
{
    public const string AppFolderName = "SecureChat";

    public const string ReceivedFolderName = "Received";

    public static string ReceivedFilesFolder(string userFolder) => Path.Combine(userFolder, ReceivedFolderName);

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    // U headeru - (sender)
    public static string NormalizeUserName(string? name)
    {
        string trimmed = name?.Trim() ?? "";
        if(trimmed.Length == 0 || trimmed.Length > ProtocolLimits.MaxSenderLength)
            throw new ArgumentException($"Korisnicko ime mora imati 1-{ProtocolLimits.MaxSenderLength} znakova.", nameof(name));
        if(trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.Trim('.').Length == 0)
            throw new ArgumentException("Korisnicko ime sadrzi nedozvoljene znakove.", nameof(name));
        return trimmed;
    }

    public static string ForUser(string userName, string? root = null)
    {
        string path = Path.Combine(root ?? DefaultRoot, NormalizeUserName(userName));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string WatchFolder(string userFolder, FswSettings settings) => Path.Combine(userFolder, settings.WatchFolderName);
}