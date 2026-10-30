using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>VS2310 Main Trace 被动观察面板：状态、链路、快照、上报管线与证据时间线。</summary>
internal sealed class MainTraceWindow : Form
{
    private readonly Func<MainTraceView> _snapshot;
    private readonly Func<string> _sourceLabel;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Font _titleFont;
    private readonly Font _subFont;
    private readonly Font _heroFont;
    private readonly Font _heroSubFont;
    private readonly Font _cardTitleFont;
    private readonly Font _bigFont;
    private readonly Font _monoBigFont;
    private readonly Font _kvLabelFont;
    private readonly Font _kvValueFont;
    private readonly Font _tinyFont;
    private readonly Font _eventFont;
    private readonly Font _eventTimeFont;
    private readonly Font _detailFont;
    private readonly Font _chipFont;
    private MainTraceView _view = new();
    private bool _paused;
    private int _clockSecond = -1;
    private EventFilter _filter = EventFilter.All;
    private int _eventScroll;
    private MainTraceEvent? _selectedEvent;
    private List<(EventFilter Filter, Rectangle Rect)> _filterRects = new();
    private List<(MainTraceEvent Event, Rectangle Rect)> _eventRects = new();
    private Rectangle _pauseRect;
    private Rectangle _closeRect;
    private Rectangle _evidenceRect;

    internal MainTraceWindow(Func<MainTraceView> snapshot, Func<string> sourceLabel)
    {
        _snapshot = snapshot;
        _sourceLabel = sourceLabel;
        Text = "VS2310 观察";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F);
        _titleFont = new Font(Font.FontFamily, 12.5F, FontStyle.Bold);
        _subFont = new Font(Font.FontFamily, 8F);
        _heroFont = new Font(Font.FontFamily, 17F, FontStyle.Bold);
        _heroSubFont = new Font(Font.FontFamily, 7.5F, FontStyle.Bold);
        _cardTitleFont = new Font(Font.FontFamily, 7.8F, FontStyle.Bold);
        _bigFont = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _monoBigFont = Theme.MonoFont(8.2F);
        _kvLabelFont = new Font(Font.FontFamily, 6.1F);
        _kvValueFont = Theme.MonoFont(7.3F);
        _tinyFont = new Font(Font.FontFamily, 5.8F);
        _eventFont = new Font(Font.FontFamily, 6.8F);
        _eventTimeFont = Theme.MonoFont(5.6F);
        _detailFont = Theme.MonoFont(5.6F);
        _chipFont = new Font(Font.FontFamily, 5.8F);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        ClientSize = new Size(Theme.Scale(this, 600), Theme.Scale(this, 780));
        var body = new Body(this) { Dock = DockStyle.Fill };
        Controls.Add(body);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    internal string SummaryForTest
    {
        get
        {
            MainTraceSnapshot s = _view.Snapshot;
            MainTraceReport? r = _view.LastReport;
            string last = _view.Events.Count > 0 ? _view.Events[0].Tag + ":" + _view.Events[0].Text : "";
            return s.MainState + "/" + s.SubState + "|" + s.Model + "|" + s.DoorState + "|"
                   + _view.SnapshotCount + "/" + _view.MalformedSnapshots + "|"
                   + (r is null ? "no-report" : r.Bytes + ":" + r.Outcome) + "|"
                   + _view.ReportSuccessCount + "/" + _view.ReportUnobservedCount + "|"
                   + _view.TxCount + "|" + _view.DownlinkCount + "|" + last;
        }
    }

    internal bool PausedForTest => _paused;
    internal void TogglePauseForTest() => TogglePause();
    internal void RefreshForTest()
    {
        _view = _snapshot();
        Invalidate(true);
    }

    internal Bitmap RenderForTest()
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        if (Controls.Count > 0)
            Controls[0].DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        else
            DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

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
            Theme.Scale(this, 600) + extra.Width,
            Theme.Scale(this, 780) + extra.Height);
        _view = _snapshot();
        Tick();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _titleFont.Dispose();
        _subFont.Dispose();
        _heroFont.Dispose();
        _heroSubFont.Dispose();
        _cardTitleFont.Dispose();
        _bigFont.Dispose();
        _monoBigFont.Dispose();
        _kvLabelFont.Dispose();
        _kvValueFont.Dispose();
        _tinyFont.Dispose();
        _eventFont.Dispose();
        _eventTimeFont.Dispose();
        _detailFont.Dispose();
        _chipFont.Dispose();
        base.OnFormClosed(e);
    }

    private void Tick()
    {
        if (IsDisposed) return;
        if (!_paused)
        {
            MainTraceView next = _snapshot();
            bool changed = !SameView(next, _view);
            _view = next;
            int second = DateTime.Now.Second;
            if (changed || second != _clockSecond)
            {
                _clockSecond = second;
                Invalidate(true);
            }
        }
        else
        {
            int second = DateTime.Now.Second;
            if (second != _clockSecond)
            {
                _clockSecond = second;
                Invalidate(true);
            }
        }
    }

    private static bool SameView(MainTraceView a, MainTraceView b) =>
        a.Snapshot.Tick == b.Snapshot.Tick
        && a.EventCount == b.EventCount
        && a.SnapshotCount == b.SnapshotCount
        && a.TxCount == b.TxCount
        && a.DownlinkCount == b.DownlinkCount
        && a.LastReport?.Outcome == b.LastReport?.Outcome
        && a.LastReport?.Bytes == b.LastReport?.Bytes;

    private void TogglePause()
    {
        _paused = !_paused;
        Invalidate(true);
    }

    private void SetFilter(EventFilter filter)
    {
        _filter = filter;
        _eventScroll = 0;
        _selectedEvent = null;
        Invalidate(true);
    }

    private enum EventFilter
    {
        All,
        State,
        Network,
        Report,
        Sensor,
        Alert,
        Camera,
    }

    private sealed class Body : Control
    {
        private readonly MainTraceWindow _owner;

        public Body(MainTraceWindow owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            MouseWheel += (_, e) => owner.OnEventWheel(e.Delta);
            MouseClick += (_, e) => owner.OnBodyClick(e.Location);
        }

        private int S(int value) => Theme.Scale(this, value);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            int topH = S(38);
            int idH = S(22);
            int pad = S(13);
            int gap = S(7);
            int contentTop = topH + idH + S(10);
            int width = Math.Max(S(300), Width - pad * 2);
            int heroH = S(72);
            int bannerH = S(24);
            int linkH = S(62);
            int cardsH = S(166);
            int reportH = S(124);
            int footerH = S(14);
            int fixedH = heroH + bannerH + linkH + cardsH + reportH + footerH + gap * 6 + S(12);
            int eventsH = Math.Max(S(120), Height - contentTop - fixedH);

            DrawTopBar(g, 0, 0, Width, topH);
            DrawIdentity(g, 0, topH, Width, idH, pad);

            int y = contentTop;
            DrawHero(g, new Rectangle(pad, y, width, heroH));
            y += heroH + gap;
            DrawBanner(g, new Rectangle(pad, y, width, bannerH));
            y += bannerH + gap;
            DrawLinkRail(g, new Rectangle(pad, y, width, linkH));
            y += linkH + gap;
            DrawSnapshotCards(g, pad, y, width, cardsH, gap);
            y += cardsH + gap;
            DrawReport(g, new Rectangle(pad, y, width, reportH));
            y += reportH + gap;
            DrawEvents(g, new Rectangle(pad, y, width, eventsH));
            y += eventsH + gap;
            DrawFooter(g, pad, Math.Min(Height - footerH - S(6), y), width);
        }

        private void DrawTopBar(Graphics g, int x, int y, int width, int height)
        {
            using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(1, width), height),
                       Theme.Surface2, Theme.Surface, 90f))
                g.FillRectangle(brush, x, y, width, height);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x, y + height - 1, x + width, y + height - 1);

            int pad = S(13);
            int mark = S(22);
            var markRect = new Rectangle(x + pad, y + (height - mark) / 2, mark, mark);
            using (var path = Theme.RoundedRect(markRect, S(6)))
            using (var brush = new LinearGradientBrush(markRect, Theme.AccentBright, Theme.AccentDeep, 45f))
                g.FillPath(brush, path);
            DrawText(g, "VS", _owner._chipFont, markRect, Theme.AccentInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int tx = markRect.Right + S(8);
            DrawText(g, "设备观察", _owner._titleFont,
                new Rectangle(tx, y, S(120), height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int titleW = Measure("设备观察", _owner._titleFont).Width;
            DrawText(g, "VS2310 · Main Trace", _owner._subFont,
                new Rectangle(tx + titleW + S(8), y, S(170), height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int right = x + width - pad;
            int closeW = S(20);
            _owner._closeRect = new Rectangle(right - closeW, y + (height - closeW) / 2, closeW, closeW);
            DrawText(g, "×", _owner._heroSubFont, _owner._closeRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= closeW + S(4);
            _owner._pauseRect = new Rectangle(right - closeW, y + (height - closeW) / 2, closeW, closeW);
            DrawText(g, _owner._paused ? "▶" : "Ⅱ", _owner._subFont, _owner._pauseRect, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= closeW + S(7);

            string clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            int clockW = Measure(clock, _owner._eventTimeFont).Width + S(4);
            DrawText(g, clock, _owner._eventTimeFont,
                new Rectangle(right - clockW, y, clockW, height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= clockW + S(8);

            string live = _owner._paused ? "已暂停" : "实时 · 1 Hz";
            Color liveColor = _owner._paused ? Theme.Amber : Theme.Green;
            Size liveSize = Measure(live, _owner._tinyFont);
            int liveW = liveSize.Width + S(18);
            var liveRect = new Rectangle(right - liveW, y + (height - S(19)) / 2, liveW, S(19));
            using (var path = Theme.RoundedRect(liveRect, liveRect.Height / 2))
            {
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 24 : 28, liveColor));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 90, liveColor), 1);
                g.DrawPath(pen, path);
            }
            using (var brush = new SolidBrush(liveColor))
                g.FillEllipse(brush, liveRect.X + S(7), liveRect.Y + (liveRect.Height - S(5)) / 2, S(5), S(5));
            DrawText(g, live, _owner._tinyFont,
                new Rectangle(liveRect.X + S(15), liveRect.Y, liveW - S(18), liveRect.Height), liveColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawIdentity(Graphics g, int x, int y, int width, int height, int pad)
        {
            using (var brush = new SolidBrush(Theme.Surface2))
                g.FillRectangle(brush, x, y, width, height);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x, y + height - 1, x + width, y + height - 1);

            MainTraceSnapshot s = _owner._view.Snapshot;
            int left = x + pad;
            int cy = y + (height - S(14)) / 2;
            left = DrawChip(g, s.ModelText, left, cy, Theme.AccentInk,
                Theme.Accent, _owner._chipFont);
            left = DrawChip(g, "FW " + Dash(s.Firmware), left, cy, Theme.Muted,
                Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);
            left = DrawChip(g, "BL " + Dash(s.Bootloader), left, cy, Theme.Muted,
                Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);
            left = DrawChip(g, "SoC " + Dash(s.SocVersion), left, cy, Theme.Muted,
                Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);
            left = DrawChip(g, "BLE " + Dash(s.McuBleVersion) + " / " + Dash(s.SocBleVersion), left, cy,
                Theme.Muted, Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);

            int right = x + width - pad;
            string details = "详情 ›";
            int detailsW = Measure(details, _owner._chipFont).Width;
            DrawText(g, details, _owner._chipFont,
                new Rectangle(right - detailsW, y, detailsW, height), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= detailsW + S(7);
            right = DrawChipRight(g, "脱敏", right, cy, Theme.Dim,
                Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);
            right = DrawChipRight(g, "Main Trace", right, cy, Blue,
                Theme.WithAlpha(Theme.IsLight ? 22 : 28, Blue), _owner._chipFont);
            if (s.DeviceTime.Length > 0)
            {
                string rtc = "RTC " + s.DeviceTime + (s.ClockOffsetText.Length > 0 ? " · " + s.ClockOffsetText : "");
                right = DrawChipRight(g, rtc, right, cy,
                    s.ClockOffsetText is "" or "±0" ? Theme.Dim : Theme.Amber,
                    Theme.WithAlpha(Theme.IsLight ? 22 : 28,
                        s.ClockOffsetText is "" or "±0" ? Theme.Dim : Theme.Amber), _owner._chipFont);
            }
        }

        private void DrawHero(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            using (var path = Theme.RoundedRect(rect, S(10)))
            {
                var start = Theme.Mix(Theme.Raised, Theme.Accent, 0.10f);
                var end = Theme.Mix(Theme.Raised, Theme.Surface, 0.35f);
                using var brush = new LinearGradientBrush(rect, start, end, 0f);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 70 : 90, Theme.Accent), 1);
                g.DrawPath(pen, path);
            }

            int pad = S(13);
            int leadW = S(210);
            int x = rect.X + pad;
            DrawText(g, "运行总状态", _owner._tinyFont,
                new Rectangle(x, rect.Y + S(7), leadW, _owner._tinyFont.Height), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, s.MainStateText, _owner._heroFont,
                new Rectangle(x - S(1), rect.Y + S(17), leadW, _owner._heroFont.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int stateW = Measure(s.MainStateText, _owner._heroFont).Width;
            int chipX = x + Math.Min(leadW - S(45), stateW + S(7));
            DrawChip(g, s.SubStateText, chipX, rect.Y + S(28), Theme.Accent,
                Theme.WithAlpha(Theme.IsLight ? 24 : 32, Theme.Accent), _owner._chipFont);
            string note = s.HasData
                ? (HasMainStateChange()
                    ? "Idle 快照 · 已记录主状态切换"
                    : "Idle 快照 · 未观测到主状态切换")
                : "等待 #HH:MM:SS VS2310:[…]";
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(x, rect.Y + S(47), leadW, S(13)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int metricX = rect.X + pad + leadW + S(2);
            int ringW = S(100);
            int metricW = Math.Max(S(150), rect.Right - pad - ringW - metricX);
            int colW = metricW / 3;
            DrawMetric(g, "快照周期", _owner._view.SnapshotPeriodText,
                s.HasData ? _owner._view.SnapshotCount + " 条" : "", metricX, rect.Y, colW, S(58));
            DrawMetric(g, "本次会话", _owner._view.SessionText,
                s.HasData ? "COM38" : "", metricX + colW, rect.Y, colW, S(58));
            MainTraceReport? report = _owner._view.LastReport;
            DrawMetric(g, "最近上报", report is null ? "—" : report.Bytes + " B",
                report is null ? "" : Age(report.EncodedAt), metricX + colW * 2, rect.Y, colW, S(58));

            int ringX = rect.Right - pad - ringW + S(4);
            DrawRing(g, new Rectangle(ringX, rect.Y + S(12), S(46), S(46)),
                _owner._view.CompleteRatio, s.HasData);
            int textX = ringX + S(52);
            DrawText(g, s.HasData ? _owner._view.CompleteSnapshotsText : "等待", _owner._eventTimeFont,
                new Rectangle(textX, rect.Y + S(11), ringW - S(54), S(13)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "快照重组完整", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(24), ringW - S(54), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "软换行已还原", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(36), ringW - S(54), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private void DrawMetric(Graphics g, string label, string value, string sub, int x, int y, int width, int height)
        {
            using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 30 : 42, Theme.Accent), 1))
                g.DrawLine(pen, x, y + S(10), x, y + height - S(7));
            int tx = x + S(9);
            DrawText(g, label, _owner._tinyFont,
                new Rectangle(tx, y + S(9), width - S(12), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, _owner._kvValueFont,
                new Rectangle(tx, y + S(23), width - S(12), S(16)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (sub.Length > 0)
                DrawText(g, sub, _owner._tinyFont,
                    new Rectangle(tx, y + S(39), width - S(12), S(12)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawRing(Graphics g, Rectangle rect, double ratio, bool hasData)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            Color color = !hasData ? Theme.Dim : ratio >= 0.99 ? Theme.Green : ratio >= 0.8 ? Theme.Amber : Theme.Danger;
            using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 60 : 90, Theme.BorderSoft), S(5)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, rect, -90, 360);
            }
            if (hasData && ratio > 0)
            {
                using var pen = new Pen(color, S(5)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(pen, rect, -90, (float)(360 * ratio));
            }
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 28 : 42, color)))
            {
                var inner = new Rectangle(rect.X + S(7), rect.Y + S(7), rect.Width - S(14), rect.Height - S(14));
                g.FillEllipse(brush, inner);
            }
            DrawText(g, hasData ? Math.Round(ratio * 100) + "%" : "—", _owner._eventTimeFont,
                rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawBanner(Graphics g, Rectangle rect)
        {
            IReadOnlyList<string> alerts = _owner._view.Alerts;
            bool waiting = !_owner._view.HasData;
            bool hasAlerts = alerts.Count > 0 && !(alerts.Count == 1 && alerts[0].StartsWith("等待", StringComparison.Ordinal));
            Color color = waiting ? Theme.Dim : hasAlerts ? Theme.Amber : Theme.Green;
            using (var path = Theme.RoundedRect(rect, S(7)))
            {
                using var brush = new LinearGradientBrush(rect,
                    Theme.WithAlpha(Theme.IsLight ? 22 : 26, color),
                    Theme.WithAlpha(Theme.IsLight ? 12 : 12, color), 0f);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 70 : 80, color), 1);
                g.DrawPath(pen, path);
            }
            int pad = S(9);
            var icon = new Rectangle(rect.X + pad, rect.Y + (rect.Height - S(13)) / 2, S(13), S(13));
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 26 : 32, color)))
                g.FillEllipse(brush, icon);
            DrawText(g, waiting ? "…" : hasAlerts ? "!" : "✓", _owner._chipFont, icon, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string text;
            if (waiting)
                text = "等待 VS2310 主日志数据 · 打开串口后自动解析 # 快照与 [INFO] 事件";
            else if (hasAlerts)
                text = "待确认 " + alerts.Count.ToString(CultureInfo.InvariantCulture) + " · " + string.Join(" · ", alerts);
            else
                text = "当前无待确认项 · 云端回执始终未观测";
            DrawText(g, text, _owner._tinyFont,
                new Rectangle(icon.Right + S(6), rect.Y, rect.Width - pad * 2 - icon.Width - S(58), rect.Height),
                hasAlerts ? Theme.AmberText : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (hasAlerts)
            {
                string link = "查看证据 →";
                int linkW = Measure(link, _owner._chipFont).Width;
                _owner._evidenceRect = new Rectangle(rect.Right - pad - linkW, rect.Y, linkW, rect.Height);
                DrawText(g, link, _owner._chipFont, _owner._evidenceRect, Theme.Accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                _owner._evidenceRect = Rectangle.Empty;
            }
        }

        private void DrawLinkRail(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(10);
            int sideW = S(56);
            DrawText(g, "固件侧链路", _owner._chipFont,
                new Rectangle(rect.X + pad, rect.Y + S(9), sideW, S(13)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            int unobserved = _owner._view.LinkStages.Count(stage => stage.State == MainTraceLinkState.Unobserved);
            DrawText(g, (_owner._view.LinkStages.Count - unobserved) + " 观察点", _owner._tinyFont,
                new Rectangle(rect.X + pad, rect.Y + S(25), sideW, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, unobserved + " 未观测", _owner._tinyFont,
                new Rectangle(rect.X + pad, rect.Y + S(36), sideW, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);

            int chainX = rect.X + pad + sideW + S(4);
            int chainW = rect.Width - (chainX - rect.X) - pad;
            int count = Math.Max(1, _owner._view.LinkStages.Count);
            int nodeW = chainW / count;
            int lineY = rect.Y + S(24);
            int firstCenter = chainX + nodeW / 2;
            int lastCenter = chainX + nodeW * (count - 1) + nodeW / 2;
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, firstCenter, lineY, lastCenter, lineY);
            for (int i = 0; i < count; i++)
            {
                MainTraceLinkStage stage = _owner._view.LinkStages[i];
                Color color = LinkColor(stage.State);
                int cx = chainX + nodeW * i + nodeW / 2;
                if (i < count - 1)
                {
                    MainTraceLinkStage next = _owner._view.LinkStages[i + 1];
                    using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 70 : 90, LinkColor(next.State)), S(2));
                    g.DrawLine(pen, cx + S(9), lineY, chainX + nodeW * (i + 1) + nodeW / 2 - S(9), lineY);
                }
                var dot = new Rectangle(cx - S(8), lineY - S(8), S(16), S(16));
                using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 30 : 36, color)))
                    g.FillEllipse(brush, dot);
                using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 90 : 110, color), 1))
                    g.DrawEllipse(pen, dot);
                string mark = stage.State switch
                {
                    MainTraceLinkState.Observed => "✓",
                    MainTraceLinkState.Warning => "!",
                    MainTraceLinkState.Unobserved => "·",
                    _ => "●",
                };
                DrawText(g, mark, _owner._chipFont, dot, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, stage.Label, _owner._tinyFont,
                    new Rectangle(cx - nodeW / 2, lineY + S(11), nodeW, S(12)), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                DrawText(g, stage.Value, _owner._chipFont,
                    new Rectangle(cx - nodeW / 2, lineY + S(23), nodeW, S(12)),
                    stage.State == MainTraceLinkState.Unobserved ? Theme.Dim : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                DrawText(g, stage.Sub, _owner._detailFont,
                    new Rectangle(cx - nodeW / 2, lineY + S(34), nodeW, S(11)), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawSnapshotCards(Graphics g, int x, int y, int width, int height, int gap)
        {
            int cardW = Math.Max(S(140), (width - gap * 2) / 3);
            DrawNetworkCard(g, new Rectangle(x, y, cardW, height));
            DrawLocationCard(g, new Rectangle(x + cardW + gap, y, cardW, height));
            DrawSensorCard(g, new Rectangle(x + (cardW + gap) * 2, y, cardW, height));
        }

        private void DrawNetworkCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceReport? report = _owner._view.LastReport;
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "网络与链路", "MOD / MQTT", Theme.Accent);
            int body = rect.Y + S(32);
            DrawText(g, s.Operator.Length > 0 ? s.Operator : "—", _owner._bigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(17)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string tech = (s.AccessTech.Length > 0 ? s.AccessTech : "—")
                          + (report?.Band is { Length: > 0 } band ? " · " + band : "");
            DrawText(g, tech, _owner._tinyFont,
                new Rectangle(rect.X + pad, body + S(16), rect.Width - pad * 2, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(32);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            DrawKv(g, "模组", s.ModemText, rect.X + pad, y, half,
                s.ModemState is "OFF" or "CLOSING" ? Theme.Danger : s.ModemState == "POWERSAVE" ? Theme.Amber : Theme.Green);
            DrawKv(g, "步骤", s.ModemStep.Length > 0 ? s.ModemStep : "—",
                rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKv(g, "MQTT", s.MqttSummary, rect.X + pad, y, half,
                s.MqttState is "" or "--" ? Theme.Dim : Theme.Green);
            DrawKv(g, "QHTTP", s.QhttpSummary, rect.X + pad + half + S(8), y, half, Theme.Dim);
            y += S(18);
            DrawKv(g, "信号 SIG", s.Sig.Length > 0 ? s.Sig : "—", rect.X + pad, y, half, Theme.Text);
            string data = _owner._view.DataSent.Length > 0
                ? "↑" + _owner._view.DataSent + " ↓" + _owner._view.DataRecv
                : "—";
            DrawKvSmall(g, "数据计数", data, rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            string lac = report is null ? "—" : (report.Lac.Length > 0 ? report.Lac : "—");
            string cid = report is null ? "—" : (report.Cid.Length > 0 ? report.Cid : "—");
            DrawKv(g, "LAC / CID", lac + " / " + cid, rect.X + pad, y, rect.Width - pad * 2, Theme.Text);
            string note = s.HasData
                ? "低功耗；MQTT 空闲；INQ_IP 为原值。"
                : "收到快照后填入 MOD / MQTT / QHTTP 原值。";
            DrawNote(g, note, new Rectangle(rect.X + pad, rect.Bottom - S(13), rect.Width - pad * 2, S(11)));
        }

        private void DrawLocationCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "定位与电源", "GPS / BAT", Blue);
            int body = rect.Y + S(32);
            DrawText(g, s.CoordinatesText, _owner._monoBigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(17)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, s.GpsText, _owner._tinyFont,
                new Rectangle(rect.X + pad, body + S(16), rect.Width - pad * 2, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(32);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            int full = rect.Width - pad * 2;
            DrawKv(g, "电池 / 电流", s.BatteryText + " · " + s.CurrentText, rect.X + pad, y, full, Theme.Green);
            y += S(18);
            DrawKv(g, "速度", s.SpeedText, rect.X + pad, y, half, Theme.Text);
            DrawKv(g, "容量", s.BatteryCapacity.Replace(" mAh", "mAh"), rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKv(g, "温度", s.BatteryTemperature.Replace(" °C", "°C"), rect.X + pad, y, half, Theme.Text);
            DrawKv(g, "GPS 分段", s.GpsMileage.Length > 0 ? s.GpsMileage : "—",
                rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKv(g, "行驶 / 弱信号", s.DrivingLabel + " · " + (s.GpsWeak.Length > 0 ? s.GpsWeak : "—"),
                rect.X + pad, y, full, Theme.Text);
            string note = s.HasData
                ? "坐标来自最新快照；年龄按主机时间。"
                : "收到 GPS / BAT 快照后填入。";
            DrawNote(g, note, new Rectangle(rect.X + pad, rect.Bottom - S(13), rect.Width - pad * 2, S(11)));
        }

        private void DrawSensorCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceReport? report = _owner._view.LastReport;
            MainTraceBindingInfo bindings = _owner._view.Bindings;
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "传感器观察", "BLE", Theme.Amber);
            int body = rect.Y + S(32);
            string door = s.DoorState.Length > 0 ? "门磁 " + s.DoorText : "门磁 —";
            DrawText(g, door, _owner._bigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(17)),
                s.DoorState == "INVALID" ? Theme.Danger : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string doorSub = s.HasData
                ? (s.DoorBound.Length > 0 ? s.DoorBound + " 绑定" : "绑定 —") + " · RPD " + (s.Rpd.Length > 0 ? s.Rpd : "—")
                : "等待快照";
            DrawText(g, doorSub, _owner._tinyFont,
                new Rectangle(rect.X + pad, body + S(16), rect.Width - pad * 2, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(32);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            string tpms = s.TpmsBound.Length > 0
                ? s.TpmsBound + " 绑定" + (report?.TpmsCount is int t ? " · " + t + " 条" : "")
                : (bindings.TpmsBound > 0 ? bindings.TpmsBound + " 绑定" : "—");
            DrawKvSmall(g, "TPMS", tpms, rect.X + pad, y, half, Theme.Text);
            string ths = report?.ThsText is { Length: > 0 } thsText
                ? thsText.Replace(" °C", "°C").Replace(" / ", " · ")
                : (s.ThsBound.Length > 0 ? s.ThsBound + " 绑定 · 未观测" : "—");
            DrawKvSmall(g, "THS", ths, rect.X + pad + half + S(8), y, half,
                report?.ThsText is { Length: > 0 } ? Theme.Green : Theme.Dim);
            y += S(18);
            DrawKv(g, "板载 TH", s.ThText, rect.X + pad, y, half, Theme.Dim);
            DrawKvSmall(g, "扫描策略", Dash(s.McuBleMode) + " / " + Dash(s.SocBleMode),
                rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKv(g, "MCU BLE", Dash(s.McuBleVersion), rect.X + pad, y, half, Theme.Text);
            DrawKv(g, "SoC BLE", Dash(s.SocBleVersion), rect.X + pad + half + S(8), y, half, Theme.Text);
            string note = report?.ThsText is { Length: > 0 }
                ? "THS 来自 " + report.EncodedAt.ToString("HH:mm:ss") + " 报文，非实时。"
                : "无传感器报文时显示未观测。";
            DrawNote(g, note, new Rectangle(rect.X + pad, rect.Bottom - S(13), rect.Width - pad * 2, S(11)));
        }

        private void DrawCardHeader(Graphics g, Rectangle rect, int pad, string title, string tag, Color color)
        {
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, rect.X + pad, rect.Y + S(12), S(5), S(5));
            DrawText(g, title, _owner._cardTitleFont,
                new Rectangle(rect.X + pad + S(9), rect.Y, rect.Width - pad * 2 - S(50), S(28)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int tagW = Measure(tag, _owner._chipFont).Width + S(8);
            var tagRect = new Rectangle(rect.Right - pad - tagW, rect.Y + S(8), tagW, S(14));
            using (var path = Theme.RoundedRect(tagRect, S(4)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text)))
                g.FillPath(brush, path);
            DrawText(g, tag, _owner._chipFont, tagRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(28), rect.Right - pad, rect.Y + S(28));
        }

        private void DrawKv(Graphics g, string label, string value, int x, int y, int width, Color color,
            Font? valueFont = null)
        {
            DrawText(g, label, _owner._kvLabelFont,
                new Rectangle(x, y, width, S(9)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, valueFont ?? _owner._kvValueFont,
                new Rectangle(x, y + S(8), width, S(11)), color,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawKvSmall(Graphics g, string label, string value, int x, int y, int width, Color color) =>
            DrawKv(g, label, value, x, y, width, color, _owner._detailFont);

        private void DrawNote(Graphics g, string text, Rectangle rect)
        {
            DrawText(g, text, _owner._tinyFont, rect, Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawReport(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(10);
            DrawText(g, "最近上报", _owner._cardTitleFont,
                new Rectangle(rect.X + pad, rect.Y, S(80), S(28)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int x = rect.X + pad + S(55);
            MainTraceReport? report = _owner._view.LastReport;
            x = DrawChip(g, report is null ? "— B" : report.Bytes + " B", x, rect.Y + S(7),
                Theme.Muted, Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Text), _owner._chipFont);
            if (report is not null)
            {
                x = DrawChip(g, report.JsonComplete ? "JSON 完整" : "JSON 未完整", x, rect.Y + S(7),
                    report.JsonComplete ? Theme.Green : Theme.Amber,
                    Theme.WithAlpha(Theme.IsLight ? 22 : 28, report.JsonComplete ? Theme.Green : Theme.Amber),
                    _owner._chipFont);
                Color outcomeColor = OutcomeColor(report.Outcome);
                x = DrawChip(g, report.OutcomeShortText, x, rect.Y + S(7), outcomeColor,
                    Theme.WithAlpha(Theme.IsLight ? 22 : 28, outcomeColor), _owner._chipFont);
            }
            string right = report is null ? "等待上报" : report.EncodedAt.ToString("HH:mm:ss.fff") + " · " + Age(report.EncodedAt);
            int rightW = Measure(right, _owner._eventTimeFont).Width;
            DrawText(g, right, _owner._eventTimeFont,
                new Rectangle(rect.Right - pad - rightW, rect.Y, rightW, S(28)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(28), rect.Right - pad, rect.Y + S(28));

            if (report is null)
            {
                DrawText(g, "等待 encode report ok / mqtt publish 日志", _owner._subFont,
                    new Rectangle(rect.X + pad, rect.Y + S(38), rect.Width - pad * 2, S(22)),
                    Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                var cloudWaiting = new Rectangle(rect.Right - pad - S(64), rect.Bottom - S(30), S(64), S(17));
                using (var path = Theme.RoundedRect(cloudWaiting, S(4)))
                using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 60 : 80, Theme.Dim), 1))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
                DrawText(g, "云端 未观测 ›", _owner._detailFont, cloudWaiting, Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, "上报阶段会在收到编码、提交、API 返回与业务成功日志后分别点亮。", _owner._tinyFont,
                    new Rectangle(rect.X + pad, rect.Bottom - S(18), rect.Width - pad * 2 - S(72), S(11)),
                    Theme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine);
                return;
            }

            int pipeY = rect.Y + S(33);
            int stageW = (rect.Width - pad * 2 - S(64)) / 4;
            string[] labels = { "编码", "提交", "API 返回", "业务成功日志" };
            string[] values = report is null
                ? new[] { "—", "—", "—", "—" }
                : new[]
                {
                    report.Bytes + " B",
                    report.Topic.Length > 0 ? "at+qmtpubex" : "等待",
                    report.HasApi ? report.ApiResult : "未观测",
                    report.BusinessSuccess ? "已观察" : "未观测",
                };
            string[] subs = report is null
                ? new[] { "", "", "", "" }
                : new[]
                {
                    report.EncodedAt.ToString("HH:mm:ss.fff"),
                    report.SubmittedAt?.ToString("HH:mm:ss.fff") ?? "未提交",
                    report.ApiAt?.ToString("HH:mm:ss.fff") ?? "无结果",
                    report.BusinessSuccessAt?.ToString("HH:mm:ss.fff") ?? "不推断失败",
                };
            Color[] colors =
            {
                Theme.Green,
                report?.SubmittedAt is null ? Theme.Dim : Theme.Green,
                report is null ? Theme.Dim : report.HasApi ? Theme.Green : Theme.Amber,
                report is null ? Theme.Dim : report.BusinessSuccess ? Theme.Green : Theme.Amber,
            };
            for (int i = 0; i < 4; i++)
            {
                int sx = rect.X + pad + stageW * i;
                using (var brush = new SolidBrush(colors[i]))
                    g.FillEllipse(brush, sx, pipeY + S(3), S(6), S(6));
                DrawText(g, labels[i], _owner._tinyFont,
                    new Rectangle(sx + S(9), pipeY, stageW - S(14), S(11)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                DrawText(g, values[i], _owner._chipFont,
                    new Rectangle(sx + S(9), pipeY + S(10), stageW - S(14), S(11)),
                    colors[i], TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (subs[i].Length > 0)
                    DrawText(g, subs[i], _owner._detailFont,
                        new Rectangle(sx + S(9), pipeY + S(20), stageW - S(14), S(9)), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (i < 3)
                {
                    using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 50 : 70, colors[i + 1]), 1);
                    g.DrawLine(pen, sx + S(12), pipeY + S(3), sx + stageW - S(6), pipeY + S(3));
                }
            }
            var cloud = new Rectangle(rect.Right - pad - S(58), pipeY + S(3), S(58), S(17));
            using (var path = Theme.RoundedRect(cloud, S(4)))
            using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 60 : 80, Theme.Dim), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawPath(pen, path);
            }
            DrawText(g, "云端 未观测 ›", _owner._detailFont, cloud, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int footY = rect.Y + S(66);
            int colW = (rect.Width - pad * 2 - S(20)) / 3;
            string battery =
                (report!.BatteryVoltage is double v ? v.ToString("0.00", CultureInfo.InvariantCulture) + " V" : "—")
                + " · " + (report.BatteryPercent is double p ? p.ToString("0", CultureInfo.InvariantCulture) + "%" : "—");
            string door = report.DoorStatus is double doorValue
                ? doorValue.ToString("0", CultureInfo.InvariantCulture) + " · " + DoorStatusLabel(doorValue)
                  + (report.EventCode is double evt ? " · event " + evt.ToString("0", CultureInfo.InvariantCulture) : "")
                : "—";
            string gps = report.CoordinatesText;
            string net = (report.Band.Length > 0 ? report.Band : "—")
                         + " · LAC " + (report.Lac.Length > 0 ? report.Lac : "—");
            string iccid = report.MaskedIccid + " · pwd •••";
            string outcome = report.Outcome switch
            {
                MainTraceReportOutcome.Success => "业务成功"
                    + (report.BusinessSuccessAt is DateTime at ? " " + at.ToString("HH:mm:ss") : ""),
                MainTraceReportOutcome.ApiReturned => "业务成功未观测",
                MainTraceReportOutcome.Submitted => "已提交 · 结果未观测",
                MainTraceReportOutcome.Failed => "失败",
                MainTraceReportOutcome.Encoded => "已编码 · 未提交",
                _ => "编码中",
            };
            DrawReportField(g, "电池 / SOC", battery, rect.X + pad, footY, colW);
            DrawReportField(g, "门磁", door, rect.X + pad + colW + S(10), footY, colW);
            DrawReportField(g, "GPS", gps, rect.X + pad + (colW + S(10)) * 2, footY, colW);
            DrawReportField(g, "网络", net, rect.X + pad, footY + S(14), colW);
            DrawReportField(g, "ICCID", iccid, rect.X + pad + colW + S(10), footY + S(14), colW);
            DrawReportField(g, "结果", outcome, rect.X + pad + (colW + S(10)) * 2, footY + S(14), colW);

            Color dot = report.BusinessSuccess ? Theme.Green
                : report.Failed ? Theme.Danger
                : Theme.Amber;
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, rect.X + pad, rect.Bottom - S(13), S(5), S(5));
            string note = report.BusinessSuccess
                ? "已观察到业务成功日志；云端回执未观测，不推断服务端已处理完成。"
                : report.Failed
                    ? "发布失败：" + Short(report.Failure, 80)
                    : "API 返回只代表固件提交；业务成功日志与云端回执均未观测，不推断为失败。";
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.X + pad + S(9), rect.Bottom - S(15), rect.Width - pad * 2 - S(66), S(11)),
                report.Failed ? Theme.Danger : report.BusinessSuccess ? Theme.Muted : Theme.AmberText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string link = "查看证据 ›";
            int linkW = Measure(link, _owner._chipFont).Width;
            DrawText(g, link, _owner._chipFont,
                new Rectangle(rect.Right - pad - linkW, rect.Bottom - S(16), linkW, S(11)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        private void DrawReportField(Graphics g, string label, string value, int x, int y, int width)
        {
            DrawText(g, label, _owner._tinyFont, new Rectangle(x, y, width, S(9)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, _owner._detailFont, new Rectangle(x, y + S(8), width, S(10)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawEvents(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(10);
            DrawText(g, "业务时间线", _owner._cardTitleFont,
                new Rectangle(rect.X + pad, rect.Y, S(80), S(27)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawFilters(g, rect, pad);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(27), rect.Right - pad, rect.Y + S(27));

            List<MainTraceEvent> events = FilteredEvents();
            int headerH = S(27);
            int detailH = S(22);
            int rowH = S(20);
            int rows = Math.Max(1, (rect.Height - headerH - detailH) / rowH);
            int maxScroll = Math.Max(0, events.Count - rows);
            if (_owner._eventScroll > maxScroll) _owner._eventScroll = maxScroll;
            if (_owner._eventScroll < 0) _owner._eventScroll = 0;
            _owner._eventRects = new List<(MainTraceEvent Event, Rectangle Rect)>();
            if (events.Count == 0)
            {
                DrawText(g, "暂无事件 · 等待主日志", _owner._subFont,
                    new Rectangle(rect.X + pad, rect.Y + headerH, rect.Width - pad * 2, rect.Height - headerH - detailH),
                    Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                for (int i = 0; i < rows; i++)
                {
                    int index = _owner._eventScroll + i;
                    if (index >= events.Count) break;
                    MainTraceEvent ev = events[index];
                    int ry = rect.Y + headerH + i * rowH;
                    var row = new Rectangle(rect.X + pad, ry, rect.Width - pad * 2, rowH);
                    _owner._eventRects.Add((ev, row));
                    bool selected = ReferenceEquals(ev, _owner._selectedEvent);
                    if (selected)
                    {
                        using var path = Theme.RoundedRect(row, S(4));
                        using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 35 : 42, Theme.Accent));
                        g.FillPath(brush, path);
                    }
                    else if (i > 0)
                    {
                        using var pen = new Pen(Theme.BorderSoft, 1);
                        g.DrawLine(pen, row.X, row.Y, row.Right, row.Y);
                    }
                    DrawEventRow(g, ev, row);
                }
            }
            int detailY = rect.Bottom - detailH;
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, detailY, rect.Right - pad, detailY);
            MainTraceEvent? shown = _owner._selectedEvent ?? (events.Count > 0 ? events[0] : null);
            if (shown is not null)
            {
                string prefix = _owner._selectedEvent is null ? "最新" : "选中 " + shown.Tag;
                DrawText(g, prefix, _owner._detailFont,
                    new Rectangle(rect.X + pad, detailY, S(54), detailH), Theme.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, "原始", _owner._detailFont,
                    new Rectangle(rect.X + pad + S(54), detailY, S(24), detailH), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, shown.Raw, _owner._detailFont,
                    new Rectangle(rect.X + pad + S(80), detailY, rect.Width - pad * 2 - S(120), detailH),
                    Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                DrawText(g, "原文 ›", _owner._detailFont,
                    new Rectangle(rect.Right - pad - S(36), detailY, S(36), detailH), Theme.Accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private void DrawFilters(Graphics g, Rectangle rect, int pad)
        {
            _owner._filterRects = new List<(EventFilter, Rectangle)>();
            string[] labels = { "全部", "状态", "网络", "上报", "传感器", "异常", "拍照" };
            EventFilter[] filters =
            {
                EventFilter.All, EventFilter.State, EventFilter.Network, EventFilter.Report,
                EventFilter.Sensor, EventFilter.Alert, EventFilter.Camera,
            };
            int right = rect.Right - pad;
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                int width = Measure(labels[i], _owner._chipFont).Width + S(8);
                var chip = new Rectangle(right - width, rect.Y + S(7), width, S(14));
                right -= width + S(3);
                _owner._filterRects.Add((filters[i], chip));
                bool active = _owner._filter == filters[i];
                if (active)
                {
                    using var path = Theme.RoundedRect(chip, S(4));
                    using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 26 : 32, Theme.Accent));
                    g.FillPath(brush, path);
                    using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 100, Theme.Accent), 1);
                    g.DrawPath(pen, path);
                }
                DrawText(g, labels[i], _owner._chipFont, chip,
                    active ? Theme.Accent : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private void DrawEventRow(Graphics g, MainTraceEvent ev, Rectangle row)
        {
            int pad = S(4);
            int x = row.X + pad;
            DrawText(g, ev.Time.ToString("HH:mm:ss.fff"), _owner._eventTimeFont,
                new Rectangle(x, row.Y, S(58), row.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(60);
            Color color = KindColor(ev.Kind);
            int tagW = Math.Max(S(25), Measure(ev.Tag, _owner._chipFont).Width + S(8));
            var tag = new Rectangle(x, row.Y + (row.Height - S(13)) / 2, tagW, S(13));
            using (var path = Theme.RoundedRect(tag, S(4)))
            {
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 22 : 28, color));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 60 : 80, color), 1);
                g.DrawPath(pen, path);
            }
            DrawText(g, ev.Tag, _owner._chipFont, tag, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x = tag.Right + S(6);
            int sourceW = S(44);
            DrawText(g, ev.Text, _owner._eventFont,
                new Rectangle(x, row.Y, Math.Max(S(40), row.Right - x - sourceW - S(14)), row.Height),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, ev.Source, _owner._detailFont,
                new Rectangle(row.Right - sourceW - S(8), row.Y, sourceW, row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "›", _owner._tinyFont,
                new Rectangle(row.Right - S(9), row.Y, S(9), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawFooter(Graphics g, int pad, int y, int width)
        {
            MainTraceView view = _owner._view;
            string left = _owner._sourceLabel();
            if (left.Length == 0) left = "COM · Main Trace";
            string mid = "会话 " + view.SessionText + " · 逻辑消息 " + view.LogicalMessageCount
                         + " · 快照 " + view.CompleteSnapshotsText + " · 事件 " + view.EventCount;
            string right = view.ReportCount + " 上报 · " + view.DownlinkCount + " 下发 · 本机 TX "
                           + view.TxCount + " · 原始日志 ›";
            int rightW = Measure(right, _owner._detailFont).Width;
            int midW = Measure(mid, _owner._detailFont).Width;
            int leftW = Math.Max(S(80), width - rightW - midW - S(24));
            DrawText(g, left, _owner._detailFont, new Rectangle(pad, y, leftW, S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, mid, _owner._detailFont, new Rectangle(pad + leftW + S(8), y, midW, S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, right, _owner._detailFont, new Rectangle(Width - pad - rightW, y, rightW, S(12)),
                Theme.Dim, TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        private bool HasMainStateChange() =>
            _owner._view.Events.Any(ev => ev.Kind == MainTraceEventKind.State
                                          && ev.Text.StartsWith("主状态 ", StringComparison.Ordinal));

        private List<MainTraceEvent> FilteredEvents()
        {
            IEnumerable<MainTraceEvent> events = _owner._view.Events;
            if (_owner._filter != EventFilter.All)
            {
                events = events.Where(ev => Matches(ev.Kind, _owner._filter));
            }
            return events.ToList();
        }

        private static bool Matches(MainTraceEventKind kind, EventFilter filter) => filter switch
        {
            EventFilter.All => true,
            EventFilter.State => kind == MainTraceEventKind.State,
            EventFilter.Network => kind is MainTraceEventKind.Network or MainTraceEventKind.Downlink,
            EventFilter.Report => kind == MainTraceEventKind.Report,
            EventFilter.Sensor => kind == MainTraceEventKind.Sensor,
            EventFilter.Alert => kind == MainTraceEventKind.Alert,
            EventFilter.Camera => kind == MainTraceEventKind.Camera,
            _ => true,
        };

        private Color LinkColor(MainTraceLinkState state) => state switch
        {
            MainTraceLinkState.Observed => Theme.Green,
            MainTraceLinkState.Active => Blue,
            MainTraceLinkState.Warning => Theme.Amber,
            _ => Theme.Dim,
        };

        private Color KindColor(MainTraceEventKind kind) => kind switch
        {
            MainTraceEventKind.State => Theme.Accent,
            MainTraceEventKind.Report => Theme.Amber,
            MainTraceEventKind.Sensor => Blue,
            MainTraceEventKind.Alert => Theme.Danger,
            MainTraceEventKind.Camera => Purple,
            MainTraceEventKind.Network => Theme.Green,
            MainTraceEventKind.Downlink => Blue,
            _ => Theme.Dim,
        };

        private Color OutcomeColor(MainTraceReportOutcome outcome) => outcome switch
        {
            MainTraceReportOutcome.Success => Theme.Green,
            MainTraceReportOutcome.ApiReturned => Theme.Amber,
            MainTraceReportOutcome.Submitted => Theme.Amber,
            MainTraceReportOutcome.Failed => Theme.Danger,
            MainTraceReportOutcome.Encoded => Blue,
            _ => Theme.Dim,
        };

        private int DrawChip(Graphics g, string text, int x, int y, Color fore, Color back, Font font)
        {
            Size size = Measure(text, font);
            int height = S(14);
            var rect = new Rectangle(x, y, size.Width + S(10), height);
            using (var path = Theme.RoundedRect(rect, S(4)))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            DrawText(g, text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return rect.Right + S(5);
        }

        private int DrawChipRight(Graphics g, string text, int right, int y, Color fore, Color back, Font font)
        {
            Size size = Measure(text, font);
            int height = S(14);
            int x = right - (size.Width + S(10));
            var rect = new Rectangle(x, y, size.Width + S(10), height);
            using (var path = Theme.RoundedRect(rect, S(4)))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            DrawText(g, text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return x - S(5);
        }

        private void DrawCard(Graphics g, Rectangle rect, Color fill, Color border)
        {
            using var path = Theme.RoundedRect(rect, S(9));
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border, 1);
            g.DrawPath(pen, path);
        }

        private Color Blue => Theme.IsLight ? Color.FromArgb(21, 101, 192) : Color.FromArgb(120, 172, 255);
        private Color Purple => Theme.IsLight ? Color.FromArgb(106, 63, 181) : Color.FromArgb(199, 146, 234);

        private static string Dash(string value) => value.Length > 0 ? value : "—";

        private static string Short(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max) + "…";

        private static string Age(DateTime time)
        {
            double seconds = Math.Max(0, (DateTime.Now - time).TotalSeconds);
            if (seconds < 1) return "刚刚";
            if (seconds < 60) return Math.Round(seconds) + " s 前";
            return Math.Floor(seconds / 60) + " min 前";
        }

        private static string DoorStatusLabel(double value) => value switch
        {
            0 => "CLOSE",
            1 => "OPEN",
            255 => "INVALID",
            _ => value.ToString("0", CultureInfo.InvariantCulture),
        };

        private static void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color,
            TextFormatFlags flags) =>
            TextRenderer.DrawText(g, text, font, rect, color,
                flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        private static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void OnEventWheel(int delta)
    {
        List<MainTraceEvent> events = GetFilteredEvents();
        int rows = Math.Max(1, _eventRects.Count);
        int max = Math.Max(0, events.Count - rows);
        _eventScroll = Math.Max(0, Math.Min(max, _eventScroll + (delta < 0 ? 3 : -3)));
        Invalidate(true);
    }

    private List<MainTraceEvent> GetFilteredEvents()
    {
        IEnumerable<MainTraceEvent> events = _view.Events;
        if (_filter != EventFilter.All)
            events = events.Where(ev => Matches(ev.Kind, _filter));
        return events.ToList();
    }

    private static bool Matches(MainTraceEventKind kind, EventFilter filter) => filter switch
    {
        EventFilter.All => true,
        EventFilter.State => kind == MainTraceEventKind.State,
        EventFilter.Network => kind is MainTraceEventKind.Network or MainTraceEventKind.Downlink,
        EventFilter.Report => kind == MainTraceEventKind.Report,
        EventFilter.Sensor => kind == MainTraceEventKind.Sensor,
        EventFilter.Alert => kind == MainTraceEventKind.Alert,
        EventFilter.Camera => kind == MainTraceEventKind.Camera,
        _ => true,
    };

    private void OnBodyClick(Point point)
    {
        if (_closeRect.Contains(point))
        {
            Close();
            return;
        }
        if (_pauseRect.Contains(point))
        {
            TogglePause();
            return;
        }
        foreach (var (filter, rect) in _filterRects)
        {
            if (rect.Contains(point))
            {
                SetFilter(filter);
                return;
            }
        }
        foreach (var (ev, rect) in _eventRects)
        {
            if (rect.Contains(point))
            {
                _selectedEvent = ev;
                Invalidate(true);
                return;
            }
        }
        if (_evidenceRect.Contains(point))
        {
            MainTraceEvent? alert = _view.Events.FirstOrDefault(ev => ev.Alert);
            if (alert is not null)
            {
                _filter = EventFilter.Alert;
                _eventScroll = 0;
                _selectedEvent = alert;
                Invalidate(true);
            }
        }
    }
}
