using SecureChat.Core.Configuration;
using SecureChat.Core.Files;
using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App.Controls;

internal sealed class ChatPanel : ControllerPanel
{
    private readonly EntryListView _entries = new();
    private readonly TextBox _message = new()
    {
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly Button _send = new() { Text = "Posalji", Width = 100, Height = 30 };
    private readonly Button _sendFile = new() { Text = "Posalji fajl…", Width = 130, Height = 30 };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly Label _progressText = new() { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly TextBox _folder = new() { ReadOnly = true };
    private readonly Button _changeFolder = new() { Text = "Promeni…" };
    private readonly Button _openFolder = new() { Text = "Otvori" };
    private bool _sendingFiles;

    public ChatPanel(ChatController controller) : base(controller)
    {
        _entries.LocalName = controller.UserName;

        var layout = LayoutHelper.Stack();
        layout.AddFill(_entries);
        layout.AddFixed(66, _message);

        var buttons = new FlowLayoutPanel { Margin = Padding.Empty, WrapContents = false };
        var hint = new Label
        {
            Text = "Enter salje, Shift+Enter pravi novi red.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 9, 0, 0)
        };
        buttons.Controls.AddRange([_send, _sendFile, hint]);
        layout.AddFixed(38, buttons);

        var progress = LayoutHelper.Columns(new ColumnStyle(SizeType.Percent, 45), new ColumnStyle(SizeType.Percent, 55));
        _progress.Dock = _progressText.Dock = DockStyle.Fill;
        progress.Controls.Add(_progress, 0, 0);
        progress.Controls.Add(_progressText, 1, 0);
        layout.AddFixed(28, progress);

        var folder = LayoutHelper.Columns(
            new ColumnStyle(SizeType.Absolute, 130), new ColumnStyle(SizeType.Percent, 100),
            new ColumnStyle(SizeType.Absolute, 90), new ColumnStyle(SizeType.Absolute, 70));
        folder.Controls.Add(new Label { Text = "Primljeni fajlovi:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _folder.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _changeFolder.Dock = _openFolder.Dock = DockStyle.Fill;
        folder.Controls.Add(_folder, 1, 0);
        folder.Controls.Add(_changeFolder, 2, 0);
        folder.Controls.Add(_openFolder, 3, 0);
        layout.AddFixed(34, folder);

        var group = new GroupBox { Text = "Razgovor", Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(layout);
        Controls.Add(group);

        _folder.Text = Controller.ReceivedFolder;
        _entries.EntryActivated += OpenFileOf;
        _message.KeyDown += OnMessageKeyDown;
        _send.Click += async (_, _) => await SendMessageAsync();
        _sendFile.Click += OnSendFileClicked;
        _changeFolder.Click += OnChangeFolderClicked;
        _openFolder.Click += (_, _) =>
        {
            if (!ShellHelper.OpenFolder(Controller.ReceivedFolder)) SetStatus("Folder nije moguce otvoriti.");
        };
        Controller.EntryRecorded += OnEntryRecorded;
        Controller.Progress += OnProgress;
        Controller.StateChanged += OnStateChanged;

        ApplyState();
    }

    private void OnEntryRecorded(HistoryEntry entry)
    {
        if (!IsDisposed) _entries.Add(entry);
    }

    private void OnProgress(FileProgress progress)
    {
        if (IsDisposed) return;
        _progress.Value = StatusPresentation.ProgressPercent(progress);
        _progressText.Text = StatusPresentation.ProgressText(progress);
    }

    private void OnStateChanged(ControllerState state)
    {
        if (IsDisposed) return;
        if (state == ControllerState.Connected)
        {
            _entries.Clear();
            _progress.Value = 0;
            _progressText.Text = "";
        }
        ApplyState();
    }

    private void ApplyState()
    {
        bool connected = Controller.State == ControllerState.Connected;
        _message.Enabled = _send.Enabled = connected;
        _sendFile.Enabled = connected && !_sendingFiles;
    }

    private void OnMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Shift) return; // Shift+Enter pravi novi red

        e.SuppressKeyPress = true;
        e.Handled = true;
        if (_message.TextLength > 0 && _send.Enabled) _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        string text = _message.Text;
        _message.Clear();

        bool sent = await TryRunAsync(() => Controller.SendTextAsync(text), "Poruka nije poslata");
        if (!sent && text.Length > 0 && _message.TextLength == 0)
        {
            _message.Text = text; // da se napisana poruka ne izgubi
            _message.SelectionStart = _message.TextLength;
        }
    }

    private async void OnSendFileClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Izaberi fajlove za slanje",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        _sendingFiles = true;
        ApplyState();
        try
        {
            foreach (string path in dialog.FileNames)
            {
                string name = Path.GetFileName(path);
                if (!await TryRunAsync(() => Controller.SendFileAsync(path), $"Slanje file'a '{name}' nije uspelo")) break;
                SetStatus($"File '{name}' je poslat.");
            }
        }
        finally
        {
            _sendingFiles = false;
            if (!IsDisposed) ApplyState();
        }
    }

    private void OnChangeFolderClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder za primljene fajlove",
            UseDescriptionForTitle = true,
            SelectedPath = Controller.ReceivedFolder,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            // Praceni folder ne sme biti unutar foldera za primljene fajlove
            FswCoordinator.EnsureWatchFolderAllowed(Controller.WatchFolder, dialog.SelectedPath);
        }
        catch (FileTransferException ex)
        {
            ShowError(ex.Message);
            return;
        }

        Controller.ReceivedFolder = dialog.SelectedPath;
        _folder.Text = Controller.ReceivedFolder;
        SetStatus("Folder za primljene file'ove je promenjen (vazi od sledeceg fajla, do zatvaranja aplikacije).");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Controller.EntryRecorded -= OnEntryRecorded;
            Controller.Progress -= OnProgress;
            Controller.StateChanged -= OnStateChanged;
        }
        base.Dispose(disposing);
    }
}