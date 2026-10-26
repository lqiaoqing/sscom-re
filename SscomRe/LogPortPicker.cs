using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>日志头里的紧凑端口下拉：显示当前端口/网络模式，点击复用端口浮层。</summary>
internal sealed class LogPortPicker : Control
{
    private string _display = "未连接";
    private bool _hover;
    private bool _opened;

    public LogPortPicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Height = 24;
    }

    public event EventHandler? PickerRequested;

    public bool Opened
    {
        get => _opened;
        set
        {
            if (_opened == value) return;
            _opened = value;
            Invalidate();
        }
    }

    public string DisplayText => _display;

    public int PreferredWidth
    {
        get
        {
            Size text = TextRenderer.MeasureText(DisplayText, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return text.Width + Theme.Scale(this, 26);
        }
    }

    public void SetDisplay(string text)
    {
        string next = string.IsNullOrWhiteSpace(text) ? "未连接" : text.Trim();
        if (_display == next) return;
        _display = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillBackground(g, this);

        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        if (_hover || _opened)
        {
            int radius = Theme.Scale(this, 7);
            using GraphicsPath path = Theme.RoundedRect(bounds, radius);
            using var fill = new SolidBrush(_opened ? Theme.WithAlpha(24, Theme.Accent) : Theme.Hover);
            g.FillPath(fill, path);
            if (_opened)
            {
                using var borderPen = new Pen(Theme.WithAlpha(70, Theme.Accent), 1);
                g.DrawPath(borderPen, path);
            }
        }

        int arrow = Theme.Scale(this, 13);
        int pad = Theme.Scale(this, 6);
        int textWidth = Math.Max(1, Width - pad * 2 - arrow);
        var textRect = new Rectangle(pad, 0, textWidth, Height);
        TextRenderer.DrawText(g, _display, Font, textRect,
            _display == "未连接" ? Theme.Dim : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        using var pen = new Pen(_opened || _hover ? Theme.Accent : Theme.Muted,
            Math.Max(1.4F, Theme.Scale(this, 1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        int cx = Width - pad - arrow / 2;
        int cy = Height / 2 + (_opened ? -1 : 1);
        int half = Math.Max(3, arrow / 3);
        g.DrawLines(pen, _opened
            ? new[]
            {
                new PointF(cx - half, cy + half / 2F),
                new PointF(cx, cy - half / 2F),
                new PointF(cx + half, cy + half / 2F),
            }
            : new[]
            {
                new PointF(cx - half, cy - half / 2F),
                new PointF(cx, cy + half / 2F),
                new PointF(cx + half, cy - half / 2F),
            });

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = Rectangle.Inflate(bounds, -1, -1);
            using var focusPen = new Pen(Theme.WithAlpha(170, Theme.Accent), 1);
            g.DrawRectangle(focusPen, focus);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            PickerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Enter or Keys.Space or Keys.Down => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down)
        {
            e.SuppressKeyPress = true;
            PickerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }
}
