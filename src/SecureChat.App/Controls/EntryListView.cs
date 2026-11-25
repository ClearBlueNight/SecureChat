using System.ComponentModel;
using SecureChat.Core.Session;
using SecureChat.Core.Storage;

namespace SecureChat.App.Controls;

public sealed class EntryListView : UserControl
{
    private const int DetailHeight = 110;
    private const int MinContentWidth = 200;
    private const int ColumnCount = 7;

    private static readonly Color SentBack = Color.FromArgb(226, 239, 255);
    private static readonly Color ReceiveBack = Color.FromArgb(232, 247, 232);
    private static readonly Color FailedBack = Color.FromArgb(255, 226, 226);
    
    private sealed class BufferedListView : ListView
    {
        public BufferedListView() => DoubleBuffered = true;
    }

    private readonly BufferedListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        VirtualMode = true,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty
    };

    private readonly TextBox _detail = new()
    {
        ReadOnly = true,
        Multiline = true,
        WordWrap = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty
    };

    private readonly List<HistoryEntry> _items = [];
    private readonly ColumnHeader _contentColumn;

    public event Action<HistoryEntry>? EntryActivated;

    [Browsable(true)]
    [DefaultValue(true)]
    public bool ScrollToNewest { get; set;} = true;
    [Browsable(true)]
    [DefaultValue(true)]
    public string LocalName { get; set; } = "";

    public int Count => _items.Count;

    public HistoryEntry? SelectedEntry => 
        _list.SelectedIndices.Count == 1 && _list.SelectedIndices[0] < _items.Count
            ? _items[_list.SelectedIndices[0]]
            : null;

    public EntryListView()
    {
    _list.Columns.Add("Vreme", 135);
        _list.Columns.Add("Smer", 75);
        _list.Columns.Add("Ucesnik", 110);
        _list.Columns.Add("Tip", 65);
        _list.Columns.Add("Algoritam", 80);
        _contentColumn = _list.Columns.Add("Sadrzaj", MinContentWidth);
        _list.Columns.Add("Status", 130);

        _list.RetrieveVirtualItem += (_, e) => e.Item = BuildItem(e.ItemIndex);
        _list.SelectedIndexChanged += (_, _) => UpdateDetail();
        _list.VirtualItemsSelectionRangeChanged += (_, _) => UpdateDetail();
        _list.SizeChanged += (_, _) => FitContentColumn();
        _list.DoubleClick += (_, _) =>
        {
            if(SelectedEntry is { } entry) EntryActivated?.Invoke(entry);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DetailHeight));
        layout.Controls.Add(_list, 0, 0);
        layout.Controls.Add(_detail, 0, 1);
        Controls.Add(layout);

        FitContentColumn();
    }

    public void SetEntries(IEnumerable<HistoryEntry> entries)
    {
        _items.Clear();
        _items.AddRange(entries);
        _list.SelectedIndices.Clear();
        _list.VirtualListSize = _items.Count;
        _list.Invalidate();
        UpdateDetail();
    }

    public void Add(HistoryEntry entry)
    {
        _items.Add(entry);
        _list.VirtualListSize = _items.Count;
        if(ScrollToNewest) _list.EnsureVisible(_items.Count - 1);
    }

    public void Clear() => SetEntries([]);

    private ListViewItem BuildItem(int index)
    {
        if(index < 0 || index >= _items.Count)
            return new ListViewItem(new string[ColumnCount]);

        HistoryEntry e = _items[index];
        return new ListViewItem([
            EntryPresentation.Time(e),
            EntryPresentation.DirectionLabel(e),
            EntryPresentation.Participant(e, LocalName),
            EntryPresentation.KindLabel(e),
            EntryPresentation.AlgorithmLabel(e.Algorithm),
            EntryPresentation.Summary(e),
            EntryPresentation.StatusLabel(e)
        ])
        {
            BackColor = EntryPresentation.ToneOf(e) switch
            {
                EntryTone.Sent => SentBack,
                EntryTone.Received => ReceiveBack,
                _ => FailedBack
            },
            ForeColor = Color.Black
        };
    }

    private void UpdateDetail() =>
        _detail.Text = SelectedEntry is { } entry ? EntryPresentation.Detail(entry) : "";

    private void FitContentColumn()
    {
        int others = _list.Columns.Cast<ColumnHeader>().Where(c => c != _contentColumn).Sum(c => c.Width);
        int available = _list.ClientSize.Width - others - SystemInformation.VerticalScrollBarWidth;
        _contentColumn.Width = Math.Max(MinContentWidth, available);
    }
}