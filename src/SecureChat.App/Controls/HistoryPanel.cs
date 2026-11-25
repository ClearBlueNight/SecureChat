using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App.Controls;

internal sealed class HistoryPanel : ControllerPanel
{
    private const string AllPeers = "(svi ucesnici)";

    private readonly ComboBox _peer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _text = new();
    private readonly Button _clear = new() { Text = "Obrisi istoriju…" };
    private readonly Label _count = new() { TextAlign = ContentAlignment.MiddleRight };
    private readonly EntryListView _list = new() { ScrollToNewest = false };
    private bool _updating;

    public HistoryPanel(ChatController controller) : base(controller)
    {
        _list.LocalName = controller.UserName;

        var toolbar = LayoutHelper.Columns(
            new ColumnStyle(SizeType.Absolute, 90), new ColumnStyle(SizeType.Absolute, 190),
            new ColumnStyle(SizeType.Absolute, 50), new ColumnStyle(SizeType.Percent, 100),
            new ColumnStyle(SizeType.Absolute, 130), new ColumnStyle(SizeType.Absolute, 190));
        toolbar.Controls.Add(new Label { Text = "Učesnik:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _peer.Anchor = _text.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.Add(_peer, 1, 0);
        toolbar.Controls.Add(new Label { Text = "Tekst:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        toolbar.Controls.Add(_text, 3, 0);
        _clear.Dock = _count.Dock = DockStyle.Fill;
        toolbar.Controls.Add(_clear, 4, 0);
        toolbar.Controls.Add(_count, 5, 0);

        var layout = LayoutHelper.Stack();
        layout.AddFixed(34, toolbar);
        layout.AddFill(_list);
        Controls.Add(layout);

        _peer.SelectedIndexChanged += (_, _) => ApplyFilter();
        _text.TextChanged += (_, _) => ApplyFilter();
        _clear.Click += OnClearClicked;
        _list.EntryActivated += OpenFileOf;
    }

    public void RefreshView()
    {
        string? selected = _peer.SelectedItem as string;

        _updating = true;
        _peer.Items.Clear();
        _peer.Items.Add(AllPeers);
        foreach (string peer in Controller.History.Peers()) _peer.Items.Add(peer);
        int index = selected is null ? 0 : _peer.Items.IndexOf(selected);
        _peer.SelectedIndex = Math.Max(0, index);
        _updating = false;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_updating) return;

        string? peer = _peer.SelectedIndex > 0 ? _peer.SelectedItem as string : null;
        IReadOnlyList<HistoryEntry> found = Controller.History.Query(peer, _text.Text);
        _list.SetEntries(found);
        _count.Text = $"Prikazano {found.Count} od {Controller.History.Count}";
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        if (!UiMessages.Confirm(FindForm(),
                "Trajno brises celu istoriju razgovora. Primljeni fajlovi i evidencija aktivnosti se ne brisu.\n\nObrisati istoriju?"))
            return;
            
        try
        {
            Controller.ClearHistory();
        }
        catch (HistoryException ex)
        {
            ShowError(ex.Message);
            return;
        }
        RefreshView();
        SetStatus("Istorija je obrisana.");
    }
}