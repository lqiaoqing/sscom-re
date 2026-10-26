using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>端口选择浮层：串口行带设备描述，网络协议收在底部分组。</summary>
internal sealed class PortPopup : Control
{
    private readonly List<string> _items = new();
    private readonly List<bool> _isPort = new();
    private readonly List<Row> _rows = new();
    private int _selectedIndex = -1;
    private int _focusIndex;
    private int _portCount;

    private sealed record Row(int Index, Rectangle Bounds, bool Port, bool Net, bool Empty);

    public PortPopup()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Visible = false;
        BackColor = Theme.Surface;
        Font = new Font("Microsoft YaHei UI", 9F);
    }

    public event Action<int>? ItemChosen;
    public event Action? Dismissed;

    public void SetItems(IEnumerable<string> items, int selectedIndex)
    {
        _items.Clear();
        _isPort.Clear();
        foreach (string item in items)
        {
            _items.Add(item);
            string normalized = ComPortService.NormalizePortName(item);
            _isPort.Add(normalized.Length > 0 && !IsProtocol(item));
        }
        _selectedIndex = selectedIndex;
        _portCount = _isPort.Count(v => v);
        _focusIndex = Math.Max(0, Math.Min(selectedIndex, _items.Count - 1));
        if (_selectedIndex < 0) _focusIndex = 0;
        RebuildRows();
        Invalidate();
    }

    public void MoveFocus(int delta)
    {
        if (_items.Count == 0) return;
        int current = _focusIndex;
        for (int step = 0; step < _items.Count; step++)
        {
            current = (current + delta + _items.Count) % _items.Count;
            if (current != _focusIndex)
            {
                _focusIndex = current;
                Invalidate();
                return;
            }
        }
    }

    private static bool IsProtocol(string text) =>
        text is "TCPClient" or "TCPServer" or "UDP";

    private int HeaderHeight() => Math.Max(Theme.Scale(this, 20), Font.Height + Theme.Scale(this, 4));
    private int PortRowHeight() => Math.Max(Theme.Scale(this, 36), Font.Height * 2);
    private int NetRowHeight() => Math.Max(Theme.Scale(this, 24), Font.Height + Theme.Scale(this, 6));
    private int SubHeaderHeight() => Math.Max(Theme.Scale(this, 16), Font.Height);
    private int DividerHeight() => Theme.Scale(this, 10);

    private void RebuildRows()
    {
        _rows.Clear();
        int pad = Theme.Scale(this, 6);
        int header = HeaderHeight();
        int portH = PortRowHeight();
        int netH = NetRowHeight();
        int divider = DividerHeight();
        int subHeader = SubHeaderHeight();
        int y = pad + header;
        int width = Math.Max(1, Width - pad * 2);

        bool hasPorts = _portCount > 0;
        if (!hasPorts)
        {
            _rows.Add(new Row(-1, new Rectangle(pad, y, width, Theme.Scale(this, 40)), false, false, true));
            y += Theme.Scale(this, 40);
        }
        else
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_isPort[i]) continue;
                _rows.Add(new Row(i, new Rectangle(pad, y, width, portH), true, false, false));
                y += portH;
            }
        }

        y += divider;
        bool hasNet = _items.Any(t => IsProtocol(t));
        if (hasNet)
        {
            y += subHeader;
            for (int i = 0; i < _items.Count; i++)
            {
                if (!IsProtocol(_items[i])) continue;
                _rows.Add(new Row(i, new Rectangle(pad, y, width, netH), false, true, false));
                y += netH;
            }
        }

        int wanted = y + pad;
        if (Height != wanted) Height = wanted;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width > 4 && Height > 4)
        {
            using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Theme.Scale(this, 12));
            Region = new Region(path);
        }
        RebuildRows();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var shell = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Scale(this, 12);
        using (var path = Theme.RoundedRect(shell, radius))
        {
            using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, path);
            using var pen = new Pen(Focused ? Theme.AccentBright : Theme.Border, 1);
            g.DrawPath(pen, path);
        }

        int pad = Theme.Scale(this, 6);
        int headerH = HeaderHeight();
        using (var headerFont = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 2F), FontStyle.Bold))
        using (var countFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 2F)))
        {
            var headerRect = new Rectangle(pad + Theme.Scale(this, 4), pad,
                Math.Max(1, Width - pad * 2 - Theme.Scale(this, 8)), headerH);
            TextRenderer.DrawText(g, "选择端口", headerFont, headerRect, Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string count = $"{_portCount} 个串口";
            TextRenderer.DrawText(g, count, countFont, headerRect, Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        foreach (Row row in _rows)
        {
            if (row.Empty)
            {
                using var emptyFont = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 1F));
                TextRenderer.DrawText(g, "未发现串口，可点标题栏刷新", emptyFont,
                    Rectangle.Inflate(row.Bounds, -Theme.Scale(this, 6), 0), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
                continue;
            }

            bool selected = row.Index == _selectedIndex;
            bool focused = row.Index == _focusIndex;
            if (selected || focused)
            {
                var fillRect = Rectangle.Inflate(row.Bounds, -2, -2);
                using var path = Theme.RoundedRect(fillRect, Theme.Scale(this, 8));
                using var brush = new SolidBrush(selected ? Theme.WithAlpha(24, Theme.Accent) : Theme.Hover);
                g.FillPath(brush, path);
            }

            int iconSize = row.Port ? Theme.Scale(this, 24) : Theme.Scale(this, 21);
            var iconRect = new Rectangle(row.Bounds.Left + Theme.Scale(this, 8),
                row.Bounds.Top + (row.Bounds.Height - iconSize) / 2, iconSize, iconSize);
            using (var path = Theme.RoundedRect(iconRect, Theme.Scale(this, 8)))
            {
                Color iconBack = selected ? Theme.WithAlpha(26, Theme.Accent) : Theme.Surface2;
                using var fill = new SolidBrush(iconBack);
                g.FillPath(fill, path);
            }
            DrawRowIcon(g, iconRect, row.Port, selected ? Theme.Accent : Theme.Muted);

            int textLeft = iconRect.Right + Theme.Scale(this, 10);
            int rightPad = Theme.Scale(this, 12);
            int checkSpace = row.Port ? Theme.Scale(this, 20) : 0;
            int textWidth = Math.Max(1, row.Bounds.Right - textLeft - rightPad - checkSpace);

            if (row.Port)
            {
                (string name, string desc) = SplitDisplay(_items[row.Index]);
                using var nameFont = new Font(Font, FontStyle.Bold);
                using var descFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 1F));
                var nameRect = new Rectangle(textLeft, row.Bounds.Top + Theme.Scale(this, 5),
                    textWidth, Theme.Scale(this, 17));
                TextRenderer.DrawText(g, name, nameFont, nameRect, Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var descRect = new Rectangle(textLeft, nameRect.Bottom - Theme.Scale(this, 3),
                    textWidth, Theme.Scale(this, 16));
                TextRenderer.DrawText(g, desc, descFont, descRect, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (selected) DrawCheck(g, row.Bounds.Right - rightPad - Theme.Scale(this, 14),
                    row.Bounds.Top + Theme.Scale(this, 20), Theme.Accent);
            }
            else
            {
                using var netFont = new Font(Font.FontFamily, Math.Max(8F, Font.Size - 0.5F));
                var textRect = new Rectangle(textLeft, row.Bounds.Top, textWidth, row.Bounds.Height);
                TextRenderer.DrawText(g, _items[row.Index], netFont, textRect, Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                string tag = _items[row.Index] == "UDP" ? "UDP" : "TCP";
                using var tagFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 2F));
                TextRenderer.DrawText(g, tag, tagFont, textRect, Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        if (_rows.Any(r => r.Net))
        {
            int firstNetTop = _rows.First(r => r.Net).Bounds.Top;
            int dividerTop = firstNetTop - SubHeaderHeight() - DividerHeight();
            int dividerY = dividerTop + DividerHeight() / 2;
            using var pen = new Pen(Theme.BorderSoft, 1);
            g.DrawLine(pen, pad + Theme.Scale(this, 4), dividerY,
                Width - pad - Theme.Scale(this, 4), dividerY);
            using var subFont = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 2F), FontStyle.Bold);
            TextRenderer.DrawText(g, "网络模式", subFont,
                new Rectangle(pad + Theme.Scale(this, 4), dividerTop + DividerHeight(),
                    Width - pad * 2, SubHeaderHeight()), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawRowIcon(Graphics g, Rectangle rect, bool port, Color color)
    {
        int inset = Theme.Scale(this, 6);
        var r = Rectangle.Inflate(rect, -inset, -inset);
        using var pen = new Pen(color, Math.Max(1.3F, Theme.Scale(this, 1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        if (port)
        {
            float cx = r.Left + r.Width / 2F;
            g.DrawLine(pen, cx, r.Top, cx, r.Bottom);
            g.DrawLine(pen, cx, r.Top + r.Height * .38F, r.Left, r.Top + r.Height * .18F);
            g.DrawLine(pen, cx, r.Top + r.Height * .62F, r.Right, r.Top + r.Height * .42F);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, cx - 1.5F, r.Bottom - 2, 3, 3);
        }
        else
        {
            g.DrawEllipse(pen, r);
            g.DrawLine(pen, r.Left, r.Top + r.Height / 2F, r.Right, r.Top + r.Height / 2F);
            g.DrawArc(pen, r.Left + r.Width * .28F, r.Top, r.Width * .44F, r.Height, 90, 180);
            g.DrawArc(pen, r.Left + r.Width * .28F, r.Top, r.Width * .44F, r.Height, 270, 180);
        }
    }

    private static void DrawCheck(Graphics g, int x, int y, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.6F, 2.2F))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new PointF(x, y + 6),
            new PointF(x + 4, y + 10),
            new PointF(x + 11, y + 1),
        });
    }

    private static (string Name, string Description) SplitDisplay(string text)
    {
        int at = text.IndexOf(" · ", StringComparison.Ordinal);
        return at >= 0
            ? (text.Substring(0, at).Trim(), text.Substring(at + 3).Trim())
            : (text.Trim(), "");
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexAt(e.Location);
        if (index >= 0 && index != _focusIndex)
        {
            _focusIndex = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int index = IndexAt(e.Location);
            if (index >= 0)
            {
                _selectedIndex = index;
                ItemChosen?.Invoke(index);
                return;
            }
        }
        base.OnMouseDown(e);
    }

    private int IndexAt(Point point)
    {
        foreach (Row row in _rows)
            if (!row.Empty && row.Bounds.Contains(point)) return row.Index;
        return -1;
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Enter or Keys.Escape => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                e.SuppressKeyPress = true;
                Dismissed?.Invoke();
                return;
            case Keys.Up:
                e.SuppressKeyPress = true;
                MoveFocus(-1);
                return;
            case Keys.Down:
                e.SuppressKeyPress = true;
                MoveFocus(1);
                return;
            case Keys.Enter:
                e.SuppressKeyPress = true;
                if (_focusIndex >= 0 && _focusIndex < _items.Count)
                {
                    _selectedIndex = _focusIndex;
                    ItemChosen?.Invoke(_focusIndex);
                }
                return;
        }
        base.OnKeyDown(e);
    }
}
