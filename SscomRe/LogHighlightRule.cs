using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SscomRe;

/// <summary>一条日志高亮规则。列表顺序就是优先级，靠前的规则先命中。</summary>
public sealed class LogHighlightRule
{
    public string Name { get; set; } = "";
    /// <summary>预置规则标识；用于升级旧配置时避免重复添加，用户自建规则为空。</summary>
    public string PresetKey { get; set; } = "";
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public bool WholeLine { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Rx { get; set; } = true;
    public bool Tx { get; set; } = true;
    /// <summary>ARGB；A=0 表示不覆盖原有文字/背景色。</summary>
    public int ForeColor { get; set; }
    /// <summary>ARGB；A=0 表示不覆盖背景。</summary>
    public int BackColor { get; set; }

    public LogHighlightRule Clone() => (LogHighlightRule)MemberwiseClone();
}

public readonly record struct LogHighlightPalette(
    Color DefaultFore,
    Color DefaultBack,
    Color Timestamp,
    Color Rx,
    Color Tx,
    Color Hex,
    Color TxBack);

public sealed class StyledLogLine
{
    public StyledLogLine(List<StyledLogRun> runs, Color back, bool matched)
    {
        Runs = runs;
        Back = back;
        Matched = matched;
    }

    public List<StyledLogRun> Runs { get; }
    public Color Back { get; }
    public bool Matched { get; }
}

public readonly record struct StyledLogRun(string Text, Color Fore, Color Back);

/// <summary>关键字/正则匹配器 + 字符级着色。纯逻辑，不依赖窗体。</summary>
public sealed class LogHighlighter
{
    private sealed record CompiledRule(LogHighlightRule Rule, Regex? Regex, string Keyword);
    private readonly List<CompiledRule> _rules = new();
    private readonly List<string> _errors = new();

    public int ActiveCount => _rules.Count;
    public IReadOnlyList<string> Errors => _errors;

    public void SetRules(IEnumerable<LogHighlightRule> rules)
    {
        _rules.Clear();
        _errors.Clear();
        foreach (LogHighlightRule rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern) || (!rule.Rx && !rule.Tx))
                continue;
            if (rule.IsRegex)
            {
                try
                {
                    var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
                    if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;
                    _rules.Add(new CompiledRule(rule, new Regex(rule.Pattern, options, TimeSpan.FromMilliseconds(50)), ""));
                }
                catch (ArgumentException ex)
                {
                    _errors.Add($"{rule.Name}: {ex.Message}");
                }
            }
            else
            {
                _rules.Add(new CompiledRule(rule, null, rule.Pattern));
            }
        }
    }

    /// <summary>整批预筛：任意规则命中即可，不需要按行构造样式。</summary>
    public bool HasAnyMatch(string text)
    {
        if (text.Length == 0) return false;
        foreach (CompiledRule rule in _rules)
        {
            if (rule.Rule.IsRegex)
            {
                if (rule.Regex is null) continue;
                try
                {
                    if (rule.Regex.IsMatch(text)) return true;
                }
                catch (RegexMatchTimeoutException)
                {
                    // 本规则忽略，继续看其他规则
                }
            }
            else if (text.IndexOf(rule.Keyword,
                rule.Rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    public StyledLogLine Style(LogFormatter.Line line, LogHighlightPalette palette)
    {
        Color baseBack = line.Tx ? palette.TxBack : palette.DefaultBack;
        string full = string.Concat(line.Spans.Select(span => span.Text));
        var baseRuns = BuildBaseRuns(line, palette, baseBack);

        if (full.Length == 0 || _rules.Count == 0)
            return new StyledLogLine(baseRuns, baseBack, false);

        var ruleHits = new List<(int RuleIndex, int Start, int Length)>();
        for (int i = 0; i < _rules.Count; i++)
        {
            CompiledRule rule = _rules[i];
            if (line.Tx ? !rule.Rule.Tx : !rule.Rule.Rx) continue;
            foreach ((int start, int length) in FindHits(full, rule))
            {
                if (length > 0) ruleHits.Add((i, start, length));
                if (ruleHits.Count >= 4000) break;
            }
        }
        if (ruleHits.Count == 0)
            return new StyledLogLine(baseRuns, baseBack, false);

        var owner = new int[full.Length];
        for (int i = 0; i < owner.Length; i++) owner[i] = -1;
        foreach ((int ruleIndex, int start, int length) in ruleHits)
        {
            for (int i = start; i < start + length && i < owner.Length; i++)
                if (owner[i] < 0) owner[i] = ruleIndex;
        }

        Color lineBack = baseBack;
        foreach (var hit in ruleHits)
        {
            LogHighlightRule rule = _rules[hit.RuleIndex].Rule;
            if (rule.WholeLine)
            {
                lineBack = Blend(rule.BackColor, baseBack);
                break;
            }
        }
        var runs = new List<StyledLogRun>();
        int offset = 0;
        foreach (LogFormatter.Span span in line.Spans)
        {
            string text = span.Text;
            int spanStart = offset;
            offset += text.Length;
            Color baseFore = BaseFore(span.Kind, line, palette);
            int i = 0;
            while (i < text.Length)
            {
                int ruleIndex = owner[spanStart + i];
                int j = i + 1;
                while (j < text.Length && owner[spanStart + j] == ruleIndex) j++;
                Color fore = baseFore;
                Color back = lineBack;
                if (ruleIndex >= 0)
                {
                    LogHighlightRule rule = _rules[ruleIndex].Rule;
                    if (Color.FromArgb(rule.ForeColor).A > 0)
                        fore = Color.FromArgb(255, Color.FromArgb(rule.ForeColor));
                    if (!rule.WholeLine && Color.FromArgb(rule.BackColor).A > 0)
                        back = Blend(rule.BackColor, lineBack);
                }
                runs.Add(new StyledLogRun(text.Substring(i, j - i), fore, back));
                i = j;
            }
        }
        return new StyledLogLine(runs, lineBack, true);
    }

    private static List<StyledLogRun> BuildBaseRuns(LogFormatter.Line line, LogHighlightPalette palette, Color back)
    {
        var runs = new List<StyledLogRun>(line.Spans.Count);
        foreach (LogFormatter.Span span in line.Spans)
            runs.Add(new StyledLogRun(span.Text, BaseFore(span.Kind, line, palette), back));
        return runs;
    }

    private static Color BaseFore(LogFormatter.Kind kind, LogFormatter.Line line, LogHighlightPalette palette) => kind switch
    {
        LogFormatter.Kind.Timestamp => palette.Timestamp,
        LogFormatter.Kind.Rx => palette.Rx,
        LogFormatter.Kind.Tx => palette.Tx,
        LogFormatter.Kind.Hex => palette.Hex,
        _ => line.Tx ? palette.Tx : palette.DefaultFore,
    };

    private static List<(int Start, int Length)> FindHits(string text, CompiledRule rule)
    {
        var hits = new List<(int Start, int Length)>();
        LogHighlightRule config = rule.Rule;
        if (config.IsRegex)
        {
            if (rule.Regex is null) return hits;
            int count = 0;
            try
            {
                foreach (Match match in rule.Regex.Matches(text))
                {
                    if (match.Length > 0) hits.Add((match.Index, match.Length));
                    if (++count >= 500) break;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // 正则超时：本规则本行不作为命中，不影响其他规则
            }
            return hits;
        }

        StringComparison comparison = config.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int at = 0;
        int guard = 0;
        while (at < text.Length && guard++ < 500)
        {
            int index = text.IndexOf(rule.Keyword, at, comparison);
            if (index < 0) break;
            hits.Add((index, rule.Keyword.Length));
            at = index + Math.Max(1, rule.Keyword.Length);
        }
        return hits;
    }

    private static Color Blend(int argb, Color baseColor)
    {
        Color color = Color.FromArgb(argb);
        if (color.A == 0) return baseColor;
        if (color.A == 255) return color;
        float t = color.A / 255F;
        return Color.FromArgb(255,
            (int)Math.Round(baseColor.R + (color.R - baseColor.R) * t),
            (int)Math.Round(baseColor.G + (color.G - baseColor.G) * t),
            (int)Math.Round(baseColor.B + (color.B - baseColor.B) * t));
    }
}

/// <summary>规则的默认值、ini Base64 JSON 和文件导入/导出。</summary>
public static class LogHighlightRuleStore
{
    public const int CurrentVersion = 2;

    private static readonly JavaScriptSerializer Json = new() { RecursionLimit = 32 };

    private sealed class RuleDto
    {
        public string Name { get; set; } = "";
        public string PresetKey { get; set; } = "";
        public string Pattern { get; set; } = "";
        public bool IsRegex { get; set; }
        public bool CaseSensitive { get; set; }
        public bool WholeLine { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Rx { get; set; } = true;
        public bool Tx { get; set; } = true;
        public int ForeColor { get; set; }
        public int BackColor { get; set; }
    }

    /// <summary>新格式带版本号，旧库存的是裸数组。</summary>
    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public List<RuleDto>? Rules { get; set; }
    }

    private static int Rgb(string hex) => ColorTranslator.FromHtml(hex).ToArgb();

    private static int Wash(int alpha, string hex)
    {
        Color color = ColorTranslator.FromHtml(hex);
        return Color.FromArgb(alpha, color.R, color.G, color.B).ToArgb();
    }

    private static LogHighlightRule Preset(string key, string name, string pattern, bool enabled,
        string fore, string back, int alpha) => new()
    {
        PresetKey = key,
        Name = name,
        Pattern = pattern,
        IsRegex = true,
        Enabled = enabled,
        Rx = true,
        Tx = true,
        ForeColor = Rgb(fore),
        BackColor = Wash(alpha, back),
    };

    /// <summary>预置规则；只有错误/警告默认开启，其余留给用户按需打开。</summary>
    public static List<LogHighlightRule> CreateDefaults(bool light)
    {
        return new List<LogHighlightRule>
        {
            Preset("error", "错误", @"ERROR|\bERR\b|FAIL|异常|错误|失败|checksum|timeout|超时", true,
                light ? "#C43D3D" : "#FF7272", light ? "#C43D3D" : "#FF7272", 0x22),
            Preset("warn", "警告", @"WARN|WARNING|警告|重试|retry|timeout|超时", true,
                light ? "#8A5A00" : "#E7CFA9", "#E0A340", 0x24),
            Preset("success", "成功 / OK", @"\bOK\b|SUCCESS|成功|完成", false,
                light ? "#1C8F52" : "#4ADE80", light ? "#1C8F52" : "#4ADE80", 0x20),
            Preset("connect", "连接状态", @"\bDISCONNECT\b|\bCONNECT\b|连接|断开|上线|离线|已注册|未注册", false,
                light ? "#0B6FA4" : "#66C2FF", light ? "#0B6FA4" : "#66C2FF", 0x20),
            Preset("gps", "GPS / 定位", @"\$G[PN]|\bG[PN](GGA|GLL|GSA|GSV|RMC|VTG)\b", false,
                light ? "#1565C0" : "#74A9FF", light ? "#1565C0" : "#74A9FF", 0x1E),
            Preset("mqtt", "MQTT / 网络", @"MQTT|PINGREQ|PINGRESP|SUBACK|PUBACK|PUBREC|PUBREL", false,
                light ? "#6A3FB5" : "#C792EA", light ? "#6A3FB5" : "#C792EA", 0x1E),
            Preset("at", "AT 命令", @"\bAT\+[A-Z0-9]+", false,
                light ? "#00695C" : "#4DD0C4", light ? "#00695C" : "#4DD0C4", 0x1C),
            Preset("heartbeat", "心跳", @"\bPING\b|\bPONG\b|心跳|HEARTBEAT", false,
                light ? "#4F6275" : "#9AA9BA", light ? "#4F6275" : "#9AA9BA", 0x18),
            Preset("debug", "调试", @"DEBUG|TRACE|调试", false,
                light ? "#607D8B" : "#9AA9BA", light ? "#607D8B" : "#9AA9BA", 0x16),
        };
    }

    /// <summary>旧版本 N1096 只有 ERROR/WARN；升级时补齐新预置（默认关闭），保留用户已有规则与开关。</summary>
    public static List<LogHighlightRule> Upgrade(IEnumerable<LogHighlightRule> stored, bool light, int storedVersion)
    {
        var rules = stored.Select(rule => rule.Clone()).ToList();
        if (storedVersion >= CurrentVersion) return rules;
        foreach (LogHighlightRule rule in rules)
        {
            if (string.IsNullOrEmpty(rule.PresetKey)) rule.PresetKey = LegacyPresetKey(rule.Name);
        }
        foreach (LogHighlightRule preset in CreateDefaults(light))
        {
            if (rules.Any(rule => rule.PresetKey == preset.PresetKey)) continue;
            preset.Enabled = false;
            rules.Add(preset);
        }
        return rules;
    }

    private static string LegacyPresetKey(string name) => name switch
    {
        "ERROR" or "错误" => "error",
        "WARN" or "警告" => "warn",
        "成功 / OK" => "success",
        "连接状态" => "connect",
        "GPS / 定位" => "gps",
        "MQTT / 网络" => "mqtt",
        "AT 命令" => "at",
        "心跳" => "heartbeat",
        "调试" => "debug",
        _ => "",
    };

    public static string ToJson(IEnumerable<LogHighlightRule> rules) =>
        Json.Serialize(new DocumentDto
        {
            Version = CurrentVersion,
            Rules = rules.Select(rule => new RuleDto
            {
                Name = rule.Name,
                PresetKey = rule.PresetKey,
                Pattern = rule.Pattern,
                IsRegex = rule.IsRegex,
                CaseSensitive = rule.CaseSensitive,
                WholeLine = rule.WholeLine,
                Enabled = rule.Enabled,
                Rx = rule.Rx,
                Tx = rule.Tx,
                ForeColor = rule.ForeColor,
                BackColor = rule.BackColor,
            }).ToList(),
        });

    public static List<LogHighlightRule> FromJson(string? json) => FromJson(json, out _);

    /// <summary>裸数组是旧版/手工文件的格式，对象是带版本的新格式。</summary>
    public static List<LogHighlightRule> FromJson(string? json, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(json)) return new List<LogHighlightRule>();
        string text = json!;
        try
        {
            List<RuleDto>? dtos;
            if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                dtos = Json.Deserialize<List<RuleDto>>(text);
            }
            else
            {
                DocumentDto? document = Json.Deserialize<DocumentDto>(text);
                dtos = document?.Rules;
                version = document?.Version ?? 0;
            }
            if (dtos is null) return new List<LogHighlightRule>();
            return dtos.Select(rule => new LogHighlightRule
            {
                Name = rule.Name ?? "",
                PresetKey = rule.PresetKey ?? "",
                Pattern = rule.Pattern ?? "",
                IsRegex = rule.IsRegex,
                CaseSensitive = rule.CaseSensitive,
                WholeLine = rule.WholeLine,
                Enabled = rule.Enabled,
                Rx = rule.Rx,
                Tx = rule.Tx,
                ForeColor = rule.ForeColor,
                BackColor = rule.BackColor,
            }).ToList();
        }
        catch
        {
            return new List<LogHighlightRule>();
        }
    }

    public static string ToBase64(IEnumerable<LogHighlightRule> rules) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson(rules)));

    public static List<LogHighlightRule> FromBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<LogHighlightRule>();
        try
        {
            return FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        }
        catch
        {
            return new List<LogHighlightRule>();
        }
    }
}
