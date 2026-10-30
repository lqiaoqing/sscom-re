using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SscomRe;

internal enum MainTraceEventKind
{
    State,
    Report,
    Sensor,
    Camera,
    Alert,
    Network,
    Downlink,
    Local,
}

internal enum MainTraceLinkState
{
    Observed,
    Active,
    Warning,
    Unobserved,
}

internal enum MainTraceReportOutcome
{
    Pending,
    Encoded,
    Submitted,
    ApiReturned,
    Success,
    Failed,
}

internal sealed record MainTraceEvent(
    DateTime Time,
    long Tick,
    MainTraceEventKind Kind,
    string Text,
    string Source,
    string Raw,
    bool Alert = false)
{
    public string Tag => Kind switch
    {
        MainTraceEventKind.State => "状态",
        MainTraceEventKind.Report => "上报",
        MainTraceEventKind.Sensor => "传感器",
        MainTraceEventKind.Camera => "拍照",
        MainTraceEventKind.Alert => "异常",
        MainTraceEventKind.Network => "网络",
        MainTraceEventKind.Downlink => "下发",
        _ => "本机",
    };
}

internal sealed record MainTraceLinkStage(
    string Label,
    string Value,
    string Sub,
    MainTraceLinkState State);

internal sealed record MainTraceDownlink(
    DateTime Time,
    long Tick,
    string Topic,
    string Summary,
    int Bytes,
    string Payload,
    string Raw,
    string State = "已收到");

/// <summary>业务时间线用的聚合事件：一次报文只保留一行结果，不展开编码/提交/API 阶段。</summary>
internal sealed record MainTraceTimelineItem(
    DateTime Time,
    long Tick,
    MainTraceEventKind Kind,
    string Direction,
    string Text,
    string Source,
    string Raw,
    bool Alert)
{
    public string Tag => Kind switch
    {
        MainTraceEventKind.Report => "报文",
        MainTraceEventKind.State => "状态",
        MainTraceEventKind.Sensor => "传感器",
        MainTraceEventKind.Camera => "拍照",
        MainTraceEventKind.Alert => "异常",
        MainTraceEventKind.Network => "网络",
        MainTraceEventKind.Downlink => "报文",
        _ => "本机",
    };
}

internal sealed record MainTraceBindingInfo(
    int TpmsBound,
    int ThsBound,
    int DoorBound,
    string TpmsList,
    string ThsList,
    string DoorList,
    string BleScan,
    DateTime At)
{
    public static MainTraceBindingInfo Empty { get; } = new(0, 0, 0, "", "", "", "", DateTime.MinValue);
}

internal sealed record MainTraceSnapshot
{
    public bool HasData { get; init; }
    public DateTime HostTime { get; init; }
    public long Tick { get; init; }
    public string DeviceTime { get; init; } = "";
    public string ClockOffsetText { get; init; } = "";
    public string Model { get; init; } = "";
    public string Firmware { get; init; } = "";
    public string Bootloader { get; init; } = "";
    public string Frequency { get; init; } = "";
    public string MainState { get; init; } = "";
    public string SubState { get; init; } = "";

    public string Operator { get; init; } = "";
    public string AccessTech { get; init; } = "";
    public string ModemState { get; init; } = "";
    public string ModemStep { get; init; } = "";
    public int? ModemStateSeconds { get; init; }

    public string MqttState { get; init; } = "";
    public int? MqttStateSeconds { get; init; }
    public string QhttpState { get; init; } = "";
    public int? QhttpStateSeconds { get; init; }
    public string Sig { get; init; } = "";

    public string GpsPower { get; init; } = "";
    public string GpsFixState { get; init; } = "";
    public double? Hdop { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string GpsSpeed { get; init; } = "";
    public string GpsWeak { get; init; } = "";
    public string Driving { get; init; } = "";
    public string GpsMileage { get; init; } = "";
    public string FilteredSpeed { get; init; } = "";

    public string ChargeFlag { get; init; } = "";
    public string BatteryVoltage { get; init; } = "";
    public string BatteryCurrent { get; init; } = "";
    public string BatteryPercent { get; init; } = "";
    public string BatteryCapacity { get; init; } = "";
    public string BatteryTemperature { get; init; } = "";

    public string DoorState { get; init; } = "";
    public string DoorBound { get; init; } = "";
    public string Rpd { get; init; } = "";
    public string McuBleVersion { get; init; } = "";
    public string McuBleMode { get; init; } = "";
    public string SocBleVersion { get; init; } = "";
    public string SocBleMode { get; init; } = "";
    public string TpmsBound { get; init; } = "";
    public string ThsBound { get; init; } = "";
    public string SocVersion { get; init; } = "";
    public string SocError { get; init; } = "";
    public int? SocErrorValue { get; init; }
    public string Ldr { get; init; } = "";
    public string BoardTemperature { get; init; } = "";
    public string BoardHumidity { get; init; } = "";
    public string Memory { get; init; } = "";
    public string Raw { get; init; } = "";
    public bool ParseComplete { get; init; }

    public static MainTraceSnapshot Empty { get; } = new();

    public string MainStateText => MainState.Length > 0 ? MainState : "等待";
    public string SubStateText => SubState.Length > 0 ? SubState : "—";
    public string ModelText => Model.Length > 0 ? Model : "VS2310";
    public string ModemText => ModemState.Length > 0 ? ModemState : "—";
    public string MqttText => MqttState.Length > 0 ? MqttState : "—";
    public string DoorText => DoorState.Length > 0 ? DoorState : "—";
    public string DoorLabel => DoorState switch
    {
        "OPEN" => "打开",
        "CLOSE" => "关闭",
        "INVALID" => "无效",
        _ => "—",
    };
    public string DrivingLabel => Driving switch
    {
        "IDL" => "非行驶",
        "DRI" => "行驶",
        _ => "未知",
    };
    public string FixStateText => GpsPower == "ON"
        ? (GpsFixState.Length > 0 ? GpsFixState : "—")
        : (GpsPower.Length > 0 ? GpsPower : "—");
    public string CoordinatesText =>
        Latitude is double lat && Longitude is double lon
            ? FormatCoordinate(lat) + ", " + FormatCoordinate(lon)
            : "—";
    public string SpeedText => GpsSpeed.Length > 0 ? GpsSpeed : "—";
    public string BatteryText =>
        BatteryVoltage.Length > 0 || BatteryPercent.Length > 0
            ? (BatteryVoltage.Length > 0 ? BatteryVoltage : "—") + " · "
              + (BatteryPercent.Length > 0 ? BatteryPercent : "—")
            : "—";
    public string CurrentText => BatteryCurrent.Length > 0 ? BatteryCurrent : "—";
    public string CapacityText =>
        BatteryCapacity.Length > 0 || BatteryTemperature.Length > 0
            ? (BatteryCapacity.Length > 0 ? BatteryCapacity : "—") + " · "
              + (BatteryTemperature.Length > 0 ? BatteryTemperature : "—")
            : "—";
    public string GpsText => GpsPower.Length > 0
        ? GpsPower + " · " + (GpsFixState.Length > 0 ? GpsFixState : "—")
          + (Hdop is double h ? " / " + h.ToString("0.0#", CultureInfo.InvariantCulture) : "")
        : "—";
    public string MqttSummary => MqttState.Length > 0
        ? MqttState + (MqttStateSeconds is int sec ? " " + sec.ToString(CultureInfo.InvariantCulture) + "s" : "")
        : "—";
    public string QhttpSummary => QhttpState.Length > 0
        ? QhttpState + (QhttpStateSeconds is int sec ? " " + sec.ToString(CultureInfo.InvariantCulture) + "s" : "")
        : "—";
    public string ThText =>
        BoardTemperature.Length > 0 || BoardHumidity.Length > 0
            ? (BoardTemperature.Length > 0 ? BoardTemperature : "—") + " / "
              + (BoardHumidity.Length > 0 ? BoardHumidity : "—")
            : "—";

    private static string FormatCoordinate(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);
}

internal sealed record MainTraceReport
{
    public DateTime EncodedAt { get; init; }
    public long EncodedTick { get; init; }
    public int Bytes { get; init; }
    public string Topic { get; init; } = "";
    public string PayloadJson { get; init; } = "";
    public string MaskedPayload { get; init; } = "";
    public bool JsonComplete { get; init; }
    public double? SocPower { get; init; }

    public double? BatteryVoltage { get; init; }
    public double? BatteryPercent { get; init; }
    public double? DoorStatus { get; init; }
    public double? EventCode { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Hdop { get; init; }
    public double? Mileage { get; init; }
    public string Band { get; init; } = "";
    public string Lac { get; init; } = "";
    public string Cid { get; init; } = "";
    public string Iccid { get; init; } = "";
    public string ThsText { get; init; } = "";
    public string ThsList { get; init; } = "";
    public int? TpmsCount { get; init; }
    public int? ThsCount { get; init; }
    public int? DoorCount { get; init; }

    public DateTime? SubmittedAt { get; init; }
    public string ApiResult { get; init; } = "";
    public DateTime? ApiAt { get; init; }
    public bool BusinessSuccess { get; init; }
    public DateTime? BusinessSuccessAt { get; init; }
    public bool Failed { get; init; }
    public string Failure { get; init; } = "";

    public bool HasApi => ApiResult.Length > 0;
    public MainTraceReportOutcome Outcome =>
        Failed ? MainTraceReportOutcome.Failed
        : BusinessSuccess ? MainTraceReportOutcome.Success
        : HasApi ? MainTraceReportOutcome.ApiReturned
        : SubmittedAt is not null ? MainTraceReportOutcome.Submitted
        : JsonComplete ? MainTraceReportOutcome.Encoded
        : MainTraceReportOutcome.Pending;

    public string OutcomeText => Outcome switch
    {
        MainTraceReportOutcome.Success => "业务成功日志",
        MainTraceReportOutcome.ApiReturned => "API 已返回 · 业务成功未观测",
        MainTraceReportOutcome.Submitted => "已提交 · 结果未观测",
        MainTraceReportOutcome.Failed => "发布失败",
        MainTraceReportOutcome.Encoded => "编码完成 · 未提交",
        _ => "编码中",
    };

    public string OutcomeShortText => Outcome switch
    {
        MainTraceReportOutcome.Success => "业务成功日志",
        MainTraceReportOutcome.ApiReturned => "结果未观测",
        MainTraceReportOutcome.Submitted => "已提交",
        MainTraceReportOutcome.Failed => "失败",
        MainTraceReportOutcome.Encoded => "已编码",
        _ => "编码中",
    };

    public string MaskedIccid => MaskIccid(Iccid);

    public string TopicTail
    {
        get
        {
            if (Topic.Length == 0) return "—";
            int slash = Topic.LastIndexOf('/');
            return slash >= 0 && slash + 1 < Topic.Length ? Topic.Substring(slash + 1) : Topic;
        }
    }

    public string CoordinatesText =>
        Latitude is double lat && Longitude is double lon
            ? lat.ToString("0.######", CultureInfo.InvariantCulture) + ", "
              + lon.ToString("0.######", CultureInfo.InvariantCulture)
            : "—";

    public string DeviceId
    {
        get
        {
            if (Topic.Length == 0) return "";
            string[] parts = Topic.Split('/');
            return parts.Length >= 2 ? parts[1] : "";
        }
    }

    public string MaskedDeviceId => MaskIccid(DeviceId);

    private static string MaskIccid(string value)
    {
        if (value.Length == 0) return "—";
        if (value.Length <= 8) return value;
        return value.Substring(0, 4) + "…" + value.Substring(value.Length - 4);
    }
}

internal sealed record MainTraceView
{
    public MainTraceSnapshot Snapshot { get; init; } = MainTraceSnapshot.Empty;
    public IReadOnlyList<MainTraceEvent> Events { get; init; } = Array.Empty<MainTraceEvent>();
    public IReadOnlyList<MainTraceReport> Reports { get; init; } = Array.Empty<MainTraceReport>();
    public IReadOnlyList<MainTraceDownlink> Downlinks { get; init; } = Array.Empty<MainTraceDownlink>();
    public IReadOnlyList<MainTraceTimelineItem> Timeline { get; init; } = Array.Empty<MainTraceTimelineItem>();
    public IReadOnlyList<MainTraceLinkStage> LinkStages { get; init; } = Array.Empty<MainTraceLinkStage>();
    public IReadOnlyList<string> Alerts { get; init; } = Array.Empty<string>();
    public MainTraceBindingInfo Bindings { get; init; } = MainTraceBindingInfo.Empty;

    public bool HasData { get; init; }
    public DateTime SessionStarted { get; init; }
    public DateTime LastUpdate { get; init; }
    public int SnapshotCount { get; init; }
    public int MalformedSnapshots { get; init; }
    public int LogicalMessageCount { get; init; }
    public int EventCount { get; init; }
    public int SocErrorSnapshots { get; init; }
    public int DoorInvalidSnapshots { get; init; }
    public string SocErrorSummary { get; init; } = "";
    public int DownlinkCount { get; init; }
    public int TxCount { get; init; }
    public string DataSent { get; init; } = "";
    public string DataRecv { get; init; } = "";
    public double? SnapshotPeriodSeconds { get; init; }

    public MainTraceReport? LastReport => Reports.Count > 0 ? Reports[0] : null;
    public int ReportCount => Reports.Count;
    public int DownlinkMessageCount => Downlinks.Count;
    public int ReportSuccessCount => Reports.Count(report => report.BusinessSuccess);
    public int ReportUnobservedCount =>
        Reports.Count(report => report.Outcome == MainTraceReportOutcome.ApiReturned);

    public string CompleteSnapshotsText =>
        SnapshotCount > 0 ? (SnapshotCount - MalformedSnapshots) + " / " + SnapshotCount : "—";
    public string SnapshotPeriodText =>
        SnapshotPeriodSeconds is double seconds ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "—";
    public double CompleteRatio =>
        SnapshotCount > 0 ? (SnapshotCount - MalformedSnapshots) / (double)SnapshotCount : 0;

    public string SourceSummary => "Main Trace";

    public string SessionText
    {
        get
        {
            if (SessionStarted == DateTime.MinValue || LastUpdate == DateTime.MinValue) return "00:00";
            TimeSpan span = LastUpdate - SessionStarted;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return ((int)span.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) + ":"
                + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// VS2310 Main Trace 被动解析。设备约每 200 字节插入一次软换行，断点可能落在
/// 字段名或 JSON key 中间，所以先按逻辑消息前缀切分，再把片段直接拼接。
/// 接收时间只到串口块级别，状态变化和上报阶段都标注来源，不推断未观测结果。
/// </summary>
internal sealed class MainTraceParser
{
    private const int MaxEvents = 300;
    private const int MaxReports = 200;
    private const int MaxCurrentChars = 64 * 1024;
    private const int MaxLineChars = 16 * 1024;

    private static readonly Regex PwdPattern = new(
        "\"pwd\"\\s*:\\s*\"[^\"]*\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IccidPattern = new(
        "\"iccid\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PacketCounterPattern = new(
        @"packet data counter\s+sent\s*=\s*(\d+)\s+recv\s*=\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BoundPattern = new(
        @"bound\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _current = new();
    private readonly List<MainTraceEvent> _events = new();
    private readonly List<MainTraceReport> _reports = new();
    private readonly List<MainTraceDownlink> _downlinks = new();
    private readonly Dictionary<string, int> _socErrors = new(StringComparer.Ordinal);
    private readonly JavaScriptSerializer _json = new()
    {
        MaxJsonLength = 4 * 1024 * 1024,
        RecursionLimit = 100,
    };

    private MainTraceSnapshot _snapshot = MainTraceSnapshot.Empty;
    private bool _hasCurrent;
    private long _lastCharTick;
    private DateTime _sessionStarted = DateTime.MinValue;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _snapshots;
    private int _malformedSnapshots;
    private int _logicalMessages;
    private int _eventTotal;
    private int _socErrorSnapshots;
    private int _doorInvalidSnapshots;
    private int _downlinkCount;
    private int _txCount;
    private string _dataSent = "";
    private string _dataRecv = "";
    private double? _snapshotPeriod;
    private MainTraceBindingInfo _bindings = MainTraceBindingInfo.Empty;

    public void Reset()
    {
        lock (_gate)
        {
            _line.Clear();
            _current.Clear();
            _hasCurrent = false;
            _events.Clear();
            _reports.Clear();
            _downlinks.Clear();
            _socErrors.Clear();
            _snapshot = MainTraceSnapshot.Empty;
            _lastCharTick = 0;
            _sessionStarted = DateTime.MinValue;
            _lastUpdate = DateTime.MinValue;
            _snapshots = _malformedSnapshots = _logicalMessages = _eventTotal = 0;
            _socErrorSnapshots = _doorInvalidSnapshots = _downlinkCount = _txCount = 0;
            _dataSent = _dataRecv = "";
            _snapshotPeriod = null;
            _bindings = MainTraceBindingInfo.Empty;
        }
    }

    public void Push(byte[] data)
    {
        if (data.Length == 0) return;
        lock (_gate)
        {
            if (!LooksLikeText(data) && !_hasCurrent && _line.Length == 0) return;
            if (_sessionStarted == DateTime.MinValue) _sessionStarted = DateTime.Now;
            string text = Encoding.ASCII.GetString(data);
            for (int i = 0; i < text.Length; i++) PushChar(text[i]);
            _lastCharTick = Stopwatch.GetTimestamp();
            TryFinalizeCompleteCurrent();
        }
    }

    public void NoteTx(byte[] data)
    {
        if (data.Length == 0) return;
        lock (_gate)
        {
            _txCount++;
            string text = Encoding.UTF8.GetString(data).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return;
            string preview = text.Length > 120 ? text.Substring(0, 120) + "…" : text;
            string label = "本机发送 · " + preview;
            if (text.IndexOf("\"cmd\":\"MANUAL\"", StringComparison.Ordinal) >= 0)
                label = "本机发送 MANUAL"
                        + (text.IndexOf("door_mac", StringComparison.Ordinal) >= 0 ? " · door_mac" : "");
            AddEvent(MainTraceEventKind.Local, label, "本机 TX", text, false);
        }
    }

    public MainTraceView Snapshot()
    {
        lock (_gate)
        {
            FlushIfIdleLocked();
            MainTraceReport[] reports = _reports.ToArray();
            MainTraceDownlink[] downlinks = _downlinks.ToArray();
            MainTraceEvent[] events = _events.ToArray();
            MainTraceReport? last = reports.Length > 0 ? reports[0] : null;
            var alerts = BuildAlerts(last);
            return new MainTraceView
            {
                Snapshot = _snapshot,
                Events = events,
                Reports = reports,
                Downlinks = downlinks,
                Timeline = BuildTimeline(reports, downlinks, events),
                LinkStages = BuildLinkStages(_snapshot, last),
                Alerts = alerts,
                Bindings = _bindings,
                HasData = _snapshot.HasData || _snapshots > 0 || _events.Count > 0 || _downlinks.Count > 0,
                SessionStarted = _sessionStarted,
                LastUpdate = _lastUpdate,
                SnapshotCount = _snapshots,
                MalformedSnapshots = _malformedSnapshots,
                LogicalMessageCount = _logicalMessages,
                EventCount = _eventTotal,
                SocErrorSnapshots = _socErrorSnapshots,
                DoorInvalidSnapshots = _doorInvalidSnapshots,
                SocErrorSummary = BuildSocErrorSummary(),
                DownlinkCount = _downlinkCount,
                TxCount = _txCount,
                DataSent = _dataSent,
                DataRecv = _dataRecv,
                SnapshotPeriodSeconds = _snapshotPeriod,
            };
        }
    }

    private static bool LooksLikeText(byte[] data)
    {
        int printable = 0;
        foreach (byte b in data)
            if (b is 9 or 10 or 13 || (b >= 0x20 && b != 0x7F)) printable++;
        return printable * 100 >= data.Length * 90;
    }

    private void PushChar(char c)
    {
        if (c is '\r' or '\n')
        {
            if (_line.Length > 0)
            {
                ProcessLine(_line.ToString());
                _line.Clear();
            }
            return;
        }
        _line.Append(c);
        if (_line.Length > MaxLineChars)
        {
            // 没有换行的二进制/异常流：保留尾部，避免无限增长。
            _line.Remove(0, _line.Length - MaxLineChars);
        }
    }

    private void ProcessLine(string line)
    {
        string body = line.TrimEnd();
        if (body.Length == 0) return;
        string trimmed = body.TrimStart();
        bool startsNew = IsLogicalStart(trimmed);
        if (startsNew)
        {
            body = trimmed;
            FinalizeCurrent();
            _current.Clear();
            _current.Append(body);
            _hasCurrent = true;
            if (body[0] == '#' && IsSnapshotComplete(body)) FinalizeCurrent();
            else if (body.IndexOf("encode report ok(", StringComparison.Ordinal) >= 0 && JsonBalanced(body))
                FinalizeCurrent();
            return;
        }

        if (!_hasCurrent)
        {
            _current.Clear();
            _current.Append(body);
            _hasCurrent = true;
            return;
        }

        _current.Append(body);
        if (_current.Length > MaxCurrentChars)
        {
            FinalizeCurrent();
            return;
        }
        string current = _current.ToString();
        if (current[0] == '#' && IsSnapshotComplete(current)) FinalizeCurrent();
        else if (current.IndexOf("encode report ok(", StringComparison.Ordinal) >= 0 && JsonBalanced(current))
            FinalizeCurrent();
    }

    private static bool IsLogicalStart(string body)
    {
        if (body.Length == 0) return false;
        if (body[0] == '#')
        {
            if (body.Length < 10) return false;
            for (int i = 1; i <= 8; i++)
                if (i is 3 or 6 ? body[i] != ':' : !char.IsDigit(body[i])) return false;
            return true;
        }
        string[] prefixes =
        {
            "[INFO", "[DEBUG", "[WARN", "[ERROR", "[TRACE",
            "receive new ", "Mainstate ", "mqtt client ", "mqtt publish ",
            "encode report ", "workingMode ", "batteryMode ", "Sensor info show:",
            "BLE ", "loading snapshot ", "snapshot command received!", "Record command received!",
            "LiveStream command received!", "gps payload ", "modem ", "tracker_config_write_data",
        };
        foreach (string prefix in prefixes)
            if (body.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    private void TryFinalizeCompleteCurrent()
    {
        if (!_hasCurrent) return;
        string current = _current.ToString();
        if (current[0] == '#' && IsSnapshotComplete(current)) FinalizeCurrent();
        else if (current.IndexOf("encode report ok(", StringComparison.Ordinal) >= 0 && JsonBalanced(current))
            FinalizeCurrent();
    }

    private void FlushIfIdleLocked()
    {
        if (!_hasCurrent || _lastCharTick == 0) return;
        double idle = (Stopwatch.GetTimestamp() - _lastCharTick) / (double)Stopwatch.Frequency;
        string current = _current.ToString();
        bool structured = current[0] == '#' || current.IndexOf("encode report ok(", StringComparison.Ordinal) >= 0;
        if (idle < (structured ? 1.0 : 0.22)) return;
        FinalizeCurrent();
    }

    private static bool IsSnapshotComplete(string text) =>
        text.IndexOf("MEM:", StringComparison.Ordinal) >= 0;

    private static bool JsonBalanced(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return false;
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return true;
            }
        }
        return false;
    }

    private void FinalizeCurrent()
    {
        if (!_hasCurrent || _current.Length == 0) return;
        string message = _current.ToString();
        _current.Clear();
        _hasCurrent = false;
        _logicalMessages++;
        if (message[0] == '#') ParseSnapshot(message);
        else ParseEvent(message);
        _lastUpdate = DateTime.Now;
    }

    private void ParseSnapshot(string message)
    {
        DateTime host = DateTime.Now;
        long tick = Stopwatch.GetTimestamp();
        string deviceTime = "";
        if (message.Length >= 9)
        {
            deviceTime = message.Substring(1, 8);
            if (!TimeSpan.TryParseExact(deviceTime, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out _))
                deviceTime = "";
        }

        int modelEnd = message.IndexOf(":[", 10, StringComparison.Ordinal);
        if (modelEnd <= 10)
        {
            _snapshots++;
            _malformedSnapshots++;
            return;
        }
        string model = message.Substring(10, modelEnd - 10).Trim();
        string[] modelFields = Fields(Section(message, model));
        if (model.Length == 0 || modelFields.Length < 5)
        {
            _snapshots++;
            _malformedSnapshots++;
            return;
        }

        string[] mod = Fields(Section(message, "MOD"));
        string[] mqtt = Tokens(Section(message, "MQTT"));
        string[] qhttp = Tokens(Section(message, "QHTTP"));
        string[] gps = Fields(Section(message, "GPS"));
        string[] bat = Fields(Section(message, "BAT"));
        string[] door = Fields(Section(message, "DOOR"));
        string[] mcuBle = Fields(Section(message, "MCU_BLE"));
        string[] socBle = Fields(Section(message, "SOC_BLE"));
        string[] th = Fields(Section(message, "TH"));
        string socError = Section(message, "SOC_ERR") ?? "";
        string[] mqttTokens = mqtt;
        string[] qhttpTokens = qhttp;
        string? mqttContent = Section(message, "MQTT");
        string? qhttpContent = Section(message, "QHTTP");
        if (mqttContent is not null)
        {
            int space = mqttContent.IndexOf(' ');
            mqttTokens = space > 0
                ? new[] { mqttContent.Substring(0, space).Trim(), mqttContent.Substring(space + 1).Trim() }
                : new[] { mqttContent.Trim() };
        }
        if (qhttpContent is not null)
        {
            int space = qhttpContent.IndexOf(' ');
            qhttpTokens = space > 0
                ? new[] { qhttpContent.Substring(0, space).Trim(), qhttpContent.Substring(space + 1).Trim() }
                : new[] { qhttpContent.Trim() };
        }

        int hasCore = (mod.Length > 0 ? 1 : 0) + (mqttTokens.Length > 0 ? 1 : 0)
            + (gps.Length > 0 ? 1 : 0) + (bat.Length > 0 ? 1 : 0) + (door.Length > 0 ? 1 : 0);
        var snapshot = new MainTraceSnapshot
        {
            HasData = true,
            HostTime = host,
            Tick = tick,
            DeviceTime = deviceTime,
            ClockOffsetText = ClockOffset(deviceTime, host),
            Model = model,
            Firmware = modelFields[0],
            Bootloader = modelFields[1],
            Frequency = modelFields[2],
            MainState = modelFields[3],
            SubState = modelFields[4],
            Operator = mod.Length >= 5 ? mod[0] : (mod.Length >= 3 ? mod[0] : ""),
            AccessTech = mod.Length >= 5 ? mod[1] : (mod.Length >= 3 ? mod[1] : ""),
            ModemState = mod.Length >= 5 ? mod[2] : (mod.Length >= 3 ? mod[2] : (mod.Length == 1 ? mod[0] : "")),
            ModemStep = mod.Length >= 5 ? mod[3] : "",
            ModemStateSeconds = mod.Length >= 5 ? Int(mod[4]) : null,
            MqttState = mqttTokens.Length > 0 ? mqttTokens[0] : "",
            MqttStateSeconds = mqttTokens.Length > 1 ? Int(mqttTokens[1]) : null,
            QhttpState = qhttpTokens.Length > 0 ? qhttpTokens[0] : "",
            QhttpStateSeconds = qhttpTokens.Length > 1 ? Int(qhttpTokens[1]) : null,
            Sig = Scalar(message, "SIG"),
            GpsPower = gps.Length >= 1 ? gps[0] : "",
            GpsFixState = gps.Length >= 2 ? gps[1] : "",
            Hdop = gps.Length >= 3 ? Double(gps[2]) : null,
            Latitude = gps.Length >= 4 ? Double(gps[3]) : null,
            Longitude = gps.Length >= 5 ? Double(gps[4]) : null,
            GpsSpeed = gps.Length >= 6 ? gps[5] : "",
            GpsWeak = gps.Length >= 7 ? gps[6] : "",
            Driving = gps.Length >= 8 ? gps[7] : "",
            GpsMileage = gps.Length >= 9 ? gps[8] : "",
            FilteredSpeed = Section(message, "f_spd") ?? "",
            ChargeFlag = bat.Length >= 1 ? bat[0] : "",
            BatteryVoltage = bat.Length >= 2 ? FormatVoltage(bat[1]) : "",
            BatteryCurrent = bat.Length >= 3 ? FormatCurrent(bat[2]) : "",
            BatteryPercent = bat.Length >= 4 ? bat[3].Trim() : "",
            BatteryCapacity = bat.Length >= 5 ? FormatCapacity(bat[4]) : "",
            BatteryTemperature = bat.Length >= 6 ? FormatTemperature(bat[5]) : "",
            DoorState = door.Length >= 1 ? door[0] : "",
            DoorBound = door.Length >= 2 ? door[1] : "",
            Rpd = Section(message, "RPD") ?? "",
            McuBleVersion = mcuBle.Length >= 1 ? mcuBle[0] : "",
            McuBleMode = mcuBle.Length >= 2 ? mcuBle[1] : "",
            SocBleVersion = socBle.Length >= 1 ? socBle[0] : "",
            SocBleMode = socBle.Length >= 2 ? socBle[1] : "",
            TpmsBound = Section(message, "TPMS") ?? "",
            ThsBound = Section(message, "THS") ?? "",
            SocVersion = Section(message, "SOC") ?? "",
            SocError = socError,
            SocErrorValue = Int(socError),
            Ldr = Section(message, "LDR") ?? "",
            BoardTemperature = th.Length >= 1 ? th[0] : "",
            BoardHumidity = th.Length >= 2 ? th[1] : "",
            Memory = Scalar(message, "MEM"),
            Raw = SanitizeRaw(message),
            ParseComplete = hasCore >= 3,
        };

        _snapshots++;
        if (!snapshot.ParseComplete) _malformedSnapshots++;
        if (snapshot.SocErrorValue is int error && error != 0)
        {
            _socErrorSnapshots++;
            _socErrors[socError] = _socErrors.TryGetValue(socError, out int count) ? count + 1 : 1;
        }
        if (snapshot.DoorState == "INVALID") _doorInvalidSnapshots++;

        MainTraceSnapshot previous = _snapshot;
        if (previous.HasData && host > previous.HostTime)
            _snapshotPeriod = (host - previous.HostTime).TotalSeconds;
        _snapshot = snapshot;
        _lastUpdate = host;

        if (!previous.HasData)
        {
            AddEvent(MainTraceEventKind.State,
                "收到首帧快照 · " + snapshot.MainStateText + " / " + snapshot.SubStateText,
                "快照", snapshot.Raw);
            return;
        }

        Changed(previous.MainState, snapshot.MainState, "主状态");
        Changed(previous.SubState, snapshot.SubState, "子状态");
        string modem = "模组 " + previous.ModemText + " → " + snapshot.ModemText;
        if (previous.ModemState.Length > 0 && snapshot.ModemState.Length > 0
            && previous.ModemState != snapshot.ModemState)
            AddEvent(snapshot.ModemState is "OFF" or "CLOSING" ? MainTraceEventKind.Alert : MainTraceEventKind.State,
                modem, "快照推断", snapshot.Raw, snapshot.ModemState is "OFF" or "CLOSING");

        if (previous.MqttState.Length > 0 && snapshot.MqttState.Length > 0
            && previous.MqttState != snapshot.MqttState)
            AddEvent(MainTraceEventKind.Network,
                "MQTT " + previous.MqttText + " → " + snapshot.MqttText,
                "快照推断", snapshot.Raw);

        if (previous.DoorState.Length > 0 && snapshot.DoorState.Length > 0
            && previous.DoorState != snapshot.DoorState)
            AddEvent(snapshot.DoorState == "INVALID" ? MainTraceEventKind.Alert : MainTraceEventKind.State,
                "门磁 " + previous.DoorText + " → " + snapshot.DoorText,
                "快照推断", snapshot.Raw, snapshot.DoorState == "INVALID");

        if (previous.GpsPower.Length > 0 && snapshot.GpsPower.Length > 0
            && previous.GpsPower != snapshot.GpsPower)
            AddEvent(MainTraceEventKind.State,
                "GPS " + previous.GpsPower + " → " + snapshot.GpsPower,
                "快照推断", snapshot.Raw);

        if (previous.SocError != snapshot.SocError
            && (previous.SocError.Length > 0 || snapshot.SocError.Length > 0))
        {
            bool alert = snapshot.SocErrorValue is int value && value != 0;
            AddEvent(alert ? MainTraceEventKind.Alert : MainTraceEventKind.State,
                "SOC_ERR " + (previous.SocError.Length > 0 ? previous.SocError : "—")
                + " → " + (snapshot.SocError.Length > 0 ? snapshot.SocError : "—"),
                "快照推断", snapshot.Raw, alert);
        }

        void Changed(string before, string after, string label)
        {
            if (before.Length == 0 || after.Length == 0 || before == after) return;
            AddEvent(MainTraceEventKind.State, label + " " + before + " → " + after,
                "快照推断", snapshot.Raw);
        }
    }

    private void ParseEvent(string message)
    {
        string text = message.Trim();
        string raw = SanitizeRaw(text);
        DateTime now = DateTime.Now;
        long tick = Stopwatch.GetTimestamp();

        if (text.IndexOf("BLE src=door_open", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Sensor, "门磁事件 · 门打开", "设备日志", raw);
            return;
        }
        if (text.IndexOf("BLE src=door_close", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Sensor, "门磁事件 · 门关闭", "设备日志", raw);
            return;
        }
        if (text.IndexOf("loading snapshot START", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Camera, "拍照 START 入队", "设备日志", raw);
            return;
        }
        if (text.IndexOf("loading snapshot STOP", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Camera, "拍照 STOP 入队", "设备日志", raw);
            return;
        }
        if (text.IndexOf("snapshot command received!", StringComparison.Ordinal) >= 0
            || text.IndexOf("Record command received!", StringComparison.Ordinal) >= 0
            || text.IndexOf("LiveStream command received!", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Camera, "拍照/录像命令已收到 · 执行结果未观测", "设备日志", raw);
            return;
        }
        if (text.IndexOf("received cmd:", StringComparison.Ordinal) >= 0)
        {
            string command = After(text, "received cmd:");
            AddEvent(MainTraceEventKind.Camera, "收到命令 " + command + " · 执行结果未观测", "设备日志", raw);
            return;
        }
        if (text.IndexOf("receive new cfg:", StringComparison.Ordinal) >= 0)
        {
            AddDownlink(now, tick, "trkcfg", "设备配置", After(text, "receive new cfg:"), raw);
            return;
        }
        if (text.IndexOf("receive new cmd:", StringComparison.Ordinal) >= 0)
        {
            string payload = After(text, "receive new cmd:");
            string summary = payload.IndexOf("SnapShot", StringComparison.OrdinalIgnoreCase) >= 0 ? "SnapShot"
                : payload.IndexOf("Record", StringComparison.OrdinalIgnoreCase) >= 0 ? "Record"
                : payload.IndexOf("LiveStream", StringComparison.OrdinalIgnoreCase) >= 0 ? "LiveStream"
                : Short(payload, 32);
            AddDownlink(now, tick, "cmd", summary.Length > 0 ? summary : "命令", payload, raw);
            return;
        }
        if (text.IndexOf("receive new upgrade:", StringComparison.Ordinal) >= 0)
        {
            AddDownlink(now, tick, "upgrade", "升级请求", After(text, "receive new upgrade:"), raw);
            return;
        }
        if (text.IndexOf("Mainstate ", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.State, "主状态机 · " + Short(text.Substring(text.IndexOf("Mainstate ")), 100),
                "设备日志", raw);
            return;
        }
        if (text.IndexOf("Sensor info show:", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Sensor, "传感器绑定刷新", "设备日志", raw);
            return;
        }
        if (TryBinding(text, "tpms:", out int tpmsBound, out string tpmsList))
        {
            UpdateBindings(tpmsBound, null, null, tpmsList, null, null, null, now);
            return;
        }
        if (TryBinding(text, "ths:", out int thsBound, out string thsList))
        {
            UpdateBindings(null, thsBound, null, null, thsList, null, null, now);
            return;
        }
        if (TryBinding(text, "door:", out int doorBound, out string doorList))
        {
            UpdateBindings(null, null, doorBound, null, null, doorList, null, now);
            return;
        }
        if (text.IndexOf("ble_scan:", StringComparison.Ordinal) >= 0)
        {
            UpdateBindings(null, null, null, null, null, null, Short(After(text, "ble_scan:"), 80), now);
            return;
        }
        if (text.IndexOf("request force scan", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Sensor, "传感器强制扫描 300s", "设备日志", raw);
            return;
        }
        if (text.IndexOf("packet data counter", StringComparison.Ordinal) >= 0)
        {
            Match match = PacketCounterPattern.Match(text);
            if (match.Success)
            {
                _dataSent = match.Groups[1].Value;
                _dataRecv = match.Groups[2].Value;
            }
            return;
        }
        if (text.IndexOf("tracker_config_write_data", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Network, "配置写入 app.ini", "设备日志", raw);
            return;
        }
        if (text.IndexOf("modem wakeup request", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Network, "Modem 唤醒请求", "设备日志", raw);
            return;
        }
        if (text.IndexOf("SOC power on and needs modem", StringComparison.Ordinal) >= 0)
        {
            AddEvent(MainTraceEventKind.Network, "SoC 上电，请求 Modem", "设备日志", raw);
            return;
        }

        if (text.IndexOf("encode report ok(", StringComparison.Ordinal) >= 0)
        {
            int start = text.IndexOf("encode report ok(", StringComparison.Ordinal);
            int end = text.IndexOf(')', start);
            int bytes = 0;
            if (end > start)
                int.TryParse(text.Substring(start + "encode report ok(".Length,
                    end - start - "encode report ok(".Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out bytes);
            string json = ExtractJson(text);
            MainTraceReport report = BuildReport(now, tick, bytes, json);
            _reports.Insert(0, report);
            while (_reports.Count > MaxReports) _reports.RemoveAt(_reports.Count - 1);
            AddEvent(MainTraceEventKind.Report,
                "编码 " + bytes + " B · JSON " + (report.JsonComplete ? "完整" : "未完整"),
                "设备日志", raw);
            return;
        }

        if (text.IndexOf("mqtt publish enter ok", StringComparison.Ordinal) >= 0)
        {
            string topic = After(text, "topic=");
            int space = topic.IndexOf(' ');
            if (space > 0) topic = topic.Substring(0, space);
            int len = 0;
            string lenText = After(text, "len=");
            int.TryParse(lenText, NumberStyles.Integer, CultureInfo.InvariantCulture, out len);
            MainTraceReport? report = FindReport(r => r.SubmittedAt is null, 30, now);
            if (report is not null)
                ReplaceReport(report, report with
                {
                    Topic = topic.Length > 0 ? topic : report.Topic,
                    SubmittedAt = now,
                });
            string topicTail = "—";
            if (topic.Length > 0)
            {
                int slash = topic.LastIndexOf('/');
                topicTail = slash >= 0 && slash + 1 < topic.Length ? topic.Substring(slash + 1) : topic;
            }
            AddEvent(MainTraceEventKind.Report,
                "提交 MQTT · " + (len > 0 ? len + " B" : "长度未观测") + " · " + topicTail,
                "固件观察", raw);
            return;
        }

        if (text.IndexOf("msg_id=", StringComparison.Ordinal) >= 0
            && text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) < 0)
        {
            string api = ReadApiResult(text);
            MainTraceReport? report = FindReport(r => !r.HasApi && !r.Failed, 30, now);
            if (report is not null)
                ReplaceReport(report, report with { ApiResult = api, ApiAt = now });
            AddEvent(MainTraceEventKind.Report, "发布 API 返回 " + api, "固件观察", raw);
            return;
        }

        if (text.IndexOf("gps payload pubilsh success", StringComparison.Ordinal) >= 0
            || text.IndexOf("gps payload publish success", StringComparison.Ordinal) >= 0)
        {
            MainTraceReport? report = FindReport(r => !r.BusinessSuccess && !r.Failed, 30, now);
            if (report is not null)
                ReplaceReport(report, report with { BusinessSuccess = true, BusinessSuccessAt = now });
            AddEvent(MainTraceEventKind.Report,
                report is null ? "业务成功日志 · 未匹配到上报" : "业务成功日志 · " + report.Bytes + " B",
                "固件观察", raw);
            return;
        }

        if (text.IndexOf("publish failed", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("publish enter failed", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("notify mqtt publish failed", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("encode report failed", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("report payload too large", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            MainTraceReport? report = FindReport(r => !r.Failed, 30, now);
            string failure = Short(text, 100);
            if (report is not null)
                ReplaceReport(report, report with { Failed = true, Failure = failure });
            AddEvent(MainTraceEventKind.Alert, "上报失败 · " + failure, "固件观察", raw, true);
            return;
        }

        if (text.IndexOf("command dispatch failed", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("Record stop ignored", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AddEvent(MainTraceEventKind.Alert, Short(text, 110), "设备日志", raw, true);
            return;
        }

        if (LooksLikeError(text))
        {
            AddEvent(MainTraceEventKind.Alert, Short(text, 110), "设备日志", raw, true);
        }
    }

    private MainTraceReport BuildReport(DateTime now, long tick, int bytes, string json)
    {
        var report = new MainTraceReport
        {
            EncodedAt = now,
            EncodedTick = tick,
            Bytes = bytes,
            PayloadJson = json,
            MaskedPayload = SanitizeRaw(json),
        };
        if (json.Length == 0) return report;
        try
        {
            object? root = _json.DeserializeObject(json);
            if (root is not Dictionary<string, object> obj) return report with { JsonComplete = false };
            report = report with
            {
                JsonComplete = true,
                BatteryVoltage = Number(Path(obj, "sta", "bat")),
                BatteryPercent = Number(Path(obj, "sta", "soc")),
                DoorStatus = Number(Path(obj, "sta", "doorStatus")),
                EventCode = Number(Path(obj, "sta", "event")),
                Latitude = Number(Path(obj, "gps", "lat")),
                Longitude = Number(Path(obj, "gps", "lng")),
                Hdop = Number(Path(obj, "gps", "hdop")),
                Mileage = Number(Path(obj, "stats", "mileage")),
                SocPower = Number(Path(obj, "SoC", "SoC_status")),
                Band = Text(Path(obj, "nwinfo", "band")),
                Lac = Text(Path(obj, "nwinfo", "lac")),
                Cid = Text(Path(obj, "nwinfo", "cid")),
                Iccid = Text(Path(obj, "info", "iccid")),
                TpmsCount = Count(Path(obj, "sensor", "bleTpms")),
                ThsCount = Count(Path(obj, "sensor", "thSensor")),
                DoorCount = Count(Path(obj, "sensor", "bleDoor")),
                ThsText = FirstThs(Path(obj, "sensor", "thSensor")),
                ThsList = ListText(Path(obj, "sensor", "thSensor")),
            };
        }
        catch (Exception)
        {
            report = report with { JsonComplete = false };
        }
        return report;
    }

    private void UpdateBindings(int? tpms, int? ths, int? door, string? tpmsList, string? thsList,
        string? doorList, string? scan, DateTime at)
    {
        _bindings = _bindings with
        {
            TpmsBound = tpms ?? _bindings.TpmsBound,
            ThsBound = ths ?? _bindings.ThsBound,
            DoorBound = door ?? _bindings.DoorBound,
            TpmsList = tpmsList ?? _bindings.TpmsList,
            ThsList = thsList ?? _bindings.ThsList,
            DoorList = doorList ?? _bindings.DoorList,
            BleScan = scan ?? _bindings.BleScan,
            At = at,
        };
    }

    private MainTraceReport? FindReport(Func<MainTraceReport, bool> predicate, int seconds, DateTime now)
    {
        foreach (MainTraceReport report in _reports)
        {
            if (!predicate(report)) continue;
            if ((now - report.EncodedAt).TotalSeconds > seconds) return null;
            return report;
        }
        return null;
    }

    private void ReplaceReport(MainTraceReport report, MainTraceReport replacement)
    {
        int index = _reports.IndexOf(report);
        if (index >= 0) _reports[index] = replacement;
    }

    private void AddDownlink(DateTime time, long tick, string topic, string summary, string payload, string raw)
    {
        _downlinkCount++;
        string clean = SanitizeRaw(payload.Trim());
        int bytes = Encoding.UTF8.GetByteCount(clean);
        _downlinks.Insert(0, new MainTraceDownlink(time, tick, topic, summary,
            bytes, clean, Short(raw, 500)));
        while (_downlinks.Count > MaxReports) _downlinks.RemoveAt(_downlinks.Count - 1);
        string label = topic switch
        {
            "cmd" => "下发命令",
            "upgrade" => "下发升级",
            _ => "下发配置",
        };
        AddEvent(MainTraceEventKind.Downlink, label + " · " + Short(summary, 80), "固件观察", raw);
    }

    private List<MainTraceTimelineItem> BuildTimeline(MainTraceReport[] reports,
        MainTraceDownlink[] downlinks, MainTraceEvent[] events)
    {
        var items = new List<MainTraceTimelineItem>();
        foreach (MainTraceReport report in reports)
        {
            DateTime when = report.BusinessSuccessAt ?? report.ApiAt ?? report.SubmittedAt ?? report.EncodedAt;
            string result = report.Outcome switch
            {
                MainTraceReportOutcome.Success => "发布成功",
                MainTraceReportOutcome.ApiReturned => "结果未观测",
                MainTraceReportOutcome.Submitted => "已提交",
                MainTraceReportOutcome.Failed => "失败",
                MainTraceReportOutcome.Encoded => "已编码",
                _ => "编码中",
            };
            string text = "↑ report " + report.Bytes + " B · " + result;
            string evidence = report.Outcome switch
            {
                MainTraceReportOutcome.Success =>
                    "gps payload pubilsh success · " + report.Topic + " · " + report.Bytes + " B",
                MainTraceReportOutcome.ApiReturned =>
                    "API " + report.ApiResult + " · 业务成功日志未观测",
                MainTraceReportOutcome.Failed => "publish failed · " + report.Failure,
                MainTraceReportOutcome.Submitted =>
                    "mqtt publish enter ok · " + report.Topic,
                MainTraceReportOutcome.Encoded => "encode report ok(" + report.Bytes + ")",
                _ => "encode report ok(" + report.Bytes + ")",
            };
            items.Add(new MainTraceTimelineItem(when, report.EncodedTick, MainTraceEventKind.Report,
                "↑", text, "固件观察", evidence, false));
        }
        foreach (MainTraceDownlink downlink in downlinks)
        {
            items.Add(new MainTraceTimelineItem(downlink.Time, downlink.Tick, MainTraceEventKind.Report,
                "↓", "↓ " + downlink.Topic + " " + downlink.Summary + " · 已收到",
                "固件观察", downlink.Payload, false));
        }
        foreach (MainTraceEvent ev in events)
        {
            if (ev.Kind is MainTraceEventKind.Report or MainTraceEventKind.Downlink) continue;
            // 报文阶段已合并为一条；常规 MQTT/模组翻转不刷时间线，保留配置和异常。
            if (ev.Kind == MainTraceEventKind.Network
                && ev.Text.IndexOf("配置写入", StringComparison.Ordinal) < 0) continue;
            if (ev.Kind == MainTraceEventKind.State
                && !ev.Text.StartsWith("主状态 ", StringComparison.Ordinal)
                && !ev.Text.StartsWith("子状态 ", StringComparison.Ordinal)
                && !ev.Text.StartsWith("收到首帧", StringComparison.Ordinal)) continue;
            items.Add(new MainTraceTimelineItem(ev.Time, ev.Tick, ev.Kind, "", ev.Text, ev.Source, ev.Raw, ev.Alert));
        }
        items.Sort((a, b) =>
        {
            int byTime = b.Time.CompareTo(a.Time);
            return byTime != 0 ? byTime : b.Tick.CompareTo(a.Tick);
        });
        return items.Count > MaxEvents ? items.GetRange(0, MaxEvents) : items;
    }

    private void AddEvent(MainTraceEventKind kind, string text, string source, string raw, bool alert = false)
    {
        _events.Insert(0, new MainTraceEvent(DateTime.Now, Stopwatch.GetTimestamp(), kind,
            Short(text, 180), source, Short(raw, 500), alert));
        while (_events.Count > MaxEvents) _events.RemoveAt(_events.Count - 1);
        _eventTotal++;
    }

    private List<string> BuildAlerts(MainTraceReport? last)
    {
        var alerts = new List<string>();
        if (!_snapshot.HasData && _snapshots == 0)
        {
            alerts.Add("等待主日志数据");
            return alerts;
        }
        if (_socErrors.Count > 0)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in _socErrors.OrderByDescending(pair => pair.Value))
                parts.Add("SOC_ERR=" + pair.Key + " ×" + pair.Value.ToString(CultureInfo.InvariantCulture) + "（本段）");
            alerts.Add(string.Join(" · ", parts));
        }
        if (_doorInvalidSnapshots > 0)
            alerts.Add("DOOR INVALID ×" + _doorInvalidSnapshots.ToString(CultureInfo.InvariantCulture) + "（历史）");
        if (last is not null && last.Outcome == MainTraceReportOutcome.ApiReturned)
            alerts.Add("最近上报业务成功日志未观测");
        else if (last is not null && last.Failed)
            alerts.Add("最近上报失败");
        if (last is not null && !last.JsonComplete)
            alerts.Add("最近上报 JSON 未完整");
        if (_malformedSnapshots > 0)
            alerts.Add("快照解析失败 ×" + _malformedSnapshots.ToString(CultureInfo.InvariantCulture));
        return alerts;
    }

    private List<MainTraceLinkStage> BuildLinkStages(MainTraceSnapshot s, MainTraceReport? report)
    {
        var stages = new List<MainTraceLinkStage>(6);
        if (!s.HasData)
        {
            stages.Add(new("模组", "等待", "MOD", MainTraceLinkState.Unobserved));
            stages.Add(new("驻网", "等待", "运营商", MainTraceLinkState.Unobserved));
            stages.Add(new("数据", "等待", "PDP", MainTraceLinkState.Unobserved));
            stages.Add(new("MQTT", "等待", "状态", MainTraceLinkState.Unobserved));
            stages.Add(new("最近发布", "等待", "上报", MainTraceLinkState.Unobserved));
            stages.Add(new("云端", "未观测", "无回执", MainTraceLinkState.Unobserved));
            return stages;
        }

        MainTraceLinkState modemState = s.ModemState switch
        {
            "ACTIVATED" => MainTraceLinkState.Observed,
            "POWERSAVE" => MainTraceLinkState.Active,
            "OFF" or "CLOSING" => MainTraceLinkState.Warning,
            "" => MainTraceLinkState.Unobserved,
            _ => MainTraceLinkState.Active,
        };
        stages.Add(new("模组", s.ModemText,
            s.ModemStep.Length > 0 ? s.ModemStep + (s.ModemStateSeconds is int ms ? " " + ms + "s" : "") : "状态",
            modemState));
        stages.Add(new("驻网", s.Operator.Length > 0 ? s.Operator : "—",
            s.AccessTech.Length > 0 ? s.AccessTech : "运营商",
            s.Operator.Length > 0 ? MainTraceLinkState.Observed : MainTraceLinkState.Unobserved));
        stages.Add(new("数据", s.ModemStep.Length > 0 ? s.ModemStep : "—",
            s.ModemState == "ACTIVATED" ? "已激活" : "低功耗保持",
            s.ModemStep.Length > 0 ? MainTraceLinkState.Active : MainTraceLinkState.Unobserved));

        MainTraceLinkState mqttState = s.MqttState switch
        {
            "Conn." => MainTraceLinkState.Observed,
            "Conn" => MainTraceLinkState.Active,
            "Idle" => MainTraceLinkState.Observed,
            "Pub" => MainTraceLinkState.Active,
            "" or "--" => MainTraceLinkState.Unobserved,
            _ => MainTraceLinkState.Active,
        };
        stages.Add(new("MQTT", s.MqttState.Length > 0 ? s.MqttState : "—",
            s.MqttStateSeconds is int mqttSec ? mqttSec + "s" : "状态", mqttState));

        string publishValue = report is null ? "等待" : report.Bytes + " B";
        string publishSub = report is null ? "上报"
            : report.BusinessSuccess ? "业务成功日志"
            : report.HasApi ? "API 已返回" : report.OutcomeText;
        MainTraceLinkState publishState = report is null ? MainTraceLinkState.Unobserved
            : report.BusinessSuccess ? MainTraceLinkState.Observed
            : report.Failed ? MainTraceLinkState.Warning
            : MainTraceLinkState.Warning;
        stages.Add(new("最近发布", publishValue, publishSub, publishState));
        stages.Add(new("云端", "未观测", "无回执", MainTraceLinkState.Unobserved));
        return stages;
    }

    private string BuildSocErrorSummary()
    {
        if (_socErrors.Count == 0) return "";
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in _socErrors.OrderByDescending(pair => pair.Value))
            parts.Add("SOC_ERR=" + pair.Key + " ×" + pair.Value.ToString(CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return "";
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start, i - start + 1);
            }
        }
        return "";
    }

    private static object? Path(object? root, params string[] keys)
    {
        object? current = root;
        foreach (string key in keys)
        {
            if (current is not Dictionary<string, object> dict) return null;
            if (!dict.TryGetValue(key, out current)) return null;
        }
        return current;
    }

    private static double? Number(object? value)
    {
        if (value is null) return null;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is double d) return d;
        if (value is decimal m) return (double)m;
        if (value is float f) return f;
        if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        return null;
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static int? Count(object? value) => value is object[] array ? array.Length : null;

    private static string FirstThs(object? value)
    {
        if (value is not object[] array || array.Length == 0 || array[0] is not object[] first)
            return "";
        if (first.Length < 5) return "";
        double? temp = Number(first[3]);
        double? hum = Number(first[4]);
        if (temp is double t)
        {
            if (Math.Abs(t - 8880) < 0.01) return "无效";
            string tempText = (t / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " °C";
            return hum is double h ? tempText + " / " + h.ToString("0", CultureInfo.InvariantCulture) + "%" : tempText;
        }
        return "";
    }

    private static string ListText(object? value)
    {
        if (value is not object[] array) return "";
        return array.Length.ToString(CultureInfo.InvariantCulture) + " 条";
    }

    private static bool TryBinding(string text, string prefix, out int bound, out string list)
    {
        bound = 0;
        list = "";
        int start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return false;
        Match match = BoundPattern.Match(text, start);
        if (!match.Success) return false;
        bound = int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
        int listStart = text.IndexOf('[', start);
        int listEnd = listStart >= 0 ? text.IndexOf(']', listStart) : -1;
        if (listStart >= 0 && listEnd > listStart)
            list = text.Substring(listStart + 1, listEnd - listStart - 1);
        return true;
    }

    private static string ReadApiResult(string text)
    {
        string id = After(text, "msg_id=");
        int comma = id.IndexOf(',');
        if (comma > 0) id = id.Substring(0, comma);
        int end = 0;
        while (end < id.Length && (char.IsDigit(id[end]) || id[end] == '-')) end++;
        id = id.Substring(0, end);
        string result = After(text, "result=");
        int endResult = 0;
        while (endResult < result.Length && (char.IsDigit(result[endResult]) || result[endResult] == '-')) endResult++;
        result = result.Substring(0, endResult);
        if (id.Length == 0 && result.Length == 0) return "结果未观测";
        return "msg_id " + (id.Length > 0 ? id : "—") + " / result " + (result.Length > 0 ? result : "—");
    }

    private static string After(string text, string marker)
    {
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return "";
        string value = text.Substring(index + marker.Length).Trim();
        return value;
    }

    private static string ClockOffset(string deviceTime, DateTime host)
    {
        if (deviceTime.Length == 0) return "";
        if (!TimeSpan.TryParseExact(deviceTime, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out TimeSpan device))
            return "";
        TimeSpan diff = device - host.TimeOfDay;
        if (diff < TimeSpan.FromHours(-12)) diff += TimeSpan.FromHours(24);
        if (diff > TimeSpan.FromHours(12)) diff -= TimeSpan.FromHours(24);
        if (Math.Abs(diff.TotalMinutes) < 1) return "±0";
        int hours = (int)Math.Truncate(diff.TotalHours);
        int minutes = Math.Abs(diff.Minutes);
        string sign = diff.TotalMinutes >= 0 ? "+" : "-";
        return sign + Math.Abs(hours).ToString(CultureInfo.InvariantCulture) + "h"
               + (minutes > 0 ? minutes.ToString(CultureInfo.InvariantCulture) + "m" : "");
    }

    private static string SanitizeRaw(string text)
    {
        string clean = text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        clean = PwdPattern.Replace(clean, "\"pwd\":\"••••••\"");
        clean = IccidPattern.Replace(clean, match =>
        {
            string value = match.Groups[1].Value;
            string masked = value.Length > 8
                ? value.Substring(0, 4) + "…" + value.Substring(value.Length - 4)
                : value;
            return "\"iccid\":\"" + masked + "\"";
        });
        return clean.Length > 2000 ? clean.Substring(0, 2000) + "…" : clean;
    }

    private static bool LooksLikeError(string text) =>
        text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf(" error", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("Error:", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("错误", StringComparison.Ordinal) >= 0
        || text.IndexOf("超时", StringComparison.Ordinal) >= 0
        || text.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Short(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max) + "…";

    private static string[] Fields(string? content)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
        string[] parts = content!.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    private static string[] Tokens(string? content)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
        string[] parts = content!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts;
    }

    private static string? Section(string text, string key)
    {
        string marker = key + ":[";
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = text.IndexOf(']', start);
        if (end < 0) return null;
        return text.Substring(start, end - start).Trim();
    }

    private static string Scalar(string text, string key)
    {
        string marker = key + ":";
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return "";
        int start = index + marker.Length;
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ']') end++;
        return text.Substring(start, end - start).Trim();
    }

    private static int? Int(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static double? Double(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    private static string FormatVoltage(string raw)
    {
        string text = raw.Trim();
        if (text.Equals("N/A", StringComparison.OrdinalIgnoreCase) || text.Length == 0) return "—";
        if (text.EndsWith("V", StringComparison.OrdinalIgnoreCase))
        {
            string number = text.Substring(0, text.Length - 1).Trim();
            return number + " V";
        }
        return text;
    }

    private static string FormatCurrent(string raw)
    {
        string text = raw.Trim();
        if (text.Equals("N/A", StringComparison.OrdinalIgnoreCase) || text.Length == 0) return "—";
        if (text.EndsWith("mA", StringComparison.OrdinalIgnoreCase))
            return text.Substring(0, text.Length - 2).Trim() + " mA";
        return text;
    }

    private static string FormatCapacity(string raw)
    {
        string text = raw.Trim();
        if (text.Equals("N/A", StringComparison.OrdinalIgnoreCase) || text.Length == 0) return "—";
        if (text.EndsWith("mAH", StringComparison.OrdinalIgnoreCase))
            return text.Substring(0, text.Length - 3).Trim() + " mAh";
        return text;
    }

    private static string FormatTemperature(string raw)
    {
        string text = raw.Trim();
        if (text.Equals("N/A", StringComparison.OrdinalIgnoreCase) || text.Length == 0) return "—";
        if (text.EndsWith("C", StringComparison.OrdinalIgnoreCase))
            return text.Substring(0, text.Length - 1).Trim() + " °C";
        return text;
    }
}
