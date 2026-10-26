using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

internal enum ToastTone
{
    Info,
    Warning,
}

/// <summary>非模态短提示，覆盖在日志底部；自身不接收鼠标输入，2.2 秒后自动消失。</summary>
internal sealed class ToastPanel : Panel
{
    private string _message = "";
    private ToastTone _tone;

    public ToastPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Raised;
        Visible = false;
        TabStop = false;
        Enabled = false;
    }

    public void SetMessage(string message, ToastTone tone, int maxWidth)
    {
        _message = message;
        _tone = tone;
        int pad = Theme.Scale(this, 13);
        int accent = Theme.Scale(this, 3);
        int gap = Theme.Scale(this, 10);
        int textWidth = Math.Max(Theme.Scale(this, 80), maxWidth - pad * 2 - accent - gap);
        Size measured = TextRenderer.MeasureText(message, Font, new Size(textWidth, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        int width = Math.Min(maxWidth, measured.Width + pad * 2 + accent + gap);
        int height = Math.Max(Font.Height + pad, measured.Height + pad);
        Size = new Size(Math.Max(Theme.Scale(this, 120), width), height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        using (var fill = new SolidBrush(Theme.Raised)) g.FillRectangle(fill, bounds);
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);

        int pad = Theme.Scale(this, 13);
        int accent = Theme.Scale(this, 3);
        int gap = Theme.Scale(this, 10);
        int barX = Theme.Scale(this, 9);
        int barInset = Theme.Scale(this, 8);
        var bar = new Rectangle(barX, barInset, Math.Max(2, accent), Math.Max(1, bounds.Height - barInset * 2));
        using (GraphicsPath path = Theme.RoundedRect(bar, Math.Max(1, accent / 2)))
        using (var brush = new SolidBrush(_tone == ToastTone.Warning ? Theme.Amber : Theme.Accent))
            g.FillPath(brush, path);

        int textX = barX + accent + gap;
        var textRect = new Rectangle(textX, 0, Math.Max(1, bounds.Width - textX - pad), bounds.Height);
        TextRenderer.DrawText(g, _message, Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
