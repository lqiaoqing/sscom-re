using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SscomRe;

internal enum MainTracePopupKind { Summary, Link, Peripheral }

internal static class MainTracePopups
{
    public static void ShowSummary(Form owner, MainTraceView view) =>
        Show(owner, MainTracePopupKind.Summary, view);

    public static void ShowLink(Form owner, MainTraceView view) =>
        Show(owner, MainTracePopupKind.Link, view);

    public static void ShowPeripheral(Form owner, MainTraceView view) =>
        Show(owner, MainTracePopupKind.Peripheral, view);

    private static void Show(Form owner, MainTracePopupKind kind, MainTraceView view)
    {
        using var dialog = new TracePopupForm(owner, kind, view);
        dialog.ShowDialog(owner);
    }

    internal static Bitmap RenderForTest(Form owner, string kindName, MainTraceView view)
    {
        MainTracePopupKind kind = kindName.ToLowerInvariant() switch
        {
            "link" => MainTracePopupKind.Link,
            "periph" or "peripheral" => MainTracePopupKind.Peripheral,
            _ => MainTracePopupKind.Summary,
        };
        using var dialog = new TracePopupForm(owner, kind, view);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-20000, -20000);
        dialog.Show(owner);
        Application.DoEvents();
        dialog.Refresh();
        Bitmap bitmap = dialog.RenderForTest();
        dialog.Close();
        return bitmap;
    }
}

internal sealed class TracePopupForm : Form
{
    private readonly PopupCanvas _canvas;

    public TracePopupForm(Form owner, MainTracePopupKind kind, MainTraceView view)
    {
        Text = kind switch
        {
            MainTracePopupKind.Summary => "会话摘要",
            MainTracePopupKind.Link => "链路与电源",
            _ => "外设明细",
        };
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        BackColor = Theme.Surface;
        Size size = kind switch
        {
            MainTracePopupKind.Summary => new Size(660, 520),
            MainTracePopupKind.Link => new Size(620, 550),
            _ => new Size(620, 358 + Math.Max(0, Math.Min(4, view.Tpms.Count) - 1) * 32 + (view.Tpms.Count > 4 ? 16 : 0)),
        };
        ClientSize = new Size(Theme.Scale(owner, size.Width), Theme.Scale(owner, size.Height));
        _canvas = new PopupCanvas(kind, view) { Dock = DockStyle.Fill };
        Controls.Add(_canvas);
        Theme.ApplyWindow(this);
        BackColor = Theme.Surface;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
    }

    internal Bitmap RenderForTest()
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        _canvas.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }
}

internal sealed class PopupCanvas : Control
{
    private readonly MainTracePopupKind _kind;
    private readonly MainTraceView _view;
    private readonly Font _titleFont;
    private readonly Font _subFont;
    private readonly Font _sectionFont;
    private readonly Font _bodyFont;
    private readonly Font _monoFont;
    private readonly Font _bigFont;
    private readonly Font _tinyFont;
    private Rectangle _closeRect;
    private Rectangle _footerCloseRect;
    private Rectangle _copyRect;
    private bool _copied;

    public PopupCanvas(MainTracePopupKind kind, MainTraceView view)
    {
        _kind = kind;
        _view = view;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Font = new Font("Microsoft YaHei UI", 9F);
        _titleFont = new Font(Font.FontFamily, 13F, FontStyle.Bold);
        _subFont = new Font(Font.FontFamily, 8.5F);
        _sectionFont = new Font(Font.FontFamily, 8F, FontStyle.Bold);
        _bodyFont = new Font(Font.FontFamily, 8.5F);
        _monoFont = Theme.MonoFont(8F);
        _bigFont = Theme.MonoFont(20F);
        _tinyFont = new Font(Font.FontFamily, 7F);
        MouseClick += OnClick;
    }

    private int S(int value) => Theme.Scale(this, value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subFont.Dispose();
            _sectionFont.Dispose();
            _bodyFont.Dispose();
            _monoFont.Dispose();
            _bigFont.Dispose();
            _tinyFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var panel = new Rectangle(0, 0, Width, Height);
        using (var brush = new LinearGradientBrush(panel, Theme.Surface, Theme.Mix(Theme.Surface, Theme.Canvas, 0.35f), 90f))
            g.FillRectangle(brush, panel);
        using (var pen = new Pen(Theme.Border, 1))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        DrawHeader(g);
        switch (_kind)
        {
            case MainTracePopupKind.Summary:
                DrawSummary(g);
                break;
            case MainTracePopupKind.Link:
                DrawLink(g);
                break;
            default:
                DrawPeripheral(g);
                break;
        }
        DrawFooter(g);
    }

    private void DrawHeader(Graphics g)
    {
        int headerH = S(50);
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, headerH),
                   Theme.Mix(Theme.Surface, Theme.Accent, 0.06f), Theme.Surface, 90f))
            g.FillRectangle(brush, 0, 0, Width, headerH);
        using (var pen = new Pen(Theme.BorderSoft, 1))
            g.DrawLine(pen, S(16), headerH - 1, Width - S(16), headerH - 1);
        string title = _kind switch
        {
            MainTracePopupKind.Summary => "会话摘要",
            MainTracePopupKind.Link => "链路与电源",
            _ => "外设明细",
        };
        string sub = _kind switch
        {
            MainTracePopupKind.Summary => "本次面板会话 · " + _view.SessionClockText + " · 已观察记录",
            MainTracePopupKind.Link => "观察详情 · 最近的链路与电源事件",
            _ => "最新快照 / 报文 · 原值展示",
        };
        DrawText(g, title, _titleFont, new Rectangle(S(18), S(9), Width - S(90), S(24)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, sub, _subFont, new Rectangle(S(18), S(28), Width - S(90), S(16)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        _closeRect = new Rectangle(Width - S(38), S(14), S(22), S(22));
        DrawText(g, "×", _subFont, _closeRect, Theme.Dim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawFooter(Graphics g)
    {
        int footerY = Height - S(50);
        using (var pen = new Pen(Theme.BorderSoft, 1))
            g.DrawLine(pen, S(16), footerY, Width - S(16), footerY);
        int right = Width - S(18);
        string primary = _kind == MainTracePopupKind.Summary ? (_copied ? "已复制" : "复制摘要") : "关闭";
        int primaryW = Measure(primary, _bodyFont).Width + S(26);
        var primaryRect = new Rectangle(right - primaryW, footerY + S(13), primaryW, S(24));
        DrawButton(g, primaryRect, primary, primary: true);
        right = primaryRect.X - S(8);
        if (_kind == MainTracePopupKind.Summary)
        {
            const string secondary = "关闭";
            int secondaryW = Measure(secondary, _bodyFont).Width + S(24);
            var secondaryRect = new Rectangle(right - secondaryW, footerY + S(13), secondaryW, S(24));
            DrawButton(g, secondaryRect, secondary, primary: false);
            _footerCloseRect = secondaryRect;
            _copyRect = primaryRect;
        }
        else
        {
            _footerCloseRect = primaryRect;
            _copyRect = Rectangle.Empty;
        }
    }

    private void DrawButton(Graphics g, Rectangle rect, string text, bool primary)
    {
        using (var path = Theme.RoundedRect(rect, S(5)))
        {
            if (primary)
            {
                using var brush = new LinearGradientBrush(rect, Theme.AccentBright, Theme.AccentDeep, 45f);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 14 : 20, Theme.Text));
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.Border, 1);
                g.DrawPath(pen, path);
            }
        }
        DrawText(g, text, _bodyFont, rect, primary ? Theme.AccentInk : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawSummary(Graphics g)
    {
        int pad = S(18);
        int width = Width - pad * 2;
        int y = S(61);
        DrawCoverage(g, new Rectangle(pad, y, width, S(28)));
        y += S(36);

        int rateW = S(180);
        var rateRect = new Rectangle(pad, y, rateW, S(96));
        DrawMiniCard(g, rateRect);
        DrawText(g, "已确认成功率", _tinyFont, new Rectangle(rateRect.X + S(10), rateRect.Y + S(8), rateRect.Width - S(20), S(14)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, _view.ConfirmedSuccessRateText, _bigFont, new Rectangle(rateRect.X + S(10), rateRect.Y + S(22), rateRect.Width - S(20), S(36)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, "缺少唯一 attempt ID", _tinyFont, new Rectangle(rateRect.X + S(10), rateRect.Y + S(59), rateRect.Width - S(20), S(13)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, "不按长度 / 顺序 / 30s 猜测", _tinyFont, new Rectangle(rateRect.X + S(10), rateRect.Y + S(73), rateRect.Width - S(20), S(13)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        int statX = rateRect.Right + S(10);
        int statW = (width - rateW - S(10) - S(12)) / 3;
        int statH = S(44);
        string[][] stats =
        {
            new[] { "编码", _view.ReportCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "MQTT 发布成功", _view.PublishSuccessLogCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "固件收尾日志", _view.TailLogCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "失败日志", _view.ReportFailedCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "未关联结果", _view.ReportUnlinkedCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "结果未观测", _view.ReportPendingCount.ToString(CultureInfo.InvariantCulture) },
        };
        for (int i = 0; i < stats.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            var rect = new Rectangle(statX + col * (statW + S(6)), y + row * (statH + S(6)), statW, statH);
            DrawMiniCard(g, rect);
            DrawText(g, stats[i][0], _tinyFont, new Rectangle(rect.X + S(8), rect.Y + S(5), rect.Width - S(16), S(13)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Color color = stats[i][0].StartsWith("MQTT", StringComparison.Ordinal) || stats[i][0].StartsWith("未关联", StringComparison.Ordinal)
                ? Theme.Accent
                : Theme.Text;
            if (stats[i][0] == "未关联结果") color = Theme.Amber;
            DrawText(g, stats[i][1], _monoFont, new Rectangle(rect.X + S(8), rect.Y + S(19), rect.Width - S(16), S(20)), color,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
        y += S(104);

        DrawSection(g, "上报间隔 · 已观察发布成功日志", pad, y);
        y += S(16);
        int miniW = (width - S(12)) / 3;
        DrawMini(g, new Rectangle(pad, y, miniW, S(46)), "平均间隔", _view.ReportIntervalAverageText,
            "样本 " + _view.ReportIntervalSampleCount.ToString(CultureInfo.InvariantCulture));
        DrawMini(g, new Rectangle(pad + miniW + S(6), y, miniW, S(46)), "最大间隔", _view.ReportIntervalMaxText,
            "只算本会话已观察");
        DrawMini(g, new Rectangle(pad + (miniW + S(6)) * 2, y, miniW, S(46)), "统计范围", _view.ReportIntervalRangeText,
            "发布成功日志");
        y += S(56);

        DrawSection(g, "链路恢复", pad, y);
        y += S(16);
        DrawMini(g, new Rectangle(pad, y, miniW, S(46)), "MQTT 连接成功", _view.MqttConnectSuccessCount.ToString(CultureInfo.InvariantCulture),
            "首次连接不计重连");
        DrawMini(g, new Rectangle(pad + miniW + S(6), y, miniW, S(46)), "MQTT 重试 / 重连",
            _view.MqttRetryCount + " / " + (_view.MqttReconnectCount > 0 ? _view.MqttReconnectCount.ToString(CultureInfo.InvariantCulture) : "—"),
            "重连需断开证据");
        DrawMini(g, new Rectangle(pad + (miniW + S(6)) * 2, y, miniW, S(46)), "QHTTP HTTP 结果",
            "OK " + _view.QhttpOkCount + " · err " + _view.QhttpErrCount,
            "重连 / 回退 未观测");
        y += S(56);

        DrawSection(g, "异常与完整性", pad, y);
        y += S(16);
        DrawMini(g, new Rectangle(pad, y, miniW, S(46)), "最近异常",
            _view.Incident is null ? "未观测" : _view.Incident.Summary,
            _view.Incident is null ? "本会话" : _view.Incident.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " · " + _view.Incident.Module);
        DrawMini(g, new Rectangle(pad + miniW + S(6), y, miniW, S(46)), "SOC_ERR",
            _view.SocErrorSummary.Length > 0 ? _view.SocErrorSummary : "未观测",
            "DOOR INVALID ×" + _view.DoorInvalidSnapshots.ToString(CultureInfo.InvariantCulture));
        DrawMini(g, new Rectangle(pad + (miniW + S(6)) * 2, y, miniW, S(46)), "解析失败",
            _view.MalformedSnapshots.ToString(CultureInfo.InvariantCulture),
            _view.CompleteSnapshotsText + " 完整快照");
        y += S(56);

        DrawText(g, "仅统计面板启用后已观察记录；成功率只统计唯一关联结果，未关联不等于失败；QHTTP 只表示固件观察到的 HTTP 请求结果。复制内容默认脱敏。",
            _tinyFont, new Rectangle(pad, y, width, S(32)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private void DrawCoverage(Graphics g, Rectangle rect)
    {
        using (var path = Theme.RoundedRect(rect, S(6)))
        {
            using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 16 : 22, Theme.Amber));
            g.FillPath(brush, path);
            using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 70 : 90, Theme.Amber), 1);
            g.DrawPath(pen, path);
        }
        DrawText(g, "本次面板会话", _sectionFont, new Rectangle(rect.X + S(10), rect.Y, S(90), rect.Height), Theme.Amber,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, "已观察记录；面板关闭后重新打开会开始新会话", _tinyFont,
            new Rectangle(rect.X + S(102), rect.Y, rect.Width - S(250), rect.Height), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, "Main Trace · 快照 " + _view.CompleteSnapshotsText, _tinyFont,
            new Rectangle(rect.Right - S(220), rect.Y, S(210), rect.Height), Theme.Dim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawLink(Graphics g)
    {
        int pad = S(18);
        int width = Width - pad * 2;
        int y = S(61);
        MainTracePowerInfo power = _view.Power;
        DrawSection(g, "唤醒 / 休眠", pad, y);
        y += S(16);
        y = DrawRow(g, pad, width, y, "最近唤醒",
            power.LastWakeAt is null ? "未观测" : power.LastWakeAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            power.LastWakeAt is null ? "等待 powersave 日志" : "唤醒源 " + (power.LastWakeSource.Length > 0 ? power.LastWakeSource : "未观测"),
            power.LastWakeAt is null ? Theme.Dim : Theme.Green);
        y = DrawRow(g, pad, width, y, "省电准备",
            power.SleepPrepareAt is null ? "未观测" : power.SleepPrepareAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            power.SleepPrepareAt is null ? "未观测" : "enter powersave state", Theme.Dim);
        string plan = power.NextWakeDeviceTime.Length > 0 ? power.NextWakeDeviceTime : "未观测";
        string planSub = power.NextWakeObservedAt is null ? "设备计划" : "设备计划 · 估算剩余 " + Remaining(power);
        y = DrawRow(g, pad, width, y, "计划下次唤醒", plan, planSub, power.NextWakeDeviceTime.Length > 0 ? Theme.Amber : Theme.Dim);
        y = DrawRow(g, pad, width, y, "本次唤醒持续",
            power.LastWakeDurationSeconds is int seconds ? seconds.ToString(CultureInfo.InvariantCulture) + " s" : "未观测",
            "来自固件日志", Theme.Dim);
        y += S(7);

        DrawSection(g, "Modem 恢复与等待", pad, y);
        y += S(16);
        y = DrawRow(g, pad, width, y, "最近唤醒请求",
            power.LastModemWakeAt is null ? "未观测" : power.LastModemWakeAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            power.LastModemWakeAt is null ? "未观测" : "modem wakeup request", power.LastModemWakeAt is null ? Theme.Dim : Theme.Text);
        y = DrawRow(g, pad, width, y, "退避",
            power.BackoffSeconds is int n ? n.ToString(CultureInfo.InvariantCulture) + " s" : "未观测",
            power.BackoffAt is null ? "未观测" : "配置总时长 · 窗口起点" + (power.BackoffAt is null ? "未观测" : "已观察"), Theme.Dim);
        y = DrawRow(g, pad, width, y, "AT 通道忙",
            power.AtBusyAt is null ? "未观测" : power.AtBusyAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            power.AtBusyAt is null ? "未观测" : "发布延迟，不等于失败", Theme.Dim);
        y = DrawRow(g, pad, width, y, "退出省电失败",
            power.ExitPowerFailAt is null ? "未观测" : power.ExitPowerFailAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            power.ExitPowerFailAt is null ? "未观测" : "恢复结果未观测", power.ExitPowerFailAt is null ? Theme.Dim : Theme.Amber);
        y += S(7);

        DrawSection(g, "双 BLE 策略", pad, y);
        y += S(16);
        int cardW = (width - S(8)) / 2;
        int cardH = S(126);
        DrawBleCard(g, new Rectangle(pad, y, cardW, cardH), "MCU BLE", _view.Snapshot.McuBleMode,
            _view.Ble.McuIntervalText, _view.Ble.McuDurationText, _view.Ble.LastFailureText,
            _view.Ble.McuSource.Length > 0 ? _view.Ble.McuSource : _view.Ble.Source);
        DrawBleCard(g, new Rectangle(pad + cardW + S(8), y, cardW, cardH), "SoC BLE", _view.Snapshot.SocBleMode,
            _view.Ble.SocIntervalText, _view.Ble.SocDurationText, _view.Ble.LastFailureText,
            _view.Ble.SocSource.Length > 0 ? _view.Ble.SocSource : _view.Ble.Source);
        y += cardH + S(8);
        DrawText(g, "STOP / PERIOD / FORCE 是固件策略缓存，不是射频扫描已验证；缺少单位的 SoC 数据不猜 ms/s。",
            _tinyFont, new Rectangle(pad, y, width, S(28)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private int DrawRow(Graphics g, int x, int width, int y, string label, string value, string sub, Color valueColor)
    {
        int h = S(25);
        DrawText(g, label, _bodyFont, new Rectangle(x, y, S(100), h), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, value, _monoFont, new Rectangle(x + S(100), y, S(90), h), valueColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, sub, _tinyFont, new Rectangle(x + S(194), y, width - S(194), h), Theme.Dim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 30 : 45, Theme.BorderSoft), 1))
            g.DrawLine(pen, x, y + h, x + width, y + h);
        return y + h;
    }

    private void DrawBleCard(Graphics g, Rectangle rect, string title, string mode,
        string interval, string duration, string failure, string source)
    {
        DrawMiniCard(g, rect);
        DrawText(g, title, _sectionFont, new Rectangle(rect.X + S(9), rect.Y + S(7), rect.Width - S(50), S(14)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, mode.Length > 0 ? mode : "未观测", _monoFont, new Rectangle(rect.Right - S(70), rect.Y + S(6), S(60), S(14)), Theme.Accent,
            TextFormatFlags.Right | TextFormatFlags.SingleLine);
        DrawText(g, "模式", _tinyFont, new Rectangle(rect.X + S(9), rect.Y + S(24), rect.Width - S(18), S(12)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, "间隔 / 时长", _tinyFont, new Rectangle(rect.X + S(9), rect.Y + S(43), S(66), S(12)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        string intervalText = interval.Length > 0 ? interval : "未观测";
        string durationText = duration.Length > 0 ? duration : "";
        DrawText(g, intervalText + (durationText.Length > 0 ? " / " + durationText : ""), _monoFont,
            new Rectangle(rect.X + S(78), rect.Y + S(42), rect.Width - S(86), S(14)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, "最近配置失败", _tinyFont, new Rectangle(rect.X + S(9), rect.Y + S(62), S(66), S(12)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, failure.Length > 0 ? failure : "未观测", _monoFont,
            new Rectangle(rect.X + S(78), rect.Y + S(61), rect.Width - S(86), S(14)),
            failure.Length > 0 ? Theme.Amber : Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, "策略缓存 · 非射频验证", _tinyFont, new Rectangle(rect.X + S(9), rect.Y + S(84), rect.Width - S(18), S(12)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        DrawText(g, source.Length > 0 ? source : "来源未观测", _tinyFont, new Rectangle(rect.X + S(9), rect.Y + S(102), rect.Width - S(18), S(12)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void DrawPeripheral(Graphics g)
    {
        int pad = S(18);
        int width = Width - pad * 2;
        int y = S(61);
        DrawSection(g, "TPMS · 按 MAC", pad, y);
        y += S(16);
        if (_view.Tpms.Count == 0)
        {
            DrawText(g, "未观测到 TPMS 原始数据", _bodyFont, new Rectangle(pad, y, width, S(30)), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            y += S(36);
        }
        else
        {
            int count = Math.Min(4, _view.Tpms.Count);
            int rowH = S(32);
            for (int i = 0; i < count; i++)
            {
                MainTraceTpmsInfo tpms = _view.Tpms[i];
                int ry = y + i * rowH;
                string mac = MaskMac(tpms.Mac);
                DrawText(g, mac, _monoFont, new Rectangle(pad, ry + S(2), S(122), S(14)), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                string type = tpms.Type is int t ? (t == 4 ? "TPMS（4）" : "type=" + t) : "类型未观测";
                DrawText(g, type, _bodyFont, new Rectangle(pad + S(128), ry + S(2), S(90), S(14)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                string temp = tpms.TemperatureC is double c
                    ? c.ToString("0.0", CultureInfo.InvariantCulture) + " °C"
                    : "温度未观测";
                DrawText(g, temp, _monoFont, new Rectangle(pad + width - S(110), ry + S(2), S(110), S(14)), Theme.Text,
                    TextFormatFlags.Right | TextFormatFlags.SingleLine);
                string pressure = tpms.PressureRaw is double p
                    ? "压力 " + p.ToString("0.##", CultureInfo.InvariantCulture) + " · 单位待确认"
                    : "压力 未观测";
                DrawText(g, pressure, _tinyFont, new Rectangle(pad, ry + S(17), S(180), S(13)), Theme.Amber,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                DrawText(g, AlarmText(tpms.AlarmRaw), _tinyFont, new Rectangle(pad + S(188), ry + S(17), S(210), S(13)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                DrawText(g, PowerText(tpms.PowerRaw), _tinyFont, new Rectangle(pad + S(404), ry + S(17), width - S(404), S(13)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                using (var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 30 : 45, Theme.BorderSoft), 1))
                    g.DrawLine(pen, pad, ry + rowH - 1, pad + width, ry + rowH - 1);
            }
            y += count * rowH + S(4);
            if (_view.Tpms.Count > count)
            {
                DrawText(g, "还有 " + (_view.Tpms.Count - count) + " 个 TPMS，未在弹层展开", _tinyFont,
                    new Rectangle(pad, y, width, S(14)), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine);
                y += S(16);
            }
        }
        DrawText(g, "JSON 压力原值单位待确认；温度按当前固件规则除以 10；不标轮位、不设正常/异常阈值。",
            _tinyFont, new Rectangle(pad, y, width, S(14)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, "固件类型：THS=1、TPMS=4、DOOR=5。固件规则版本随设备固件显示。",
            _tinyFont, new Rectangle(pad, y + S(14), width, S(14)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += S(36);

        DrawSection(g, "门磁 · 整体门状态", pad, y);
        y += S(16);
        string doorState = _view.Snapshot.DoorState.Length > 0 ? _view.Snapshot.DoorLabel : "未观测";
        string doorDuration;
        if (_view.DoorOpenActive && _view.DoorOpenSince is DateTime since)
            doorDuration = FormatDuration(DateTime.Now - since) + " · 仍在观察";
        else if (_view.LastDoorDurationSeconds is int last)
            doorDuration = FormatDuration(TimeSpan.FromSeconds(last)) + " · 上次区间";
        else
            doorDuration = "未观测";
        var doorRect = new Rectangle(pad, y, width, S(48));
        DrawMiniCard(g, doorRect);
        DrawText(g, doorState, _monoFont, new Rectangle(doorRect.X + S(10), doorRect.Y, S(86), doorRect.Height),
            _view.Snapshot.DoorState == "OPEN" ? Theme.Amber : _view.Snapshot.DoorState == "CLOSE" ? Theme.Green : Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, doorDuration, _bodyFont, new Rectangle(doorRect.X + S(102), doorRect.Y, S(250), doorRect.Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        DrawText(g, _view.DoorObservationSource.Length > 0 ? "来源 " + _view.DoorObservationSource : "来源未观测",
            _tinyFont, new Rectangle(doorRect.Right - S(180), doorRect.Y, S(170), doorRect.Height), Theme.Dim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        y += S(54);
        DrawText(g, "门磁观察区间是整体门状态估算；首帧 OPEN 起算，INVALID/断连/运行段变化会冻结，不跨间隔拼接。",
            _tinyFont, new Rectangle(pad, y, width, S(28)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        y += S(34);

        DrawSection(g, "THS", pad, y);
        y += S(16);
        MainTraceReport? report = _view.LastReport;
        string ths = report?.ThsText is { Length: > 0 } text ? text : _view.Snapshot.ThText;
        DrawText(g, ths.Length > 0 ? ths : "未观测", _monoFont, new Rectangle(pad, y, width, S(22)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawMini(Graphics g, Rectangle rect, string label, string value, string sub)
    {
        DrawMiniCard(g, rect);
        DrawText(g, label, _tinyFont, new Rectangle(rect.X + S(8), rect.Y + S(5), rect.Width - S(16), S(13)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, value, _monoFont, new Rectangle(rect.X + S(8), rect.Y + S(18), rect.Width - S(16), S(14)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, sub, _tinyFont, new Rectangle(rect.X + S(8), rect.Y + S(32), rect.Width - S(16), S(11)), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void DrawMiniCard(Graphics g, Rectangle rect)
    {
        using var path = Theme.RoundedRect(rect, S(6));
        using var brush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 12 : 18, Theme.Text));
        g.FillPath(brush, path);
        using var pen = new Pen(Theme.BorderSoft, 1);
        g.DrawPath(pen, path);
    }

    private void DrawSection(Graphics g, string title, int x, int y)
    {
        DrawText(g, title, _sectionFont, new Rectangle(x, y, Width - x * 2, S(14)), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.SingleLine);
        using var pen = new Pen(Theme.WithAlpha(Theme.IsLight ? 45 : 60, Theme.Accent), 1);
        g.DrawLine(pen, x, y + S(15), Width - x, y + S(15));
    }

    private void OnClick(object? sender, MouseEventArgs e)
    {
        if (_closeRect.Contains(e.Location) || _footerCloseRect.Contains(e.Location))
        {
            FindForm()?.Close();
            return;
        }
        if (_copyRect.Contains(e.Location) && _kind == MainTracePopupKind.Summary)
        {
            try
            {
                Clipboard.SetText(BuildSummaryText());
                _copied = true;
                Invalidate();
            }
            catch
            {
                // 剪贴板被占用时不打断弹层。
            }
        }
    }

    private string BuildSummaryText()
    {
        var text = new StringBuilder();
        text.AppendLine("Trace监控 会话摘要");
        text.AppendLine("会话：" + _view.SessionClockText + " · " + _view.CompleteSnapshotsText + " 完整快照");
        text.AppendLine("已确认成功率：" + _view.ConfirmedSuccessRateText + "（缺少唯一 attempt ID）");
        text.AppendLine("编码 " + _view.ReportCount + " · MQTT 发布成功日志 " + _view.PublishSuccessLogCount
                        + " · 固件收尾日志 " + _view.TailLogCount + " · 失败日志 " + _view.ReportFailedCount
                        + " · 未关联 " + _view.ReportUnlinkedCount + " · 结果未观测 " + _view.ReportPendingCount);
        text.AppendLine("上报间隔：平均 " + _view.ReportIntervalAverageText + " · 最大 " + _view.ReportIntervalMaxText
                        + " · 样本 " + _view.ReportIntervalSampleCount + " · " + _view.ReportIntervalRangeText);
        text.AppendLine("MQTT：连接成功 " + _view.MqttConnectSuccessCount + " · 重试日志 " + _view.MqttRetryCount
                        + " · 重连 " + (_view.MqttReconnectCount > 0 ? _view.MqttReconnectCount.ToString(CultureInfo.InvariantCulture) : "—（缺断开证据）"));
        text.AppendLine("QHTTP：HTTP OK " + _view.QhttpOkCount + " · err " + _view.QhttpErrCount + " · 重连/回退 未观测");
        text.AppendLine("SOC_ERR：" + (_view.SocErrorSummary.Length > 0 ? _view.SocErrorSummary : "未观测")
                        + " · DOOR INVALID ×" + _view.DoorInvalidSnapshots + " · 解析失败 " + _view.MalformedSnapshots);
        if (_view.Incident is MainTraceIncident incident)
        {
            text.AppendLine("最近异常：" + incident.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                            + " · " + incident.Module + " · " + incident.Summary);
            if (incident.ReferenceFix is MainTraceFixInfo fix)
                text.AppendLine("参考定位：" + fix.CoordinatesText + " · " + AgeText(fix.ObservedAt));
        }
        text.AppendLine("口径：仅本面板会话已观察记录；未关联不等于失败；QHTTP 只表示 HTTP 请求结果；复制不发送外部。");
        return text.ToString();
    }

    private string Remaining(MainTracePowerInfo power)
    {
        if (power.NextWakeObservedAt is null) return "未观测";
        int wait = Math.Max(0, (power.NextWakeWaitSeconds ?? 0)
            - (int)Math.Round((DateTime.Now - power.NextWakeObservedAt.Value).TotalSeconds));
        return FormatClock(wait);
    }

    private static string FormatClock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return (seconds / 60).ToString("00", CultureInfo.InvariantCulture) + ":"
               + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return ((int)span.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":"
               + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
               + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string MaskMac(string mac)
    {
        if (mac.Length <= 8) return mac;
        char separator = mac.IndexOf(':') >= 0 ? ':' : '-';
        string[] parts = mac.Split(separator);
        if (parts.Length >= 3) return parts[0] + separator + parts[1] + separator + "…" + separator + parts[parts.Length - 1];
        return mac.Substring(0, 4) + "…" + mac.Substring(mac.Length - 4);
    }

    private static string AlarmText(int? raw)
    {
        if (raw is not int value) return "报警未观测";
        if (value == 0) return "alarm 0x00 · 无";
        var bits = new List<string>();
        if ((value & 1) != 0) bits.Add("快速漏气");
        if ((value & 2) != 0) bits.Add("充气");
        if ((value & 4) != 0) bits.Add("通信失联");
        return "alarm 0x" + value.ToString("X2", CultureInfo.InvariantCulture)
               + (bits.Count > 0 ? " · " + string.Join("/", bits) : " · 未知位");
    }

    private static string PowerText(int? raw) => raw switch
    {
        1 => "power 1 · 固件非低电路径",
        2 => "power 2 · 固件低电路径",
        0 => "power 0 · 未指示",
        _ => "power 未观测",
    };

    private static string AgeText(DateTime time)
    {
        double seconds = Math.Max(0, (DateTime.Now - time).TotalSeconds);
        if (seconds < 1) return "刚刚";
        if (seconds < 60) return Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture) + " s 前";
        return Math.Floor(seconds / 60).ToString("0", CultureInfo.InvariantCulture) + " min 前";
    }

    private static void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, font, rect, color,
            flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    private static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
}
