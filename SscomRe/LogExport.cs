using System.Drawing;
using System.Text;

namespace SscomRe;

/// <summary>
/// 带颜色的日志导出：把当前显示文本按当前规则重新着色，生成 RTF 或 HTML 文档。
/// 纯逻辑，不依赖窗体，便于自检。
/// </summary>
internal static class LogExport
{
    public static string ToRtf(string text, bool hex, LogHighlighter highlighter,
        LogHighlightPalette palette, Font font)
    {
        var colors = new List<Color>();
        int ColorIndex(Color color)
        {
            int i = colors.IndexOf(color);
            if (i >= 0) return i + 1;
            colors.Add(color);
            return colors.Count;
        }

        int defaultCf = ColorIndex(palette.DefaultFore);
        int defaultHl = ColorIndex(palette.DefaultBack);
        var body = new StringBuilder(Math.Max(64, text.Length * 2));
        foreach (StyledLogLine line in StyleLines(text, hex, highlighter, palette))
        {
            foreach (StyledLogRun run in line.Runs)
            {
                if (run.Fore == palette.DefaultFore && run.Back == palette.DefaultBack)
                {
                    AppendRtfText(body, run.Text);
                    continue;
                }
                body.Append(@"{\cf").Append(ColorIndex(run.Fore))
                    .Append(@"\highlight").Append(ColorIndex(run.Back)).Append(' ');
                AppendRtfText(body, run.Text);
                body.Append('}');
            }
            body.Append(@"\par").Append("\r\n");
        }

        var rtf = new StringBuilder(body.Length + 512);
        rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset")
            .Append(Math.Max(0, (int)font.GdiCharSet)).Append(' ')
            .Append(RtfFontName(font.Name)).Append(@";}}");
        rtf.Append(@"{\colortbl ;");
        foreach (Color color in colors)
        {
            rtf.Append(@"\red").Append(color.R)
                .Append(@"\green").Append(color.G)
                .Append(@"\blue").Append(color.B).Append(';');
        }
        rtf.Append(@"}\viewkind4\uc1\pard\f0\fs")
            .Append(Math.Max(1, (int)Math.Round(font.SizeInPoints * 2)))
            .Append(@" \cf").Append(defaultCf)
            .Append(@"\highlight").Append(defaultHl).Append(' ');
        rtf.Append(body).Append('}');
        return rtf.ToString();
    }

    public static string ToHtml(string text, bool hex, LogHighlighter highlighter,
        LogHighlightPalette palette, Font font, string title)
    {
        var html = new StringBuilder(Math.Max(256, text.Length * 2));
        html.Append(HtmlHeader(palette, font.Name, Math.Max(1, (int)Math.Round(font.SizeInPoints)), title));

        bool firstLine = true;
        foreach (StyledLogLine line in StyleLines(text, hex, highlighter, palette))
        {
            if (!firstLine) html.Append('\n');
            firstLine = false;
            AppendHtmlLine(html, line, palette);
        }

        html.Append(HtmlFooter);
        return html.ToString();
    }

    internal const string HtmlFooter = "</pre>\n</body>\n</html>\n";

    internal static string HtmlHeader(LogHighlightPalette palette, string fontFamily, int fontSizePt, string title)
    {
        var html = new StringBuilder(512);
        html.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n<title>").Append(HtmlEscape(title)).Append("</title>\n")
            .Append("<style>\n")
            .Append("body{margin:0;padding:12px;background:#").Append(Hex(palette.DefaultBack)).Append(";}\n")
            .Append("pre{margin:0;white-space:pre-wrap;word-break:break-all;")
            .Append("font-family:'").Append(HtmlEscape(fontFamily)).Append("',Consolas,'Microsoft YaHei UI',monospace;")
            .Append("font-size:").Append(fontSizePt).Append("pt;line-height:1.45;")
            .Append("color:#").Append(Hex(palette.DefaultFore)).Append(";}\n")
            .Append("</style>\n</head>\n<body>\n<pre>");
        return html.ToString();
    }

    internal static void AppendHtmlLine(StringBuilder html, StyledLogLine line, LogHighlightPalette palette)
    {
        foreach (StyledLogRun run in line.Runs)
        {
            bool fore = run.Fore != palette.DefaultFore;
            bool back = run.Back != palette.DefaultBack;
            if (!fore && !back)
            {
                html.Append(HtmlEscape(run.Text));
                continue;
            }
            html.Append("<span style=\"");
            if (fore) html.Append("color:#").Append(Hex(run.Fore)).Append(';');
            if (back) html.Append("background-color:#").Append(Hex(run.Back)).Append(';');
            html.Append("\">").Append(HtmlEscape(run.Text)).Append("</span>");
        }
    }

    private static IEnumerable<StyledLogLine> StyleLines(string text, bool hex,
        LogHighlighter highlighter, LogHighlightPalette palette)
    {
        foreach (LogFormatter.Line line in LogFormatter.Parse(text, hex))
            yield return highlighter.Style(line, palette);
    }

    internal static string RtfFontName(string name) =>
        name.Replace(@"\", "").Replace("{", "").Replace("}", "").Replace(";", " ");

    internal static void AppendRtfText(StringBuilder sb, string text)
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

    private static string Hex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static string HtmlEscape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}