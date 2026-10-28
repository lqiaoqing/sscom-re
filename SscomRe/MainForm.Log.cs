using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>接收日志的分色追加、整屏重画、搜索/暂停跟随和复制。</summary>
public sealed partial class MainForm
{
    private const int WM_SETREDRAW = 0x000B;
    private const int RenderChunkChars = 64 * 1024;
    private bool _scrollLatestScheduled;

    // 整屏重画按块异步进行：单块 RTF 插入只有几十毫秒，避免 1M 字符一次重建把界面冻住。
    private int _renderToken;
    private bool _renderBusy;
    private string? _renderText;
    private int _renderOffset;
    private int _renderSelStart;
    private int _renderSelLen;
    private Point _renderScroll;

    private void AppendFormatted(string text)
    {
        List<LogFormatter.Line> lines = LogFormatter.Parse(text, _hexDisplayBox.Checked);
        Color defaultColor = _recvBox.ForeColor;
        Color defaultBack = _recvBox.BackColor;
        var palette = CurrentLogPalette();

        // 纯数据批次（无时间戳、收发标记、HEX 着色）不值得构造 RTF：
        // AppendText 走 EM_REPLACESEL 纯文本路径，高频接收时省掉整段 RTF 解析。
        bool plain = true;
        foreach (LogFormatter.Line line in lines)
        {
            if (line.Tx) { plain = false; break; }
            foreach (LogFormatter.Span span in line.Spans)
            {
                if (span.Kind is not (LogFormatter.Kind.Data or LogFormatter.Kind.Newline))
                {
                    plain = false;
                    break;
                }
            }
            if (!plain) break;
        }
        // 规则启用时先做一次整批预筛；常见纯数据无命中就不必逐行跑规则。
        if (plain && !_logHighlighter.HasAnyMatch(text))
        {
            AppendPlainBatch(text, defaultColor, defaultBack);
            return;
        }

        var styledLines = new List<StyledLogLine>(lines.Count);
        bool anyHighlight = false;
        foreach (LogFormatter.Line line in lines)
        {
            StyledLogLine styled = _logHighlighter.Style(line, palette);
            styledLines.Add(styled);
            if (styled.Matched) anyHighlight = true;
        }
        if (plain && !anyHighlight)
        {
            AppendPlainBatch(text, defaultColor, defaultBack);
            return;
        }
        var colors = new List<Color>();
        int ColorIndex(Color c)
        {
            int i = colors.IndexOf(c);
            if (i >= 0) return i + 1;
            colors.Add(c);
            return colors.Count;
        }

        int defaultCf = ColorIndex(defaultColor);
        int defaultHl = ColorIndex(defaultBack);

        // 一次构造整批 RTF，再用 SelectedRtf 单次插入；逐段 SelectionColor/AppendText 在长日志上是 O(n²)。
        var body = new StringBuilder(Math.Max(64, text.Length * 2));
        bool first = true;
        foreach (StyledLogLine line in styledLines)
        {
            if (!first)
            {
                body.Append(@"{\cf").Append(defaultCf)
                    .Append(@"\highlight").Append(defaultHl).Append(@"\line}");
            }
            first = false;

            foreach (StyledLogRun run in line.Runs)
            {
                body.Append(@"{\cf").Append(ColorIndex(run.Fore))
                    .Append(@"\highlight").Append(ColorIndex(run.Back)).Append(' ');
                LogExport.AppendRtfText(body, run.Text);
                body.Append('}');
            }
        }

        var rtf = new StringBuilder(body.Length + 256);
        rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset")
            .Append(Math.Max(0, (int)_recvBox.Font.GdiCharSet)).Append(' ')
            .Append(LogExport.RtfFontName(_recvBox.Font.Name)).Append(@";}}");
        rtf.Append(@"{\colortbl ;");
        foreach (Color c in colors)
        {
            rtf.Append(@"\red").Append(c.R)
                .Append(@"\green").Append(c.G)
                .Append(@"\blue").Append(c.B).Append(';');
        }
        rtf.Append(@"}\viewkind4\uc1\pard\f0\fs")
            .Append(Math.Max(1, (int)Math.Round(_recvBox.Font.SizeInPoints * 2))).Append(' ');
        rtf.Append(body).Append('}');

        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.SelectedRtf = rtf.ToString();
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.SelectionColor = defaultColor;
        _recvBox.SelectionBackColor = defaultBack;
    }

    private void AppendPlainBatch(string text, Color fore, Color back)
    {
        _recvBox.AppendText(text);
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.SelectionColor = fore;
        _recvBox.SelectionBackColor = back;
    }

    private LogHighlightPalette CurrentLogPalette() => new(
        _recvBox.ForeColor, _recvBox.BackColor, Theme.Dim, Theme.Accent, Theme.StatusSend,
        Theme.Accent, Theme.TxWash);

    /// <summary>
    /// 整屏重画：清空后按 64K 字符一块、每块一次 BeginInvoke 追加。
    /// 单块 RTF 插入只有几十毫秒，既保留分色又不会像一次插入 1M 字符那样把界面冻住。
    /// </summary>
    private void RenderLogFromText(string text)
    {
        if (!_recvBox.IsHandleCreated)
        {
            _recvBox.Text = text;
            return;
        }
        _renderSelStart = _recvBox.SelectionStart;
        _renderSelLen = _recvBox.SelectionLength;
        _renderScroll = GetRecvScroll();
        ClearSearchHighlight();

        CancelRenderLog();                     // 先结束上一轮未完成的重画，恢复重绘
        int token = _renderToken;
        _renderText = text;
        _renderOffset = 0;

        SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        _recvBox.Clear();
        if (string.IsNullOrEmpty(text))
        {
            FinishRenderLog();
            return;
        }
        _renderBusy = true;
        BeginInvoke(new Action(() => RenderLogChunk(token)));
    }

    private void RenderLogChunk(int token)
    {
        if (!RenderLogChunkOnce(token)) return;
        BeginInvoke(new Action(() => RenderLogChunk(token)));
    }

    /// <summary>处理一块；返回 true 表示还有下一块。同步收尾和异步分块共用这段。</summary>
    private bool RenderLogChunkOnce(int token)
    {
        if (!_renderBusy || token != _renderToken || IsDisposed || !_recvBox.IsHandleCreated) return false;
        string? text = _renderText;
        if (text is null)
        {
            FinishRenderLog();
            return false;
        }
        int end = NextRenderChunkEnd(text, _renderOffset);
        string chunk = text.Substring(_renderOffset, end - _renderOffset);
        _renderOffset = end;
        try
        {
            AppendFormatted(chunk);
        }
        catch
        {
            CancelRenderLog();
            throw;
        }
        if (_renderOffset < text.Length) return true;
        FinishRenderLog();
        return false;
    }

    /// <summary>保存/复制/搜索前调用：把剩余分块在当前 UI 事件内画完，避免读到半屏日志。</summary>
    private void CompletePendingRenderLog()
    {
        if (!_renderBusy) return;
        int token = _renderToken;
        while (_renderBusy && token == _renderToken)
        {
            if (!RenderLogChunkOnce(token)) break;
        }
    }

    /// <summary>优先在换行处切块；单行过长（HEX 连续输出）时按字符数切，代理对不劈开。</summary>
    private static int NextRenderChunkEnd(string text, int start)
    {
        int limit = Math.Min(text.Length, start + RenderChunkChars);
        if (limit >= text.Length) return text.Length;
        int cut = text.LastIndexOf('\n', limit - 1, limit - start);
        if (cut >= start) return cut + 1;
        if (char.IsHighSurrogate(text[limit - 1]) && limit < text.Length) limit++;
        return limit;
    }

    private void FinishRenderLog()
    {
        _renderText = null;
        _renderOffset = 0;
        _renderBusy = false;
        if (_recvBox.IsHandleCreated && !IsDisposed)
        {
            SendMessage(_recvBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _recvBox.Invalidate();
        }
        if (IsDisposed || !IsHandleCreated) return;

        if (_autoScrollBox.Checked)
        {
            EnsureRecvAtLatest();
        }
        else
        {
            int start = Math.Min(_renderSelStart, _recvBox.TextLength);
            int len = Math.Min(_renderSelLen, Math.Max(0, _recvBox.TextLength - start));
            _recvBox.Select(start, len);
            SetRecvScroll(_renderScroll);
        }
        // 重画期间接收线程可能积压了新数据，画完继续排一次刷新。
        bool pending;
        lock (_displayLock)
            pending = _pendingDisplay.Length > 0 || _pendingReplace is not null;
        if (pending) StartDisplayFlush();
    }

    /// <summary>取消未完成的整屏重画并恢复重绘；清屏/切换前调用。</summary>
    private void CancelRenderLog()
    {
        _renderToken++;
        _renderText = null;
        _renderOffset = 0;
        if (!_renderBusy) return;
        _renderBusy = false;
        if (IsDisposed || !_recvBox.IsHandleCreated) return;
        SendMessage(_recvBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        _recvBox.Invalidate();
    }

    private void ScrollRecvToLatest()
    {
        if (_recvBox is null || !_recvBox.IsHandleCreated) return;
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.ScrollToCaret();
    }

    /// <summary>已显示最后一行时不再重复 ScrollToCaret，避免高频接收时白做一次全文档定位。</summary>
    private void EnsureRecvAtLatest()
    {
        if (!RecvAtBottom()) ScrollRecvToLatest();
    }

    /// <summary>尺寸/DPI/激活变化后等一次布局完成，再保证视图在末尾。</summary>
    private void ScheduleScrollRecvToLatest()
    {
        if (_autoScrollBox is null || !_autoScrollBox.Checked ||
            _scrollLatestScheduled || !IsHandleCreated || IsDisposed) return;
        _scrollLatestScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _scrollLatestScheduled = false;
            if (!IsDisposed && _autoScrollBox.Checked) EnsureRecvAtLatest();
        }));
    }

    /// <summary>主题/字体/颜色变化时重画当前可见日志（含发送回显和本地输入）。</summary>
    private void RenderLogAll()
    {
        RenderLogFromText(_recvBox.Text);
    }

    /// <summary>切换 HEX/文本显示时，从对应累积缓冲整屏重画。</summary>
    private void RenderLogBuffer()
    {
        RenderLogFromText(_hexDisplayBox.Checked ? _receive.HexText : _receive.TextAccum);
    }

    private void ToggleFollow() => _autoScrollBox.Checked = !_autoScrollBox.Checked;

    private void SyncFollowButton()
    {
        if (_pauseButton is null) return;
        _pauseButton.Text = _autoScrollBox.Checked ? "暂停" : "继续";
        _pauseButton.Kind = _autoScrollBox.Checked ? ThemeButton.ButtonKind.Default : ThemeButton.ButtonKind.Outline;
        _pauseButton.Invalidate();
    }

    private void SearchNext(bool forward)
    {
        if (_searchBox is null) return;
        string query = _searchBox.Text;
        if (query.Length == 0)
        {
            ClearSearchHighlight();
            HideToast();
            return;
        }
        CompletePendingRenderLog();            // 分块重画期间控件里只有部分日志
        ClearSearchHighlight();
        RichTextBoxFinds options = forward ? RichTextBoxFinds.None : RichTextBoxFinds.Reverse;
        int start = forward
            ? Math.Min(_recvBox.TextLength, _recvBox.SelectionStart + Math.Max(1, _recvBox.SelectionLength))
            : Math.Max(0, _recvBox.SelectionStart - 1);
        int index = _recvBox.Find(query, start, options);
        if (index < 0 && _recvBox.TextLength > 0)
            index = _recvBox.Find(query, forward ? 0 : _recvBox.TextLength, options);
        if (index < 0)
        {
            string shown = query.Length > 24 ? query.Substring(0, 24) + "…" : query;
            ShowToast("未找到「" + shown + "」", ToastTone.Warning);
            return;
        }
        HideToast();
        _recvBox.Select(index, query.Length);
        _recvBox.SelectionBackColor = Theme.SearchHighlight;
        _searchStart = index;
        _searchLength = query.Length;
        _recvBox.ScrollToCaret();
    }

    private void ClearSearchHighlight()
    {
        if (_searchStart < 0 || _searchStart + _searchLength > _recvBox.TextLength)
        {
            _searchStart = -1;
            _searchLength = 0;
            return;
        }
        int saveStart = _recvBox.SelectionStart;
        int saveLength = _recvBox.SelectionLength;
        _recvBox.Select(_searchStart, _searchLength);
        _recvBox.SelectionBackColor = _recvBox.BackColor;
        _recvBox.Select(Math.Min(saveStart, _recvBox.TextLength),
            Math.Min(saveLength, Math.Max(0, _recvBox.TextLength - saveStart)));
        _searchStart = -1;
        _searchLength = 0;
    }

    private void CopyPlainLog()
    {
        CompletePendingRenderLog();
        string text = _recvBox.SelectionLength > 0 ? _recvBox.SelectedText : _recvBox.Text;
        try
        {
            Clipboard.SetText(LogFormatter.StripMeta(text));
            ShowToast("已复制纯数据");
        }
        catch
        {
            // 剪贴板被占用时忽略
        }
    }

    private static string RateText(double bytes)
    {
        if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB/s";
        if (bytes >= 1024) return (bytes / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB/s";
        return ((int)bytes).ToString(System.Globalization.CultureInfo.InvariantCulture) + " B/s";
    }

    private void RateTick()
    {
        if (IsDisposed) return;
        long sent = Interlocked.Read(ref _sentCount);
        long recv = Interlocked.Read(ref _recvCount);
        if (sent < _lastSentSample) _lastSentSample = sent;
        if (recv < _lastRecvSample) _lastRecvSample = recv;
        double seconds = Math.Max(0.1, _rateTimer.Interval / 1000.0);
        double tx = (sent - _lastSentSample) / seconds;
        double rx = (recv - _lastRecvSample) / seconds;
        _lastSentSample = sent;
        _lastRecvSample = recv;

        _rxRateLabel.Text = "RX " + RateText(rx);
        _txRateLabel.Text = "TX " + RateText(tx);
        long used = Math.Min(_receive.Counter, Math.Max(1, _receive.Limit));
        int percent = (int)Math.Round(used * 100.0 / Math.Max(1, _receive.Limit));
        _bufferLabel.Text = "缓冲 " + percent.ToString() + "%";
    }

    private ContextMenuStrip BuildLogContextMenu()
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem("复制");
        copy.Click += (_, _) =>
        {
            CompletePendingRenderLog();
            _recvBox.Copy();
        };
        var plain = new ToolStripMenuItem("复制纯数据（去掉时间戳和收/发标记）");
        plain.Click += (_, _) => CopyPlainLog();
        var all = new ToolStripMenuItem("全选");
        all.Click += (_, _) =>
        {
            CompletePendingRenderLog();
            _recvBox.SelectAll();
        };
        var clear = new ToolStripMenuItem("清除窗口");
        clear.Click += (_, _) => ClearReceive();
        menu.Items.AddRange(new ToolStripItem[] { copy, plain, new ToolStripSeparator(), all, clear });
        Theme.StyleContextMenu(menu);
        return menu;
    }

    private ContextMenuStrip BuildSaveMenu()
    {
        var menu = new ContextMenuStrip();
        var data = new ToolStripMenuItem("保存数据（TXT + DAT）");
        data.Click += (_, _) => SaveData();
        var rtf = new ToolStripMenuItem("另存为带颜色日志（RTF）...");
        rtf.Click += (_, _) => SaveColoredLogRtf();
        var html = new ToolStripMenuItem("另存为带颜色日志（HTML）...");
        html.Click += (_, _) => SaveColoredLogHtml();
        menu.Items.AddRange(new ToolStripItem[] { data, new ToolStripSeparator(), rtf, html });
        Theme.StyleContextMenu(menu);
        return menu;
    }

    internal IReadOnlyList<string> SaveMenuTextsForTest =>
        _saveMenu is null
            ? Array.Empty<string>()
            : _saveMenu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();

    // ———————————————————————— 压测钩子（--bench-log） ————————————————————————

    internal void PrepareLogBenchmark(bool hex)
    {
        _displayTimer.Stop();
        ClearReceive();
        HideToast();
        _autoScrollBox.Checked = true;
        if (_hexDisplayBox.Checked != hex) _hexDisplayBox.Checked = hex;
        RefreshRecvSnapshot();
        BenchDecodeMs = 0;
        BenchDrainMs = 0;
        BenchPumpMs = 0;
    }

    internal double BenchDecodeMs;
    internal double BenchDrainMs;
    internal double BenchPumpMs;

    internal double FeedLogChunkForBenchmark(byte[] chunk)
    {
        var watch = Stopwatch.StartNew();
        HandleIncoming(chunk);
        BenchDecodeMs += watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        DrainDisplay();
        double drain = watch.Elapsed.TotalMilliseconds;
        BenchDrainMs += drain;

        watch.Restart();
        Application.DoEvents();
        double pump = watch.Elapsed.TotalMilliseconds;
        BenchPumpMs += pump;
        return drain + pump;
    }

    internal int LogTextLengthForBenchmark => _recvBox.TextLength;
    internal bool RecvAtBottomForTest() => RecvAtBottom();
    internal void AppendLogForTest(string text) => AppendRecv(text);
    internal void RenderLogForTest(string text) => RenderLogFromText(text);
    internal void SetHighlightRulesForTest(IEnumerable<LogHighlightRule> rules)
    {
        _highlightRules.Clear();
        foreach (LogHighlightRule rule in rules) _highlightRules.Add(rule);
        _logHighlighter.SetRules(_highlightRules);
    }
    internal bool RenderLogBusyForTest => _renderBusy;
    internal void CompletePendingRenderLogForTest() => CompletePendingRenderLog();
    internal bool AutoScrollEnabledForTest => _autoScrollBox?.Checked == true;
    internal void SetAutoScrollForTest(bool enabled) => _autoScrollBox.Checked = enabled;

    internal bool SearchForTest(string query)
    {
        if (_searchBox is null) return false;
        _searchBox.Text = query;
        SearchNext(true);
        return _toast is { Visible: true };
    }

    internal void HideToastForTest() => HideToast();

    internal string ExportRtfForTest()
    {
        CompletePendingRenderLog();
        return LogExport.ToRtf(_recvBox.Text, _hexDisplayBox.Checked, _logHighlighter, CurrentLogPalette(), _recvBox.Font);
    }

    internal string ExportHtmlForTest()
    {
        CompletePendingRenderLog();
        return LogExport.ToHtml(_recvBox.Text, _hexDisplayBox.Checked, _logHighlighter, CurrentLogPalette(),
            _recvBox.Font, "sscomRe 日志");
    }

    private void SaveColoredLogRtf() => SaveColoredLog(rtf: true);

    private void SaveColoredLogHtml() => SaveColoredLog(rtf: false);

    /// <summary>另存为带颜色日志：按当前显示重新解析并着色，RTF 保真、HTML 便于分享。</summary>
    private void SaveColoredLog(bool rtf)
    {
        CompletePendingRenderLog();                       // 必须是完整窗口内容
        string stamp = SendComposer.FileTimestamp();
        string extension = rtf ? "rtf" : "html";
        using var dlg = new SaveFileDialog
        {
            Filter = rtf ? "RTF 富文本 (*.rtf)|*.rtf" : "HTML 网页 (*.html)|*.html",
            FileName = "SaveWindows" + stamp + "." + extension,
            InitialDirectory = Directory.GetCurrentDirectory(),
            Title = rtf ? "另存为带颜色日志（RTF）" : "另存为带颜色日志（HTML）",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            string text = _recvBox.Text;
            LogHighlightPalette palette = CurrentLogPalette();
            string content = rtf
                ? LogExport.ToRtf(text, _hexDisplayBox.Checked, _logHighlighter, palette, _recvBox.Font)
                : LogExport.ToHtml(text, _hexDisplayBox.Checked, _logHighlighter, palette, _recvBox.Font,
                    "sscomRe 日志 " + stamp);
            File.WriteAllText(dlg.FileName, content, rtf ? Encoding.ASCII : new UTF8Encoding(true));
            ShowToast("已导出 " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "另存为带颜色日志",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    internal void ShowBenchmarkDemo()
    {
        ClearReceive();
        HideToast();
        _displayTimer.Stop();
        AppendFormatted(
            "[12:34:56.789]收←◆Hello 世界\r\n" +
            "[12:34:57.000]发→◇12 FF 00\r\n" +
            "[12:34:57.100]收←◆ERR checksum 校验失败\r\n" +
            "普通数据 ABC 0123");
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.ScrollToCaret();
    }
}
