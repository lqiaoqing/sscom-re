using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>状态栏里的轻量 RX 速率曲线。</summary>
internal sealed class Sparkline : Control
{
    private float[] _values = [];

    public Sparkline()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Surface2;
        TabStop = false;
    }

    public void SetValues(float[] values)
    {
        _values = values;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Color back = Parent is { BackColor.A: 255 } parent ? parent.BackColor : Theme.Surface2;
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var baseline = new Pen(Theme.WithAlpha(40, Theme.Dim), 1);
        g.DrawLine(baseline, 0, Height - 1, Width, Height - 1);
        if (_values.Length < 2) return;

        float max = 1;
        foreach (float value in _values) max = Math.Max(max, value);
        float step = (float)Width / (_values.Length - 1);
        var points = new PointF[_values.Length];
        for (int i = 0; i < _values.Length; i++)
        {
            float y = Height - 2 - (_values[i] / max) * (Height - 4);
            points[i] = new PointF(i * step, y);
        }
        using var fill = new SolidBrush(Theme.WithAlpha(24, Theme.Accent));
        var area = new GraphicsPath();
        area.AddLines(points);
        area.AddLine(points[points.Length - 1].X, points[points.Length - 1].Y, Width, Height);
        area.AddLine(Width, Height, 0, Height);
        area.CloseFigure();
        g.FillPath(fill, area);
        using var pen = new Pen(Theme.Accent, 1.4F) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, points);
    }
}
