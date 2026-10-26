using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>开关式复选框：用于多条面板的循环发送，保留原 CheckBox 的 Checked 语义。</summary>
internal sealed class ThemeSwitch : CheckBox
{
    public ThemeSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Muted;
        Cursor = Cursors.Hand;
        AutoSize = true;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int trackW = Theme.Scale(this, 30);
        int trackH = Theme.Scale(this, 16);
        Size text = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int gap = text.IsEmpty ? 0 : Theme.Scale(this, 7);
        int h = Math.Max(trackH, text.Height) + Theme.Scale(this, 4);
        return new Size(trackW + gap + text.Width + Padding.Horizontal,
            h + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillBackground(g, this);

        int trackW = Theme.Scale(this, 30);
        int trackH = Theme.Scale(this, 16);
        int gap = string.IsNullOrEmpty(Text) ? 0 : Theme.Scale(this, 7);
        int trackY = (Height - trackH) / 2;
        var track = new Rectangle(Theme.Scale(this, 2), trackY, trackW, trackH);

        Color trackBack = Checked ? Theme.Accent : Theme.CheckFill;
        Color trackBorder = Checked ? Theme.Accent : Theme.CheckBorder;
        using (GraphicsPath path = Theme.RoundedRect(track, trackH / 2))
        {
            using var fill = new SolidBrush(trackBack);
            g.FillPath(fill, path);
            using var pen = new Pen(trackBorder, 1);
            g.DrawPath(pen, path);
        }

        int knob = trackH - Theme.Scale(this, 4);
        int knobX = Checked ? track.Right - knob - Theme.Scale(this, 2) : track.Left + Theme.Scale(this, 2);
        var knobRect = new Rectangle(knobX, track.Top + Theme.Scale(this, 2), knob, knob);
        using (var knobBrush = new SolidBrush(Checked ? Theme.AccentInk : Theme.Muted))
            g.FillEllipse(knobBrush, knobRect);

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(track.Right + gap, 0,
                Math.Max(1, Width - track.Right - gap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Checked ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = Rectangle.Inflate(ClientRectangle, -1, -1);
            using var pen = new Pen(Theme.WithAlpha(160, Theme.Accent), 1);
            g.DrawRectangle(pen, focus);
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
