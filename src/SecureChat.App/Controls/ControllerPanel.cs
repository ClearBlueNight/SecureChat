using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App.Controls;

internal abstract class ControllerPanel : UserControl
{
    protected ChatController Controller { get; }
    
    public event Action<string>? StatusChanged;
    
    protected ControllerPanel(ChatController controller)
    {
        Controller = controller;
        Dock = DockStyle.Fill;
    }
    
    protected void SetStatus(string text) => StatusChanged?.Invoke(text);
    
    protected void ShowError(string message)
    {
        SetStatus(message);
        UiMessages.Error(FindForm(), message);
    }

    protected async Task<bool> TryRunAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ShowError($"{failurePrefix}: {ex.Message}");
            return false;
        }
    }

    protected void OpenFileOf(HistoryEntry entry)
    {
        if (entry.Kind == HistoryKind.File && !ShellHelper.ShowInFolder(entry.FilePath))
            SetStatus("File ili njegov folder vise ne postoji.");
    }
}