using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

public sealed class CommandPaletteItem
{
    public CommandPaletteItem(string section, string badge, string title, string subtitle, string data, bool hex, int row)
    {
        Section = section;
        Badge = badge;
        Title = title;
        Subtitle = subtitle;
        Data = data;
        Hex = hex;
        Row = row;
    }

    public string Section { get; }
    public string Badge { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Data { get; }
    public bool Hex { get; }
    public int Row { get; }
}

/// <summary>Ctrl+K 命令面板：只列已保存命令；Enter 发送，Ctrl+Enter 填入发送框。</summary>
internal sealed class CommandPalette : Panel
{
    private readonly ThemeTextBox _search;
    private readonly Panel _listHost;
    private readonly PaletteList _list;
    private readonly Label _footer;
    private readonly List<CommandPaletteItem> _items = new();
    private readonly TableLayoutPanel _root;

    public CommandPalette()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BorderSoft;
        Padding = new Padding(1);
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
        };

        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 58)));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 34)));

        var searchRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(Theme.Scale(this, 14), Theme.Scale(this, 10), Theme.Scale(this, 14), Theme.Scale(this, 8)),
            BackColor = Theme.Surface,
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _search = new ThemeTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, Font.Size + 1F),
            PlaceholderText = "搜索命令、注释或 AT 关键字…",
            Margin = new Padding(0, 0, Theme.Scale(this, 10), 0),
        };
        _search.TextChanged += (_, _) => RefreshFilter();
        _search.KeyDown += OnSearchKeyDown;
        searchRow.Controls.Add(_search, 0, 0);
        var ctrlK = new Label
        {
            Text = "Ctrl K",
            AutoSize = true,
            ForeColor = Theme.Dim,
            Font = new Font("Consolas", Math.Max(8F, Font.Size - 1F)),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Theme.Scale(this, 6), 0, 0),
        };
        searchRow.Controls.Add(ctrlK, 1, 0);
        _root.Controls.Add(searchRow, 0, 0);

        _listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        _list = new PaletteList
        {
            Dock = DockStyle.Top,
            BackColor = Theme.Surface,
        };
        _list.SelectionChanged += (_, _) => EnsureSelectionVisible();
        _list.ItemActivated += item => Activate?.Invoke(item);
        _list.ItemClicked += (_, _) => _search.Focus();
        _listHost.Controls.Add(_list);
        _root.Controls.Add(_listHost, 0, 1);

        _footer = new Label
        {
            Dock = DockStyle.Fill,
            Text = "↑↓ 选择    Enter 发送    Ctrl+Enter 填入发送框    Esc 关闭",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Dim,
            Padding = new Padding(Theme.Scale(this, 14), 0, Theme.Scale(this, 10), 0),
            BackColor = Theme.Surface,
            Font = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 0.5F)),
            Margin = new Padding(0),
        };
        _root.Controls.Add(_footer, 0, 2);
        Controls.Add(_root);
        RefreshTheme();
    }

    public void RefreshTheme()
    {
        BackColor = Theme.BorderSoft;
        _root.BackColor = Theme.Surface;
        _search.BackColor = Theme.Field;
        _search.ForeColor = Theme.Text;
        ApplyThemeRecursive(this);
        _listHost.BackColor = Theme.Surface;
        _list.BackColor = Theme.Surface;
        _list.Invalidate();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width > 4 && Height > 4)
        {
            using GraphicsPath path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Theme.Scale(this, 14));
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using GraphicsPath path = Theme.RoundedRect(bounds, Theme.Scale(this, 14));
        using var pen = new Pen(Theme.Border, 1);
        g.DrawPath(pen, path);
    }

    private static void ApplyThemeRecursive(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case TableLayoutPanel panel:
                    panel.BackColor = Theme.Surface;
                    break;
                case Panel panel:
                    panel.BackColor = Theme.Surface;
                    break;
                case Label label:
                    label.ForeColor = Theme.Dim;
                    label.BackColor = Color.Transparent;
                    break;
                case ThemeTextBox box:
                    box.BackColor = Theme.Field;
                    box.ForeColor = Theme.Text;
                    break;
            }
            ApplyThemeRecursive(child);
        }
    }

    public event Action<CommandPaletteItem>? Activate;
    public event Action<CommandPaletteItem>? FillRequested;
    public event Action? Dismissed;
    public int ItemCount => _items.Count;
    public int FilteredItemCount => _list.ItemCount;
    public void SetQueryForTest(string query) => _search.Text = query;

    public void SetItems(IEnumerable<CommandPaletteItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _search.Clear();
        RefreshFilter();
    }

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    public void CloseFromOwner()
    {
        _search.Clear();
    }

    private void RefreshFilter()
    {
        string query = _search.Text.Trim();
        string[] tokens = query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        List<CommandPaletteItem> filtered = _items.Where(item =>
        {
            string haystack = item.Section + " " + item.Badge + " " + item.Title + " " + item.Subtitle;
            return tokens.All(token => haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }).ToList();
        _list.SetItems(filtered);
        _listHost.AutoScrollPosition = new Point(0, 0);
        if (filtered.Count > 0) EnsureSelectionVisible();
    }

    private void EnsureSelectionVisible()
    {
        int index = _list.SelectedItemIndex;
        if (index < 0) return;
        int top = _list.ItemTop(index);
        int viewTop = -_listHost.AutoScrollPosition.Y;
        int viewBottom = viewTop + _listHost.ClientSize.Height;
        if (top < viewTop || top + _list.ItemHeight > viewBottom)
        {
            int wanted = Math.Max(0, top - Theme.Scale(this, 6));
            _listHost.AutoScrollPosition = new Point(0, wanted);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                _list.MoveSelection(1);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.Up:
                _list.MoveSelection(-1);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.PageDown:
                _list.MoveSelection(5);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.PageUp:
                _list.MoveSelection(-5);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.Enter:
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (_list.SelectedItem is not { } item) return;
                if (e.Control) FillRequested?.Invoke(item);
                else Activate?.Invoke(item);
                break;
            case Keys.Escape:
                e.SuppressKeyPress = true;
                e.Handled = true;
                Dismissed?.Invoke();
                break;
        }
    }

    /// <summary>内部列表：分组标题 + 两行命令；自身高度即内容高度，外层 AutoScroll 负责滚动。</summary>
    private sealed class PaletteList : Control
    {
        private readonly List<CommandPaletteItem> _items = new();
        private readonly List<Row> _rows = new();
        private int _selectedItem = -1;
        private int _height = 1;

        public PaletteList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Theme.Surface;
            TabStop = false;
        }

        public event EventHandler? SelectionChanged;
        public event Action<CommandPaletteItem>? ItemActivated;
        public event EventHandler? ItemClicked;
        public int SelectedItemIndex => _selectedItem;
        public int ItemCount => _items.Count;
        public int ItemHeight => Theme.Scale(this, 50);
        public CommandPaletteItem? SelectedItem =>
            _selectedItem >= 0 && _selectedItem < _items.Count ? _items[_selectedItem] : null;

        private sealed record Row(CommandPaletteItem? Item, string Header, int Top, int Height);

        public void SetItems(List<CommandPaletteItem> items)
        {
            _items.Clear();
            _items.AddRange(items);
            _selectedItem = _items.Count > 0 ? 0 : -1;
            BuildRows();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            int next = Math.Max(0, Math.Min(_items.Count - 1, _selectedItem + delta));
            if (next == _selectedItem) return;
            _selectedItem = next;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public int ItemTop(int itemIndex)
        {
            foreach (Row row in _rows)
                if (row.Item is not null && _items.IndexOf(row.Item) == itemIndex) return row.Top;
            return 0;
        }

        private void BuildRows()
        {
            _rows.Clear();
            int y = Theme.Scale(this, 5);
            string section = "";
            foreach (CommandPaletteItem item in _items)
            {
                if (item.Section != section)
                {
                    section = item.Section;
                    _rows.Add(new Row(null, section, y, Theme.Scale(this, 27)));
                    y += Theme.Scale(this, 27);
                }
                _rows.Add(new Row(item, "", y, ItemHeight));
                y += ItemHeight;
            }
            _height = Math.Max(Theme.Scale(this, 64), y + Theme.Scale(this, 4));
            Height = _height;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.FillBackground(g, this);
            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, "没有匹配的已保存命令", Font,
                    new Rectangle(Theme.Scale(this, 18), Theme.Scale(this, 20), Math.Max(1, Width - Theme.Scale(this, 36)), Theme.Scale(this, 24)),
                    Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }

            foreach (Row row in _rows)
            {
                var band = new Rectangle(0, row.Top, Width, row.Height);
                if (!band.IntersectsWith(e.ClipRectangle)) continue;
                if (row.Item is null)
                {
                    using var line = new Pen(Theme.BorderSoft, 1);
                    g.DrawLine(line, Theme.Scale(this, 16), row.Top + row.Height - 8,
                        Width - Theme.Scale(this, 16), row.Top + row.Height - 8);
                    using var font = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 1.5F), FontStyle.Bold);
                    TextRenderer.DrawText(g, row.Header, font,
                        new Rectangle(Theme.Scale(this, 16), row.Top, Width - Theme.Scale(this, 32), row.Height),
                        Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis);
                    continue;
                }

                int itemIndex = _items.IndexOf(row.Item);
                bool isSelected = itemIndex == _selectedItem;
                var fill = new Rectangle(Theme.Scale(this, 8), row.Top + 2,
                    Math.Max(1, Width - Theme.Scale(this, 16)), row.Height - 4);
                if (isSelected)
                {
                    using var path = Theme.RoundedRect(fill, Theme.Scale(this, 8));
                    using var brush = new SolidBrush(Theme.WithAlpha(24, Theme.Accent));
                    g.FillPath(brush, path);
                    using var border = new Pen(Theme.WithAlpha(70, Theme.Accent), 1);
                    g.DrawPath(border, path);
                    using var bar = new SolidBrush(Theme.Accent);
                    g.FillRectangle(bar, fill.Left, row.Top + 9, Theme.Scale(this, 3), row.Height - 18);
                }

                int left = Theme.Scale(this, 18);
                int badgeWidth = Theme.Scale(this, 54);
                var badge = new Rectangle(left, row.Top + (row.Height - Theme.Scale(this, 20)) / 2,
                    badgeWidth, Theme.Scale(this, 20));
                using (var path = Theme.RoundedRect(badge, Theme.Scale(this, 4)))
                using (var brush = new SolidBrush(isSelected ? Theme.WithAlpha(34, Theme.Accent) : Theme.Surface2))
                    g.FillPath(brush, path);
                using (var badgeFont = new Font("Consolas", Math.Max(7F, Font.Size - 1.2F)))
                    TextRenderer.DrawText(g, row.Item.Badge, badgeFont,
                        new Rectangle(badge.Left, badge.Top, badge.Width, badge.Height),
                        isSelected ? Theme.Accent : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                int textLeft = badge.Right + Theme.Scale(this, 10);
                int textWidth = Math.Max(1, Width - textLeft - Theme.Scale(this, 12));
                using var titleFont = new Font(Font.FontFamily, Math.Max(8F, Font.Size + 0.5F), FontStyle.Bold);
                using var subFont = new Font("Consolas", Math.Max(7F, Font.Size - 1.2F));
                TextRenderer.DrawText(g, row.Item.Title, titleFont,
                    new Rectangle(textLeft, row.Top + 4, textWidth, Theme.Scale(this, 20)),
                    Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (row.Item.Subtitle.Length > 0)
                {
                    TextRenderer.DrawText(g, row.Item.Subtitle, subFont,
                        new Rectangle(textLeft, row.Top + Theme.Scale(this, 23), textWidth, Theme.Scale(this, 18)),
                        Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
                if (isSelected)
                {
                    using var hintFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 1F));
                    TextRenderer.DrawText(g, "Enter", hintFont,
                        new Rectangle(Width - Theme.Scale(this, 54), row.Top, Theme.Scale(this, 40), row.Height),
                        Theme.Dim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            CommandPaletteItem? hit = HitTest(e.Location);
            if (hit is null) return;
            int index = _items.IndexOf(hit);
            if (index < 0) return;
            _selectedItem = index;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ItemClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            CommandPaletteItem? hit = HitTest(e.Location);
            if (hit is not null) ItemActivated?.Invoke(hit);
        }

        private CommandPaletteItem? HitTest(Point point)
        {
            foreach (Row row in _rows)
                if (row.Item is not null && point.Y >= row.Top && point.Y < row.Top + row.Height)
                    return row.Item;
            return null;
        }
    }
}
