using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>Trace监控：设备快照、上行/下发报文历史与聚合业务时间线。</summary>
internal sealed class MainTraceWindow : Form
{
    private enum MessageTab { Up, Down }
    private enum TimelineCategory { All, Report, State, Network, Sensor, Alert, Camera }

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
    private bool _diagnosticView;
    private TimelineCategory _timelineCategory = TimelineCategory.All;
    private bool _categoryMenuOpen;
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
    private Rectangle _incidentRect;
    private Rectangle _linkRect;
    private Rectangle _peripheralRect;
    private Rectangle _summaryRect;
    private Rectangle _keyRect;
    private Rectangle _diagRect;
    private Rectangle _categoryRect;
    private readonly List<(TimelineCategory Category, Rectangle Rect)> _categoryMenuRects = new();
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
        _titleFont = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        _subFont = new Font(Font.FontFamily, 8.5F);
        _heroFont = new Font(Font.FontFamily, 17F, FontStyle.Bold);
        _heroSubFont = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);
        _cardTitleFont = new Font(Font.FontFamily, 8.8F, FontStyle.Bold);
        _bigFont = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _monoBigFont = Theme.MonoFont(9F);
        _kvLabelFont = new Font(Font.FontFamily, 7F);
        _kvValueFont = Theme.MonoFont(8.8F);
        _tinyFont = new Font(Font.FontFamily, 6.8F);
        _eventFont = new Font(Font.FontFamily, 7.8F);
        _eventTimeFont = Theme.MonoFont(6.6F);
        _detailFont = Theme.MonoFont(7.2F);
        _chipFont = new Font(Font.FontFamily, 7F);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        ClientSize = new Size(Theme.Scale(this, 900), Theme.Scale(this, 800));
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
    internal MainTraceView ViewForTest => _view;
    internal void TogglePauseForTest() => TogglePause();
    internal void ShowDownForTest() => SetMessageTab(MessageTab.Down);
    internal void ShowUpForTest() => SetMessageTab(MessageTab.Up);
    internal void CycleLimitForTest() => CycleLimit();
    internal void ShowDiagnosticForTest() => SetTimelineView(true);
    internal void ShowKeyForTest() => SetTimelineView(false);
    internal void ToggleCategoryMenuForTest()
    {
        _categoryMenuOpen = !_categoryMenuOpen;
        Invalidate(true);
    }
    internal void SetTimelineCategoryForTest(string category)
    {
        if (Enum.TryParse(category, true, out TimelineCategory parsed))
            SetTimelineCategory(parsed);
    }
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
            Theme.Scale(this, 900) + extra.Width,
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

    private void SetTimelineView(bool diagnostic)
    {
        _diagnosticView = diagnostic;
        _timelineScroll = 0;
        _selectedTimeline = null;
        _categoryMenuOpen = false;
        Invalidate(true);
    }

    private void SetTimelineCategory(TimelineCategory category)
    {
        _timelineCategory = category;
        _timelineScroll = 0;
        _selectedTimeline = null;
        _categoryMenuOpen = false;
        Invalidate(true);
    }

    private void JumpToIncident()
    {
        MainTraceIncident? incident = _view.Incident;
        if (incident is null) return;
        _diagnosticView = true;
        _timelineCategory = TimelineCategory.All;
        _categoryMenuOpen = false;
        List<MainTraceTimelineItem> items = FilteredTimeline();
        string probe = incident.Summary.Length > 80 ? incident.Summary.Substring(0, 80) : incident.Summary;
        int index = items.FindIndex(item => item.Alert && item.Text.IndexOf(probe, StringComparison.Ordinal) >= 0);
        if (index < 0) index = items.FindIndex(item => item.Alert);
        if (index >= 0)
        {
            _selectedTimeline = items[index];
            _timelineScroll = Math.Max(0, Math.Min(index, Math.Max(0, items.Count - _timelineVisibleRows)));
            Invalidate(true);
        }
        else if (incident.Raw.Length > 0)
        {
            ShowTextViewer("异常原文", incident.Raw, 660);
        }
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
        if (_incidentRect.Contains(point))
        {
            JumpToIncident();
            return;
        }
        if (_linkRect.Contains(point))
        {
            ShowLinkDetails();
            return;
        }
        if (_peripheralRect.Contains(point))
        {
            ShowPeripheralDetails();
            return;
        }
        if (_summaryRect.Contains(point))
        {
            ShowSessionSummary();
            return;
        }
        if (_keyRect.Contains(point))
        {
            SetTimelineView(false);
            return;
        }
        if (_diagRect.Contains(point))
        {
            SetTimelineView(true);
            return;
        }
        if (_categoryRect.Contains(point))
        {
            _categoryMenuOpen = !_categoryMenuOpen;
            Invalidate(true);
            return;
        }
        if (_categoryMenuOpen)
        {
            foreach (var (category, rect) in _categoryMenuRects)
            {
                if (!rect.Contains(point)) continue;
                SetTimelineCategory(category);
                return;
            }
            _categoryMenuOpen = false;
            Invalidate(true);
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

    private void ShowSessionSummary() => MainTracePopups.ShowSummary(this, _view);
    private void ShowLinkDetails() => MainTracePopups.ShowLink(this, _view);
    private void ShowPeripheralDetails() => MainTracePopups.ShowPeripheral(this, _view);

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
        IEnumerable<MainTraceTimelineItem> items = _diagnosticView
            ? _view.Timeline
            : _view.Timeline.Where(item => !item.Diagnostic);
        if (_timelineCategory != TimelineCategory.All)
        {
            items = items.Where(item => CategoryOf(item.Kind) == _timelineCategory);
        }
        return items.ToList();
    }

    private static TimelineCategory CategoryOf(MainTraceEventKind kind) => kind switch
    {
        MainTraceEventKind.Report or MainTraceEventKind.Downlink => TimelineCategory.Report,
        MainTraceEventKind.State => TimelineCategory.State,
        MainTraceEventKind.Network => TimelineCategory.Network,
        MainTraceEventKind.Sensor => TimelineCategory.Sensor,
        MainTraceEventKind.Alert => TimelineCategory.Alert,
        MainTraceEventKind.Camera => TimelineCategory.Camera,
        _ => TimelineCategory.All,
    };

    private static string CategoryName(TimelineCategory category) => category switch
    {
        TimelineCategory.Report => "报文",
        TimelineCategory.State => "状态",
        TimelineCategory.Network => "网络",
        TimelineCategory.Sensor => "传感器",
        TimelineCategory.Alert => "异常",
        TimelineCategory.Camera => "拍照/录像",
        _ => "全部",
    };

    private static int CategoryCount(MainTraceTimelineCounts counts, TimelineCategory category) => category switch
    {
        TimelineCategory.Report => counts.Report,
        TimelineCategory.State => counts.State,
        TimelineCategory.Network => counts.Network,
        TimelineCategory.Sensor => counts.Sensor,
        TimelineCategory.Alert => counts.Alert,
        TimelineCategory.Camera => counts.Camera,
        _ => counts.All,
    };

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
            int topH = S(42);
            int pad = S(14);
            int gap = S(4);
            int width = Math.Max(S(500), Width - pad * 2);
            int y = topH + S(10);
            DrawTopBar(g, 0, 0, Width, topH, pad);
            y = DrawDeviceInfo(g, pad, y, width) + gap;
            int heroH = S(74);
            DrawHero(g, new Rectangle(pad, y, width, heroH));
            y += heroH + gap;
            DrawIncident(g, pad, y, width, S(22));
            y += S(22) + gap;
            y = DrawSnapshots(g, pad, y, width, S(196)) + gap;
            y = DrawPeripheral(g, pad, y, width, S(28)) + gap;
            int bottomCardH = S(326);
            DrawBottom(g, pad, y, width, bottomCardH);
            y += bottomCardH + gap;
            DrawFooter(g, pad, y, width);
            if (_owner._categoryMenuOpen)
                DrawCategoryMenu(g, width);
        }

        private void DrawFooter(Graphics g, int pad, int y, int width)
        {
            MainTraceView view = _owner._view;
            string left = _owner._sourceLabel();
            if (left.Length == 0) left = "COM · 921600 · Main Trace";
            int alertCount = view.TimelineCounts.Alert;
            string mid = "已观察 " + view.PublishSuccessLogCount + " 条发布成功日志 · "
                         + view.ReportUnlinkedCount + " 未关联 · " + alertCount + " 异常";
            string logText = "原始日志 ›";
            int logW = Measure(logText, _owner._detailFont).Width;
            string pillText = "会话摘要";
            string rateText = "成功率 " + view.ConfirmedSuccessRateText;
            int pillW = Measure(pillText, _owner._chipFont).Width
                        + Measure(rateText, _owner._tinyFont).Width + S(38);
            int pillH = S(15);
            int logX = Width - pad - logW;
            int pillX = logX - S(10) - pillW;
            var pillRect = new Rectangle(pillX, y + (S(16) - pillH) / 2, pillW, pillH);
            _owner._summaryRect = pillRect;
            using (var path = Theme.RoundedRect(pillRect, pillH / 2))
            {
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 20 : 28, Theme.Accent));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 80 : 100, Theme.Accent), 1);
                g.DrawPath(pen, path);
            }
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillEllipse(brush, pillRect.X + S(9), pillRect.Y + (pillH - S(5)) / 2, S(5), S(5));
            int px = pillRect.X + S(18);
            int pw = Measure(pillText, _owner._chipFont).Width;
            DrawText(g, pillText, _owner._chipFont, new Rectangle(px, pillRect.Y, pw, pillH), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            px += pw + S(6);
            int rw = Measure(rateText, _owner._tinyFont).Width;
            DrawText(g, rateText, _owner._tinyFont, new Rectangle(px, pillRect.Y, rw, pillH), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "›", _owner._chipFont, new Rectangle(pillRect.Right - S(12), pillRect.Y, S(8), pillH), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, logText, _owner._detailFont, new Rectangle(logX, y, logW, S(16)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

            int leftW = Math.Max(S(120), pillX - pad - S(16));
            DrawText(g, left, _owner._detailFont, new Rectangle(pad, y, leftW, S(16)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int midX = pad + Math.Min(leftW, Measure(left, _owner._detailFont).Width + S(10));
            int midW = Math.Max(S(100), pillX - S(10) - midX);
            DrawText(g, mid, _owner._detailFont, new Rectangle(midX, y, midW, S(16)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

        private int DrawDeviceInfo(Graphics g, int x, int y, int width)
        {
            int h = S(50);
            DrawCard(g, new Rectangle(x, y, width, h), Theme.Raised, Theme.BorderSoft);
            string[] labels = { "设备型号", "MCU 固件", "Bootloader", "SoC 固件", "MCU BLE", "SoC BLE", "本次面板会话" };
            MainTraceSnapshot s = _owner._view.Snapshot;
            string[] values =
            {
                s.ModelText, Dash(s.Firmware), Dash(s.Bootloader), Dash(s.SocVersion),
                Dash(s.McuBleVersion), Dash(s.SocBleVersion), _owner._view.SessionClockText,
            };
            int cellW = width / 7;
            for (int i = 0; i < 7; i++)
            {
                int cx = x + i * cellW;
                if (i > 0)
                    using (var pen = new Pen(Theme.BorderSoft, 1))
                        g.DrawLine(pen, cx, y + S(6), cx, y + h - S(6));
                DrawText(g, labels[i], _owner._tinyFont,
                    new Rectangle(cx + S(11), y + S(7), cellW - S(14), S(12)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                DrawText(g, values[i], _owner._monoBigFont,
                    new Rectangle(cx + S(11), y + S(21), cellW - S(14), S(18)),
                    i == 0 || i == 6 ? Theme.Accent : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            return y + h;
        }

        private void DrawHero(Graphics g, Rectangle rect)
        {
            MainTraceView view = _owner._view;
            MainTraceSnapshot s = view.Snapshot;
            using (var path = Theme.RoundedRect(rect, S(10)))
            {
                var start = Theme.Mix(Theme.Raised, Theme.Accent, 0.10f);
                var end = Theme.Mix(Theme.Raised, Theme.Surface, 0.35f);
                using var brush = new LinearGradientBrush(rect, start, end, 0f);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 70 : 90, Theme.Accent), 1);
                g.DrawPath(pen, path);
            }
            int pad = S(14);
            int leadW = S(260);
            int x = rect.X + pad;
            DrawText(g, "运行总状态", _owner._tinyFont,
                new Rectangle(x, rect.Y + S(5), leadW, S(12)), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, s.MainStateText, _owner._heroFont,
                new Rectangle(x - S(1), rect.Y + S(15), leadW, S(27)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int stateW = Measure(s.MainStateText, _owner._heroFont).Width;
            int chipX = x + Math.Min(leadW - S(48), stateW + S(8));
            DrawChip(g, s.SubStateText, chipX, rect.Y + S(24), Theme.Accent,
                Theme.WithAlpha(Theme.IsLight ? 24 : 32, Theme.Accent), _owner._chipFont);
            string deviceTime = s.DeviceTime.Length > 0 ? s.DeviceTime : "未观测";
            DrawText(g, "设备时间 " + deviceTime, _owner._eventTimeFont,
                new Rectangle(x, rect.Y + S(46), leadW, S(14)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);

            int ringW = S(130);
            int metricX = rect.X + pad + leadW + S(2);
            int metricW = Math.Max(S(200), rect.Right - pad - ringW - metricX);
            int colW = metricW / 4;
            MainTraceReport? report = view.LastReport;
            DrawMetric(g, "快照周期", view.SnapshotPeriodText,
                s.HasData ? view.SnapshotCount + " 条" : "", metricX, rect.Y, colW, rect.Height);
            MainTracePowerInfo power = view.Power;
            bool hasWake = power.LastWakeAt is not null;
            DrawMetric(g, "最近唤醒", hasWake ? power.LastWakeAt!.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "未观测",
                hasWake ? "唤醒源 " + (power.LastWakeSource.Length > 0 ? power.LastWakeSource : "未观测") : "等待省电日志",
                metricX + colW, rect.Y, colW, rect.Height,
                hasWake ? Theme.Green : Theme.Dim);
            DrawMetric(g, "最近报文", report is null ? "—" : report.Bytes + " B",
                report is null ? "" : Age(report.EncodedAt), metricX + colW * 2, rect.Y, colW, rect.Height);
            string planSub = "等待省电计划";
            bool hasPlan = power.NextWakeDeviceTime.Length > 0 && power.NextWakeObservedAt is not null;
            if (hasPlan)
            {
                int wait = Math.Max(0, (power.NextWakeWaitSeconds ?? 0)
                    - (int)Math.Round((DateTime.Now - power.NextWakeObservedAt!.Value).TotalSeconds));
                planSub = "剩余 " + FormatClock(wait) + " · 估算";
            }
            DrawMetric(g, "计划下次唤醒", hasPlan ? power.NextWakeDeviceTime : "未观测",
                planSub, metricX + colW * 3, rect.Y, colW, rect.Height,
                hasPlan ? Theme.Amber : Theme.Dim);

            int ringX = rect.Right - pad - ringW + S(4);
            DrawRing(g, new Rectangle(ringX, rect.Y + S(12), S(50), S(50)),
                view.CompleteRatio, s.HasData);
            int textX = ringX + S(58);
            DrawText(g, s.HasData ? view.CompleteSnapshotsText : "等待", _owner._eventTimeFont,
                new Rectangle(textX, rect.Y + S(11), ringW - S(60), S(13)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "快照重组完整", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(26), ringW - S(60), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, "软换行已还原", _owner._tinyFont,
                new Rectangle(textX, rect.Y + S(40), ringW - S(60), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private static string FormatClock(int seconds)
        {
            seconds = Math.Max(0, seconds);
            int minutes = seconds / 60;
            int rest = seconds % 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                   + rest.ToString("00", CultureInfo.InvariantCulture);
        }

        private void DrawMetric(Graphics g, string label, string value, string sub, int x, int y, int width, int height,
            Color? valueColor = null, Color? subColor = null)
        {
            using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 30 : 42, Theme.Accent), 1))
                g.DrawLine(pen, x, y + S(10), x, y + height - S(8));
            int tx = x + S(9);
            DrawText(g, label, _owner._tinyFont, new Rectangle(tx, y + S(9), width - S(12), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, _owner._kvValueFont, new Rectangle(tx, y + S(23), width - S(12), S(16)),
                valueColor ?? Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (sub.Length > 0)
                DrawText(g, sub, _owner._tinyFont, new Rectangle(tx, y + S(42), width - S(12), S(11)),
                    subColor ?? Theme.Dim,
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

        private void DrawIncident(Graphics g, int x, int y, int width, int height)
        {
            MainTraceIncident? incident = _owner._view.Incident;
            var rect = new Rectangle(x, y, width, height);
            Color border = incident is null
                ? Theme.BorderSoft
                : Theme.WithAlpha(Theme.IsLight ? 110 : 120, Theme.Danger);
            Color fill = incident is null
                ? Theme.WithAlpha(Theme.IsLight ? 12 : 16, Theme.Accent)
                : Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Danger);
            using (var path = Theme.RoundedRect(rect, S(6)))
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                using var pen = new Pen(border, 1);
                g.DrawPath(pen, path);
            }
            int pad = S(10);
            int cy = rect.Y + height / 2;
            int left = rect.X + pad;
            using (var brush = new SolidBrush(incident is null ? Theme.Dim : Theme.Danger))
                g.FillEllipse(brush, left, cy - S(3), S(6), S(6));
            left += S(11);
            string label = "最近异常";
            int labelW = Measure(label, _owner._chipFont).Width;
            DrawText(g, label, _owner._chipFont, new Rectangle(left, rect.Y, labelW, height),
                incident is null ? Theme.Muted : Theme.Danger,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            left += labelW + S(8);
            if (incident is null)
            {
                DrawText(g, "本会话未观测到异常", _owner._eventFont,
                    new Rectangle(left, rect.Y, width - (left - rect.X) - S(120), height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, "不代表设备正常", _owner._tinyFont,
                    new Rectangle(rect.Right - pad - S(90), rect.Y, S(90), height), Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                _owner._incidentRect = Rectangle.Empty;
                return;
            }
            string time = incident.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            int timeW = Measure(time, _owner._eventTimeFont).Width;
            DrawText(g, time, _owner._eventTimeFont, new Rectangle(left, rect.Y, timeW, height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            left += timeW + S(7);
            string module = incident.Module.Length > 0 ? incident.Module : "模块未观测";
            int moduleW = Math.Min(S(86), Measure(module, _owner._tinyFont).Width + S(8));
            var moduleRect = new Rectangle(left, cy - S(7), moduleW, S(14));
            using (var path = Theme.RoundedRect(moduleRect, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Danger)))
                g.FillPath(brush, path);
            DrawText(g, module, _owner._tinyFont, moduleRect, Theme.Danger,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            left = moduleRect.Right + S(8);

            const string linkText = "时间线 ›";
            int linkW = Measure(linkText, _owner._chipFont).Width;
            int linkX = rect.Right - pad - linkW;
            DrawText(g, linkText, _owner._chipFont, new Rectangle(linkX, rect.Y, linkW, height), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string location;
            if (incident.ReferenceFix is MainTraceFixInfo fix)
                location = "参考定位 " + fix.CoordinatesText + " · " + Age(fix.ObservedAt);
            else
                location = "位置未观测";
            int locationW = Math.Min(S(250), Measure(location, _owner._eventTimeFont).Width);
            int locationX = linkX - S(10) - locationW;
            DrawText(g, location, _owner._eventTimeFont,
                new Rectangle(locationX, rect.Y, locationW, height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int summaryW = Math.Max(S(60), locationX - S(8) - left);
            DrawText(g, incident.Summary, _owner._eventFont,
                new Rectangle(left, rect.Y, summaryW, height), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            _owner._incidentRect = rect;
        }

        private void DrawNetworkHeader(Graphics g, Rectangle rect, int pad)
        {
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillEllipse(brush, rect.X + pad, rect.Y + S(13), S(6), S(6));
            DrawText(g, "网络与通信", _owner._cardTitleFont,
                new Rectangle(rect.X + pad + S(11), rect.Y, rect.Width / 2, S(32)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            const string linkText = "链路详情 ›";
            int linkW = Measure(linkText, _owner._chipFont).Width;
            int linkX = rect.Right - pad - linkW;
            _owner._linkRect = new Rectangle(linkX - S(4), rect.Y + S(6), linkW + S(8), S(20));
            DrawText(g, linkText, _owner._chipFont, new Rectangle(linkX, rect.Y, linkW, S(32)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            const string note = "MOD / MQTT / QHTTP";
            int noteW = Measure(note, _owner._tinyFont).Width;
            int noteX = linkX - S(8) - noteW;
            DrawText(g, note, _owner._tinyFont, new Rectangle(noteX, rect.Y, noteW, S(32)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(32), rect.Right - pad, rect.Y + S(32));
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
            DrawNetworkHeader(g, rect, pad);
            int body = rect.Y + S(42);
            DrawText(g, s.Operator.Length > 0 ? s.Operator : "—", _owner._bigFont,
                new Rectangle(rect.X + pad, body, rect.Width - pad * 2, S(20)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string tech = (s.AccessTech.Length > 0 ? s.AccessTech : "—")
                          + (report?.Band is { Length: > 0 } band ? " · " + band : "");
            DrawText(g, tech, _owner._tinyFont, new Rectangle(rect.X + pad, body + S(18), rect.Width - pad * 2, S(12)),
                Theme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(38);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            DrawKv(g, "模组 / 步骤", s.ModemText + (s.ModemStep.Length > 0 ? " · " + s.ModemStep : ""),
                rect.X + pad, y, half,
                s.ModemState is "OFF" or "CLOSING" ? Theme.Danger : s.ModemState == "POWERSAVE" ? Theme.Amber : Theme.Green);
            DrawKv(g, "MQTT / QHTTP", s.MqttSummary + " / " + s.QhttpSummary,
                rect.X + pad + half + S(8), y, half, Theme.Green);
            y += S(30);
            DrawKv(g, "信号 SIG", s.Sig.Length > 0 ? s.Sig + " 原始值" : "—", rect.X + pad, y, half, Theme.Text);
            string data = _owner._view.DataSent.Length > 0
                ? "↑" + _owner._view.DataSent + " ↓" + _owner._view.DataRecv
                : "—";
            DrawKvSmall(g, "数据计数", data, rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(30);
            DrawKvSmall(g, "LAC / CID · 报文时点", (report?.Lac.Length > 0 ? report.Lac : "—") + " / "
                + (report?.Cid.Length > 0 ? report.Cid : "—"), rect.X + pad, y, rect.Width - pad * 2, Theme.Text);
            string note = s.HasData
                ? "低功耗保持；SIG 不自动换算 dBm；LAC/CID 只在该条报文时点有效。"
                : "收到快照后填入 MOD / MQTT / QHTTP 原值。";
            DrawNote(g, note, new Rectangle(rect.X + pad, rect.Bottom - S(14), rect.Width - pad * 2, S(12)));
        }

        private void DrawLocationCard(Graphics g, Rectangle rect)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceFixInfo? fix = _owner._view.Fix;
            bool live = IsLiveFix(s);
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(9);
            DrawCardHeader(g, rect, pad, "定位与电源", live ? "GPS / BAT · 最新" : "GPS / BAT · 上次定位", Blue);
            int body = rect.Y + S(42);
            string coordinates = fix?.CoordinatesText ?? "未观测";
            string fixTag = fix is null ? "" : (live ? "最新" : "上次") + " · " + AgeTag(fix.ObservedAt);
            int tagW = fixTag.Length > 0 ? Measure(fixTag, _owner._tinyFont).Width + S(10) : 0;
            int coordW = Math.Max(S(80), rect.Width - pad * 2 - tagW - S(6));
            DrawText(g, coordinates, _owner._monoBigFont,
                new Rectangle(rect.X + pad, body, coordW, S(20)),
                fix is null ? Theme.Dim : live ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (fixTag.Length > 0)
            {
                var tagRect = new Rectangle(rect.Right - pad - tagW, body + S(2), tagW, S(15));
                Color tagColor = live ? Theme.Green : Theme.Amber;
                using (var path = Theme.RoundedRect(tagRect, S(3)))
                using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 18 : 24, tagColor)))
                    g.FillPath(brush, path);
                DrawText(g, fixTag, _owner._tinyFont, tagRect, tagColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            string sub;
            if (fix is null)
            {
                sub = s.GpsText;
            }
            else
            {
                string observed = (live ? "最新观察 " : "上次有效观察 ")
                                  + fix.ObservedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                                  + " · 样本新鲜度未确认";
                sub = s.GpsText + (s.GpsText == "—" ? "" : " · ") + observed;
            }
            DrawText(g, sub, _owner._tinyFont,
                new Rectangle(rect.X + pad, body + S(18), rect.Width - pad * 2, S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int y = body + S(38);
            int half = (rect.Width - pad * 2 - S(8)) / 2;
            DrawKv(g, "定位 / HDOP", s.GpsText, rect.X + pad, y, half, live ? Theme.Green : Theme.Dim);
            DrawKv(g, "速度", s.SpeedText, rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(30);
            DrawKv(g, "电池", s.BatteryText, rect.X + pad, y, half, Theme.Green);
            DrawKvSmall(g, "电流", s.CurrentText + (s.ChargeFlag == "0" ? " · 未充电" : ""),
                rect.X + pad + half + S(8), y, half, Theme.Text);
            y += S(30);
            DrawKvSmall(g, "容量 / 温度", s.CapacityText.Replace(" mAh", "mAh").Replace(" °C", "°C"),
                rect.X + pad, y, half, Theme.Text);
            DrawKv(g, "行驶 / 分段", s.DrivingLabel + " · " + (s.GpsMileage.Length > 0 ? s.GpsMileage : "—"),
                rect.X + pad + half + S(8), y, half, Theme.Text);
            DrawNote(g, s.HasData
                    ? "坐标保留最近有效观察；年龄按接收时间，样本新鲜度未确认。"
                    : "收到 GPS / BAT 快照后填入。",
                new Rectangle(rect.X + pad, rect.Bottom - S(14), rect.Width - pad * 2, S(12)));
        }

        private static bool IsLiveFix(MainTraceSnapshot s)
        {
            if (s.GpsPower != "ON") return false;
            if (s.GpsFixState is not ("1" or "2")) return false;
            if (s.Latitude is not double lat || s.Longitude is not double lon) return false;
            return lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
        }

        private static string AgeTag(DateTime time)
        {
            double seconds = Math.Max(0, (DateTime.Now - time).TotalSeconds);
            if (seconds < 1) return "刚刚";
            if (seconds < 60) return Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture) + " s";
            return Math.Floor(seconds / 60).ToString("0", CultureInfo.InvariantCulture) + " min";
        }

        private int DrawPeripheral(Graphics g, int x, int y, int width, int height)
        {
            MainTraceSnapshot s = _owner._view.Snapshot;
            MainTraceReport? report = _owner._view.LastReport;
            var rect = new Rectangle(x, y, width, height);
            using (var path = Theme.RoundedRect(rect, S(7)))
            {
                using var brush = new LinearGradientBrush(rect, Theme.Raised,
                    Theme.Mix(Theme.Raised, Theme.Surface, Theme.IsLight ? 0.6f : 0.75f), 0f);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.BorderSoft, 1);
                g.DrawPath(pen, path);
            }
            int pad = S(11);
            int cx = rect.X + pad;
            DrawText(g, "外设状态", _owner._chipFont,
                new Rectangle(cx, rect.Y, S(52), height), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            cx += S(54);
            DrawPeripheralDivider(g, rect, ref cx);
            string door = s.HasData ? s.DoorLabel + " · RPD " + Dash(s.Rpd) : "未观测";
            cx = DrawPeripheralItem(g, rect, cx, "门磁", door);
            DrawPeripheralDivider(g, rect, ref cx);
            string tpms = report?.TpmsCount is int count
                ? count + " 条"
                : s.TpmsBound.Length > 0 ? s.TpmsBound + " 绑定" : "未观测";
            cx = DrawPeripheralItem(g, rect, cx, "TPMS", tpms);
            DrawPeripheralDivider(g, rect, ref cx);
            string ths = report?.ThsText is { Length: > 0 } text
                ? text
                : s.ThsBound.Length > 0 ? s.ThsBound + " 绑定" : "未观测";
            cx = DrawPeripheralItem(g, rect, cx, "THS", ths);
            const string linkText = "明细 ›";
            int linkW = Measure(linkText, _owner._chipFont).Width;
            int linkX = rect.Right - pad - linkW;
            _owner._peripheralRect = new Rectangle(linkX - S(5), rect.Y + S(4), linkW + S(9), height - S(8));
            DrawText(g, linkText, _owner._chipFont, new Rectangle(linkX, rect.Y, linkW, height), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string note = "来源：快照 / 报文";
            int noteW = Measure(note, _owner._tinyFont).Width;
            int noteX = linkX - S(8) - noteW;
            if (noteX > cx + S(12))
                DrawText(g, note, _owner._tinyFont, new Rectangle(noteX, rect.Y, noteW, height), Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return y + height;
        }

        private void DrawPeripheralDivider(Graphics g, Rectangle rect, ref int x)
        {
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x, rect.Y + S(8), x, rect.Bottom - S(8));
            x += S(9);
        }

        private int DrawPeripheralItem(Graphics g, Rectangle rect, int x, string label, string value)
        {
            string labelText = label + " ";
            int labelW = Measure(labelText, _owner._chipFont).Width;
            DrawText(g, labelText, _owner._chipFont, new Rectangle(x, rect.Y, labelW, rect.Height), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int valueW = Measure(value, _owner._chipFont).Width;
            DrawText(g, value, _owner._chipFont, new Rectangle(x + labelW, rect.Y, valueW, rect.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return x + labelW + valueW;
        }

        private void DrawBottom(Graphics g, int x, int y, int width, int cardH)
        {
            int gap = S(10);
            int leftW = (width - gap) * 57 / 100;
            int rightW = width - gap - leftW;
            DrawMessageCard(g, new Rectangle(x, y, leftW, cardH));
            DrawTimelineCard(g, new Rectangle(x + leftW + gap, y, rightW, cardH));
        }

        private void DrawMessageCard(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(11);
            int toolbarH = S(40);
            int detailH = S(90);
            int listH = rect.Height - toolbarH - detailH;
            DrawMessageToolbar(g, rect, pad, toolbarH);
            var list = new Rectangle(rect.X + S(1), rect.Y + toolbarH, rect.Width - S(2), listH);
            _owner._messageListRect = list;
            MainTraceReport? report = _owner._selectedReport ?? (_owner._view.Reports.Count > 0 ? _owner._view.Reports[0] : null);
            MainTraceDownlink? downlink = _owner._selectedDownlink ?? (_owner._view.Downlinks.Count > 0 ? _owner._view.Downlinks[0] : null);

            int count = _owner._messageTab == MessageTab.Up ? _owner._view.Reports.Count : _owner._view.Downlinks.Count;
            int limit = Math.Min(count, _owner._messageLimit);
            int rowH = S(28);
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
                DrawText(g, empty, _owner._eventFont,
                    new Rectangle(list.X + S(11), list.Y, list.Width - S(22), list.Height), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            else
            {
                for (int i = 0; i < visible; i++)
                {
                    int index = _owner._messageScroll + i;
                    if (index >= limit) break;
                    int ry = list.Y + i * rowH;
                    var row = new Rectangle(list.X + S(4), ry, list.Width - S(11), rowH);
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
            DrawDetailBackground(g, rect, detailY);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, detailY, rect.Right - pad, detailY);
            var detailRect = new Rectangle(rect.X + pad, detailY, rect.Width - pad * 2, detailH);
            _owner._messageDetailRect = detailRect;
            DrawMessageDetail(g, detailRect,
                _owner._messageTab == MessageTab.Up ? report : null,
                _owner._messageTab == MessageTab.Down ? downlink : null);
        }

        private void DrawMessageToolbar(Graphics g, Rectangle rect, int pad, int toolbarH)
        {
            int chipH = S(18);
            int y = rect.Y + (toolbarH - chipH) / 2;
            int x = rect.X + pad;
            DrawText(g, "报文记录", _owner._cardTitleFont,
                new Rectangle(x, rect.Y, S(70), toolbarH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += Measure("报文记录", _owner._cardTitleFont).Width + S(8);
            string up = "↑ 上行 " + _owner._view.Reports.Count;
            string down = "↓ 下发 " + _owner._view.Downlinks.Count;
            Rectangle tabUp = DrawTab(g, up, x, y, chipH, _owner._messageTab == MessageTab.Up);
            _owner._tabUpRect = tabUp;
            _owner._tabDownRect = DrawTab(g, down, tabUp.Right + S(3), y, chipH, _owner._messageTab == MessageTab.Down);
            int count = _owner._messageTab == MessageTab.Up ? _owner._view.Reports.Count : _owner._view.Downlinks.Count;
            string limitText = "显示 " + _owner._messageLimit + " 条 ▾";
            int limitW = Measure(limitText, _owner._chipFont).Width + S(14);
            Rectangle limitRect = new Rectangle(rect.Right - pad - limitW, y, limitW, chipH);
            _owner._limitRect = limitRect;
            using (var path = Theme.RoundedRect(limitRect, S(4)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 18 : 24, Theme.Text)))
                g.FillPath(brush, path);
            DrawText(g, limitText, _owner._chipFont, limitRect, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string countText = Math.Min(count, _owner._messageLimit) + " / " + count + " 条";
            int countW = Measure(countText, _owner._tinyFont).Width;
            DrawText(g, countText, _owner._tinyFont,
                new Rectangle(limitRect.X - countW - S(8), rect.Y, countW, toolbarH), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + toolbarH, rect.Right - pad, rect.Y + toolbarH);
        }

        private Rectangle DrawTab(Graphics g, string text, int x, int y, int height, bool active)
        {
            int w = Measure(text, _owner._chipFont).Width + S(14);
            var rect = new Rectangle(x, y, w, height);
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

        private void DrawDetailBackground(Graphics g, Rectangle card, int top)
        {
            GraphicsState state = g.Save();
            using (var clip = Theme.RoundedRect(card, S(8)))
                g.SetClip(clip, CombineMode.Intersect);
            using (var brush = new SolidBrush(Theme.Log))
                g.FillRectangle(brush, card.X, top, card.Width, card.Bottom - top);
            g.Restore(state);
            using var border = new Pen(Theme.BorderSoft, 1);
            using var path = Theme.RoundedRect(card, S(8));
            g.DrawPath(border, path);
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
            int chipH = S(16);
            int chipY = row.Y + (row.Height - chipH) / 2;
            int x = row.X + S(9);
            string time = report?.EncodedAt.ToString("HH:mm:ss") ?? downlink?.Time.ToString("HH:mm:ss") ?? "";
            DrawText(g, time, _owner._eventTimeFont, new Rectangle(x, row.Y, S(56), row.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(61);
            string topic = report is not null ? "report" : downlink?.Topic ?? "";
            Color topicColor = report is not null ? Blue : downlink?.Topic switch
            {
                "cmd" => Purple,
                "upgrade" => Theme.Amber,
                _ => Theme.Green,
            };
            var topicRect = new Rectangle(x, chipY, S(52), chipH);
            using (var path = Theme.RoundedRect(topicRect, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 20 : 26, topicColor)))
                g.FillPath(brush, path);
            DrawText(g, topic, _owner._chipFont, topicRect, topicColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(59);
            var stateRect = new Rectangle(row.Right - S(20) - S(60), chipY, S(60), chipH);
            var bytesRect = new Rectangle(stateRect.X - S(48), row.Y, S(44), row.Height);
            int nameW = Math.Max(S(40), bytesRect.X - S(6) - x);
            string name = report is not null ? "位置报文" : downlink?.Summary ?? "";
            DrawText(g, name, _owner._eventFont,
                new Rectangle(x, row.Y, nameW, row.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            string bytes = report is not null ? report.Bytes + " B" : downlink?.Bytes + " B";
            DrawText(g, bytes, _owner._eventTimeFont, bytesRect, Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string state;
            Color stateColor;
            if (report is not null)
            {
                (state, stateColor) = report.Outcome switch
                {
                    MainTraceReportOutcome.Success or MainTraceReportOutcome.ApiReturned
                        or MainTraceReportOutcome.Failed => ("未关联", Slate),
                    MainTraceReportOutcome.Submitted => ("已提交", Blue),
                    _ => ("已编码", Theme.Dim),
                };
            }
            else
            {
                state = "已收到";
                stateColor = Blue;
            }
            using (var path = Theme.RoundedRect(stateRect, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 20 : 26, stateColor)))
                g.FillPath(brush, path);
            DrawText(g, state, _owner._chipFont, stateRect, stateColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "›", _owner._chipFont, new Rectangle(row.Right - S(12), row.Y, S(9), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawMessageDetail(Graphics g, Rectangle rect, MainTraceReport? report, MainTraceDownlink? downlink)
        {
            MainTraceView view = _owner._view;
            string header;
            string result;
            string payload;
            string note;
            string evidence = "";
            Color resultColor;
            if (report is not null)
            {
                header = report.EncodedAt.ToString("HH:mm:ss") + " · report · 位置报文";
                (result, resultColor) = report.Outcome switch
                {
                    MainTraceReportOutcome.Submitted => ("已提交 · 结果未关联", Blue),
                    MainTraceReportOutcome.Encoded or MainTraceReportOutcome.Pending => ("已编码 · 结果未关联", Theme.Dim),
                    MainTraceReportOutcome.Failed => ("失败日志 · 未关联", Slate),
                    _ => ("结果未关联", Slate),
                };
                payload = report.MaskedPayload.Length > 0 ? report.MaskedPayload : "（payload 未观测）";
                evidence = "本会话已观察 MQTT 发布成功 " + view.PublishSuccessLogCount
                           + " · 固件收尾 " + view.TailLogCount
                           + " · 未关联 " + view.ReportUnlinkedCount;
                note = report.Outcome switch
                {
                    MainTraceReportOutcome.Failed => "已观察失败日志，但未能与本报文唯一关联；未关联不等于失败。",
                    MainTraceReportOutcome.Submitted => "已提交；未观察到可唯一关联的发布结果。",
                    MainTraceReportOutcome.Encoded or MainTraceReportOutcome.Pending => "已编码；结果日志尚未观察或无法唯一关联。",
                    _ => "结果日志未能与本报文唯一关联；成功率见会话摘要。",
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
            DrawText(g, header, _owner._eventFont,
                new Rectangle(rect.X, rect.Y + S(5), rect.Width - S(150), S(14)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (result.Length > 0)
                DrawText(g, result, _owner._chipFont,
                    new Rectangle(rect.Right - S(150), rect.Y + S(5), S(150), S(14)), resultColor,
                    TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (payload.Length > 0)
            {
                var payloadRect = new Rectangle(rect.X, rect.Y + S(20), rect.Width, S(36));
                GraphicsState state = g.Save();
                g.SetClip(payloadRect);
                DrawText(g, payload, _owner._detailFont, payloadRect, Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                g.Restore(state);
            }
            if (evidence.Length > 0)
                DrawText(g, evidence, _owner._tinyFont,
                    new Rectangle(rect.X, rect.Y + S(57), rect.Width - S(60), S(11)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.X, rect.Bottom - S(15), rect.Width - S(62), S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "查看原文 ›", _owner._chipFont,
                new Rectangle(rect.Right - S(58), rect.Bottom - S(16), S(58), S(13)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        private void DrawTimelineCard(Graphics g, Rectangle rect)
        {
            DrawCard(g, rect, Theme.Raised, Theme.BorderSoft);
            int pad = S(11);
            int toolbarH = S(40);
            int detailH = S(90);
            int listH = rect.Height - toolbarH - detailH;
            DrawTimelineToolbar(g, rect, pad, toolbarH);
            var list = new Rectangle(rect.X + S(1), rect.Y + toolbarH, rect.Width - S(2), listH);
            _owner._timelineListRect = list;
            List<MainTraceTimelineItem> items = _owner.FilteredTimeline();
            int rowH = S(28);
            int visible = Math.Max(1, listH / rowH);
            _owner._timelineVisibleRows = visible;
            int maxScroll = Math.Max(0, items.Count - visible);
            if (_owner._timelineScroll > maxScroll) _owner._timelineScroll = maxScroll;
            _owner._timelineRects.Clear();
            if (items.Count == 0)
            {
                DrawText(g, "暂无事件 · 等待主日志", _owner._eventFont,
                    new Rectangle(list.X + S(11), list.Y, list.Width - S(22), list.Height), Theme.Dim,
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
                    var row = new Rectangle(list.X + S(4), ry, list.Width - S(11), rowH);
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
            DrawDetailBackground(g, rect, detailY);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, detailY, rect.Right - pad, detailY);
            var detailRect = new Rectangle(rect.X + pad, detailY, rect.Width - pad * 2, detailH);
            _owner._timelineDetailRect = detailRect;
            MainTraceTimelineItem? shown = _owner._selectedTimeline ?? (items.Count > 0 ? items[0] : null);
            DrawTimelineDetail(g, detailRect, shown);
        }

        private void DrawTimelineToolbar(Graphics g, Rectangle rect, int pad, int toolbarH)
        {
            int chipH = S(18);
            int y = rect.Y + (toolbarH - chipH) / 2;
            DrawText(g, "业务时间线", _owner._cardTitleFont,
                new Rectangle(rect.X + pad, rect.Y, S(72), toolbarH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int x = rect.X + pad + S(72) + S(4);
            var seg = new Rectangle(x, y, S(88), chipH);
            using (var path = Theme.RoundedRect(seg, S(4)))
            {
                using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 12 : 18, Theme.Text)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Theme.BorderSoft, 1))
                    g.DrawPath(pen, path);
            }
            int half = seg.Width / 2;
            var keyRect = new Rectangle(seg.X, seg.Y, half, seg.Height);
            var diagRect = new Rectangle(seg.X + half, seg.Y, half, seg.Height);
            _owner._keyRect = keyRect;
            _owner._diagRect = diagRect;
            bool keyActive = !_owner._diagnosticView;
            if (keyActive)
            {
                using var path = Theme.RoundedRect(keyRect, S(3));
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 28 : 34, Theme.Accent));
                g.FillPath(brush, path);
            }
            else
            {
                using var path = Theme.RoundedRect(diagRect, S(3));
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 28 : 34, Theme.Accent));
                g.FillPath(brush, path);
            }
            DrawText(g, "关键", _owner._chipFont, keyRect, keyActive ? Theme.Accent : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "诊断", _owner._chipFont, diagRect, keyActive ? Theme.Dim : Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            string categoryText = CategoryName(_owner._timelineCategory) + " "
                                  + CategoryCount(_owner._view.TimelineCounts, _owner._timelineCategory) + " ▾";
            int categoryW = Measure(categoryText, _owner._chipFont).Width + S(14);
            var categoryRect = new Rectangle(rect.Right - pad - categoryW, y, categoryW, chipH);
            _owner._categoryRect = categoryRect;
            using (var path = Theme.RoundedRect(categoryRect, S(4)))
            {
                using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 14 : 22, Theme.Text)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Theme.BorderSoft, 1))
                    g.DrawPath(pen, path);
            }
            DrawText(g, categoryText, _owner._chipFont, categoryRect, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + toolbarH, rect.Right - pad, rect.Y + toolbarH);
        }

        private void DrawCategoryMenu(Graphics g, int width)
        {
            TimelineCategory[] categories =
            {
                TimelineCategory.All, TimelineCategory.Report, TimelineCategory.State,
                TimelineCategory.Network, TimelineCategory.Sensor, TimelineCategory.Alert,
                TimelineCategory.Camera,
            };
            int menuW = S(206);
            int itemH = S(20);
            int headH = S(4);
            int footH = S(24);
            int menuH = headH + categories.Length * itemH + footH;
            Rectangle categoryRect = _owner._categoryRect;
            int x = Math.Min(categoryRect.Right - menuW, width + S(14) - menuW);
            int y = categoryRect.Bottom + S(3);
            var rect = new Rectangle(x, y, menuW, menuH);
            using (var path = Theme.RoundedRect(rect, S(7)))
            {
                using var brush = new SolidBrush(Theme.IsLight ? Color.FromArgb(252, 253, 255) : Color.FromArgb(16, 26, 36));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.Border, 1);
                g.DrawPath(pen, path);
            }
            _owner._categoryMenuRects.Clear();
            int iy = y + headH;
            foreach (TimelineCategory category in categories)
            {
                var item = new Rectangle(x + S(5), iy, menuW - S(10), itemH);
                bool active = category == _owner._timelineCategory;
                if (active)
                {
                    using var path = Theme.RoundedRect(item, S(4));
                    using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 22 : 28, Theme.Accent));
                    g.FillPath(brush, path);
                }
                int count = CategoryCount(_owner._view.TimelineCounts, category);
                string name = CategoryName(category);
                DrawText(g, name, _owner._chipFont,
                    new Rectangle(item.X + S(8), item.Y, item.Width - S(50), itemH),
                    active ? Theme.Accent : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                DrawText(g, count.ToString(CultureInfo.InvariantCulture), _owner._tinyFont,
                    new Rectangle(item.Right - S(38), item.Y, S(30), itemH), Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                _owner._categoryMenuRects.Add((category, item));
                iy += itemH;
            }
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, x + S(9), iy - S(2), rect.Right - S(9), iy - S(2));
            DrawText(g, "会话总数，含诊断", _owner._tinyFont,
                new Rectangle(x + S(9), iy, menuW - S(18), S(18)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawTimelineRow(Graphics g, Rectangle row, MainTraceTimelineItem item)
        {
            int x = row.X + S(9);
            DrawText(g, item.Time.ToString("HH:mm:ss.fff"), _owner._eventTimeFont,
                new Rectangle(x, row.Y, S(62), row.Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(66);
            Color tagColor = KindColor(item.Kind);
            int chipH = S(16);
            var tag = new Rectangle(x, row.Y + (row.Height - chipH) / 2, S(46), chipH);
            using (var path = Theme.RoundedRect(tag, S(3)))
            using (var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 22 : 28, tagColor)))
                g.FillPath(brush, path);
            DrawText(g, item.Tag, _owner._chipFont, tag, tagColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            x += S(50);
            var srcRect = new Rectangle(row.Right - S(56), row.Y, S(42), row.Height);
            int textW = Math.Max(S(40), srcRect.X - S(6) - x);
            DrawText(g, item.Text, _owner._eventFont,
                new Rectangle(x, row.Y, textW, row.Height), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, item.Source, _owner._tinyFont,
                srcRect, Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "›", _owner._chipFont,
                new Rectangle(row.Right - S(12), row.Y, S(9), row.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawTimelineDetail(Graphics g, Rectangle rect, MainTraceTimelineItem? item)
        {
            if (item is null)
            {
                DrawText(g, "选择事件查看原始证据", _owner._eventFont, rect, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            string head = item.Tag + " · " + item.Text;
            DrawText(g, head, _owner._eventFont,
                new Rectangle(rect.X, rect.Y + S(7), rect.Width - S(52), S(15)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, "原文 ›", _owner._chipFont,
                new Rectangle(rect.Right - S(48), rect.Y + S(7), S(48), S(15)), Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.SingleLine);
            var rawRect = new Rectangle(rect.X, rect.Y + S(25), rect.Width, S(42));
            GraphicsState state = g.Save();
            g.SetClip(rawRect);
            DrawText(g, item.Raw, _owner._detailFont, rawRect, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            g.Restore(state);
            DrawText(g, (item.Diagnostic ? "诊断 · " : "关键 · ") + item.Source, _owner._tinyFont,
                new Rectangle(rect.X, rect.Bottom - S(15), rect.Width, S(12)), Theme.Dim,
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
                g.FillEllipse(brush, rect.X + pad, rect.Y + S(14), S(6), S(6));
            DrawText(g, title, _owner._cardTitleFont,
                new Rectangle(rect.X + pad + S(11), rect.Y, rect.Width - pad * 2 - S(110), S(32)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int noteW = Measure(note, _owner._tinyFont).Width;
            DrawText(g, note, _owner._tinyFont,
                new Rectangle(rect.Right - pad - noteW, rect.Y, noteW, S(32)), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using (var pen = new Pen(Theme.BorderSoft, 1))
                g.DrawLine(pen, rect.X + pad, rect.Y + S(32), rect.Right - pad, rect.Y + S(32));
        }

        private void DrawKv(Graphics g, string label, string value, int x, int y, int width, Color color,
            Font? valueFont = null)
        {
            DrawText(g, label, _owner._kvLabelFont, new Rectangle(x, y, width, S(12)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(g, value, valueFont ?? _owner._kvValueFont,
                new Rectangle(x, y + S(11), width, S(15)), color,
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
        private Color Slate => Theme.IsLight ? Color.FromArgb(82, 103, 122) : Color.FromArgb(157, 182, 196);

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
