using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>Trace监控：设备快照、上行/下发报文历史与聚合业务时间线。</summary>
internal sealed class MainTraceWindow : Form
{
    private enum MessageTab { Up, Down }
    private enum TimelineFilter { All, State, Report, Sensor, Alert, Camera }

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
    private MessageTab _messageTab = MessageTab.Up;
    private int _messageLimit = 20;
    private int _messageScroll;
    private TimelineFilter _timelineFilter = TimelineFilter.All;
    private int _timelineScroll;
    private MainTraceReport? _selectedReport;
    private MainTraceDownlink? _selectedDownlink;
    private MainTraceTimelineItem? _selectedTimeline;
    private Rectangle _pauseRect;
    private Rectangle _closeRect;
    private Rectangle _tabUpRect;
    private Rectangle _tabDownRect;
    private Rectangle _limitRect;
    private Rectangle _messageListRect;
    private Rectangle _timelineListRect;
    private Rectangle _messageDetailRect;
    private Rectangle _timelineDetailRect;
    private readonly List<(TimelineFilter Filter, Rectangle Rect)> _filterRects = new();
    private readonly List<(MainTraceReport? Report, MainTraceDownlink? Downlink, Rectangle Rect)> _messageRects = new();
    private readonly List<(MainTraceTimelineItem Item, Rectangle Rect)> _timelineRects = new();
    private int _messageVisibleRows = 1;
    private int _timelineVisibleRows = 1;

    internal MainTraceWindow(Func<MainTraceView> snapshot, Func<string> sourceLabel)
    {
        _snapshot = snapshot;
        _sourceLabel = sourceLabel;
        Text = "Trace监控";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F);
        _titleFont = new Font(Font.FontFamily, 11.5F, FontStyle.Bold);
        _subFont = new Font(Font.FontFamily, 7.5F);
        _heroFont = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        _heroSubFont = new Font(Font.FontFamily, 7F, FontStyle.Bold);
        _cardTitleFont = new Font(Font.FontFamily, 7.2F, FontStyle.Bold);
        _bigFont = new Font(Font.FontFamily, 8.8F, FontStyle.Bold);
        _monoBigFont = Theme.MonoFont(7.2F);
        _kvLabelFont = new Font(Font.FontFamily, 5.6F);
        _kvValueFont = Theme.MonoFont(6.6F);
        _tinyFont = new Font(Font.FontFamily, 5.4F);
        _eventFont = new Font(Font.FontFamily, 6F);
        _eventTimeFont = Theme.MonoFont(5.2F);
        _detailFont = Theme.MonoFont(5.2F);
        _chipFont = new Font(Font.FontFamily, 5.4F);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        ClientSize = new Size(Theme.Scale(this, 760), Theme.Scale(this, 800));
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
            return s.MainState + "/" + s.SubState + "|" + s.Model + "|" + s.DoorState + "|"
                   + _view.SnapshotCount + "/" + _view.MalformedSnapshots + "|"
                   + (r is null ? "no-report" : r.Bytes + ":" + r.Outcome) + "|"
                   + _view.ReportSuccessCount + "/" + _view.ReportUnobservedCount + "|"
                   + _view.DownlinkMessageCount + "|" + _view.TxCount + "|" + _view.Timeline.Count;
        }
    }

    internal bool PausedForTest => _paused;
    internal void TogglePauseForTest() => TogglePause();
    internal void ShowDownForTest() => SetMessageTab(MessageTab.Down);
    internal void ShowUpForTest() => SetMessageTab(MessageTab.Up);
    internal void CycleLimitForTest() => CycleLimit();
    internal string MessageStateForTest =>
        _messageTab + "|" + _messageLimit + "|" + _view.Reports.Count + "|" + _view.Downlinks.Count + "|" + _view.Timeline.Count;
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
            Theme.Scale(this, 760) + extra.Width,
            Theme.Scale(this, 800) + extra.Height);
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
        && a.DownlinkMessageCount == b.DownlinkMessageCount
        && a.Timeline.Count == b.Timeline.Count
        && a.LastReport?.Outcome == b.LastReport?.Outcome
        && a.LastReport?.Bytes == b.LastReport?.Bytes;

    private void TogglePause()
    {
        _paused = !_paused;
        Invalidate(true);
    }

    private void SetMessageTab(MessageTab tab)
    {
        _messageTab = tab;
        _messageScroll = 0;
        Invalidate(true);
    }

    private void CycleLimit()
    {
        _messageLimit = _messageLimit switch { 10 => 20, 20 => 30, _ => 10 };
        _messageScroll = 0;
        Invalidate(true);
    }

    private void SetTimelineFilter(TimelineFilter filter)
    {
        _timelineFilter = filter;
        _timelineScroll = 0;
        _selectedTimeline = null;
        Invalidate(true);
    }

    private void OnMessageWheel(int delta)
    {
        int count = Math.Min(MessageTotal(), _messageLimit);
        int max = Math.Max(0, count - _messageVisibleRows);
        _messageScroll = Math.Max(0, Math.Min(max, _messageScroll + (delta < 0 ? 3 : -3)));
        Invalidate(true);
    }

    private void OnTimelineWheel(int delta)
    {
        int count = FilteredTimeline().Count;
        int max = Math.Max(0, count - _timelineVisibleRows);
        _timelineScroll = Math.Max(0, Math.Min(max, _timelineScroll + (delta < 0 ? 3 : -3)));
        Invalidate(true);
    }

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
        if (_tabUpRect.Contains(point))
        {
            SetMessageTab(MessageTab.Up);
            return;
        }
        if (_tabDownRect.Contains(point))
        {
            SetMessageTab(MessageTab.Down);
            return;
        }
        if (_limitRect.Contains(point))
        {
            CycleLimit();
            return;
        }
        foreach (var (filter, rect) in _filterRects)
        {
            if (!rect.Contains(point)) continue;
            SetTimelineFilter(filter);
            return;
        }
        foreach (var (report, downlink, rect) in _messageRects)
        {
            if (!rect.Contains(point)) continue;
            if (report is not null) _selectedReport = report;
            if (downlink is not null) _selectedDownlink = downlink;
            Invalidate(true);
            return;
        }
        foreach (var (item, rect) in _timelineRects)
        {
            if (!rect.Contains(point)) continue;
            _selectedTimeline = item;
            Invalidate(true);
            return;
        }
        if (_messageDetailRect.Contains(point))
        {
            if (_messageTab == MessageTab.Up && _view.Reports.Count > 0)
            {
                MainTraceReport report = _selectedReport ?? _view.Reports[0];
                ShowTextViewer("报文原文", report.MaskedPayload, 660);
            }
            else if (_messageTab == MessageTab.Down && _view.Downlinks.Count > 0)
            {
                MainTraceDownlink downlink = _selectedDownlink ?? _view.Downlinks[0];
                ShowTextViewer("报文原文", downlink.Payload, 660);
            }
            return;
        }
        if (_timelineDetailRect.Contains(point))
        {
            MainTraceTimelineItem? item = _selectedTimeline ?? FilteredTimeline().FirstOrDefault();
            if (item is not null) ShowTextViewer("事件原文", item.Raw, 620);
            return;
        }
    }

    private void ShowTextViewer(string title, string content, int width)
    {
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.MonoFont(9F),
            Text = content,
        };
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            ShowInTaskbar = false,
            ShowIcon = false,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.None,
            ClientSize = new Size(Theme.Scale(this, width), Theme.Scale(this, 380)),
        };
        Theme.ApplyWindow(dialog);
        dialog.BackColor = Theme.Surface;
        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }

    private int MessageTotal() =>
        _messageTab == MessageTab.Up ? _view.Reports.Count : _view.Downlinks.Count;

    private List<MainTraceTimelineItem> FilteredTimeline()
    {
        IEnumerable<MainTraceTimelineItem> items = _view.Timeline;
        if (_timelineFilter != TimelineFilter.All)
        {
            items = _timelineFilter switch
            {
                TimelineFilter.State => items.Where(i => i.Kind == MainTraceEventKind.State),
                TimelineFilter.Report => items.Where(i => i.Kind == MainTraceEventKind.Report),
                TimelineFilter.Sensor => items.Where(i => i.Kind == MainTraceEventKind.Sensor),
                TimelineFilter.Alert => items.Where(i => i.Kind == MainTraceEventKind.Alert),
                TimelineFilter.Camera => items.Where(i => i.Kind == MainTraceEventKind.Camera),
                _ => items,
            };
        }
        return items.ToList();
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
            MouseWheel += (_, e) =>
            {
                Point point = PointToClient(Cursor.Position);
                Rectangle messageList = _owner._messageListRect;
                Rectangle timelineList = _owner._timelineListRect;
                if (messageList.Contains(point)) _owner.OnMessageWheel(e.Delta);
                else if (timelineList.Contains(point)) _owner.OnTimelineWheel(e.Delta);
            };
            MouseClick += (_, e) => owner.OnBodyClick(e.Location);
        }

        private int S(int value) => Theme.Scale(this, value);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int topH = S(38);
            int pad = S(14);
            int gap = S(5);
            int width = Math.Max(S(500), Width - pad * 2);
            int y = topH + S(10);
            DrawTopBar(g, 0, 0, Width, topH, pad);
            y = DrawSection(g, "设备信息", "版本以实际可观察数据为准", pad, y, width);
            y = DrawDeviceInfo(g, pad, y, width) + gap;
            int heroH = S(62);
            DrawHero(g, new Rectangle(pad, y, width, heroH));
            y += heroH + gap;
            y = DrawSection(g, "当前快照", "最近可观察到的设备数据", pad, y, width);
            y = DrawSnapshots(g, pad, y, width, S(144)) + gap;
            y = DrawSection(g, "传感器观察", "无传感器报文时不推断在线或正常", pad, y, width);
            y = DrawSensor(g, pad, y, width, S(80)) + gap;
            int bottomHeading = S(15);
            int bottomCardH = S(300);
            int bottomH = bottomHeading + S(3) + bottomCardH;
            DrawBottom(g, pad, y, width, bottomHeading, bottomCardH);
            y += bottomH + gap;
            DrawFooter(g, pad, y, width);
        }

        private void DrawFooter(Graphics g, int pad, int y, int width)
        {
            MainTraceView view = _owner._view;
            string left = _owner._sourceLabel();
            if (left.Length == 0) left = "COM · 921600 · Main Trace";
            string mid = "本会话 " + view.SessionText + " · 真实上行 " + view.Reports.Count
                         + " · 真实下发 " + view.Downlinks.Count + " · 关键事件 " + view.Timeline.Count;
            string right = "本机 TX " + view.TxCount + " · 原始日志 ›";
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

        private void DrawTopBar(Graphics g, int x, int y, int width, int height, int pad)
        {
            using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(1, width), height),
                       Theme.Surface2, Theme.Surface, 90f))
                g.FillRectangle(brush, x, y, width, height);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x, y + height - 1, x + width, y + height - 1);
            int mark = S(22);
            var markRect = new Rectangle(x + pad, y + (height - mark) / 2, mark, mark);
            using (var path = Theme.RoundedRect(markRect, S(6)))
            using (var brush = new LinearGradientBrush(markRect, Theme.AccentBright, Theme.AccentDeep, 45f))
                g.FillPath(brush, path);
            DrawText(g, "TR", _owner._chipFont, markRect, Theme.AccentInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int tx = markRect.Right + S(8);
            DrawText(g, "Trace监控", _owner._titleFont, new Rectangle(tx, y, S(100), height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int titleW = Measure("Trace监控", _owner._titleFont).Width;
            DrawText(g, "MAIN TRACE · 状态、报文与业务事件", _owner._subFont,
                new Rectangle(tx + titleW + S(8), y, S(230), height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int right = x + width - pad;
            int icon = S(20);
            _owner._closeRect = new Rectangle(right - icon, y + (height - icon) / 2, icon, icon);
            DrawText(g, "×", _owner._heroSubFont, _owner._closeRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= icon + S(4);
            _owner._pauseRect = new Rectangle(right - icon, y + (height - icon) / 2, icon, icon);
            DrawText(g, _owner._paused ? "▶" : "Ⅱ", _owner._subFont, _owner._pauseRect, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            right -= icon + S(7);
            string clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            int clockW = Measure(clock, _owner._eventTimeFont).Width + S(4);
            DrawText(g, clock, _owner._eventTimeFont, new Rectangle(right - clockW, y, clockW, height), Theme.Dim,
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
            right -= liveW + S(8);
            string source = _owner._sourceLabel();
            if (source.Length == 0) source = "COM · 921600 · Main Trace";
            int sourceW = Measure(source, _owner._tinyFont).Width + S(10);
            var sourceRect = new Rectangle(right - sourceW, y + (height - S(18)) / 2, sourceW, S(18));
            using (var path = Theme.RoundedRect(sourceRect, S(4)))
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawPath(pen, path);
            DrawText(g, source, _owner._tinyFont, sourceRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private int DrawSection(Graphics g, string title, string note, int pad, int y, int width)
        {
            var bar = new Rectangle(pad + S(1), y + S(2), S(3), S(11));
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillRectangle(brush, bar);
            DrawText(g, title, _owner._cardTitleFont,
                new Rectangle(pad + S(8), y, width - S(120), S(15)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(pad + width - S(260), y, S(260), S(15)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return y + S(15) + S(3);
        }

        private int DrawDeviceInfo(Graphics g, int x, int y, int width)
        {
            int h = S(42);
            DrawCard(g, new Rectangle(x, y, width, h), Theme.Raised, Theme.BorderSoft);
            string[] labels = { "设备型号", "MCU 固件", "Bootloader", "SoC 固件", "MCU BLE", "SoC BLE", "Linux 内核" };
            MainTraceSnapshot s = _owner._view.Snapshot;
            string[] values =
            {
                s.ModelText, Dash(s.Firmware), Dash(s.Bootloader), Dash(s.SocVersion),
                Dash(s.McuBleVersion), Dash(s.SocBleVersion), "未观测",
            };
            int cellW = width / 7;
            for (int i = 0; i < 7; i++)
            {
                int cx = x + i * cellW;
                if (i > 0)
                    using (var pen = new Pen(Theme.BorderSoft, 1))
                        g.DrawLine(pen, cx, y + S(6), cx, y + h - S(6));
                DrawText(g, labels[i], _owner._tinyFont,
                    new Rectangle(cx + S(9), y + S(6), cellW - S(12), S(11)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                DrawText(g, values[i], _owner._monoBigFont,
                    new Rectangle(cx + S(9), y + S(18), cellW - S(12), S(15)),
                    i == 0 ? Theme.Accent : i == 6 && values[i] == "未观测" ? Theme.Dim : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            return y + h;
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
            int pad = S(12);
            int leadW = S(200);
            int x = rect.X + pad;
            DrawText(g, "运行总状态", _owner._tinyFont,
                new Rectangle(x, rect.Y + S(5), leadW, S(11)), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, s.MainStateText, _owner._heroFont,
                new Rectangle(x - S(1), rect.Y + S(14), leadW, S(22)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int stateW = Measure(s.MainStateText, _owner._heroFont).Width;
            int chipX = x + Math.Min(leadW - S(42), stateW + S(7));
            DrawChip(g, s.SubStateText, chipX, rect.Y + S(21), Theme.Accent,
                Theme.WithAlpha(Theme.IsLight ? 24 : 32, Theme.Accent), _owner._chipFont);
            string note = s.HasData
                ? (HasMainStateChange() ? "Idle 快照 · 已记录主状态切换" : "Idle 快照 · 未观测到主状态切换")
                : "等待 #HH:MM:SS 快照";
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(x, rect.Y + S(39), leadW, S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int ringW = S(106);
            int metricX = rect.X + pad + leadW + S(2);
            int metricW = Math.Max(S(180), rect.Right - pad - ringW - metricX);
            int colW = metricW / 4;
            MainTraceReport? report = _owner._view.LastReport;
            DrawMetric(g, "快照周期", _owner._view.SnapshotPeriodText,
                s.HasData ? _owner._view.SnapshotCount + " 条" : "", metricX, rect.Y, colW, rect.Height);
            DrawMetric(g, "本次会话", _owner._view.SessionText, s.HasData ? "COM38" : "",
                metricX + colW, rect.Y, colW, rect.Height);
            DrawMetric(g, "最近报文", report is null ? "—" : report.Bytes + " B",
                report is null ? "" : Age(report.EncodedAt), metricX + colW * 2, rect.Y, colW, rect.Height);
            string soc = report?.SocPower is double power
                ? power.ToString("0", CultureInfo.InvariantCulture)
                : "未观测";
            DrawMetric(g, "SoC 电源", soc, report?.SocPower is double ? "报文时点" : "等待来源",
                metricX + colW * 3, rect.Y, colW, rect.Height);

            int ringX = rect.Right - pad - ringW + S(4);
            DrawRing(g, new Rectangle(ringX, rect.Y + S(9), S(44), S(44)),
                _owner._view.CompleteRatio, s.HasData);
            int textX = ringX + S(50);
            DrawText(g, s.HasData ? _owner._view.CompleteSnapshotsText : "等待", _owner._eventTimeFont,
                new Rectangle(textX, rect.Y + S(10), ringW - S(52), S(12)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "快照重组完整", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(23), ringW - S(52), S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "软换行已还原", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(34), ringW - S(52), S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private void DrawMetric(Graphics g, string label, string value, string sub, int x, int y, int width, int height)
        {
            using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 30 : 42, Theme.Accent), 1))
                g.DrawLine(pen, x, y + S(10), x, y + height - S(8));
            int tx = x + S(8);
            DrawText(g, label, _owner._tinyFont, new Rectangle(tx, y + S(9), width - S(10), S(11)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, _owner._kvValueFont, new Rectangle(tx, y + S(22), width - S(10), S(14)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (sub.Length > 0)
                DrawText(g, sub, _owner._tinyFont, new Rectangle(tx, y + S(37), width - S(10), S(10)), Theme.Dim,
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
            DrawText(g, hasData ? Math.Round(ratio * 100) + "%" : "—", _owner._eventTimeFont, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private int DrawSnapshots(Graphics g, int x, int y, int width, int height)
        {
            int gap = S(10);
            int cardW = (width - gap) / 2;
            DrawNetworkCard(g, new Rectangle(x, y, cardW, height));
            DrawLocationCard(g, new Rectangle(x + cardW + gap, y, cardW, height));
            return y + height;
        }

        private void DrawNetworkCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceReport? report = _owner._view.LastReport;
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "网络与通信", "MOD / MQTT / QHTTP", Theme.Accent);
            int body = rect.Y + S(32);
            DrawText(g, s.Operator.Length > 0 ? s.Operator : "—", _owner._bigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(16)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string tech = (s.AccessTech.Length > 0 ? s.AccessTech : "—")
                          + (report?.Band is { Length: > 0 } band ? " · " + band : "");
            DrawText(g, tech, _owner._tinyFont, new Rectangle(rect.X + pad, body + S(14), rect.Width - pad * 2, S(10)),
                Theme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(28);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            DrawKv(g, "模组 / 步骤", s.ModemText + (s.ModemStep.Length > 0 ? " · " + s.ModemStep : ""),
                rect.X + pad, y, half,
                s.ModemState is "OFF" or "CLOSING" ? Theme.Danger : s.ModemState == "POWERSAVE" ? Theme.Amber : Theme.Green);
            DrawKv(g, "MQTT / QHTTP", s.MqttSummary + " / " + s.QhttpSummary,
                rect.X + pad + half + S(8), y, half, Theme.Green);
            y += S(18);
            DrawKv(g, "信号 SIG", s.Sig.Length > 0 ? s.Sig + " 原始值" : "—", rect.X + pad, y, half, Theme.Text);
            string data = _owner._view.DataSent.Length > 0
                ? "↑" + _owner._view.DataSent + " ↓" + _owner._view.DataRecv
                : "—";
            DrawKvSmall(g, "数据计数", data, rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKvSmall(g, "LAC / CID · 报文时点", (report?.Lac.Length > 0 ? report.Lac : "—") + " / "
                + (report?.Cid.Length > 0 ? report.Cid : "—"), rect.X + pad, y, rect.Width - pad * 2, Theme.Text);
            string note = s.HasData
                ? "低功耗保持；SIG 不自动换算 dBm；LAC/CID 只在该条报文时点有效。"
                : "收到快照后填入 MOD / MQTT / QHTTP 原值。";
            DrawNote(g, note, new Rectangle(rect.X + pad, rect.Bottom - S(12), rect.Width - pad * 2, S(10)));
        }

        private void DrawLocationCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "定位与电源", "GPS / BAT", Blue);
            int body = rect.Y + S(32);
            DrawText(g, s.CoordinatesText, _owner._monoBigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(16)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, s.GpsText, _owner._tinyFont,
                new Rectangle(rect.X + pad, body + S(14), rect.Width - pad * 2, S(10)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(28);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            DrawKv(g, "定位 / HDOP", s.GpsText, rect.X + pad, y, half, Theme.Green);
            DrawKv(g, "速度", s.SpeedText, rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKv(g, "电池", s.BatteryText, rect.X + pad, y, half, Theme.Green);
            DrawKvSmall(g, "电流", s.CurrentText + (s.ChargeFlag == "0" ? " · 未充电" : ""),
                rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(18);
            DrawKvSmall(g, "容量 / 温度", s.CapacityText.Replace(" mAh", "mAh").Replace(" °C", "°C"),
                rect.X + pad, y, half, Theme.Text);
            DrawKv(g, "行驶 / 分段", s.DrivingLabel + " · " + (s.GpsMileage.Length > 0 ? s.GpsMileage : "—"),
                rect.X + pad + half + S(8), y, half, Theme.Text);
            DrawNote(g, s.HasData ? "坐标来自最新快照；年龄按主机时间，设备 RTC 仅作展示。" : "收到 GPS / BAT 快照后填入。",
                new Rectangle(rect.X + pad, rect.Bottom - S(12), rect.Width - pad * 2, S(10)));
        }

        private int DrawSensor(Graphics g, int x, int y, int width, int height)
        {
            DrawCard(g, new Rectangle(x, y, width, height), Theme.Raised, Theme.BorderSoft);
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceReport? report = _owner._view.LastReport;
            int pad = S(9);
            int cellW = width / 3;
            int cellH = height - S(16);
            MainTraceEvent? doorEvent = _owner._view.Events.FirstOrDefault(ev =>
                ev.Kind == MainTraceEventKind.Sensor && ev.Text.Contains("门磁事件"));
            string doorValue = s.HasData ? "门" + s.DoorLabel + " · RPD " + (s.Rpd.Length > 0 ? s.Rpd : "—") : "等待快照";
            string doorEvidence = doorEvent is null ? "最近事件 未观测"
                : "最近事件 " + doorEvent.Text.Replace("门磁事件 · ", "") + " · " + doorEvent.Time.ToString("HH:mm:ss");
            DrawSensorCell(g, new Rectangle(x, y, cellW, cellH), "门磁 / RPD", "快照", doorValue,
                doorEvidence, s.HasData ? Dash(s.DoorBound) + " 绑定 · 快照 " + s.HostTime.ToString("HH:mm:ss") : "等待快照");
            string tpms = s.TpmsBound.Length > 0
                ? s.TpmsBound + " 绑定" + (report?.TpmsCount is int t ? " · 报文 " + t + " 条" : "")
                : "—";
            DrawSensorCell(g, new Rectangle(x + cellW, y, cellW, cellH), "TPMS",
                Dash(s.McuBleMode), tpms,
                report?.TpmsCount is int count ? "报文 " + count + " 条 · 测量值语义待确认" : "测量未观测",
                "策略是缓存值，不代表正在扫描");
            string ths = report?.ThsText is { Length: > 0 } t2 ? t2 : (s.ThsBound.Length > 0 ? s.ThsBound + " 绑定 · 未观测" : "—");
            DrawSensorCell(g, new Rectangle(x + cellW * 2, y, cellW, cellH), "THS",
                Dash(s.SocBleMode), ths,
                report?.ThsText is { Length: > 0 } ? "来自 " + report.EncodedAt.ToString("HH:mm:ss") + " 上报 JSON · 非实时" : "无报文时显示未观测",
                "绑定数 " + Dash(s.ThsBound) + " · 策略 " + Dash(s.SocBleMode));
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x + pad, y + cellH, x + width - pad, y + cellH);
            DrawText(g, "扫描模式是策略值；无报文时显示未观测，不能据此推断传感器在线、离线或故障。",
                _owner._tinyFont, new Rectangle(x + pad, y + cellH, width - pad * 2, S(16)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            return y + height;
        }

        private void DrawSensorCell(Graphics g, Rectangle rect, string title, string policy,
            string value, string evidence, string note)
        {
            int pad = S(10);
            DrawText(g, title, _owner._tinyFont,
                new Rectangle(rect.X + pad, rect.Y + S(5), rect.Width - pad * 2 - S(40), S(10)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, policy, _owner._chipFont,
                new Rectangle(rect.Right - pad - S(40), rect.Y + S(5), S(40), S(10)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
            DrawText(g, value, _owner._bigFont,
                new Rectangle(rect.X + pad, rect.Y + S(16), rect.Width - pad * 2, S(14)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, evidence, _owner._tinyFont,
                new Rectangle(rect.X + pad, rect.Y + S(29), rect.Width - pad * 2, S(10)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.X + pad, rect.Y + S(39), rect.Width - pad * 2, S(10)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawBottom(Graphics g, int x, int y, int width, int headingH, int cardH)
        {
            int gap = S(10);
            int leftW = (width - gap) * 57 / 100;
            int rightW = width - gap - leftW;
            DrawSection(g, "报文记录", "10–30 条滚动历史 · 含上行与下发", x, y, leftW);
            DrawSection(g, "业务时间线", "关键过程与结果 · 不展开每个报文阶段", x + leftW + gap, y, rightW);
            int cardY = y + headingH + S(3);
            DrawMessageCard(g, new Rectangle(x, cardY, leftW, cardH));
            DrawTimelineCard(g, new Rectangle(x + leftW + gap, cardY, rightW, cardH));
        }

        private void DrawMessageCard(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(8);
            int toolbarH = S(32);
            int detailH = S(76);
            int listH = rect.Height - toolbarH - detailH;
            DrawTabs(g, rect, pad, toolbarH);
            var list = new Rectangle(rect.X + S(1), rect.Y + toolbarH, rect.Width - S(2), listH);
            _owner._messageListRect = list;
            MainTraceReport? report = _owner._selectedReport ?? (_owner._view.Reports.Count > 0 ? _owner._view.Reports[0] : null);
            MainTraceDownlink? downlink = _owner._selectedDownlink ?? (_owner._view.Downlinks.Count > 0 ? _owner._view.Downlinks[0] : null);

            int count = _owner._messageTab == MessageTab.Up ? _owner._view.Reports.Count : _owner._view.Downlinks.Count;
            int limit = Math.Min(count, _owner._messageLimit);
            int rowH = S(19);
            int visible = Math.Max(1, listH / rowH);
            _owner._messageVisibleRows = visible;
            int maxScroll = Math.Max(0, limit - visible);
            if (_owner._messageScroll > maxScroll) _owner._messageScroll = maxScroll;
            _owner._messageRects.Clear();
            if (count == 0)
            {
                string empty = _owner._messageTab == MessageTab.Up
                    ? "等待 encode report ok / mqtt publish 日志"
                    : "本会话未收到下发报文 · 等待 receive new cfg / cmd / upgrade";
                DrawText(g, empty, _owner._subFont,
                    new Rectangle(list.X + S(8), list.Y, list.Width - S(16), list.Height), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            else
            {
                for (int i = 0; i < visible; i++)
                {
                    int index = _owner._messageScroll + i;
                    if (index >= limit) break;
                    int ry = list.Y + i * rowH;
                    var row = new Rectangle(list.X + S(4), ry, list.Width - S(10), rowH);
                    if (_owner._messageTab == MessageTab.Up)
                    {
                        MainTraceReport item = _owner._view.Reports[index];
                        bool selected = ReferenceEquals(item, report);
                        DrawMessageRow(g, row, item, null, selected, i);
                        _owner._messageRects.Add((item, null, row));
                    }
                    else
                    {
                        MainTraceDownlink item = _owner._view.Downlinks[index];
                        bool selected = ReferenceEquals(item, downlink);
                        DrawMessageRow(g, row, null, item, selected, i);
                        _owner._messageRects.Add((null, item, row));
                    }
                }
                DrawScrollbar(g, list, _owner._messageScroll, limit, visible);
            }
            int detailY = rect.Bottom - detailH;
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, detailY, rect.Right - pad, detailY);
            var detailRect = new Rectangle(rect.X + pad, detailY, rect.Width - pad * 2, detailH);
            _owner._messageDetailRect = detailRect;
            DrawMessageDetail(g, detailRect,
                _owner._messageTab == MessageTab.Up ? report : null,
                _owner._messageTab == MessageTab.Down ? downlink : null);
        }

        private void DrawTabs(Graphics g, Rectangle rect, int pad, int toolbarH)
        {
            int y = rect.Y + (toolbarH - S(16)) / 2;
            int x = rect.X + pad;
            string up = "↑ 上行 " + _owner._view.Reports.Count;
            string down = "↓ 下发 " + _owner._view.Downlinks.Count;
            Rectangle tabUp = DrawTab(g, up, x, y, _owner._messageTab == MessageTab.Up);
            _owner._tabUpRect = tabUp;
            _owner._tabDownRect = DrawTab(g, down, tabUp.Right + S(3), y, _owner._messageTab == MessageTab.Down);
            int count = _owner._messageTab == MessageTab.Up ? _owner._view.Reports.Count : _owner._view.Downlinks.Count;
            string limit = "显示 " + _owner._messageLimit + " 条 ▾";
            int limitW = Measure(limit, _owner._chipFont).Width + S(10);
            Rectangle limitRect = new Rectangle(rect.Right - pad - limitW, y, limitW, S(16));
            _owner._limitRect = limitRect;
            using (var path = Theme.RoundedRect(limitRect, S(4)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 18 : 24, Theme.Text)))
                g.FillPath(brush, path);
            DrawText(g, limit, _owner._chipFont, limitRect, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string countText = Math.Min(count, _owner._messageLimit) + " / " + count + " 条";
            int countW = Measure(countText, _owner._tinyFont).Width;
            DrawText(g, countText, _owner._tinyFont,
                new Rectangle(limitRect.X - countW - S(8), y, countW, S(16)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + toolbarH, rect.Right - pad, rect.Y + toolbarH);
        }

        private Rectangle DrawTab(Graphics g, string text, int x, int y, bool active)
        {
            int w = Measure(text, _owner._chipFont).Width + S(10);
            var rect = new Rectangle(x, y, w, S(16));
            if (active)
            {
                using var path = Theme.RoundedRect(rect, S(4));
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 26 : 32, Theme.Accent));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 100, Theme.Accent), 1);
                g.DrawPath(pen, path);
            }
            DrawText(g, text, _owner._chipFont, rect, active ? Theme.Accent : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return rect;
        }

        private void DrawMessageRow(Graphics g, Rectangle row, MainTraceReport? report,
            MainTraceDownlink? downlink, bool selected, int index)
        {
            if (selected)
            {
                using var path = Theme.RoundedRect(row, S(3));
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 30 : 38, Theme.Accent));
                g.FillPath(brush, path);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, row.X, row.Y, S(2), row.Height);
            }
            else if (index > 0)
            {
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 18 : 30, Theme.BorderSoft), 1);
                g.DrawLine(pen, row.X, row.Y, row.Right, row.Y);
            }
            int x = row.X + S(5);
            string time = report?.EncodedAt.ToString("HH:mm:ss") ?? downlink?.Time.ToString("HH:mm:ss") ?? "";
            DrawText(g, time, _owner._eventTimeFont, new Rectangle(x, row.Y, S(55), row.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(58);
            string topic = report is not null ? "report" : downlink?.Topic ?? "";
            Color topicColor = report is not null ? Blue : downlink?.Topic switch
            {
                "cmd" => Purple,
                "upgrade" => Theme.Amber,
                _ => Theme.Green,
            };
            var topicRect = new Rectangle(x, row.Y + (row.Height - S(13)) / 2, S(52), S(13));
            using (var path = Theme.RoundedRect(topicRect, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 20 : 26, topicColor)))
                g.FillPath(brush, path);
            DrawText(g, topic, _owner._chipFont, topicRect, topicColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(57);
            string name = report is not null ? "位置报文" : downlink?.Summary ?? "";
            DrawText(g, name, _owner._eventFont,
                new Rectangle(x, row.Y, Math.Max(S(40), row.Right - x - S(156)), row.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int bytesX = row.Right - S(150);
            string bytes = report is not null ? report.Bytes + " B" : downlink?.Bytes + " B";
            DrawText(g, bytes, _owner._eventTimeFont, new Rectangle(bytesX, row.Y, S(42), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string state;
            Color stateColor;
            if (report is not null)
            {
                (state, stateColor) = report.Outcome switch
                {
                    MainTraceReportOutcome.Success => ("发布成功", Theme.Green),
                    MainTraceReportOutcome.ApiReturned => ("待确认", Theme.Amber),
                    MainTraceReportOutcome.Submitted => ("待确认", Theme.Amber),
                    MainTraceReportOutcome.Failed => ("失败", Theme.Danger),
                    MainTraceReportOutcome.Encoded => ("已编码", Blue),
                    _ => ("编码中", Theme.Dim),
                };
            }
            else
            {
                state = "已收到";
                stateColor = Blue;
            }
            var stateRect = new Rectangle(row.Right - S(103), row.Y + (row.Height - S(13)) / 2, S(54), S(13));
            using (var path = Theme.RoundedRect(stateRect, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 20 : 26, stateColor)))
                g.FillPath(brush, path);
            DrawText(g, state, _owner._chipFont, stateRect, stateColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "›", _owner._tinyFont, new Rectangle(row.Right - S(9), row.Y, S(8), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawMessageDetail(Graphics g, Rectangle rect, MainTraceReport? report, MainTraceDownlink? downlink)
        {
            string header;
            string result;
            string payload;
            string note;
            Color resultColor;
            if (report is not null)
            {
                header = report.EncodedAt.ToString("HH:mm:ss") + " · report · 位置报文";
                (result, resultColor) = report.Outcome switch
                {
                    MainTraceReportOutcome.Success => ("发布成功 · 固件观察", Theme.Green),
                    MainTraceReportOutcome.ApiReturned => ("结果未观测 · 不推断失败", Theme.Amber),
                    MainTraceReportOutcome.Submitted => ("已提交 · 结果未观测", Theme.Amber),
                    MainTraceReportOutcome.Failed => ("发布失败", Theme.Danger),
                    _ => ("编码完成", Blue),
                };
                payload = report.MaskedPayload.Length > 0 ? report.MaskedPayload : "（payload 未观测）";
                note = report.Outcome switch
                {
                    MainTraceReportOutcome.Success => "业务成功日志已观察；云端回执未观测，不推断服务端处理完成。",
                    MainTraceReportOutcome.ApiReturned => "API 返回只代表固件提交；业务成功日志与云端回执均未观测。",
                    MainTraceReportOutcome.Failed => "发布失败：" + report.Failure,
                    _ => "编码完成，等待发布结果。",
                };
            }
            else if (downlink is not null)
            {
                header = downlink.Time.ToString("HH:mm:ss") + " · " + downlink.Topic + " · " + downlink.Summary;
                result = "已收到 · 不代表执行成功";
                resultColor = Blue;
                payload = downlink.Payload.Length > 0 ? downlink.Payload : "（内容未观测）";
                note = "下行只表示已收到并解析；执行、拍摄或上传结果仍需看后续日志。";
            }
            else
            {
                header = "等待报文";
                result = "";
                resultColor = Theme.Dim;
                payload = "";
                note = "收到 encode report ok / mqtt publish 或 receive new 后显示。";
            }
            DrawText(g, header, _owner._tinyFont,
                new Rectangle(rect.X, rect.Y + S(2), rect.Width - S(120), S(11)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (result.Length > 0)
                DrawText(g, result, _owner._tinyFont,
                    new Rectangle(rect.Right - S(120), rect.Y + S(2), S(120), S(11)), resultColor,
                    TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (payload.Length > 0)
            {
                var payloadRect = new Rectangle(rect.X, rect.Y + S(13), rect.Width, S(33));
                GraphicsState state = g.Save();
                g.SetClip(payloadRect);
                DrawText(g, payload, _owner._detailFont, payloadRect, Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                g.Restore(state);
            }
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.X, rect.Bottom - S(12), rect.Width - S(52), S(10)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "查看原文 ›", _owner._chipFont,
                new Rectangle(rect.Right - S(50), rect.Bottom - S(13), S(50), S(11)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        private void DrawTimelineCard(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(8);
            int toolbarH = S(32);
            int detailH = S(58);
            int listH = rect.Height - toolbarH - detailH;
            DrawTimelineToolbar(g, rect, pad, toolbarH);
            var list = new Rectangle(rect.X + S(1), rect.Y + toolbarH, rect.Width - S(2), listH);
            _owner._timelineListRect = list;
            List<MainTraceTimelineItem> items = _owner.FilteredTimeline();
            int rowH = S(25);
            int visible = Math.Max(1, listH / rowH);
            _owner._timelineVisibleRows = visible;
            int maxScroll = Math.Max(0, items.Count - visible);
            if (_owner._timelineScroll > maxScroll) _owner._timelineScroll = maxScroll;
            _owner._timelineRects.Clear();
            if (items.Count == 0)
            {
                DrawText(g, "暂无事件 · 等待主日志", _owner._subFont,
                    new Rectangle(list.X + S(8), list.Y, list.Width - S(16), list.Height), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                MainTraceTimelineItem? selected = _owner._selectedTimeline ?? items[0];
                for (int i = 0; i < visible; i++)
                {
                    int index = _owner._timelineScroll + i;
                    if (index >= items.Count) break;
                    MainTraceTimelineItem item = items[index];
                    int ry = list.Y + i * rowH;
                    var row = new Rectangle(list.X + S(4), ry, list.Width - S(10), rowH);
                    bool isSelected = ReferenceEquals(item, selected);
                    if (isSelected)
                    {
                        using var path = Theme.RoundedRect(row, S(3));
                        using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 30 : 38, Theme.Accent));
                        g.FillPath(brush, path);
                    }
                    else if (i > 0)
                    {
                        using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 18 : 30, Theme.BorderSoft), 1);
                        g.DrawLine(pen, row.X, row.Y, row.Right, row.Y);
                    }
                    DrawTimelineRow(g, row, item);
                    _owner._timelineRects.Add((item, row));
                }
                DrawScrollbar(g, list, _owner._timelineScroll, items.Count, visible);
            }
            int detailY = rect.Bottom - detailH;
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, detailY, rect.Right - pad, detailY);
            var detailRect = new Rectangle(rect.X + pad, detailY, rect.Width - pad * 2, detailH);
            _owner._timelineDetailRect = detailRect;
            MainTraceTimelineItem? shown = _owner._selectedTimeline ?? (items.Count > 0 ? items[0] : null);
            DrawTimelineDetail(g, detailRect, shown);
        }

        private void DrawTimelineToolbar(Graphics g, Rectangle rect, int pad, int toolbarH)
        {
            int y = rect.Y + (toolbarH - S(16)) / 2;
            DrawText(g, "最近事件", _owner._cardTitleFont,
                new Rectangle(rect.X + pad, rect.Y, S(52), toolbarH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            _owner._filterRects.Clear();
            string[] labels = { "全部", "状态", "报文", "传感器", "异常", "拍照" };
            TimelineFilter[] filters =
            {
                TimelineFilter.All, TimelineFilter.State, TimelineFilter.Report,
                TimelineFilter.Sensor, TimelineFilter.Alert, TimelineFilter.Camera,
            };
            int right = rect.Right - pad;
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                int w = Measure(labels[i], _owner._chipFont).Width + S(7);
                var r = new Rectangle(right - w, y, w, S(16));
                right -= w + S(2);
                _owner._filterRects.Add((filters[i], r));
                bool active = _owner._timelineFilter == filters[i];
                if (active)
                {
                    using var path = Theme.RoundedRect(r, S(3));
                    using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 26 : 32, Theme.Accent));
                    g.FillPath(brush, path);
                    using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 100, Theme.Accent), 1);
                    g.DrawPath(pen, path);
                }
                DrawText(g, labels[i], _owner._chipFont, r, active ? Theme.Accent : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + toolbarH, rect.Right - pad, rect.Y + toolbarH);
        }

        private void DrawTimelineRow(Graphics g, Rectangle row, MainTraceTimelineItem item)
        {
            int x = row.X + S(4);
            DrawText(g, item.Time.ToString("HH:mm:ss.fff"), _owner._eventTimeFont,
                new Rectangle(x, row.Y, S(56), row.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(58);
            Color tagColor = KindColor(item.Kind);
            var tag = new Rectangle(x, row.Y + (row.Height - S(13)) / 2, S(36), S(13));
            using (var path = Theme.RoundedRect(tag, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 22 : 28, tagColor)))
                g.FillPath(brush, path);
            DrawText(g, item.Tag, _owner._chipFont, tag, tagColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(40);
            DrawText(g, item.Text, _owner._eventFont,
                new Rectangle(x, row.Y, Math.Max(S(50), row.Right - x - S(48)), row.Height), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, item.Source, _owner._tinyFont,
                new Rectangle(row.Right - S(46), row.Y, S(42), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "›", _owner._tinyFont,
                new Rectangle(row.Right - S(10), row.Y, S(9), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawTimelineDetail(Graphics g, Rectangle rect, MainTraceTimelineItem? item)
        {
            if (item is null)
            {
                DrawText(g, "选择事件查看原始证据", _owner._subFont, rect, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            string head = item.Tag + " · " + item.Text;
            DrawText(g, head, _owner._tinyFont,
                new Rectangle(rect.X, rect.Y + S(2), rect.Width - S(48), S(11)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "原文 ›", _owner._chipFont,
                new Rectangle(rect.Right - S(42), rect.Y + S(2), S(42), S(11)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
            var rawRect = new Rectangle(rect.X, rect.Y + S(14), rect.Width, S(24));
            GraphicsState state = g.Save();
            g.SetClip(rawRect);
            DrawText(g, item.Raw, _owner._detailFont, rawRect, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            g.Restore(state);
            DrawText(g, item.Source, _owner._tinyFont,
                new Rectangle(rect.X, rect.Bottom - S(10), rect.Width, S(9)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private void DrawScrollbar(Graphics g, Rectangle list, int scroll, int total, int visible)
        {
            if (total <= visible) return;
            int railX = list.Right - S(4);
            var rail = new Rectangle(railX, list.Y + S(3), S(3), list.Height - S(6));
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 35 : 55, Theme.BorderSoft)))
                g.FillRectangle(brush, rail);
            double fraction = visible / (double)total;
            int thumbH = Math.Max(S(20), (int)Math.Round(rail.Height * fraction));
            int max = Math.Max(1, total - visible);
            int thumbY = rail.Y + (int)Math.Round((rail.Height - thumbH) * (scroll / (double)max));
            using var thumb = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 80 : 120, Theme.Muted));
            g.FillRectangle(thumb, rail.X, thumbY, rail.Width, thumbH);
        }

        private void DrawCardHeader(Graphics g, Rectangle rect, int pad, string title, string note, Color color)
        {
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, rect.X + pad, rect.Y + S(12), S(5), S(5));
            DrawText(g, title, _owner._cardTitleFont,
                new Rectangle(rect.X + pad + S(9), rect.Y, rect.Width - pad * 2 - S(100), S(27)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int noteW = Measure(note, _owner._tinyFont).Width;
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.Right - pad - noteW, rect.Y, noteW, S(27)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(27), rect.Right - pad, rect.Y + S(27));
        }

        private void DrawKv(Graphics g, string label, string value, int x, int y, int width, Color color,
            Font? valueFont = null)
        {
            DrawText(g, label, _owner._kvLabelFont, new Rectangle(x, y, width, S(9)), Theme.Dim,
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
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private int DrawChip(Graphics g, string text, int x, int y, Color fore, Color back, Font font)
        {
            Size size = Measure(text, font);
            int height = S(14);
            var rect = new Rectangle(x, y, size.Width + S(9), height);
            using (var path = Theme.RoundedRect(rect, S(4)))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            DrawText(g, text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return rect.Right + S(4);
        }

        private void DrawCard(Graphics g, Rectangle rect, Color fill, Color border)
        {
            using var path = Theme.RoundedRect(rect, S(8));
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border, 1);
            g.DrawPath(pen, path);
        }

        private Color KindColor(MainTraceEventKind kind) => kind switch
        {
            MainTraceEventKind.State => Theme.Accent,
            MainTraceEventKind.Report => Theme.Accent,
            MainTraceEventKind.Sensor => Blue,
            MainTraceEventKind.Alert => Theme.Danger,
            MainTraceEventKind.Camera => Purple,
            MainTraceEventKind.Network => Theme.Green,
            MainTraceEventKind.Downlink => Blue,
            _ => Theme.Dim,
        };

        private Color Blue => Theme.IsLight ? Color.FromArgb(21, 101, 192) : Color.FromArgb(120, 172, 255);
        private Color Purple => Theme.IsLight ? Color.FromArgb(106, 63, 181) : Color.FromArgb(199, 146, 234);

        private bool HasMainStateChange() =>
            _owner._view.Events.Any(ev => ev.Kind == MainTraceEventKind.State
                                          && ev.Text.StartsWith("主状态 ", StringComparison.Ordinal));

        private static string Dash(string value) => value.Length > 0 ? value : "—";

        private static string Age(DateTime time)
        {
            double seconds = Math.Max(0, (DateTime.Now - time).TotalSeconds);
            if (seconds < 1) return "刚刚";
            if (seconds < 60) return Math.Round(seconds) + " s 前";
            return Math.Floor(seconds / 60) + " min 前";
        }

        private static void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color,
            TextFormatFlags flags) =>
            TextRenderer.DrawText(g, text, font, rect, color,
                flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        private static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
