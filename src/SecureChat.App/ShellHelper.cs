using System.ComponentModel;
using System.Diagnostics;

namespace SecureChat.App;

internal static class ShellHelper
{
    public static bool ShowInFolder(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if(File.Exists(path)) return Start($"/select,\"{path}\"");
            if(Directory.Exists(path)) return Start($"\"{path}\"");

            string? parent = Path.GetDirectoryName(path);
            return parent is not null && Directory.Exists(parent) && Start($"\"{parent}\"");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Start(string arguments)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose();
        return true;
    }

    public static bool OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        return ShowInFolder(path);
    }
}