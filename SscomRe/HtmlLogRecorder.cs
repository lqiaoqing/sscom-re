using System.Drawing;
using System.Text;

namespace SscomRe;

/// <summary>
/// 持续记录带颜色的日志为 HTML 分卷。每卷是完整可独立打开的文档；
/// 只在界面线程调用，规则/调色板在开始记录时快照。
/// </summary>
internal sealed class HtmlLogRecorder : IDisposable
{
    private const int WriterBuffer = 64 * 1024;
    private const string FontFamily = "Consolas";
    private const int FontSizePt = 10;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _piece;
    private readonly int _splitBytes;
    private readonly LogHighlightPalette _palette;
    private readonly bool _hexMode;
    private readonly bool _includeTx;
    private readonly LogHighlighter _highlighter = new();
    private readonly string _sessionStamp;
    private StreamWriter? _writer;
    private string _filePath = "";
    private long _fileBytes;
    private long _totalBytes;
    private int _linesInFile;
    private int _part;
    private bool _disposed;

    public HtmlLogRecorder(string directory, string piece, int splitBytes,
        LogHighlightPalette palette, bool hexMode, bool includeTx, IEnumerable<LogHighlightRule> rules)
    {
        _directory = directory;
        _piece = SanitizePiece(piece);
        _splitBytes = Math.Max(0, splitBytes);
        _palette = palette;
        _hexMode = hexMode;
        _includeTx = includeTx;
        _sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        _highlighter.SetRules(rules.Select(rule => rule.Clone()));
    }

    public string CurrentFilePath { get { lock (_gate) return _filePath; } }
    public int PartCount { get { lock (_gate) return _part; } }
    public long TotalBytes { get { lock (_gate) return _totalBytes; } }
    public string? LastError { get; private set; }

    /// <summary>创建目录和第一卷；失败时抛出，由调用方提示并取消勾选。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (!_disposed) OpenPart();
        }
    }

    public void Append(string text) => AppendCore(text, allowTx: false);

    /// <summary>发送数据由 WriteChannel 直接调用，不依赖“加时间戳”是否开启显示回显。</summary>
    public void AppendTx(string content)
    {
        if (!_includeTx || string.IsNullOrEmpty(content)) return;
        AppendCore($"[{DateTime.Now:HH:mm:ss.fff}]发→◇{content}\r\n", allowTx: true);
    }

    private void AppendCore(string text, bool allowTx)
    {
        if (_disposed || string.IsNullOrEmpty(text)) return;
        lock (_gate)
        {
            if (_writer is null || LastError is not null) return;
            try
            {
                foreach (LogFormatter.Line line in LogFormatter.Parse(text, _hexMode))
                {
                    if (!allowTx && line.Tx) continue;
                    var html = new StringBuilder(160);
                    LogExport.AppendHtmlLine(html, _highlighter.Style(line, _palette), _palette);
                    html.Append('\n');
                    string chunk = html.ToString();
                    int bytes = Encoding.UTF8.GetByteCount(chunk);
                    if (_splitBytes > 0 && _linesInFile > 0 && _fileBytes + bytes > _splitBytes)
                        Rotate();
                    if (_writer is null) return;
                    _writer.Write(chunk);
                    _fileBytes += bytes;
                    _totalBytes += bytes;
                    _linesInFile++;
                }
                _writer.Flush();                 // 每批落盘，记录中可直接用浏览器查看
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                CloseWriter();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            WriteFooter();
            CloseWriter();
        }
    }

    private void Rotate()
    {
        WriteFooter();
        CloseWriter();
        OpenPart();
    }

    private void OpenPart()
    {
        if (_disposed) return;
        Directory.CreateDirectory(_directory);
        _part++;
        string baseName = $"ReceivedHtml-{_piece}-{_sessionStamp}-p{_part:000}";
        string path = Path.Combine(_directory, baseName + ".html");
        int suffix = 2;
        while (File.Exists(path))
            path = Path.Combine(_directory, baseName + "-" + suffix++ + ".html");

        _filePath = path;
        _fileBytes = 0;
        _linesInFile = 0;
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), WriterBuffer) { NewLine = "\n" };
        string header = LogExport.HtmlHeader(_palette, FontFamily, FontSizePt,
            $"sscomRe 日志 - {_piece} - p{_part:000}");
        _writer.Write(header);
        _fileBytes = Encoding.UTF8.GetByteCount(header);
    }

    private void WriteFooter()
    {
        if (_writer is null) return;
        try
        {
            _writer.Write(LogExport.HtmlFooter);
            _writer.Flush();
        }
        catch (Exception ex)
        {
            LastError ??= ex.Message;
        }
    }

    private void CloseWriter()
    {
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _linesInFile = 0;
    }

    private static string SanitizePiece(string piece)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(piece.Length);
        foreach (char ch in piece)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.Length == 0 ? "LOG" : sb.ToString();
    }
}