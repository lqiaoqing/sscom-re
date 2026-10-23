using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// 深色自绘复选框。勾选时用琥珀色，保留原版“已启用”提示的语义；
/// <see cref="Highlighted"/> 控制是否给整块标签加暖色底（原版用黄底）。
/// </summary>
internal sealed class ThemeCheckBox : CheckBox
{
    public bool Highlighted { get; set; } = true;

    public ThemeCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Muted;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int box = Theme.Scale(this, 14);
        Size text = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int gap = text.IsEmpty ? 0 : Theme.Scale(this, 6);
        int w = box + gap + text.Width + Theme.Scale(this, 4) + Padding.Horizontal;
        int h = Math.Max(box, text.Height) + Theme.Scale(this, 4) + Padding.Vertical;
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillBackground(g, this);

        Rectangle client = ClientRectangle;
        bool lit = Highlighted && Checked;
        if (lit && Text.Length > 0)
        {
            Rectangle warm = Rectangle.Inflate(client, -1, -1);
            using GraphicsPath warmPath = Theme.RoundedRect(warm, Theme.Scale(this, 4));
            using var fill = new SolidBrush(Theme.WithAlpha(20, Theme.Amber));
            using var edge = new Pen(Theme.WithAlpha(44, Theme.Amber), 1);
            g.FillPath(fill, warmPath);
            g.DrawPath(edge, warmPath);
        }

        int boxSize = Theme.Scale(this, 14);
        int radius = Math.Max(2, Theme.Scale(this, 2));
        int boxX = Theme.Scale(this, 2);
        int boxY = (ClientSize.Height - boxSize) / 2;
        if (Text.Length == 0)
            boxX = (ClientSize.Width - boxSize) / 2;
        var box = new Rectangle(boxX, boxY, boxSize, boxSize);
        using (GraphicsPath boxPath = Theme.RoundedRect(box, radius))
        {
            using var fill = new SolidBrush(Checked ? Theme.Amber : Theme.Hex("#0C141F"));
            g.FillPath(fill, boxPath);
            using var edge = new Pen(Checked ? Theme.Hex("#FFD08A") : Theme.Hex("#4A5C74"), 1);
            g.DrawPath(edge, boxPath);
        }
        if (Checked)
        {
            using var check = new Pen(Theme.AmberInk, Math.Max(1.6F, Theme.Scale(this, 2) * 0.9F))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            float x = box.X + box.Width * 0.22F;
            float y = box.Y + box.Height * 0.52F;
            g.DrawLines(check, new[]
            {
                new PointF(x, y),
                new PointF(box.X + box.Width * 0.43F, box.Y + box.Height * 0.72F),
                new PointF(box.X + box.Width * 0.78F, box.Y + box.Height * 0.28F),
            });
        }

        if (Text.Length > 0)
        {
            int textX = box.Right + Theme.Scale(this, 6);
            var textRect = new Rectangle(textX, 0, Math.Max(1, ClientSize.Width - textX), ClientSize.Height);
            Color fore = !Enabled ? Theme.Dim : lit ? Theme.Hex("#E7CFA9") : Theme.Muted;
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = Rectangle.Inflate(client, -1, -1);
            using var pen = new Pen(Theme.WithAlpha(160, Theme.Accent), 1);
            g.DrawRectangle(pen, focus);
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }
}
