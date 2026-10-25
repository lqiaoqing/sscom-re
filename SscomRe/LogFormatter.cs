using System.Text.RegularExpressions;

namespace SscomRe;

/// <summary>把接收日志的纯文本拆成可着色片段；纯逻辑，便于自检。</summary>
public static class LogFormatter
{
    public enum Kind
    {
        Timestamp,
        Rx,
        Tx,
        Error,
        Data,
        Hex,
        Newline,
    }

    public readonly record struct Span(string Text, Kind Kind);

    public sealed class Line
    {
        public Line(List<Span> spans, bool tx, bool error)
        {
            Spans = spans;
            Tx = tx;
            Error = error;
        }

        public List<Span> Spans { get; }
        public bool Tx { get; }
        public bool Error { get; }
    }

    private static readonly Regex Head = new(
        @"^\[(\d{2}:\d{2}:\d{2}\.\d{3})\](收←◆|发→◇|IN←◆|OUT→◇)?(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex ErrorPattern = new(
        @"(\bERR\b|error|错误|失败|校验失败|checksum|timeout|超时)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<Line> Parse(string text, bool hexMode)
    {
        var result = new List<Line>();
        if (string.IsNullOrEmpty(text)) return result;
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        foreach (string line in lines)
        {
            var spans = new List<Span>();
            Match match = Head.Match(line);
            bool tx = false;
            string data;
            if (match.Success)
            {
                spans.Add(new Span("[" + match.Groups[1].Value + "]", Kind.Timestamp));
                string marker = match.Groups[2].Value;
                if (marker.Length > 0)
                {
                    tx = marker is "发→◇" or "OUT→◇";
                    spans.Add(new Span(marker, tx ? Kind.Tx : Kind.Rx));
                }
                data = match.Groups[3].Value;
            }
            else
            {
                data = line;
            }

            bool error = ErrorPattern.IsMatch(line);
            if (data.Length > 0)
                spans.Add(new Span(data, error ? Kind.Error : hexMode ? Kind.Hex : Kind.Data));
            result.Add(new Line(spans, tx, error));
        }
        return result;
    }

    /// <summary>复制用：去掉时间戳和收/发标记，保留数据行。</summary>
    public static string StripMeta(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match match = Head.Match(lines[i]);
            if (match.Success) lines[i] = match.Groups[3].Value;
        }
        return string.Join("\n", lines);
    }
}
