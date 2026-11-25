using SecureChat.App.Controls;
using SecureChat.Core.Configuration;
using SecureChat.Core.Files;
using SecureChat.Core.Network;
using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App;

internal sealed class MainForm : Form
{
    private const double TopShare = 0.62;

    private readonly ChatController _controller;
    private readonly HistoryPanel _history;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _historyPage = new("Istorija") { Padding = new Padding(4) };
    private readonly TabPage _logPage = new("Evidencija aktivnosti") { Padding = new Padding(4) };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private ApprovalDialog? _approval;

    public MainForm(ChatController controller, AppSettings settings)
    {
        _controller = controller;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;

        var connection = new ConnectionPanel(controller, settings);
        var chat = new ChatPanel(controller);
        var fsw = new FswPanel(controller);
        _history = new HistoryPanel(controller);
        var log = new LogPanel(controller);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.Controls.Add(connection, 0, 0);
        top.Controls.Add(chat, 1, 0);
        top.Controls.Add(fsw, 2, 0);

        _historyPage.Controls.Add(_history);
        _logPage.Controls.Add(log);
        _tabs.TabPages.AddRange([_historyPage, _logPage]);

        _split.Panel1.Controls.Add(top);
        _split.Panel2.Controls.Add(_tabs);

        var strip = new StatusStrip();
        strip.Items.Add(_statusText);
        Controls.Add(_split); // Fill se dodaje prvi, pa tek onda trake uz ivice
        Controls.Add(strip);

        Load += (_, _) => ArrangeSplit();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _historyPage) _history.RefreshView();
        };

        connection.StatusChanged += SetStatus;
        chat.StatusChanged += SetStatus;
        fsw.StatusChanged += SetStatus;
        _history.StatusChanged += SetStatus;

        controller.StateChanged += OnStateChanged;
        controller.EntryRecorded += OnEntryRecorded;
        controller.Disconnected += OnDisconnected;
        controller.Notice += SetStatus;
        controller.FswActivity += OnFswActivity;
        controller.ApprovalRequested += OnApprovalRequested;

        UpdateTitle();
        SetStatus("Spremno. Izaberi \"Slušaj\" ili \"Poveži se\".");
    }

    private void ArrangeSplit()
    {
        _split.SplitterDistance = Math.Clamp((int)(_split.Height * TopShare), 330, _split.Height - 190);
        _split.Panel1MinSize = 330;
        _split.Panel2MinSize = 180;
    }

    // dogadjaji kontrolera (UI nit)

    private void OnStateChanged(ControllerState _) => UpdateTitle();

    private void OnEntryRecorded(HistoryEntry _)
    {
        UpdateTitle(); // ime druge strane se saznaje iz prve poruke
        if (_tabs.SelectedTab == _historyPage) _history.RefreshView();
    }

    private void OnDisconnected(DisconnectInfo info)
    {
        _approval?.CloseByRemote();
        SetStatus(info.Error is null ? info.Reason : $"{info.Reason} ({info.Error.Message})");
    }

    private void OnFswActivity(FswEvent e)
    {
        if (e.Kind == FswEventKind.ApprovalCancelled) _approval?.CloseByRemote();
    }

    private void OnApprovalRequested(FswApprovalRequest request)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => ShowApproval(request)));
    }

    private async void ShowApproval(FswApprovalRequest request)
    {
        if (IsDisposed || _controller.FswWatchState != FswLocalState.AwaitingApproval) return;

        using var dialog = new ApprovalDialog(request);
        _approval = dialog;
        DialogResult result;
        try { result = dialog.ShowDialog(this); }
        finally { _approval = null; }

        if (dialog.ClosedByRemote || IsDisposed) return;

        try
        {
            if (result == DialogResult.Yes) await _controller.AcceptApprovalAsync();
            else await _controller.DeclineApprovalAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Odgovor na zahtev nije poslat: {ex.Message}");
        }
    }

    // Helper methods

    private void UpdateTitle() =>
        Text = $"SecureChat – {_controller.UserName} – " + StatusPresentation.ConnectionText(_controller.State, _controller.PeerName, _controller.ListeningPort);

    private void SetStatus(string text)
    {
        string oneLine = text.ReplaceLineEndings(" ");
        _statusText.Text = oneLine;
        _statusText.ToolTipText = oneLine;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing
            && _controller.State == ControllerState.Connected
            && !UiMessages.Confirm(this, "Veza je aktivna. Zatvoriti aplikaciju?"))
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.StateChanged -= OnStateChanged;
        _controller.EntryRecorded -= OnEntryRecorded;
        _controller.Disconnected -= OnDisconnected;
        _controller.Notice -= SetStatus;
        _controller.FswActivity -= OnFswActivity;
        _controller.ApprovalRequested -= OnApprovalRequested;
        base.OnFormClosed(e);
    }
}