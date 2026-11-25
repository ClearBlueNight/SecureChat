using SecureChat.Core.Files;
using SecureChat.Core.Session;

namespace SecureChat.App.Controls;

internal sealed class FswPanel : ControllerPanel
{
    private const int MaxEvents = 200;

    private readonly Label _requestStatus = new() { AutoSize = false };
    private readonly Button _request = new() { Text = "Zatrazi automatsko slanje" };
    private readonly Button _stopRequest = new() { Text = "Prekini automatsko slanje" };
    private readonly Label _watchStatus = new() { AutoSize = false };
    private readonly TextBox _watchFolder = new() { ReadOnly = true };
    private readonly Button _openFolder = new() { Text = "Otvori folder za automatsko slanje" };
    private readonly CheckBox _autoAccept = new() { Text = "Automatski prihvati zahteve (bez dijaloga)", AutoSize = false };
    private readonly ListBox _events = new() { IntegralHeight = false, HorizontalScrollbar = true };

    public FswPanel(ChatController controller) : base(controller)
    {
        var layout = LayoutHelper.Stack();
        layout.AddFixed(22, LayoutHelper.Caption("Zatrazi od druge strane"));
        layout.AddFixed(34, _requestStatus);
        layout.AddFixed(30, _request);
        layout.AddFixed(30, _stopRequest);
        layout.AddFixed(26, LayoutHelper.Caption("Ova strana salje file'ove"));
        layout.AddFixed(34, _watchStatus);
        _watchFolder.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        layout.AddFixed(26, _watchFolder);
        layout.AddFixed(30, _openFolder);
        layout.AddFixed(36, _autoAccept);
        layout.AddFixed(22, LayoutHelper.Caption("Dogadjaji"));
        layout.AddFill(_events);

        var group = new GroupBox { Text = "Automatska razmena (FSW)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(layout);
        Controls.Add(group);

        _watchFolder.Text = Controller.WatchFolder;
        _autoAccept.Checked = Controller.AutoAccept;

        _request.Click += async (_, _) =>
        {
            await TryRunAsync(Controller.RequestAutoSendAsync, "Zahtev nije poslat");
            RefreshState();
        };
        _stopRequest.Click += async (_, _) =>
        {
            await TryRunAsync(Controller.StopAutoSendAsync, "Zahtev za prekid nije poslat");
            RefreshState();
        };
        _autoAccept.CheckedChanged += (_, _) => Controller.AutoAccept = _autoAccept.Checked;
        _openFolder.Click += (_, _) =>
        {
            if (!ShellHelper.OpenFolder(Controller.WatchFolder)) SetStatus("Folder nije moguce otvoriti.");
        };
        Controller.FswActivity += OnFswActivity;
        Controller.StateChanged += OnStateChanged;

        RefreshState();
    }

    private void OnStateChanged(ControllerState _)
    {
        if (!IsDisposed) RefreshState();
    }

    private void OnFswActivity(FswEvent e)
    {
        if (IsDisposed) return;

        _events.Items.Add(StatusPresentation.FswLine(e, DateTime.Now));
        if (_events.Items.Count > MaxEvents) _events.Items.RemoveAt(0);
        _events.TopIndex = Math.Max(0, _events.Items.Count - Math.Max(1, _events.ClientSize.Height / _events.ItemHeight));

        if (e.Kind is FswEventKind.Error or FswEventKind.FileFailed or FswEventKind.RemoteDeclined)
            SetStatus(e.Message);
        RefreshState();
    }

    private void RefreshState()
    {
        bool connected = Controller.State == ControllerState.Connected;
        FswRemoteState remote = Controller.FswRequestState;
        FswLocalState local = Controller.FswWatchState;

        _requestStatus.Text = connected ? StatusPresentation.FswRequesterText(remote) : "Nema veze.";
        _watchStatus.Text = connected ? StatusPresentation.FswWatcherText(local) : "Nema veze.";
        _request.Enabled = connected && remote is FswRemoteState.Idle or FswRemoteState.Declined;
        _stopRequest.Enabled = connected && remote is FswRemoteState.Requested or FswRemoteState.Active;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Controller.FswActivity -= OnFswActivity;
            Controller.StateChanged -= OnStateChanged;
        }
        base.Dispose(disposing);
    }
}