using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>工具菜单里的 GNSS 小窗。标题栏可拖动，关掉即由主窗停止解析。</summary>
internal sealed class GnssWindow : Form
{
    private readonly Func<GnssFix> _snapshot;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly Font _mono;
    private GnssFix _fix = GnssFix.Waiting;

    public GnssWindow(Func<GnssFix> snapshot)
    {
        _snapshot = snapshot;
        _mono = Theme.MonoFont(12F);
        Text = "GNSS";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        ClientSize = new Size(Theme.Scale(this, 300), Theme.Scale(this, 188));
        var body = new Body(this) { Dock = DockStyle.Fill };
        Controls.Add(body);
        _timer.Tick += (_, _) => RefreshFix();
        _timer.Start();
    }

    internal string SummaryForTest =>
        _fix.View + "|" + (_fix.HasPosition ? _fix.FormatLatitude() : "") + "|" + _fix.Quality + "|"
        + (_fix.ChecksumFailed ? "bad" : "ok");

    internal void RefreshForTest() => RefreshFix();

    public void RefreshTheme()
    {
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        Invalidate(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Size extra = Size - ClientSize;
        MinimumSize = new Size(Theme.Scale(this, 280) + extra.Width, Theme.Scale(this, 168) + extra.Height);
        RefreshFix();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _mono.Dispose();
        base.OnFormClosed(e);
    }

    private void RefreshFix()
    {
        if (IsDisposed) return;
        _fix = _snapshot();
        Invalidate(true);
    }

    private sealed class Body : Control
    {
        private readonly GnssWindow _owner;

        public Body(GnssWindow owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            GnssFix fix = _owner._fix;
            int pad = Theme.Scale(this, 12);
            int y = pad;
            y = DrawStatus(g, fix, pad, y);
            y += Theme.Scale(this, 8);
            if (fix.View == GnssView.Waiting)
            {
                DrawHint(g, "正在解析 $GGA / $RMC", pad, y);
                y += Font.Height + 2;
                DrawHint(g, "还没有收到完整句", pad, y);
                y += Font.Height + Theme.Scale(this, 8);
            }
            else if (fix.HasPosition)
            {
                Color coord = fix.View == GnssView.Fixed ? Theme.Text : Theme.Dim;
                y = DrawCoords(g, fix.FormatLatitude(), pad, y, coord);
                y = DrawCoords(g, fix.FormatLongitude(), pad, y, coord);
                y += Theme.Scale(this, 4);
                if (fix.View == GnssView.Fixed)
                    y = DrawMetrics(g, fix, pad, y) + Theme.Scale(this, 6);
                y = DrawFooter(g, fix, pad, y);
            }
            if (fix.ChecksumFailed)
                DrawNote(g, "最近一句校验失败，仍显示上一帧有效定位", pad, y);
        }

        private int DrawStatus(Graphics g, GnssFix fix, int pad, int y)
        {
            bool waiting = fix.View == GnssView.Waiting;
            bool invalid = fix.View == GnssView.Invalid;
            Color state = waiting ? Theme.Muted : invalid ? Theme.Amber : Theme.Green;
            string label = waiting ? "等待语句" : invalid ? "定位无效" : "已定位";
            int dot = Theme.Scale(this, 7);
            int cy = y + Font.Height / 2;
            using (var brush = new SolidBrush(state))
                g.FillEllipse(brush, pad, cy - dot / 2, dot, dot);
            int x = pad + dot + Theme.Scale(this, 8);
            TextRenderer.DrawText(g, label, Font, new Point(x, y), state,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 8);
            if (invalid)
                x = DrawChip(g, "V", x, y, Theme.Amber, Theme.WithAlpha(40, Theme.Amber));
            else if (fix.View == GnssView.Fixed && fix.Quality.Length > 0)
                x = DrawChip(g, fix.Quality, x, y, Theme.Green, Theme.WithAlpha(40, Theme.Green));
            string age = fix.View == GnssView.Fixed && fix.Talker.Length > 0
                ? fix.Talker + " · " + fix.FormatAge(Stopwatch.GetTimestamp())
                : fix.FormatAge(Stopwatch.GetTimestamp());
            if (age.Length > 0)
            {
                Size size = TextRenderer.MeasureText(age, Font, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, age, Font, new Point(Width - pad - size.Width, y), Theme.Dim,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            return y + Font.Height;
        }

        private int DrawChip(Graphics g, string text, int x, int y, Color fore, Color back)
        {
            Size size = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int h = Font.Height;
            var rect = new Rectangle(x, y, size.Width + Theme.Scale(this, 10), h);
            using (var path = Theme.RoundedRect(rect, h / 2))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, Font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return rect.Right + Theme.Scale(this, 6);
        }

        private void DrawHint(Graphics g, string text, int x, int y)
        {
            TextRenderer.DrawText(g, text, Font, new Point(x, y), Theme.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private int DrawCoords(Graphics g, string text, int x, int y, Color color)
        {
            TextRenderer.DrawText(g, text, _owner._mono, new Point(x, y), color,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return y + _owner._mono.Height + 1;
        }

        private int DrawMetrics(Graphics g, GnssFix fix, int pad, int y)
        {
            int gap = Theme.Scale(this, 6);
            int width = Math.Max(1, (Width - pad * 2 - gap * 2) / 3);
            int height = Font.Height * 2 + Theme.Scale(this, 8);
            DrawMetric(g, "海拔", fix.FormatAltitude(), pad, y, width, height);
            DrawMetric(g, "速度", fix.FormatSpeed(), pad + width + gap, y, width, height);
            DrawMetric(g, "航向", fix.FormatCourse(), pad + (width + gap) * 2, y, width, height);
            return y + height;
        }

        private void DrawMetric(Graphics g, string caption, string value, int x, int y, int width, int height)
        {
            var rect = new Rectangle(x, y, width, height);
            using (var path = Theme.RoundedRect(rect, Theme.Scale(this, 6)))
            using (var brush = new SolidBrush(Theme.WithAlpha(48, Theme.Canvas)))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, caption, Font, new Point(x + 6, y + 3), Theme.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            using var bold = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(g, value, bold, new Point(x + 6, y + Font.Height + 2), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private int DrawFooter(Graphics g, GnssFix fix, int pad, int y)
        {
            string left = fix.View == GnssView.Invalid
                ? "上一帧仍保留"
                : "卫星 " + (fix.Satellites?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—")
                  + "    HDOP " + (fix.Hdop?.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) ?? "—");
            TextRenderer.DrawText(g, left, Font, new Point(pad, y), Theme.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            string right = fix.View == GnssView.Invalid ? fix.Source : fix.FormatUtc();
            if (right.Length > 0)
            {
                Size size = TextRenderer.MeasureText(right, Font, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, right, Font, new Point(Width - pad - size.Width, y), Theme.Dim,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            return y + Font.Height + Theme.Scale(this, 6);
        }

        private void DrawNote(Graphics g, string text, int pad, int y)
        {
            var rect = new Rectangle(pad, y, Math.Max(1, Width - pad * 2), Font.Height * 2);
            TextRenderer.DrawText(g, text, Font, rect, Theme.AmberText,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
