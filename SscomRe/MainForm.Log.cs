using System.Drawing;
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
        bool first = true;
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        foreach (LogFormatter.Line line in lines)
        {
            if (!first)
            {
                _recvBox.SelectionColor = _recvBox.ForeColor;
                _recvBox.SelectionBackColor = _recvBox.BackColor;
                _recvBox.AppendText("\n");
            }
            first = false;

            Color back = line.Error ? Theme.ErrorWash : line.Tx ? Theme.TxWash : _recvBox.BackColor;
            foreach (LogFormatter.Span span in line.Spans)
            {
                Color color = LogColorOf(span.Kind);
                if (line.Tx && span.Kind == LogFormatter.Kind.Data)
                    color = Theme.StatusSend;
                _recvBox.SelectionStart = _recvBox.TextLength;
                _recvBox.SelectionLength = 0;
                _recvBox.SelectionColor = color;
                _recvBox.SelectionBackColor = back;
                _recvBox.AppendText(span.Text);
            }
        }
        _recvBox.SelectionStart = _recvBox.TextLength;
        _recvBox.SelectionLength = 0;
        _recvBox.SelectionColor = _recvBox.ForeColor;
        _recvBox.SelectionBackColor = _recvBox.BackColor;
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
            return;
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
        try { Clipboard.SetText(LogFormatter.StripMeta(text)); } catch { /* 剪贴板被占用时忽略 */ }
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
}
