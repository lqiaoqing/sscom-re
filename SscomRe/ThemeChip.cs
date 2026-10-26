using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>标题行的小圆角标签，例如“串口 / 网络”。</summary>
internal sealed class ThemeChip : Control
{
    public ThemeChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        AutoSize = false;
        Height = 24;
        Width = 52;
        BackColor = Color.Transparent;
        ForeColor = Theme.Accent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Scale(this, 8);
        using (var path = Theme.RoundedRect(bounds, radius))
        using (var fill = new SolidBrush(Theme.WithAlpha(26, Theme.Accent)))
            g.FillPath(fill, path);
        TextRenderer.DrawText(g, Text, Font, bounds, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
