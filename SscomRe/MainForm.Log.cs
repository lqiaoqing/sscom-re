using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>接收日志的分色追加、整屏重画、停止显示、搜索和复制。</summary>
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
    private bool _renderClear = true;

    // 停止显示：窗口保持不动，新内容先记在这里。截断发生过则 _displayHoldBase 是保留下来的整屏。
    private bool _displayPaused;
    private bool _displayHoldTruncated;
    private string? _displayHoldBase;
    private readonly StringBuilder _displayHold = new();

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
    private void RenderLogFromText(string text) => BeginPaintLog(text, clear: true);

    /// <summary>把大段积压追加到现有日志上，不先清空。重绘先关掉，补完一次画出。</summary>
    private void AppendLogChunked(string text) => BeginPaintLog(text, clear: false);

    private void BeginPaintLog(string text, bool clear)
    {
        if (!_recvBox.IsHandleCreated)
        {
            if (clear) _recvBox.Text = text;
            else _recvBox.AppendText(text);
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
        _renderClear = clear;

        SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        if (clear) _recvBox.Clear();
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
        // 累积里已经有停止期间收到的数据。先丢掉未上屏队列，避免重画结束后再追加一次。
        DiscardUnpainted();
        RenderLogFromText(_hexDisplayBox.Checked ? _receive.HexText : _receive.TextAccum);
    }

    private void ToggleDisplayPause()
    {
        if (_displayPaused) ResumeDisplay();
        else PauseDisplay();
    }

    private void PauseDisplay()
    {
        _displayPaused = true;
        SyncDisplayPauseButton();
        ShowToast("已暂停显示，串口仍在接收");
    }

    private void ResumeDisplay()
    {
        string folded = FoldQueueIntoHold();
        if (folded.Length > 0)
            NoteHtml(folded);
        bool truncated = _displayHoldTruncated;
        string held = truncated
            ? (_displayHoldBase ?? "") + _displayHold.ToString()
            : _displayHold.ToString();
        _displayHoldTruncated = false;
        _displayHoldBase = null;
        _displayHold.Clear();

        bool queueHeld = false;
        lock (_displayLock)
        {
            // 折叠之后、放开闸门之前又到的段，接在积压末尾。截断则整屏以新快照为准。
            string tail = _pendingDisplay.ToString();
            string? tailReplace = _pendingReplace;
            _pendingDisplay.Clear();
            _pendingReplace = null;
            if (tailReplace is not null)
            {
                truncated = true;
                held = tailReplace + tail;
            }
            else if (tail.Length > 0)
            {
                held += tail;
            }
            _displayPaused = false;
            if (_renderBusy && held.Length > 0)
            {
                if (tail.Length > 0)
                    NoteHtml(tail);
                _htmlSkip += held.Length;
                if (truncated)
                {
                    _pendingReplace = held;
                    _pendingDisplay.Clear();
                }
                else
                {
                    _pendingDisplay.Append(held);
                }
                _drainScheduled = true;
                queueHeld = true;
            }
        }
        if (queueHeld) StartDisplayFlush();
        SyncDisplayPauseButton();
        if (!queueHeld && held.Length > 0)
        {
            if (truncated)
                RenderLogFromText(held);
            else if (held.Length > RenderChunkChars)
                AppendLogChunked(held);
            else
                AppendRecv(held, recordHtml: false);
        }
        if (truncated)
            ShowToast("暂停期间数据超过显示缓冲上限，已补上保留的内容");
        else if (held.Length > 0)
            ShowToast("已补上暂停期间的内容");
        else
            ShowToast("已继续显示");
    }

    /// <summary>两种文字宽度取较大值后固定，避免切换时把所在行和菜单栏重新排一遍。</summary>
    private void LockPauseButtonWidth()
    {
        if (_pauseButton is null) return;
        string keep = _pauseButton.Text;
        _pauseButton.AutoSize = true;
        _pauseButton.Text = "暂停显示";
        Size pause = _pauseButton.GetPreferredSize(Size.Empty);
        _pauseButton.Text = "继续显示";
        Size resume = _pauseButton.GetPreferredSize(Size.Empty);
        _pauseButton.AutoSize = false;
        _pauseButton.Size = new Size(Math.Max(pause.Width, resume.Width), Math.Max(pause.Height, resume.Height));
        _pauseButton.Text = keep;
    }

    private void SyncDisplayPauseButton()
    {
        if (_pauseButton is null) return;
        _pauseButton.Text = _displayPaused ? "继续显示" : "暂停显示";
        _pauseButton.Kind = _displayPaused ? ThemeButton.ButtonKind.Outline : ThemeButton.ButtonKind.Default;
        _rowTip.SetToolTip(_pauseButton, _displayPaused
            ? "把暂停期间收到的内容补到窗口里。"
            : "窗口停止刷新。接收、计数和写文件都继续，再点一次把刚才的内容补上。");
        _pauseButton.Invalidate();
    }

    /// <summary>接收队列里还没画的段，并入停止显示的积压。截断快照会换掉之前积压的增量。</summary>
    private void AbsorbDisplayHold(string text, string? replace, bool recordHtml = false)
    {
        if (recordHtml && text.Length > 0)
            NoteHtml(text);
        if (replace is not null)
        {
            _displayHoldTruncated = true;
            _displayHoldBase = replace;
            _displayHold.Clear();
        }
        if (text.Length > 0)
            _displayHold.Append(text);
    }

    private string FoldQueueIntoHold()
    {
        TakePending(out string text, out string? replace);
        if (text.Length > 0 || replace is not null)
            AbsorbDisplayHold(text, replace);
        return text;
    }

    private void DiscardUnpainted()
    {
        lock (_displayLock)
        {
            _pendingDisplay.Clear();
            _pendingReplace = null;
            _drainScheduled = false;
        }
        _displayTimer.Stop();
        _displayHoldTruncated = false;
        _displayHoldBase = null;
        _displayHold.Clear();
    }

    /// <summary>
    /// 窗口里能看到的，加上停止显示还没画上的。发生过截断时，冻结的旧画面已经不算数。
    /// </summary>
    private string LogicalWindowText()
    {
        CompletePendingRenderLog();
        if (_displayPaused) FoldQueueIntoHold();
        string raw = _displayPaused && _displayHoldTruncated
            ? (_displayHoldBase ?? "") + _displayHold.ToString()
            : _recvBox.Text + (_displayPaused ? _displayHold.ToString() : "");
        return raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
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
        if (_displayPaused) FoldQueueIntoHold();
        bool hidden = _displayPaused && (_displayHold.Length > 0 || _displayHoldTruncated);
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
            ShowToast("未找到「" + shown + "」" + (hidden ? "。未显示的内容不在搜索范围内" : ""), ToastTone.Warning);
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
        var rtf = new ToolStripMenuItem("另存为带颜色日志（RTF）...");
        rtf.Click += (_, _) => SaveColoredLogRtf();
        var html = new ToolStripMenuItem("另存为带颜色日志（HTML）...");
        html.Click += (_, _) => SaveColoredLogHtml();
        menu.Items.AddRange(new ToolStripItem[]
        {
            copy, plain, new ToolStripSeparator(), all, clear, new ToolStripSeparator(), rtf, html,
        });
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
        _displayPaused = false;
        SyncDisplayPauseButton();
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
    internal bool DisplayPausedForTest => _displayPaused;
    internal void SetDisplayPausedForTest(bool paused)
    {
        if (paused == _displayPaused) return;
        if (paused) PauseDisplay();
        else ResumeDisplay();
    }
    internal void SetDisplayLimitForTest(int limit)
    {
        _displayLimit = limit;
        _receive.Limit = limit;
    }
    internal string LogicalWindowTextForTest() => LogicalWindowText();
    internal void SimulateIncomingForTest(byte[] data)
    {
        bool file = _snapRecvToFile;
        _snapRecvToFile = false;
        try
        {
            HandleIncoming(data);
            DrainDisplay();
        }
        finally
        {
            _snapRecvToFile = file;
        }
    }
    internal void ClearReceiveForTest() => ClearReceive();

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
