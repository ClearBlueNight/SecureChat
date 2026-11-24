using SecureChat.Core.Configuration;
using SecureChat.Core.Logging;
using SecureChat.Core.Protocol;
using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App;

internal sealed class LoginForm : Form
{
    private const int InputRowHeight = 34;
    private const int ConfirmRow = 3;

    private readonly TextBox _user = new() { MaxLength = ProtocolLimits.MaxSenderLength, Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly Label _confirmLabel = NewLable("Ponovi lozinku");
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button _login = new() { Text = "Prijavi se", Width = 110, Height = 30 };
    private readonly Button _forgot = new() { Text = "Zaboravljena lozinka…", Width = 160, Height = 30 };
    private readonly Button _exit = new() { Text = "Izlaz", Width = 80, Height = 30 };
    private readonly TableLayoutPanel _layout = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        Padding = new Padding(16)
    };
    private bool _busy;

    public LoginResult? Result { get; private set; }

    public LoginForm()
    {
        Text = "SecureChat – prijava";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 310);
        AcceptButton = _login;
        CancelButton = _exit;

        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));             // into
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, InputRowHeight)); // Username
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, InputRowHeight)); // Password
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));              // Confirm 
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));             // message
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));             // button

        var intro = new Label
        {
            Text = "Lozinka aplikacije stiti istoriju razgovora na ovom racunaru. " +
                   "Ona nije chat lozinka i druga strana je ne zna.",
            Dock = DockStyle.Fill
        };

        AddSpanning(intro, 0);
        AddRow(1, "Korisnicko ime", _user);
        AddRow(2, "Lozinka aplikacije", _password);
        AddRow(ConfirmRow, _confirmLabel, _confirm);
        AddSpanning(_status, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_login, _forgot, _exit]);
        AddSpanning(buttons, 5);
        Controls.Add(_layout);

        _user.TextChanged += (_, _) => UpdateMode();
        _login.Click += OnLoginClicked;
        _forgot.Click += OnForgotClicked;
        _exit.Click += (_, _) => DialogResult = DialogResult.Cancel;
        FormClosing += (_, e) => e.Cancel = _busy; // dok traje provera lozinke, prozor se ne zatvara

        UpdateMode();
        ShowMessage("Prvi put? Unesi novo korisnicko ime i izaberi lozinku (dvaput).", error: false);
    }

    // Events
    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        string name = _user.Text;
        string password = _password.Text;
        string? confirm = _confirm.Visible? _confirm.Text : null;

        if(string.IsNullOrWhiteSpace(name)) { Reject("Unesi korisnicko ime.", _user); return;}
        if(password.Length == 0) { Reject("Unesi lozinku aplikacije.", _password); return;}

        SetBusy(true);
        LoginResult result;
        try
        {
            result = await Task.Run(() => UserProfile.Login(name, password, confirm));
        }
        catch (WrongHistoryPasswordException)
        {
            SetBusy(false);
            Reject("Pogresna lozinka aplikacije.", _password);
            _password.SelectAll();
            return;
        }
        catch (Exception ex) when (ex is HistoryException or ArgumentException)
        {
            SetBusy(false);
            Reject(ex.Message, null);
            return;
        }

        SetBusy(false);
        Result = result;
        DialogResult = DialogResult.OK;
    }

    private void OnForgotClicked(object? sender, EventArgs e)
    {
        string name;
        try { name = AppPaths.NormalizeUserName(_user.Text); }
        catch (ArgumentException ex) { Reject(ex.Message, _user); return; }

        DialogResult answer = MessageBox.Show(this, 
            $"Ovo trajno brise sifrovanu istoriju korisnika '{name}'. Bez lozinke se istorija ne moze povratiti, " + 
            "pa je jedina opcija da pocnes ispocetka.\n\nPrimljeni file'ovi i evidencija aktivnosti se ne brisu.\n\nObrisati istoriju?", 
            "Zaboravljena lozinka.", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if(answer != DialogResult.Yes) return;

        try
        {
            UserProfile.ResetHistory(name);
            new ActivityLogger(AppPaths.LogsFolder(AppPaths.ForUser(name)))
                .Warning("HISTORY", "Istorija je obrisana zbog zaboravljene lozinke aplikacije.");
        }
        catch (HistoryException ex)
        {
            Reject(ex.Message, null);
            return;
        }

        _password.Clear();
        _confirm.Clear();
        UpdateMode();
        ShowMessage("Istorija je obrisana. Unesi novu lozinku (dvaput).", error: false);
        _password.Focus();
    }

    // Helper methods
    private bool? HasHistory()
    {
        try { return UserProfile.HasHistory(_user.Text); }
        catch (ArgumentException) { return null; }
    }
    private void UpdateMode()
    {
        bool? hasHistory = HasHistory();
        bool showConfirm = hasHistory == false;

        _confirmLabel.Visible = _confirm.Visible = showConfirm;
        _layout.RowStyles[ConfirmRow].Height = showConfirm ? InputRowHeight : 0;
        _forgot.Enabled = hasHistory == true;
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _user.Enabled = _password.Enabled = _confirm.Enabled = !busy;
        _login.Enabled = _forgot.Enabled = _exit.Enabled = !busy;
        if (busy) ShowMessage("Provera lozinke…", error: false);
        else UpdateMode();
    }
    private void Reject(string message, Control? focus)
    {
        ShowMessage(message, error: true);
        focus?.Focus();
    }
    private void ShowMessage(string text, bool error)
    {
        _status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
        _status.Text = text;
    }
    private void AddRow(int row, string label, Control input) => AddRow(row, NewLable(label), input);
    private void AddRow(int row, Label label, Control input)
    {
        _layout.Controls.Add(label, 0, row);
        _layout.Controls.Add(input, 1, row);
    }
    private void AddSpanning(Control control, int row)
    {
        _layout.Controls.Add(control, 0, row);
        _layout.SetColumnSpan(control, 2);
    }
    private static Label NewLable(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
}