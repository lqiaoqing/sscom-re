using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>可拖动的细分隔条：左栏用竖条，发送区用横条。</summary>
internal sealed class ResizeSplitter : Control
{
    private Orientation _orientation = Orientation.Vertical;
    private bool _dragging;
    private bool _hover;
    private Point _lastScreen;

    public ResizeSplitter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        TabStop = false;
        BackColor = Color.Transparent;
        Cursor = Cursors.VSplit;
    }

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            Cursor = value == Orientation.Vertical ? Cursors.VSplit : Cursors.HSplit;
            Invalidate();
        }
    }

    public event Action<int>? Dragged;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color line = _hover || _dragging ? Theme.Accent : Theme.Dim;
        using var pen = new Pen(line, Math.Max(1F, Theme.Scale(this, 1)));
        if (_orientation == Orientation.Vertical)
        {
            int x = Width / 2;
            g.DrawLine(pen, x, 0, x, Height);
            DrawGrip(g, new Rectangle(x - Theme.Scale(this, 1), Height / 2 - Theme.Scale(this, 10),
                Math.Max(3, Theme.Scale(this, 3)), Theme.Scale(this, 20)), line);
        }
        else
        {
            int y = Height / 2;
            g.DrawLine(pen, 0, y, Width, y);
            DrawGrip(g, new Rectangle(Width / 2 - Theme.Scale(this, 10), y - Theme.Scale(this, 1),
                Theme.Scale(this, 20), Math.Max(3, Theme.Scale(this, 3))), line);
        }
    }

    private void DrawGrip(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        for (int i = 0; i < 3; i++)
        {
            if (_orientation == Orientation.Vertical)
                g.FillEllipse(brush, bounds.Left, bounds.Top + i * Theme.Scale(this, 7),
                    Math.Max(2, bounds.Width), Math.Max(2, bounds.Width));
            else
                g.FillEllipse(brush, bounds.Left + i * Theme.Scale(this, 7), bounds.Top,
                    Math.Max(2, bounds.Height), Math.Max(2, bounds.Height));
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _lastScreen = PointToScreen(e.Location);
        Capture = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        Point current = PointToScreen(e.Location);
        int delta = _orientation == Orientation.Vertical
            ? current.X - _lastScreen.X
            : current.Y - _lastScreen.Y;
        if (delta != 0)
        {
            _lastScreen = current;
            Dragged?.Invoke(delta);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }
        base.OnMouseUp(e);
    }
}
