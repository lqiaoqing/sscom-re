using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>连接栏里的连接状态卡：圆点 + 状态 + 线路参数。</summary>
internal sealed class SideStatusCard : Control
{
    private bool _open;
    private string _state = "未打开";
    private string _meta = "";
    private Font? _stateFont;
    private Font? _metaFont;

    public SideStatusCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        AutoSize = false;
        Height = 46;
        BackColor = Color.Transparent;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _stateFont?.Dispose();
        _stateFont = null;
        _metaFont?.Dispose();
        _metaFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateFont?.Dispose();
            _stateFont = null;
            _metaFont?.Dispose();
            _metaFont = null;
        }
        base.Dispose(disposing);
    }

    public void SetStatus(bool open, string state, string meta)
    {
        _open = open;
        _state = state;
        _meta = meta;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Scale(this, 9);
        Color fill = _open ? Theme.WithAlpha(20, Theme.Green) : Theme.WithAlpha(120, Theme.Surface);
        Color border = _open ? Theme.WithAlpha(75, Theme.Green) : Theme.Border;
        Color dot = _open ? Theme.Green : Theme.Dim;
        Color state = _open ? Theme.Green : Theme.Muted;

        using (var path = Theme.RoundedRect(bounds, radius))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            using var pen = new Pen(border, 1);
            g.DrawPath(pen, path);
        }

        int pad = Theme.Scale(this, 12);
        int dotSize = Theme.Scale(this, 7);
        int x = pad;
        int cy = Height / 2;
        using (var brush = new SolidBrush(dot))
            g.FillEllipse(brush, x, cy - dotSize / 2, dotSize, dotSize);

        x += dotSize + Theme.Scale(this, 8);
        Font stateFont = _stateFont ??= new Font(Font, FontStyle.Bold);
        Size stateSize = TextRenderer.MeasureText(_state, stateFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, _state, stateFont,
            new Rectangle(x, 0, stateSize.Width + 2, Height), state,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        x += stateSize.Width + Theme.Scale(this, 8);
        var metaRect = new Rectangle(x, 0, Math.Max(1, Width - x - pad), Height);
        Font metaFont = _metaFont ??= new Font(Font.FontFamily, Math.Max(7F, Font.Size - 0.75F));
        TextRenderer.DrawText(g, _meta, metaFont, metaRect, _open ? Theme.Muted : Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}
