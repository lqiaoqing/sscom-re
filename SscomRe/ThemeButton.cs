using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>深色自绘按钮：普通、主操作、描边、危险和纯图标五种状态。</summary>
internal sealed class ThemeButton : Button
{
    public enum ButtonKind
    {
        Default,
        Primary,
        Outline,
        Danger,
        Icon,
        Soft,
        Segment,
        SegmentSelected,
    }

    private bool _hover;
    private bool _pressed;

    public ButtonKind Kind { get; set; } = ButtonKind.Default;
    public bool AccentText { get; set; }

    public ThemeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Muted;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    private int IconSize => Math.Max(12, (int)(Font.Height * 0.72F));
    private int IconGap => Math.Max(4, Theme.Scale(this, 6));

    private static bool IsIconOnly(string text) => text is "↻" or "−" or "＋" or "×" or "━" or "■" or "«" or "»";

    private static bool HasIcon(string text) => text switch
    {
        "清除窗口" or "打开文件" or "发送文件" or "停止" or "清发送区"
            or "保存参数" or "保存数据" or "扩展" or "更多串口设置"
            or "打开串口" or "关闭串口" => true,
        _ => false,
    };

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Kind == ButtonKind.Icon || IsIconOnly(Text))
        {
            int side = Math.Max(Font.Height + 10, IconSize + Theme.Scale(this, 12));
            return new Size(side + Padding.Horizontal, side + Padding.Vertical);
        }
        Size text = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int icon = HasIcon(Text) ? IconSize + IconGap : 0;
        int w = text.Width + icon + Theme.Scale(this, 18) + Padding.Horizontal;
        // 比行高略矮，留出 FlowLayoutPanel 的上下 Margin，避免 200% DPI 下被行裁切。
        int h = Math.Max(Font.Height + 12, text.Height + Theme.Scale(this, 6)) + Padding.Vertical;
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillBackground(g, this);

        Rectangle bounds = ClientRectangle;
        bounds.Width = Math.Max(1, bounds.Width - 1);
        bounds.Height = Math.Max(1, bounds.Height - 1);
        int radius = Theme.Scale(this, Kind is ButtonKind.Segment or ButtonKind.SegmentSelected ? 8 : 5);

        Color back = Color.Transparent;
        Color border = Color.Transparent;
        Color fore = Theme.Muted;

        if (!Enabled)
        {
            fore = Theme.Dim;
            back = Kind == ButtonKind.Primary ? Theme.WithAlpha(70, Theme.Accent) : Color.Transparent;
        }
        else
        {
            switch (Kind)
            {
                case ButtonKind.Primary:
                    back = Theme.Accent;
                    border = Theme.AccentBright;
                    fore = Theme.AccentInk;
                    if (_pressed) back = Theme.Hex("#27B9AA");
                    else if (_hover) back = Theme.AccentBright;
                    break;
                case ButtonKind.Outline:
                    back = Theme.WithAlpha(16, Theme.Accent);
                    border = Theme.WithAlpha(90, Theme.Accent);
                    fore = Theme.AccentBright;
                    if (_hover) back = Theme.WithAlpha(28, Theme.Accent);
                    if (_pressed) back = Theme.WithAlpha(40, Theme.Accent);
                    break;
                case ButtonKind.Danger:
                    fore = _hover ? Theme.Danger : Theme.Muted;
                    back = _hover ? Theme.WithAlpha(18, Theme.Danger) : Color.Transparent;
                    border = _hover ? Theme.WithAlpha(70, Theme.Danger) : Color.Transparent;
                    break;
                case ButtonKind.Icon:
                    back = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Color.Transparent;
                    border = _hover ? Theme.Border : Color.Transparent;
                    fore = _hover ? Theme.Text : Theme.Muted;
                    break;
                case ButtonKind.Soft:
                    back = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.WithAlpha(14, Theme.Accent);
                    border = _hover ? Theme.WithAlpha(95, Theme.Accent) : Theme.BorderSoft;
                    fore = _hover ? Theme.AccentBright : Theme.Muted;
                    break;
                case ButtonKind.Segment:
                    back = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.Surface;
                    border = _hover ? Theme.Border : Theme.BorderSoft;
                    fore = _hover ? Theme.Text : Theme.Muted;
                    break;
                case ButtonKind.SegmentSelected:
                    back = _pressed ? Theme.AccentDeep : _hover ? Theme.AccentBright : Theme.Accent;
                    border = Theme.Accent;
                    fore = Theme.AccentInk;
                    break;
                default:
                    back = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Color.Transparent;
                    border = _hover ? Theme.Border : Color.Transparent;
                    fore = _hover ? Theme.Text : Theme.Muted;
                    break;
            }
        }

        if (AccentText && Enabled && Kind == ButtonKind.Soft)
            fore = _hover ? Theme.AccentBright : Theme.Accent;

        if (back.A > 0)
        {
            using GraphicsPath path = Theme.RoundedRect(bounds, radius);
            if (Kind == ButtonKind.Primary && back.A == 255)
            {
                using var gradient = new LinearGradientBrush(bounds, Theme.AccentBright, Theme.AccentDeep, 90F);
                g.FillPath(gradient, path);
            }
            else
            {
                using var brush = new SolidBrush(back);
                g.FillPath(brush, path);
            }
        }
        if (border.A > 0)
        {
            using GraphicsPath path = Theme.RoundedRect(bounds, radius);
            using var pen = new Pen(border, Math.Max(1, Theme.Scale(this, 1)));
            g.DrawPath(pen, path);
        }

        if (Focused && ShowFocusCues && Kind != ButtonKind.Primary)
        {
            Rectangle focus = Rectangle.Inflate(bounds, -Theme.Scale(this, 2), -Theme.Scale(this, 2));
            using GraphicsPath path = Theme.RoundedRect(focus, Math.Max(2, radius - 1));
            using var pen = new Pen(Theme.WithAlpha(170, Theme.Accent), 1);
            g.DrawPath(pen, path);
        }

        bool iconOnly = IsIconOnly(Text);
        bool withIcon = !iconOnly && HasIcon(Text);
        if (iconOnly)
        {
            DrawGlyph(g, Center(bounds, IconSize + Theme.Scale(this, 4)), Text, fore);
            return;
        }

        Size textSize = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int gap = withIcon ? IconGap : 0;
        int iconWidth = withIcon ? IconSize : 0;
        int contentWidth = iconWidth + gap + textSize.Width;
        int contentLeft = bounds.Left + Math.Max(0, (bounds.Width - contentWidth) / 2);
        if (withIcon)
            DrawGlyph(g, new Rectangle(contentLeft, bounds.Top + (bounds.Height - IconSize) / 2, IconSize, IconSize), Text, fore);
        var textRect = new Rectangle(contentLeft + iconWidth + gap, bounds.Top,
            Math.Max(1, bounds.Right - contentLeft - iconWidth - gap), bounds.Height);
        TextRenderer.DrawText(g, Text, Font, textRect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static Rectangle Center(Rectangle bounds, int size)
    {
        return new Rectangle(bounds.Left + (bounds.Width - size) / 2,
            bounds.Top + (bounds.Height - size) / 2, size, size);
    }

    private static void DrawGlyph(Graphics g, Rectangle r, string text, Color color)
    {
        float x = r.Left, y = r.Top, w = r.Width, h = r.Height;
        float stroke = Math.Max(1.5F, r.Width / 9F);
        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        switch (text)
        {
            case "↻":
                g.DrawArc(pen, x + w * .12F, y + h * .12F, w * .76F, h * .76F, 35, 295);
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .84F, y + h * .06F),
                    new PointF(x + w * .84F, y + h * .30F),
                    new PointF(x + w * .60F, y + h * .30F),
                });
                break;
            case "−":
            case "━":
                g.DrawLine(pen, x + w * .18F, y + h * .5F, x + w * .82F, y + h * .5F);
                break;
            case "＋":
                g.DrawLine(pen, x + w * .18F, y + h * .5F, x + w * .82F, y + h * .5F);
                g.DrawLine(pen, x + w * .5F, y + h * .18F, x + w * .5F, y + h * .82F);
                break;
            case "×":
                g.DrawLine(pen, x + w * .28F, y + h * .28F, x + w * .72F, y + h * .72F);
                g.DrawLine(pen, x + w * .72F, y + h * .28F, x + w * .28F, y + h * .72F);
                break;
            case "«":
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .62F, y + h * .26F),
                    new PointF(x + w * .42F, y + h * .5F),
                    new PointF(x + w * .62F, y + h * .74F),
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .42F, y + h * .26F),
                    new PointF(x + w * .22F, y + h * .5F),
                    new PointF(x + w * .42F, y + h * .74F),
                });
                break;
            case "»":
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .38F, y + h * .26F),
                    new PointF(x + w * .58F, y + h * .5F),
                    new PointF(x + w * .38F, y + h * .74F),
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .58F, y + h * .26F),
                    new PointF(x + w * .78F, y + h * .5F),
                    new PointF(x + w * .58F, y + h * .74F),
                });
                break;
            case "清除窗口":   // trash
                g.DrawLine(pen, x + w * .18F, y + h * .25F, x + w * .82F, y + h * .25F);
                g.DrawLine(pen, x + w * .38F, y + h * .25F, x + w * .38F, y + h * .14F);
                g.DrawLine(pen, x + w * .62F, y + h * .25F, x + w * .62F, y + h * .14F);
                g.DrawLine(pen, x + w * .38F, y + h * .14F, x + w * .62F, y + h * .14F);
                g.DrawLine(pen, x + w * .26F, y + h * .31F, x + w * .31F, y + h * .84F);
                g.DrawLine(pen, x + w * .74F, y + h * .31F, x + w * .69F, y + h * .84F);
                g.DrawLine(pen, x + w * .31F, y + h * .84F, x + w * .69F, y + h * .84F);
                break;
            case "打开文件":   // folder
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .12F, y + h * .28F),
                    new PointF(x + w * .12F, y + h * .80F),
                    new PointF(x + w * .88F, y + h * .80F),
                    new PointF(x + w * .88F, y + h * .34F),
                    new PointF(x + w * .44F, y + h * .34F),
                    new PointF(x + w * .36F, y + h * .22F),
                    new PointF(x + w * .12F, y + h * .22F),
                });
                break;
            case "发送文件":   // upload
                g.DrawLine(pen, x + w * .5F, y + h * .70F, x + w * .5F, y + h * .16F);
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .26F, y + h * .40F),
                    new PointF(x + w * .5F, y + h * .16F),
                    new PointF(x + w * .74F, y + h * .40F),
                });
                g.DrawLine(pen, x + w * .18F, y + h * .84F, x + w * .82F, y + h * .84F);
                break;
            case "停止":
            case "■":          // stop square
                using (var fill = new SolidBrush(color))
                    g.FillRectangle(fill, x + w * .26F, y + h * .26F, w * .48F, h * .48F);
                break;
            case "清发送区":   // eraser
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * .14F, y + h * .68F),
                    new PointF(x + w * .55F, y + h * .24F),
                    new PointF(x + w * .86F, y + h * .52F),
                    new PointF(x + w * .62F, y + h * .78F),
                    new PointF(x + w * .30F, y + h * .78F),
                });
                g.DrawLine(pen, x + w * .52F, y + h * .30F, x + w * .78F, y + h * .54F);
                break;
            case "保存参数":
            case "保存数据":   // floppy
                g.DrawRectangle(pen, x + w * .16F, y + h * .16F, w * .68F, h * .68F);
                g.DrawRectangle(pen, x + w * .32F, y + h * .16F, w * .38F, h * .25F);
                g.DrawRectangle(pen, x + w * .30F, y + h * .58F, w * .40F, h * .26F);
                break;
            case "扩展":       // panel
                g.DrawRectangle(pen, x + w * .14F, y + h * .18F, w * .72F, h * .64F);
                g.DrawLine(pen, x + w * .56F, y + h * .18F, x + w * .56F, y + h * .82F);
                break;
            case "更多串口设置": // sliders
                g.DrawLine(pen, x + w * .16F, y + h * .32F, x + w * .72F, y + h * .32F);
                g.DrawLine(pen, x + w * .84F, y + h * .32F, x + w * .90F, y + h * .32F);
                g.DrawLine(pen, x + w * .16F, y + h * .68F, x + w * .34F, y + h * .68F);
                g.DrawLine(pen, x + w * .46F, y + h * .68F, x + w * .90F, y + h * .68F);
                g.DrawEllipse(pen, x + w * .60F, y + h * .22F, w * .16F, h * .20F);
                g.DrawEllipse(pen, x + w * .28F, y + h * .58F, w * .16F, h * .20F);
                break;
            case "打开串口":
            case "关闭串口": // power
                g.DrawArc(pen, x + w * .18F, y + h * .18F, w * .64F, h * .64F, -55, 290);
                g.DrawLine(pen, x + w * .5F, y + h * .12F, x + w * .5F, y + h * .46F);
                break;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}
