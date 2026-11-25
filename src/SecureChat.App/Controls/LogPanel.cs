using SecureChat.Core.Session;

namespace SecureChat.App.Controls;

internal sealed class LogPanel : ControllerPanel
{
    private const int MaxChars = 300_000;

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 9F),
        BackColor = SystemColors.Window
    };
    private readonly CheckBox _autoScroll = new() { Text = "Automatsko skrolovanje", Checked = true, AutoSize = true };
    private readonly HashSet<string> _alreadyShown = [];

    public LogPanel(ChatController controller) : base(controller)
    {
        var existing = new System.Text.StringBuilder();
        foreach (string line in Controller.ReadLogLines())
        {
            _alreadyShown.Add(line);
            existing.AppendLine(line);
        }
        _log.Text = existing.ToString();
        ScrollToEnd();

        var layout = LayoutHelper.Stack();
        layout.AddFixed(26, _autoScroll);
        layout.AddFill(_log);
        Controls.Add(layout);

        Controller.LogLine += OnLogLine;
    }

    private void OnLogLine(string line)
    {
        if (IsDisposed) return;
        // Redovi napisani pre nego sto je prozor nastao stisu i kroz dogadjaj, a vec su ucitani iz file'a
        if (_alreadyShown.Remove(line)) return;

        _log.AppendText(line + Environment.NewLine);
        if (_log.TextLength > MaxChars) TrimOldest();
        if (_autoScroll.Checked) ScrollToEnd();
    }

    private void TrimOldest()
    {
        string text = _log.Text;
        int cut = text.IndexOf('\n', text.Length - MaxChars / 2);
        _log.Text = cut < 0 ? "" : text[(cut + 1)..];
    }

    private void ScrollToEnd()
    {
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Controller.LogLine -= OnLogLine;
        base.Dispose(disposing);
    }
}