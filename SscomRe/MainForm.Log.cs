using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>接收日志的分色追加、整屏重画、搜索/暂停跟随和复制。</summary>
public sealed partial class MainForm
{
    private const int WM_SETREDRAW = 0x000B;

    private Color LogColorOf(LogFormatter.Kind kind) => kind switch
    {
        LogFormatter.Kind.Timestamp => Theme.Dim,
        LogFormatter.Kind.Rx => Theme.Accent,
        LogFormatter.Kind.Tx => Theme.StatusSend,
        LogFormatter.Kind.Error => Theme.Danger,
        LogFormatter.Kind.Hex => Theme.Accent,
        _ => _recvBox.ForeColor,
    };

    private void AppendFormatted(string text)
    {
        List<LogFormatter.Line> lines = LogFormatter.Parse(text, _hexDisplayBox.Checked);
        Color defaultColor = _recvBox.ForeColor;
        Color defaultBack = _recvBox.BackColor;
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
        int txHl = ColorIndex(Theme.TxWash);
        int errorHl = ColorIndex(Theme.ErrorWash);

        // 一次构造整批 RTF，再用 SelectedRtf 单次插入；逐段 SelectionColor/AppendText 在长日志上是 O(n²)。
        var body = new StringBuilder(Math.Max(64, text.Length * 2));
        bool first = true;
        foreach (LogFormatter.Line line in lines)
        {
            if (!first)
            {
                body.Append(@"{\cf").Append(defaultCf)
                    .Append(@"\highlight").Append(defaultHl).Append(@"\line}");
            }
            first = false;

            int back = line.Error ? errorHl : line.Tx ? txHl : defaultHl;
            foreach (LogFormatter.Span span in line.Spans)
            {
                Color color = LogColorOf(span.Kind);
                if (line.Tx && span.Kind == LogFormatter.Kind.Data)
                    color = Theme.StatusSend;
                body.Append(@"{\cf").Append(ColorIndex(color))
                    .Append(@"\highlight").Append(back).Append(' ');
                AppendRtfText(body, span.Text);
                body.Append('}');
            }
        }

        var rtf = new StringBuilder(body.Length + 256);
        rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset")
            .Append(Math.Max(0, (int)_recvBox.Font.GdiCharSet)).Append(' ')
            .Append(RtfFontName(_recvBox.Font.Name)).Append(@";}}");
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

    private static string RtfFontName(string name) =>
        name.Replace(@"\", "").Replace("{", "").Replace("}", "").Replace(";", " ");

    private static void AppendRtfText(StringBuilder sb, string text)
    {
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\t': sb.Append(@"\tab "); break;
                case '\r': break;
                case '\n': sb.Append(@"\line "); break;
                default:
                    if (ch < 32 || ch > 126) sb.Append(@"\u").Append((short)ch).Append('?');
                    else sb.Append(ch);
                    break;
            }
        }
    }

    private void RenderLogFromText(string text)
    {
        if (!_recvBox.IsHandleCreated)
        {
            _recvBox.Text = text;
            return;
        }
        bool atBottom = RecvAtBottom();
        int selStart = _recvBox.SelectionStart;
        int selLen = _recvBox.SelectionLength;
        Point scroll = GetRecvScroll();
        ClearSearchHighlight();

        SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            _recvBox.Clear();
            if (!string.IsNullOrEmpty(text)) AppendFormatted(text);
        }
        finally
        {
            SendMessage(_recvBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _recvBox.Invalidate();
        }

        if (_autoFollow && atBottom)
        {
            _recvBox.SelectionStart = _recvBox.TextLength;
            _recvBox.SelectionLength = 0;
            _recvBox.ScrollToCaret();
        }
        else
        {
            int start = Math.Min(selStart, _recvBox.TextLength);
            int len = Math.Min(selLen, Math.Max(0, _recvBox.TextLength - start));
            _recvBox.Select(start, len);
            SetRecvScroll(scroll);
        }
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

    private void ToggleFollow()
    {
        _autoFollow = !_autoFollow;
        if (_pauseButton is not null)
        {
            _pauseButton.Text = _autoFollow ? "暂停" : "继续";
            _pauseButton.Kind = _autoFollow ? ThemeButton.ButtonKind.Default : ThemeButton.ButtonKind.Outline;
            _pauseButton.Invalidate();
        }
        if (_autoFollow)
        {
            _recvBox.SelectionStart = _recvBox.TextLength;
            _recvBox.SelectionLength = 0;
            _recvBox.ScrollToCaret();
        }
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
        if (IsDisposed || _sparkline is null) return;
        long sent = Interlocked.Read(ref _sentCount);
        long recv = Interlocked.Read(ref _recvCount);
        if (sent < _lastSentSample) _lastSentSample = sent;
        if (recv < _lastRecvSample) _lastRecvSample = recv;
        double seconds = Math.Max(0.1, _rateTimer.Interval / 1000.0);
        double tx = (sent - _lastSentSample) / seconds;
        double rx = (recv - _lastRecvSample) / seconds;
        _lastSentSample = sent;
        _lastRecvSample = recv;

        _rxRateSamples.Add((float)rx);
        while (_rxRateSamples.Count > 24) _rxRateSamples.RemoveAt(0);
        _sparkline.SetValues(_rxRateSamples.ToArray());
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
        copy.Click += (_, _) => _recvBox.Copy();
        var plain = new ToolStripMenuItem("复制纯数据（去掉时间戳和收/发标记）");
        plain.Click += (_, _) => CopyPlainLog();
        var all = new ToolStripMenuItem("全选");
        all.Click += (_, _) => _recvBox.SelectAll();
        var clear = new ToolStripMenuItem("清除窗口");
        clear.Click += (_, _) => ClearReceive();
        menu.Items.AddRange(new ToolStripItem[] { copy, plain, new ToolStripSeparator(), all, clear });
        Theme.StyleContextMenu(menu);
        return menu;
    }

    // ———————————————————————— 压测钩子（--bench-log） ————————————————————————

    internal void PrepareLogBenchmark(bool hex)
    {
        _displayTimer.Stop();
        ClearReceive();
        HideToast();
        _autoFollow = true;
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

    internal bool SearchForTest(string query)
    {
        if (_searchBox is null) return false;
        _searchBox.Text = query;
        SearchNext(true);
        return _toast is { Visible: true };
    }

    internal void HideToastForTest() => HideToast();

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
