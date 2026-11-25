namespace SecureChat.App;

internal static class UiMessages
{
    private const string Title = "SecureChat";

    public static void Error(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Confirm(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}