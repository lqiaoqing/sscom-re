using System.Globalization;
using System.Text;

namespace SscomRe;

/// <summary>N1080 读入后的端口选择。1 和 2 都是 TCPClient，3 是 UDP，其余整串是端口名。</summary>
public enum PortKind
{
    Serial,
    TcpClient,
    Udp,
}

/// <summary>
/// sscom51.ini 的文本格式（规格「保存参数与导入 ini」）。
/// 行尾 CRLF。行数据里的逗号、CR、LF 写成 0x02、0x03、0x04。设置行是 N键=,&lt;值&gt;。
/// </summary>
public static class IniDocument
{
    public const string CommentHex = ";\"=\"后面的H表示这是个HEX数据串";
    public const string CommentNx = ";Nx表示第几条定义的字符串(1<x<=N)";
    public const string SaveFailedMessage = "无法保存ini文件!请拷贝exe文件到且它目录试试.";
    public const string ReadFailedMessage = "文件读取失败!";

    public const int MultiPageCount = 3;
    public const int MultiRowsPerPage = 99;
    public const int MultiRowCount = MultiPageCount * MultiRowsPerPage;

    private static readonly Encoding Gbk = CreateGbk();

    public static string CurrentPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), "sscom51.ini");

    public static string Save(IniSnapshot snap)
    {
        var sb = new StringBuilder();
        sb.Append(CommentHex).Append("\r\n");
        sb.Append(CommentNx).Append("\r\n");

        for (int page = 0; page < MultiPageCount; page++)
        {
            if (page > 0)
                sb.Append(";第").Append(page + 1).Append("页\r\n");
            for (int k = 1; k <= MultiRowsPerPage; k++)
            {
                int i = page * MultiRowsPerPage + (k - 1);
                sb.Append('N').Append(MultiMetaKey(page, k)).Append('=')
                    .Append(Esc(snap.Order[i])).Append(',')
                    .Append(Esc(snap.Comment[i])).Append(',')
                    .Append(Esc(snap.Delay[i])).Append("\r\n");
                sb.Append('N').Append(MultiDataKey(page, k)).Append('=')
                    .Append(snap.Hex[i] ? 'H' : 'A').Append(',')
                    .Append(Esc(snap.Data[i])).Append("\r\n");
            }
        }

        Setting(sb, "发送文件时每256字节延时时间ms", 1051, snap.SendFileDelayMs.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "打开文件地址和名称", 1052, snap.FilePath);
        Setting(sb, "主面板ASC字符串", 1053, snap.AscText);
        Setting(sb, "主面板HEX数据串", 1054, snap.HexText);
        Setting(sb, "主面板发送方式(ASC or HEX)", 1055, Bit(snap.HexSend));
        Setting(sb, "主面板字符串发送间隔时间ms", 1056, snap.TimerInterval);
        Setting(sb, "主面板字符串发送新行", 1057, Bit(snap.AddCrLf));
        Setting(sb, "多条字符串发送间隔时间ms", 1058, "1000");
        Setting(sb, "接收窗口是否HEX显示方式", 1059, Bit(snap.HexDisplay));
        Setting(sb, "校验方式", 1060, snap.VerifyMode.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "保存DTR", 1061, Bit(snap.Dtr));
        Setting(sb, "保存RTS", 1062, Bit(snap.Rts));
        Setting(sb, "程序启动时是否打开串口", 1063, Bit(snap.OpenImmediately));
        Setting(sb, "是否分包显示", 1064, Bit(snap.Timestamp));
        Setting(sb, "分包超时时间ms", 1065, snap.TimeoutMs);
        Setting(sb, "接收窗背景颜色", 1066, snap.MemoBackColor.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "显示缓冲上限", 1067, snap.DisplayLimit.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "远程IP地址", 1068, snap.RemoteIp);
        Setting(sb, "远程端口", 1069, snap.RemotePort);
        Setting(sb, "本地端口", 1070, snap.LocalPort);
        Setting(sb, "回车后发送行", 1071, Bit(snap.TermEnter));
        Setting(sb, "按键立即发送", 1072, Bit(snap.TermKey));
        Setting(sb, "回显", 1073, Bit(snap.TermEcho));
        Setting(sb, "第几字节至末尾加校验", 1074, snap.VerifyFrom);
        Setting(sb, "至末尾倒数第几字节加校验", 1075, snap.TailReserve.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "终端仿真输入回车后是否自动加换行", 1076, Bit(snap.TermAddLf));
        Setting(sb, "当前串口号或网络模式", 1080, snap.PortValue);
        Setting(sb, "波特率", 1081, snap.Baud);
        Setting(sb, "窗口宽", 1082, snap.FormWidth.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "窗口高", 1083, snap.FormHeight.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "窗口left", 1084, snap.FormLeft.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "窗口top", 1085, snap.FormTop.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "分割线位置", 1086, snap.MultiPanelWidth.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "多条发送按钮的宽度", 1087, snap.MultiButtonWidth.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "窗口字体名称", 1088, snap.FontName);
        Setting(sb, "窗口字体大小", 1089, snap.FontSize);
        Setting(sb, "窗口字体颜色", 1090, snap.FontColor.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "窗口字体样式", 1091, snap.FontStyle.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "主题(0=深色,1=浅色)", 1092, snap.ThemeLight ? "1" : "0");
        Setting(sb, "连接栏折叠(0=展开,1=折叠)", 1093, snap.SideCollapsed ? "1" : "0");
        Setting(sb, "多条字符串页数(1-3)", 1094, snap.MultiPageCount.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "自动滚动到最新(0=否,1=是)", 1095, Bit(snap.AutoScroll));
        string rulesJson = snap.HighlightRulesJson;
        Setting(sb, "日志高亮规则(base64 JSON)", 1096,
            rulesJson.Length == 0 ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(rulesJson)));
        Setting(sb, "HTML日志记录目录", 1097, snap.RecvHtmlDir);
        Setting(sb, "HTML日志分卷MB(0=不分卷)", 1098, snap.RecvHtmlSplitMb.ToString(CultureInfo.InvariantCulture));
        Setting(sb, "HTML日志浅色调色板", 1099, Bit(snap.RecvHtmlLight));
        Setting(sb, "C:Chinese汉语, E:English", 1100, "C");
        Setting(sb, "HTML日志含发送回显", 1101, Bit(snap.RecvHtmlIncludeTx));
        Setting(sb, "HTML日志设置版本", 1102,
            snap.RecvHtmlSettingsVersion.ToString(CultureInfo.InvariantCulture));
        sb.Append(";end\r\n");
        return sb.ToString();
    }

    public static IniSnapshot Load(string text)
    {
        var snap = new IniSnapshot();
        if (string.IsNullOrEmpty(text)) return snap;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string raw in normalized.Split('\n'))
        {
            string line = raw;
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] != 'N' && line[0] != 'n') continue;
            int eq = line.IndexOf('=');
            if (eq <= 1) continue;
            if (!int.TryParse(line.Substring(1, eq - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
                continue;
            string payload = line.Substring(eq + 1);
            string[] parts = payload.Split(',');

            if (TryMapMultiKey(key, out int index, out bool meta))
            {
                if (!meta)
                {
                    snap.Hex[index] = parts.Length > 0 && Unesc(parts[0]) == "H";
                    snap.Data[index] = parts.Length > 1 ? Unesc(string.Join(",", parts.Skip(1))) : "";
                }
                else
                {
                    string orderText = parts.Length > 0 ? Unesc(parts[0]) : "";
                    int order = int.TryParse(orderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int o)
                        ? Fx.Clamp(o, 0, 99) : 0;
                    snap.Order[index] = order.ToString(CultureInfo.InvariantCulture);
                    if (parts.Length >= 3)
                        snap.Comment[index] = Unesc(string.Join(",", parts.Skip(1).Take(parts.Length - 2)));
                    else if (parts.Length == 2)
                        snap.Comment[index] = Unesc(parts[1]);
                    string delayText = parts.Length >= 3 ? Unesc(parts[parts.Length - 1]) : "";
                    snap.Delay[index] = int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)
                        ? d.ToString(CultureInfo.InvariantCulture) : "1000";
                }
                continue;
            }

            string value = parts.Length >= 2 ? Unesc(string.Join(",", parts.Skip(1))) : Unesc(payload);
            ApplySetting(snap, key, value);
        }
        return snap;
    }

    /// <summary>
    /// 第 1 页沿用原来的 N1–N99（数据）和 N101–N199（顺序、注释、延时）。
    /// 第 2 页是 N201–N299 / N301–N399，第 3 页是 N401–N499 / N501–N599。
    /// </summary>
    public static int MultiDataKey(int page, int row) => page switch
    {
        0 => row,
        1 => 200 + row,
        _ => 400 + row,
    };

    public static int MultiMetaKey(int page, int row) => page switch
    {
        0 => 100 + row,
        1 => 300 + row,
        _ => 500 + row,
    };

    public static bool TryMapMultiKey(int key, out int index, out bool meta)
    {
        for (int page = 0; page < MultiPageCount; page++)
        {
            int dataBase = MultiDataKey(page, 0);
            int metaBase = MultiMetaKey(page, 0);
            if (key > dataBase && key <= dataBase + MultiRowsPerPage)
            {
                index = page * MultiRowsPerPage + (key - dataBase - 1);
                meta = false;
                return true;
            }
            if (key > metaBase && key <= metaBase + MultiRowsPerPage)
            {
                index = page * MultiRowsPerPage + (key - metaBase - 1);
                meta = true;
                return true;
            }
        }
        index = 0;
        meta = false;
        return false;
    }

    /// <summary>整段按整数解析。0 或解析失败时整串是端口名；1 和 2 都选 TCPClient；3 选 UDP。</summary>
    public static (PortKind Kind, string PortName) ParsePortMode(string value)
    {
        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n != 0)
        {
            if (n == 3) return (PortKind.Udp, "");
            if (n is 1 or 2) return (PortKind.TcpClient, "");
        }
        return (PortKind.Serial, value);
    }

    public static void WriteFile(string path, IniSnapshot snap)
    {
        File.WriteAllText(path, Save(snap), Gbk);
    }

    public static string ReadFile(string path) => File.ReadAllText(path, Gbk);

    /// <summary>启动早期只取主题偏好，避免先按深色建好控件再整体换色。</summary>
    public static bool ReadThemePreference(string path)
    {
        try
        {
            return File.Exists(path) && Load(ReadFile(path)).ThemeLight;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplySetting(IniSnapshot snap, int key, string value)
    {
        switch (key)
        {
            case 1051:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay))
                    snap.SendFileDelayMs = delay;
                break;
            case 1052: snap.FilePath = value; break;
            case 1053: snap.AscText = value; break;
            case 1054: snap.HexText = value; break;
            case 1055: snap.HexSend = IsOn(value); break;
            case 1056: snap.TimerInterval = value; break;
            case 1057: snap.AddCrLf = IsOn(value); break;
            case 1058:
                // 重写没有隐藏的 Edit35。读到的值不写进主发送间隔。
                break;
            case 1059: snap.HexDisplay = IsOn(value); break;
            case 1060:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode))
                    snap.VerifyMode = mode;
                break;
            case 1061: snap.Dtr = IsOn(value); break;
            case 1062: snap.Rts = IsOn(value); break;
            case 1063: snap.OpenImmediately = IsOn(value); break;
            case 1064: snap.Timestamp = IsOn(value); break;
            case 1065: snap.TimeoutMs = value; break;
            case 1066:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int back))
                    snap.MemoBackColor = back;
                break;
            case 1067:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
                    snap.DisplayLimit = limit;
                break;
            case 1068: snap.RemoteIp = value; break;
            case 1069: snap.RemotePort = value; break;
            case 1070: snap.LocalPort = value; break;
            case 1071: snap.TermEnter = IsOn(value); break;
            case 1072: snap.TermKey = IsOn(value); break;
            case 1073: snap.TermEcho = IsOn(value); break;
            case 1074: snap.VerifyFrom = value; break;
            case 1075:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tail))
                    snap.TailReserve = tail;
                break;
            case 1076: snap.TermAddLf = IsOn(value); break;
            case 1080: snap.PortValue = value; break;
            case 1081: snap.Baud = value; break;
            case 1082: snap.FormWidth = ParseInt(value, snap.FormWidth); break;
            case 1083: snap.FormHeight = ParseInt(value, snap.FormHeight); break;
            case 1084: snap.FormLeft = ParseInt(value, snap.FormLeft); break;
            case 1085: snap.FormTop = ParseInt(value, snap.FormTop); break;
            case 1086: snap.MultiPanelWidth = ParseInt(value, 515); break;
            case 1087: snap.MultiButtonWidth = ParseInt(value, 120); break;
            case 1088: snap.FontName = value; break;
            case 1089: snap.FontSize = value; break;
            case 1090:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fc))
                    snap.FontColor = fc;
                break;
            case 1091:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fontStyle))
                    snap.FontStyle = fontStyle;
                break;
            case 1092: snap.ThemeLight = IsOn(value); break;
            case 1093: snap.SideCollapsed = IsOn(value); break;
            case 1094: snap.MultiPageCount = Fx.Clamp(ParseInt(value, 1), 1, MultiPageCount); break;
            case 1095: snap.AutoScroll = IsOn(value); break;
            case 1096:
                try
                {
                    snap.HighlightRulesJson = value.Length == 0
                        ? ""
                        : Encoding.UTF8.GetString(Convert.FromBase64String(value));
                }
                catch
                {
                    snap.HighlightRulesJson = "";
                }
                break;
            case 1100:
                snap.Language = value;
                break;
            case 1097: snap.RecvHtmlDir = value; break;
            case 1098: snap.RecvHtmlSplitMb = Math.Max(0, ParseInt(value, 50)); break;
            case 1099: snap.RecvHtmlLight = IsOn(value); break;
            case 1101: snap.RecvHtmlIncludeTx = IsOn(value); break;
            case 1102: snap.RecvHtmlSettingsVersion = Math.Max(0, ParseInt(value, 0)); break;
        }
    }

    private static void Setting(StringBuilder sb, string comment, int key, string value)
    {
        sb.Append(';').Append(comment).Append("\r\n");
        sb.Append('N').Append(key).Append("=,").Append(Esc(value)).Append("\r\n");
    }

    private static string Bit(bool on) => on ? "1" : "0";

    private static bool IsOn(string value) => value is "1" or "True" or "true";

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    private static string Esc(string s) =>
        s.Replace("\u0002", "0x02").Replace("\u0003", "0x03").Replace("\u0004", "0x04")
            .Replace(",", "0x02").Replace("\r", "0x03").Replace("\n", "0x04");

    private static string Unesc(string s) =>
        s.Replace("0x04", "\n").Replace("0x03", "\r").Replace("0x02", ",")
            .Replace("\u0004", "\n").Replace("\u0003", "\r").Replace("\u0002", ",");

    private static Encoding CreateGbk() => Encoding.GetEncoding(936);
}

/// <summary>一份和界面无关的 ini 数据。缺文件或没写到的键用这里的默认值。</summary>
public sealed class IniSnapshot
{
    public IniSnapshot()
    {
        int n = IniDocument.MultiRowCount;
        Order = Enumerable.Repeat("0", n).ToArray();
        Comment = new string[n];
        for (int i = 0; i < n; i++)
            Comment[i] = ((i % IniDocument.MultiRowsPerPage) + 1).ToString(CultureInfo.InvariantCulture);
        Delay = Enumerable.Repeat("1000", n).ToArray();
        Hex = new bool[n];
        Data = Enumerable.Repeat("", n).ToArray();
    }

    public string[] Order { get; }
    public string[] Comment { get; }
    public string[] Delay { get; }
    public bool[] Hex { get; }
    public string[] Data { get; }

    public int SendFileDelayMs { get; set; } = 1;
    public string FilePath { get; set; } = "";
    public string AscText { get; set; } = "abcdefg";
    public string HexText { get; set; } = "12 FF 00 30";
    public bool HexSend { get; set; }
    public string TimerInterval { get; set; } = "1000";
    public bool AddCrLf { get; set; }
    public bool HexDisplay { get; set; }
    public int VerifyMode { get; set; }
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
    public bool OpenImmediately { get; set; }
    public bool Timestamp { get; set; } = true;
    public string TimeoutMs { get; set; } = "20";
    public int MemoBackColor { get; set; } = Theme.Log.ToArgb();
    public int DisplayLimit { get; set; } = 1_000_000;
    public string RemoteIp { get; set; } = "";
    public string RemotePort { get; set; } = "8080";
    public string LocalPort { get; set; } = "8080";
    public bool TermEnter { get; set; }
    public bool TermKey { get; set; }
    public bool TermEcho { get; set; }
    public string VerifyFrom { get; set; } = "1";
    public int TailReserve { get; set; }
    public bool TermAddLf { get; set; }
    public string PortValue { get; set; } = "COM1";
    public string Baud { get; set; } = "115200";
    public int FormWidth { get; set; } = 700;
    public int FormHeight { get; set; } = 600;
    public int FormLeft { get; set; } = 250;
    public int FormTop { get; set; } = 180;
    public int MultiPanelWidth { get; set; } = 515;
    public int MultiButtonWidth { get; set; } = 120;
    public string FontName { get; set; } = "Consolas";
    public string FontSize { get; set; } = "10";
    public int FontColor { get; set; } = Theme.Text.ToArgb();
    /// <summary>FontStyle 标志：1 粗体、2 斜体、4 下划线、8 删除线。原程序不记这项，缺省为 0。</summary>
    public int FontStyle { get; set; }
    /// <summary>N1092：0 深色、1 浅色；这是重写版新增键，原版没有。</summary>
    public bool ThemeLight { get; set; }
    /// <summary>N1093：连接栏 0 展开、1 折叠；重写版新增键。</summary>
    public bool SideCollapsed { get; set; }
    /// <summary>N1094：可见多条字符串页数 1–3；重写版新增键，缺省只显示第 1 页。</summary>
    public int MultiPageCount { get; set; } = 1;
    /// <summary>N1095：接收日志是否自动滚动到最新；重写版新增键，缺省开启。</summary>
    public bool AutoScroll { get; set; } = true;
    /// <summary>N1096：日志高亮规则 JSON；ini 内以 UTF-8 Base64 存放。</summary>
    public string HighlightRulesJson { get; set; } = "";
    /// <summary>N1097：持续 HTML 记录目录；空表示程序当前目录。</summary>
    public string RecvHtmlDir { get; set; } = "";
    /// <summary>N1098：持续 HTML 记录分卷大小 MB；0 表示不分卷。</summary>
    public int RecvHtmlSplitMb { get; set; } = 50;
    /// <summary>N1099：持续 HTML 记录使用浅色文档调色板。</summary>
    public bool RecvHtmlLight { get; set; } = true;
    /// <summary>N1101：持续 HTML 记录包含发送回显。</summary>
    public bool RecvHtmlIncludeTx { get; set; } = true;
    /// <summary>N1102：持续 HTML 记录设置版本；缺省 0 表示旧配置（10MB 默认）。</summary>
    public int RecvHtmlSettingsVersion { get; set; }
    public string Language { get; set; } = "C";
}
