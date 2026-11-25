using System.Globalization;
using SecureChat.Core.Configuration;
using SecureChat.Core.Crypto;
using SecureChat.Core.Network;
using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App.Controls;

internal sealed class ConnectionPanel : ControllerPanel
{
    private sealed record AlgorithmChoice(CipherAlgorithm Value)
    {
        public override string ToString() => EntryPresentation.AlgorithmLabel(Value);
    }

    private sealed record Input(string Host, int Port, string Password);

    private readonly RadioButton _listen = new() { Text = "Slusaj (cekaj vezu)", Checked = true };
    private readonly RadioButton _connect = new() { Text = "Povezi se na drugu stranu" };
    private readonly TextBox _host = new() { Text = "127.0.0.1" };
    private readonly TextBox _port = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _algorithm = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Label _status = new() { AutoSize = false, TextAlign = ContentAlignment.TopLeft };
    private CancellationTokenSource? _connectCts;

    public ConnectionPanel(ChatController controller, AppSettings settings) : base(controller)
    {
        _port.Text = settings.Network.DefaultPort.ToString(CultureInfo.InvariantCulture);

        foreach (CipherAlgorithm algorithm in Enum.GetValues<CipherAlgorithm>())
            _algorithm.Items.Add(new AlgorithmChoice(algorithm));
        _algorithm.SelectedItem = new AlgorithmChoice(controller.Algorithm);

        var layout = LayoutHelper.Form(labelWidth: 95);
        layout.AddWide(_listen, 30);
        layout.AddWide(_connect, 35);
        layout.AddField("IP adresa", _host);
        layout.AddField("Port", _port);
        layout.AddField("Chat lozinka", _password);
        layout.AddWide(LayoutHelper.Hint("Obe strane moraju uneti istu chat lozinku (preporuka: najmanje 6 znakova)."), 40);
        layout.AddField("Algoritam", _algorithm);

        var buttons = LayoutHelper.Columns(new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Percent, 50));
        _start.Dock = _stop.Dock = DockStyle.Fill;
        buttons.Controls.Add(_start, 0, 0);
        buttons.Controls.Add(_stop, 1, 0);
        layout.AddWide(buttons, 36);
        layout.AddWide(_status, 60);

        var group = new GroupBox { Text = "Veza", Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(layout);
        Controls.Add(group);

        _listen.CheckedChanged += (_, _) => ApplyState();
        _connect.CheckedChanged += (_, _) => ApplyState();
        _algorithm.SelectedIndexChanged += (_, _) =>
        {
            if(_algorithm.SelectedItem is AlgorithmChoice choice) Controller.Algorithm = choice.Value;
        };
        _start.Click += OnStartClicked;
        _stop.Click += OnStopClicked;
        Controller.StateChanged += OnStateChanged;
        Controller.EntryRecorded += OnEntryRecorded;

        ApplyState();
    }
     private void OnEntryRecorded(HistoryEntry _)
    {
        if(!IsDisposed) RefreshStatus();
    }

    private void OnStateChanged(ControllerState _)
    {
        if(!IsDisposed) ApplyState();
    }

    private void ApplyState()
    {
        ControllerState state = Controller.State;
        bool idle = state == ControllerState.Idle;

        _listen.Enabled = _connect.Enabled = _port.Enabled = _password.Enabled = idle;
        _host.Enabled = idle && _connect.Checked;
        _start.Enabled = idle;
        _start.Text = _connect.Checked ? "Povezi se" : "Pocni slusanje";
        _stop.Enabled = !idle;
        _stop.Text = state switch
        {
            ControllerState.Listening => "Prekini slusanje",
            ControllerState.Connecting => "Otkazi",
            ControllerState.Connected => "Prekini vezu",
            _ => "Prekini"
        };
        RefreshStatus();
    }

    private void RefreshStatus() =>
        _status.Text = StatusPresentation.ConnectionText(Controller.State, Controller.PeerName, Controller.ListeningPort);

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        Input? input = ReadInput();
        if(input is null) return;

        try
        {
            if(_listen.Checked)
            {
                Controller.StartListening(input.Port, input.Password);
                SetStatus($"Slusa se na portu {input.Port}. Druga strana se povezuje na tvoju IP adresu i ovaj port.");
                return;
            }

            using var cts = new CancellationTokenSource();
            _connectCts = cts;
            try
            {
                await Controller.ConnectAsync(input.Host, input.Port, input.Password, cts.Token);
            }
            finally
            {
                _connectCts = null;
            }
            SetStatus($"Povezano sa {input.Host}:{input.Port}.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Povezivanje je otkazano.");
        }
        catch (Exception ex) when (ex is NetworkException or ArgumentException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        switch (Controller.State)
        {
            case ControllerState.Listening: Controller.StopListening(); break;
            case ControllerState.Connecting: _connectCts?.Cancel(); break;
            case ControllerState.Connected: Controller.Disconnect(); break;
        }
    }

    private Input? ReadInput()
    {
        if(!NetworkValidation.TryParsePort(_port.Text, out int port))
            return Reject($"Port mora biti ceo broj između {NetworkValidation.MinPort} i {NetworkValidation.MaxPort}.", _port);

        string host = _host.Text.Trim();
        if(_connect.Checked && host.Length == 0)
            return Reject("Unesi IP adresu druge strane.", _host);

        string password = _password.Text;
        switch (StatusPresentation.CheckChatPassword(password))
        {
            case ChatPasswordCheck.Empty:
                return Reject("Unesi chat lozinku (ista mora biti uneta na obe strane).", _password);
            case ChatPasswordCheck.Short:
                if (!UiMessages.Confirm(FindForm(),
                        $"Chat lozinka ima manje od {StatusPresentation.MinChatPasswordLength} znakova i lako se pogađa. Nastaviti?"))
                {
                    _password.Focus();
                    return null;
                }
                break;
        }
        return new Input(host, port, password);
    }

    private Input? Reject(string message, Control focus)
    {
        ShowError(message);
        focus.Focus();
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            Controller.StateChanged -= OnStateChanged;
            Controller.EntryRecorded -= OnEntryRecorded;
            _connectCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}