using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>等宽胶囊式复选框，用于 RTS / DTR；保留 CheckBox 的 Checked/Click 语义。</summary>
internal sealed class ThemeCheckChip : CheckBox
{
    private bool _hover;
    private bool _pressed;

    public ThemeCheckChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        BackColor = Theme.Surface2;
        ForeColor = Theme.Muted;
        Cursor = Cursors.Hand;
        Height = 38;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 用不透明父背景先清整块，避免取消勾选后圆角外侧留下上一帧的琥珀色。
        using (var clear = new SolidBrush(Theme.Surface2))
            g.FillRectangle(clear, ClientRectangle);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Scale(this, 8);
        bool filled = Checked;

        Color back = filled ? Theme.WithAlpha(24, Theme.Amber) : Theme.Field;
        Color border = filled ? Theme.WithAlpha(130, Theme.Amber) : Theme.Border;
        if (_pressed) back = filled ? Theme.WithAlpha(40, Theme.Amber) : Theme.Pressed;
        else if (_hover && !filled) back = Theme.Hover;

        using (var path = Theme.RoundedRect(bounds, radius))
        {
            using (var brush = new SolidBrush(back)) g.FillPath(brush, path);
            using var pen = new Pen(border, 1);
            g.DrawPath(pen, path);
        }

        int box = Theme.Scale(this, 16);
        int left = Theme.Scale(this, 10);
        int top = (Height - box) / 2;
        var boxRect = new Rectangle(left, top, box, box);
        using (var path = Theme.RoundedRect(boxRect, Theme.Scale(this, 4)))
        {
            using var fill = new SolidBrush(filled ? Theme.Amber : Theme.CheckFill);
            g.FillPath(fill, path);
            using var pen = new Pen(filled ? Theme.Amber : Theme.CheckBorder, Math.Max(1F, Theme.Scale(this, 1)));
            g.DrawPath(pen, path);
        }
        if (filled)
        {
            using var pen = new Pen(Color.White, Math.Max(1.5F, Theme.Scale(this, 1)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            float x = boxRect.Left, y = boxRect.Top, w = boxRect.Width, h = boxRect.Height;
            g.DrawLines(pen, new[]
            {
                new PointF(x + w * .22F, y + h * .52F),
                new PointF(x + w * .42F, y + h * .72F),
                new PointF(x + w * .78F, y + h * .30F),
            });
        }

        var textRect = new Rectangle(left + box + Theme.Scale(this, 8), 0,
            Math.Max(1, Width - left - box - Theme.Scale(this, 18)), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, filled ? Theme.AmberText : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(bounds, -Theme.Scale(this, 2), -Theme.Scale(this, 2));
            using var path = Theme.RoundedRect(focus, Math.Max(2, radius - 1));
            using var pen = new Pen(Theme.WithAlpha(150, Theme.Accent), 1);
            g.DrawPath(pen, path);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
