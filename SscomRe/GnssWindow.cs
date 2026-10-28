using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>GNSS 增强面板：坐标、精度、时间、差分与 SNR 前 6 卫星。</summary>
internal sealed class GnssWindow : Form
{
    private readonly Func<GnssFix> _snapshot;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly Font _coordFont;
    private readonly Font _monoSmall;
    private readonly Font _valueFont;
    private readonly Font _smallFont;
    private readonly Font _tinyFont;
    private readonly Font _boldFont;
    private readonly Font _tinyBold;
    private GnssFix _fix = GnssFix.Waiting;

    public GnssWindow(Func<GnssFix> snapshot)
    {
        _snapshot = snapshot;
        Text = "GNSS 定位";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F);
        _coordFont = Theme.MonoFont(16F);
        _monoSmall = Theme.MonoFont(8.5F);
        _valueFont = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _smallFont = new Font(Font.FontFamily, 8F);
        _tinyFont = new Font(Font.FontFamily, 7.5F);
        _boldFont = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _tinyBold = new Font(Font.FontFamily, 7.5F, FontStyle.Bold);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        ClientSize = new Size(Theme.Scale(this, 420), Theme.Scale(this, 620));
        var body = new Body(this) { Dock = DockStyle.Fill };
        Controls.Add(body);
        _timer.Tick += (_, _) => RefreshFix();
        _timer.Start();
    }

    internal string SummaryForTest
    {
        get
        {
            string top = _fix.SatellitesInView.Count > 0
                ? _fix.SatellitesInView[0].Code + _fix.SatellitesInView[0].Prn.ToString("00", CultureInfo.InvariantCulture)
                  + ":" + (_fix.SatellitesInView[0].Snr?.ToString(CultureInfo.InvariantCulture) ?? "-")
                : "";
            return _fix.View + "|" + (_fix.HasPosition ? _fix.FormatLatitude() : "") + "|" + _fix.Quality + "|"
                + (_fix.ChecksumFailed ? "bad" : "ok") + "|" + _fix.VisibleSatellites + "|" + top;
        }
    }

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
        MinimumSize = new Size(
            Theme.Scale(this, 420) + extra.Width,
            Theme.Scale(this, 620) + extra.Height);
        RefreshFix();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _coordFont.Dispose();
        _monoSmall.Dispose();
        _valueFont.Dispose();
        _smallFont.Dispose();
        _tinyFont.Dispose();
        _boldFont.Dispose();
        _tinyBold.Dispose();
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

        private int S(int value) => Theme.Scale(this, value);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            GnssFix fix = _owner._fix;
            int pad = S(14);
            int width = Math.Max(1, Width - pad * 2);
            int y = pad;
            y = DrawStatus(g, fix, pad, y);
            y += S(8);

            int coordH = S(112);
            DrawCoordsCard(g, fix, new Rectangle(pad, y, width, coordH));
            y += coordH + S(10);

            int tileH = S(58);
            int tileGap = S(7);
            DrawTiles(g, fix, pad, y, width, tileH, tileGap);
            y += tileH * 2 + tileGap + S(10);

            int footerH = _owner._tinyFont.Height + S(6);
            int footerY = Height - pad - footerH;
            int satH = Math.Max(S(220), footerY - S(8) - y);
            DrawSatellites(g, fix, new Rectangle(pad, y, width, satH));
            DrawFooter(g, fix, pad, footerY, width);
        }

        private int DrawStatus(Graphics g, GnssFix fix, int pad, int y)
        {
            bool waiting = fix.View == GnssView.Waiting;
            bool invalid = fix.View == GnssView.Invalid;
            Color state = waiting ? Theme.Muted : invalid ? Theme.Amber : Theme.Green;
            string label = waiting ? "等待语句" : invalid ? "定位无效" : "已定位";
            int dot = S(7);
            int cy = y + Font.Height / 2;
            using (var brush = new SolidBrush(state))
                g.FillEllipse(brush, pad, cy - dot / 2, dot, dot);

            int x = pad + dot + S(8);
            DrawText(g, label, Font, new Rectangle(x, y, S(100), Font.Height), state,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += Measure(label, Font).Width + S(8);

            string quality = fix.FormatQuality();
            if (quality.Length > 0)
            {
                Color chip = invalid ? Theme.Amber : Theme.Green;
                x = DrawChip(g, quality, x, y, chip, Theme.WithAlpha(Theme.IsLight ? 24 : 30, chip), _owner._smallFont);
            }

            string right = "NMEA 0183";
            string age = fix.FormatAge(Stopwatch.GetTimestamp());
            if (age.Length > 0) right += " · " + age;
            Size size = Measure(right, _owner._smallFont);
            DrawText(g, right, _owner._smallFont,
                new Rectangle(Width - pad - size.Width, y, size.Width, Font.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return y + Font.Height + S(2);
        }

        private void DrawCoordsCard(Graphics g, GnssFix fix, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft, gradient: true);
            int pad = S(12);
            int x = rect.X + pad;
            int width = rect.Width - pad * 2;
            int rowH = Math.Max(1, (rect.Height - S(4)) / 2);
            if (!fix.HasPosition)
            {
                DrawText(g, fix.View == GnssView.Waiting ? "正在解析 $GGA / $RMC" : "没有有效定位",
                    Font, new Rectangle(x, rect.Y + S(10), width, rowH), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, "收到完整语句后显示经纬度与定位质量", _owner._smallFont,
                    new Rectangle(x, rect.Y + S(10) + rowH, width, rowH - S(4)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }

            Color coord = fix.View == GnssView.Fixed ? Theme.Text : Theme.Dim;
            DrawCoordRow(g, "纬度", "LAT", fix.FormatLatitude(), fix.FormatLatitudeDms(),
                x, rect.Y + S(2), width, rowH, coord);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x + S(34), rect.Y + rowH + S(1), rect.Right - pad, rect.Y + rowH + S(1));
            DrawCoordRow(g, "经度", "LON", fix.FormatLongitude(), fix.FormatLongitudeDms(),
                x, rect.Y + rowH + S(2), width, rowH, coord);
        }

        private void DrawCoordRow(Graphics g, string caption, string latin, string value, string dms,
            int x, int y, int width, int rowH, Color color)
        {
            DrawText(g, caption, _owner._boldFont,
                new Rectangle(x, y + S(8), S(34), _owner._boldFont.Height), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            DrawText(g, latin, _owner._tinyFont,
                new Rectangle(x, y + S(8) + _owner._boldFont.Height + S(1), S(34), S(14)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);

            int space = value.LastIndexOf(' ');
            string number = space > 0 ? value.Substring(0, space) : value;
            string hemi = space > 0 ? value.Substring(space + 1) : "";
            int textX = x + S(36);
            DrawText(g, number, _owner._coordFont, new Rectangle(textX, y, S(200), rowH), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (hemi.Length > 0)
            {
                int numberW = Measure(number, _owner._coordFont).Width;
                DrawText(g, hemi, _owner._coordFont,
                    new Rectangle(textX + numberW + S(1), y, S(24), rowH), Theme.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            int dmsW = Measure(dms, _owner._monoSmall).Width;
            DrawText(g, dms, _owner._monoSmall,
                new Rectangle(x + width - dmsW, y + S(7), dmsW, _owner._monoSmall.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            DrawText(g, "十进制度 / DMS", _owner._tinyFont,
                new Rectangle(x + width - S(100), y + S(7) + _owner._monoSmall.Height + S(1), S(100), S(14)),
                Theme.Dim, TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.SingleLine);
        }

        private void DrawTiles(Graphics g, GnssFix fix, int pad, int y, int width, int tileH, int gap)
        {
            int tileW = Math.Max(1, (width - gap * 3) / 4);
            string age = fix.FormatAge(Stopwatch.GetTimestamp());
            string utc = fix.FormatUtc().Replace(" UTC", "");
            string local = fix.FormatLocalTime();
            string used = fix.Satellites?.ToString(CultureInfo.InvariantCulture) ?? "—";
            int visible = Math.Max(fix.VisibleSatellites, fix.SatellitesInView.Count);
            string visibleText = visible > 0 ? visible.ToString(CultureInfo.InvariantCulture) : "—";
            bool gsvShort = fix.Satellites is int usedCount && visible > 0 && usedCount > visible;

            DrawTile(g, new Rectangle(pad, y, tileW, tileH), "海拔 MSL",
                fix.HasPosition ? fix.FormatAltitude() : "—",
                "水准面 " + fix.FormatGeoid(), true);
            DrawTile(g, new Rectangle(pad + (tileW + gap), y, tileW, tileH), "速度",
                fix.HasPosition ? fix.FormatSpeed() : "—",
                fix.HasPosition ? fix.FormatSpeedKnots() : "—", false);
            DrawTile(g, new Rectangle(pad + (tileW + gap) * 2, y, tileW, tileH), "航向",
                fix.HasPosition ? fix.FormatCourse() : "—",
                fix.HasPosition ? fix.FormatCourseCardinal() + " · 真北" : "—", false);
            string dopSub = "";
            if (fix.Vdop is double vdop) dopSub = "V" + vdop.ToString("0.0#", CultureInfo.InvariantCulture);
            if (fix.Pdop is double pdop) dopSub += (dopSub.Length > 0 ? " · P" : "P") + pdop.ToString("0.0#", CultureInfo.InvariantCulture);
            DrawTile(g, new Rectangle(pad + (tileW + gap) * 3, y, tileW, tileH), "精度因子",
                fix.FormatHdop(), dopSub.Length > 0 ? dopSub : "—", false);

            int y2 = y + tileH + gap;
            DrawTile(g, new Rectangle(pad, y2, tileW, tileH), "卫星",
                used + " 参与", visibleText == "—" ? "GSV —" : "GSV " + visibleText + " 可见",
                false, gsvShort ? Theme.AmberText : null);
            DrawTile(g, new Rectangle(pad + (tileW + gap), y2, tileW, tileH), "差分龄期",
                fix.FormatDiffAge(), fix.FormatBaseStation(), false);
            DrawTile(g, new Rectangle(pad + (tileW + gap) * 2, y2, tileW, tileH), "UTC 时间",
                utc.Length > 0 ? utc : "—",
                local.Length > 0 ? "本地 " + local : "—", false);
            DrawTile(g, new Rectangle(pad + (tileW + gap) * 3, y2, tileW, tileH), "数据龄",
                age.Length > 0 ? age : "—",
                fix.View == GnssView.Waiting ? "NMEA 0183"
                    : fix.Source + " · " + (fix.ChecksumFailed ? "校验失败" : "校验正常"), false);
        }

        private void DrawTile(Graphics g, Rectangle rect, string caption, string value, string sub,
            bool accent, Color? subColor = null)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            if (accent)
            {
                var bar = new Rectangle(rect.X + 2, rect.Y + S(9), S(2), Math.Max(1, rect.Height - S(18)));
                using var brush = new SolidBrush(Theme.Accent);
                g.FillRectangle(brush, bar);
            }

            int x = rect.X + S(8);
            int y = rect.Y + S(6);
            int width = Math.Max(1, rect.Width - S(16));
            DrawText(g, caption, _owner._smallFont,
                new Rectangle(x, y, width, _owner._smallFont.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            int valueY = y + _owner._smallFont.Height + S(1);
            DrawText(g, value, _owner._valueFont,
                new Rectangle(x, valueY, width, _owner._valueFont.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int subY = valueY + _owner._valueFont.Height + S(1);
            DrawText(g, sub, _owner._tinyFont,
                new Rectangle(x, subY, width, _owner._tinyFont.Height), subColor ?? Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawSatellites(Graphics g, GnssFix fix, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(12);
            int x = rect.X + pad;
            int width = rect.Width - pad * 2;
            int headerH = S(32);
            int colsH = S(19);
            int footerH = S(27);
            int rowsTop = rect.Y + headerH + colsH;
            int rowsBottom = rect.Bottom - footerH;
            int rowH = Math.Max(S(28), (rowsBottom - rowsTop) / 6);

            int visible = Math.Max(fix.VisibleSatellites, fix.SatellitesInView.Count);
            string used = fix.Satellites?.ToString(CultureInfo.InvariantCulture) ?? "—";
            bool gsvShort = fix.Satellites is int usedCount && visible > 0 && usedCount > visible;
            int titleY = rect.Y + (headerH - _owner._boldFont.Height) / 2;
            DrawText(g, "卫星信号", _owner._boldFont,
                new Rectangle(x, titleY, S(90), _owner._boldFont.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int titleW = Measure("卫星信号", _owner._boldFont).Width;
            DrawChip(g, "SNR 前 6", x + titleW + S(8),
                rect.Y + (headerH - (_owner._tinyFont.Height + S(2))) / 2,
                Theme.Accent, Theme.WithAlpha(Theme.IsLight ? 24 : 32, Theme.Accent), _owner._tinyFont);
            string count = "GSV " + (visible > 0 ? visible.ToString(CultureInfo.InvariantCulture) : "—")
                + " 可见 · 参与 " + used + (gsvShort ? " · GSV 未覆盖全部" : "");
            Size countSize = Measure(count, _owner._tinyFont);
            DrawText(g, count, _owner._tinyFont,
                new Rectangle(rect.Right - pad - countSize.Width, rect.Y, countSize.Width, headerH),
                gsvShort ? Theme.AmberText : Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int barX = x + S(88);
            int barW = Math.Max(S(60), width - S(88) - S(42) - S(82));
            int snrX = barX + barW + S(6);
            int eaX = rect.Right - pad - S(82);
            int colsY = rect.Y + headerH;
            DrawText(g, "#", _owner._tinyFont, new Rectangle(x, colsY, S(20), colsH), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "卫星", _owner._tinyFont, new Rectangle(x + S(22), colsY, S(60), colsH), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "信噪比 dB-Hz", _owner._tinyFont, new Rectangle(barX, colsY, barW, colsH), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "SNR", _owner._tinyFont, new Rectangle(snrX, colsY, S(42), colsH), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "仰角 / 方位", _owner._tinyFont, new Rectangle(eaX, colsY, S(82), colsH), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (fix.SatellitesInView.Count == 0)
            {
                int hintY = rowsTop + Math.Max(0, (rowsBottom - rowsTop - _owner._smallFont.Height) / 2);
                DrawText(g, "等待 GSV 卫星数据…", _owner._smallFont,
                    new Rectangle(x, hintY, width, _owner._smallFont.Height), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, "需要 $GPGSV / $BDGSV / $GAGSV / $GLGSV", _owner._tinyFont,
                    new Rectangle(x, hintY + _owner._smallFont.Height + S(2), width, S(14)), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            }
            else
            {
                int countRows = Math.Min(6, fix.SatellitesInView.Count);
                for (int i = 0; i < countRows; i++)
                {
                    int ry = rowsTop + i * rowH;
                    if (i > 0)
                        using (var pen = new Pen(Theme.BorderSoft, 1))
                            g.DrawLine(pen, x, ry, rect.Right - pad, ry);
                    DrawSatelliteRow(g, fix.SatellitesInView[i], i, x, ry, rowH, width, barX, barW, snrX, eaX);
                }
            }

            int footerY = rect.Bottom - footerH;
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x, footerY, rect.Right - pad, footerY);
            DrawSatelliteFooter(g, fix, x, footerY, width, footerH);
        }

        private void DrawSatelliteRow(Graphics g, GnssSatellite sat, int index, int x, int y, int rowH,
            int width, int barX, int barW, int snrX, int eaX)
        {
            DrawText(g, (index + 1).ToString("00", CultureInfo.InvariantCulture), _owner._tinyFont,
                new Rectangle(x, y, S(20), rowH), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int badgeX = x + S(22);
            int badgeH = S(18);
            var badge = new Rectangle(badgeX, y + (rowH - badgeH) / 2, S(20), badgeH);
            Color constellation = ConstellationColor(sat.Constellation);
            using (var path = Theme.RoundedRect(badge, S(5)))
            {
                using var fill = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 30 : 38, constellation));
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 100, constellation), 1);
                g.DrawPath(pen, path);
            }
            DrawText(g, sat.Code, _owner._tinyBold, badge, constellation,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            DrawText(g, sat.Prn.ToString("00", CultureInfo.InvariantCulture), _owner._monoSmall,
                new Rectangle(x + S(46), y, S(40), rowH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int barH = S(6);
            var track = new Rectangle(barX, y + (rowH - barH) / 2, barW, barH);
            using (var path = Theme.RoundedRect(track, barH / 2))
            using (var fill = new SolidBrush(Theme.Canvas))
            {
                g.FillPath(fill, path);
                if (sat.Snr is int snr)
                {
                    int fillW = Math.Max(0, Math.Min(barW, (int)Math.Round(barW * Math.Min(1, snr / 50.0))));
                    if (fillW > 0)
                    {
                        var bar = new Rectangle(track.X, track.Y, fillW, track.Height);
                        using var barPath = Theme.RoundedRect(bar, barH / 2);
                        using var barBrush = new SolidBrush(SnrColor(snr));
                        g.FillPath(barBrush, barPath);
                    }
                }
            }

            Color snrColor = SnrTextColor(sat.Snr);
            DrawText(g, sat.Snr?.ToString(CultureInfo.InvariantCulture) ?? "—", _owner._monoSmall,
                new Rectangle(snrX, y, S(42), rowH), snrColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string ea = sat.Elevation is int elevation && sat.Azimuth is int azimuth
                ? elevation + "° / " + azimuth.ToString("000", CultureInfo.InvariantCulture) + "°"
                : "—";
            DrawText(g, ea, _owner._tinyFont, new Rectangle(eaX, y, S(82), rowH), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawSatelliteFooter(Graphics g, GnssFix fix, int x, int y, int width, int height)
        {
            int textY = y + (height - _owner._tinyFont.Height) / 2;
            string composition = Composition(fix.SatellitesInView);
            var items = new (string Text, Color Color)[]
            {
                ("≥40", Theme.Accent),
                ("25–39", Theme.Amber),
                ("<25", Theme.Danger),
            };
            int legendWidth = 0;
            foreach (var item in items)
                legendWidth += S(5) + S(4) + Measure(item.Text, _owner._tinyFont).Width + S(8);
            int compWidth = Math.Max(S(60), width - legendWidth - S(8));
            DrawText(g, composition, _owner._tinyFont, new Rectangle(x, textY, compWidth, _owner._tinyFont.Height),
                Theme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int lx = x + width - legendWidth;
            foreach (var item in items)
            {
                using (var dot = new SolidBrush(item.Color))
                    g.FillEllipse(dot, lx, textY + (_owner._tinyFont.Height - S(5)) / 2, S(5), S(5));
                lx += S(9);
                DrawText(g, item.Text, _owner._tinyFont,
                    new Rectangle(lx, textY, Measure(item.Text, _owner._tinyFont).Width, _owner._tinyFont.Height),
                    Theme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine);
                lx += Measure(item.Text, _owner._tinyFont).Width + S(8);
            }
        }

        private void DrawFooter(Graphics g, GnssFix fix, int pad, int y, int width)
        {
            string left;
            Color color = Theme.Dim;
            if (fix.View == GnssView.Waiting)
            {
                left = "NMEA 0183 · 等待 $GGA / $RMC";
            }
            else
            {
                left = (fix.Source.Length > 0 ? fix.Source : "NMEA") + " · GSV · "
                    + (fix.ChecksumFailed ? "校验失败" : "校验正常");
                if (fix.ChecksumFailed) color = Theme.AmberText;
            }

            string age = fix.FormatAge(Stopwatch.GetTimestamp());
            string right = "";
            if (fix.View == GnssView.Invalid)
            {
                right = "上一帧保留";
                if (age.Length > 0) right += " · " + age + " 前";
            }
            else if (age.Length > 0)
            {
                right = "更新 " + age + " 前";
            }
            string utc = fix.FormatUtc();
            if (utc.Length > 0) right += (right.Length > 0 ? " · " : "") + utc;

            int rightW = right.Length > 0 ? Measure(right, _owner._tinyFont).Width : 0;
            int leftW = Math.Max(1, width - rightW - S(8));
            DrawText(g, left, _owner._tinyFont, new Rectangle(pad, y, leftW, _owner._tinyFont.Height), color,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (rightW > 0)
                DrawText(g, right, _owner._tinyFont,
                    new Rectangle(Width - pad - rightW, y, rightW, _owner._tinyFont.Height), Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        private void DrawCard(Graphics g, Rectangle rect, Color fill, Color border, bool gradient = false)
        {
            using var path = Theme.RoundedRect(rect, S(10));
            if (gradient)
            {
                using var brush = new LinearGradientBrush(rect, Theme.Raised, Theme.Surface2, 90f);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            using var pen = new Pen(border, 1);
            g.DrawPath(pen, path);
        }

        private int DrawChip(Graphics g, string text, int x, int y, Color fore, Color back, Font font)
        {
            Size size = Measure(text, font);
            int height = font.Height + S(2);
            var rect = new Rectangle(x, y, size.Width + S(12), height);
            using (var path = Theme.RoundedRect(rect, height / 2))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            DrawText(g, text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return rect.Right + S(6);
        }

        private static void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color,
            TextFormatFlags flags) =>
            TextRenderer.DrawText(g, text, font, rect, color,
                flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        private static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        private static string Composition(IReadOnlyList<GnssSatellite> satellites)
        {
            if (satellites.Count == 0) return "等待 GSV";
            var counts = new Dictionary<string, int>();
            foreach (GnssSatellite sat in satellites)
            {
                counts.TryGetValue(sat.DisplayName, out int count);
                counts[sat.DisplayName] = count + 1;
            }
            string[] order = { "GPS", "北斗", "Galileo", "GLONASS", "QZSS", "SBAS", "NavIC", "GNSS" };
            var parts = new List<string>();
            foreach (string name in order)
                if (counts.TryGetValue(name, out int count) && count > 0)
                    parts.Add(name + " " + count.ToString(CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "等待 GSV" : string.Join(" · ", parts);
        }

        private static Color ConstellationColor(string constellation) => constellation switch
        {
            "GPS" => Theme.Accent,
            "BeiDou" => Theme.Amber,
            "Galileo" => Theme.IsLight ? Color.FromArgb(21, 101, 192) : Color.FromArgb(116, 169, 255),
            "GLONASS" => Theme.IsLight ? Color.FromArgb(106, 63, 181) : Color.FromArgb(199, 146, 234),
            "QZSS" => Theme.Green,
            "NavIC" => Theme.Danger,
            "SBAS" => Theme.Muted,
            _ => Theme.Muted,
        };

        private static Color SnrColor(int snr) =>
            snr >= 40 ? Theme.Accent : snr >= 25 ? Theme.Amber : Theme.Danger;

        private static Color SnrTextColor(int? snr)
        {
            if (snr is not int value) return Theme.Dim;
            if (value >= 40) return Theme.Accent;
            if (value >= 25) return Theme.IsLight ? Theme.AmberText : Theme.Amber;
            return Theme.Danger;
        }
    }
}
