namespace SecureChat.App;

internal static class LayoutHelper
{
    private const int FieldRowHeight = 30;
    private static readonly Font BoldFont = new(SystemFonts.DefaultFont, FontStyle.Bold);

    public static Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = BoldFont
    };

    public static Label Hint(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = SystemColors.GrayText
    };

    public static TableLayoutPanel Stack()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    public static TableLayoutPanel Columns(params ColumnStyle[] styles)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = styles.Length,
            RowCount = 1,
            Margin = Padding.Empty
        };
        foreach (ColumnStyle style in styles) table.ColumnStyles.Add(style);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    public static TableLayoutPanel Form(int labelWidth)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    public static void AddFixed(this TableLayoutPanel table, int height, Control control)
    {
        int row = table.RowStyles.Count;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 0, row);
    }

    public static void AddFill(this TableLayoutPanel table, Control control)
    {
        int row = table.RowStyles.Count;
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 0, row);
    }

    public static void AddField(this TableLayoutPanel table, string label, Control input)
    {
        int row = table.RowStyles.Count;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        input.Dock = DockStyle.Fill;
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        table.Controls.Add(input, 1, row);
    }

    public static void AddWide(this TableLayoutPanel table, Control control, int height)
    {
        int row = table.RowStyles.Count;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }
}