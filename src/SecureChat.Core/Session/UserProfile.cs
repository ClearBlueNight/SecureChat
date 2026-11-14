using SecureChat.Core.Configuration;
using SecureChat.Core.Storage;

namespace SecureChat.Core.Session;

public sealed record LoginResult(string UserName, string UserFolder, HistoryStore History);

public static class UserProfile
{
    public static bool HasHistory(string userName, string? root = null) 
        => HistoryStore.Exists(AppPaths.HistoryFile(AppPaths.UserFolderPath(userName, root)));

    public static LoginResult Login(string userName, string password, string? confirmPassword, string? root = null, int iterations = HistoryKeyDerivation.DefaultIterations)
    {
        string name = AppPaths.NormalizeUserName(userName);
        string folder = AppPaths.ForUser(name, root);
        string path = AppPaths.HistoryFile(folder);

        HistoryStore history;
        if(HistoryStore.Exists(path))
        {
            history = HistoryStore.Open(path, password);
        }
        else
        {
            if(confirmPassword != password) throw new HistoryException("Lozinke se ne poklapaju.");
            history = HistoryStore.Create(path, password, iterations);
        }
        return new LoginResult(name, folder, history);
    }

    public static void ResetHistory(string userName, string? root = null) 
        => HistoryStore.Delete(AppPaths.HistoryFile(AppPaths.UserFolderPath(userName, root)));
}