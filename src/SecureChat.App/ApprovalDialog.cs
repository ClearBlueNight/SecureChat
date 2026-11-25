using SecureChat.Core.Files;

namespace SecureChat.App;

internal sealed class ApprovalDialog : Form
{
    public bool ClosedByRemote { get; private set; }

    public ApprovalDialog(FswApprovalRequest request)
    {
        Text = "Zahtev za automatsko slanje";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 200);

        var message = new Label
        {
            Text = $"{request.Peer} trazi da automatski saljes nove file'ove iz foldera:\n\n{request.Folder}\n\n" +
                   "File'ovi se sifruju izabranim algoritmom pre slanja. Prihvatis li zahtev?",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        var accept = new Button { Text = "Prihvati", DialogResult = DialogResult.Yes, Width = 100, Height = 30 };
        var decline = new Button { Text = "Odbij", DialogResult = DialogResult.No, Width = 100, Height = 30 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.AddRange([decline, accept]);

        Controls.Add(message);
        Controls.Add(buttons);
        CancelButton = decline;
        ActiveControl = decline; // bezbedniji podrazumevani izbor
    }

    public void CloseByRemote()
    {
        ClosedByRemote = true;
        DialogResult = DialogResult.Cancel;
    }
}