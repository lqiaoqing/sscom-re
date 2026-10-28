using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SscomRe;

internal enum GnssView
{
    Waiting,
    Fixed,
    Invalid,
}

/// <summary>实时定位卡片用的一帧快照。坐标是十进制度，速度已换成 km/h。</summary>
internal readonly record struct GnssFix
{
    public GnssView View { get; init; }
    public bool HasPosition { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? AltitudeMeters { get; init; }
    public double? SpeedKmh { get; init; }
    public double? CourseDegrees { get; init; }
    public int? Satellites { get; init; }
    public double? Hdop { get; init; }
    public string GgaQuality { get; init; }
    public string GsaQuality { get; init; }
    public string Talker { get; init; }
    public string Source { get; init; }
    public bool ChecksumFailed { get; init; }
    public long UpdatedTick { get; init; }
    public long SentenceTick { get; init; }
    public int UtcSecondsOfDay { get; init; }

    public string Quality => Rank(GgaQuality) >= Rank(GsaQuality) ? GgaQuality : GsaQuality;

    public bool HasUtc => UtcSecondsOfDay >= 0;

    public static GnssFix Waiting { get; } = new()
    {
        View = GnssView.Waiting,
        GgaQuality = "",
        GsaQuality = "",
        Talker = "",
        Source = "",
        UtcSecondsOfDay = -1,
    };

    public string FormatLatitude() => FormatAngle(Latitude, 'N', 'S');
    public string FormatLongitude() => FormatAngle(Longitude, 'E', 'W');

    public string FormatAltitude() =>
        AltitudeMeters is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "—";

    public string FormatSpeed() =>
        SpeedKmh is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " km/h" : "—";

    public string FormatCourse() =>
        CourseDegrees is double v ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°" : "—";

    public string FormatUtc()
    {
        if (UtcSecondsOfDay < 0) return "";
        int h = UtcSecondsOfDay / 3600;
        int m = UtcSecondsOfDay % 3600 / 60;
        int s = UtcSecondsOfDay % 60;
        return $"{h:00}:{m:00}:{s:00} UTC";
    }

    public string FormatAge(long nowTick)
    {
        long tick = View == GnssView.Invalid ? SentenceTick : UpdatedTick;
        if (tick <= 0) return "";
        double sec = Math.Max(0, nowTick - tick) / (double)Stopwatch.Frequency;
        string text = sec < 10
            ? sec.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : Math.Floor(sec).ToString("0", CultureInfo.InvariantCulture) + "s";
        return View == GnssView.Invalid ? text + " 前" : text;
    }

    private static string FormatAngle(double value, char positive, char negative)
    {
        char hemi = value >= 0 ? positive : negative;
        string number = Math.Abs(value).ToString("0.000000", CultureInfo.InvariantCulture);
        return number + "° " + hemi;
    }

    private static int Rank(string quality) => quality switch
    {
        "RTK固定" => 5,
        "RTK浮动" => 4,
        "DGPS" => 3,
        "3D" => 2,
        "2D" => 1,
        _ => 0,
    };
}

/// <summary>
/// NMEA 0183 文本句解析。只在持有者的线程上 Push；Snapshot 可从界面线程读。
/// 校验失败保留上一帧有效定位，并标出失败。
/// </summary>
internal sealed class NmeaParser
{
    private const int MaxSentence = 256;
    private readonly object _gate = new();
    private readonly StringBuilder _buf = new();
    private bool _collecting;
    private GnssFix _fix = GnssFix.Waiting;

    public GnssFix Snapshot()
    {
        lock (_gate) return _fix;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _buf.Clear();
            _collecting = false;
            _fix = GnssFix.Waiting;
        }
    }

    public void Push(byte[] data)
    {
        if (data.Length == 0) return;
        lock (_gate)
        {
            foreach (byte b in data) PushByte(b);
        }
    }

    private void PushByte(byte b)
    {
        if (!_collecting)
        {
            if (b is (byte)'$' or (byte)'!')
            {
                _collecting = true;
                _buf.Clear();
                _buf.Append((char)b);
            }
            return;
        }
        if (b is (byte)'$' or (byte)'!')
        {
            _buf.Clear();
            _buf.Append((char)b);
            return;
        }
        if (b == (byte)'\n')
        {
            Finish();
            _collecting = false;
            _buf.Clear();
            return;
        }
        if (b == (byte)'\r') return;
        if (b is < 32 or > 126 || _buf.Length >= MaxSentence)
        {
            _collecting = false;
            _buf.Clear();
            return;
        }
        _buf.Append((char)b);
    }

    private void Finish()
    {
        string raw = _buf.ToString();
        if (raw.Length < 2) return;
        int star = raw.LastIndexOf('*');
        bool hasChecksum = star >= 1 && star + 3 <= raw.Length;
        string body = hasChecksum ? raw.Substring(1, star - 1) : raw.Substring(1);
        bool ok = hasChecksum && ChecksumMatches(raw, star);
        long now = Stopwatch.GetTimestamp();
        if (!ok)
        {
            _fix = _fix with { ChecksumFailed = true, SentenceTick = now };
            return;
        }

        string[] parts = body.Split(',');
        if (parts.Length == 0 || parts[0].Length < 5 || parts[0][0] == 'P')
        {
            _fix = _fix with { ChecksumFailed = false, SentenceTick = now };
            return;
        }
        string address = parts[0];
        string talker = address.Substring(0, 2);
        string type = address.Substring(address.Length - 3);
        GnssFix next = _fix with { ChecksumFailed = false, SentenceTick = now, Talker = talker };
        switch (type)
        {
            case "RMC": ApplyRmc(ref next, parts, now); break;
            case "GGA": ApplyGga(ref next, parts, now); break;
            case "GLL": ApplyGll(ref next, parts, now); break;
            case "GNS": ApplyGns(ref next, parts, now); break;
            case "VTG": ApplyVtg(ref next, parts); break;
            case "GSA": ApplyGsa(ref next, parts); break;
            case "ZDA": ApplyTime(ref next, Field(parts, 1)); break;
            default: break;
        }
        _fix = next;
    }

    private static void ApplyRmc(ref GnssFix fix, string[] parts, long now)
    {
        ApplyTime(ref fix, Field(parts, 1));
        bool valid = Field(parts, 2) == "A";
        bool moved = TryPosition(Field(parts, 3), Field(parts, 4), Field(parts, 5), Field(parts, 6),
            out double lat, out double lon);
        if (!valid)
        {
            MarkInvalid(ref fix, moved, lat, lon, "RMC");
            return;
        }
        if (TryDouble(Field(parts, 7), out double knots))
            fix = fix with { SpeedKmh = knots * 1.852 };
        if (TryDouble(Field(parts, 8), out double course))
            fix = fix with { CourseDegrees = course };
        if (!moved) return;
        fix = fix with
        {
            View = GnssView.Fixed,
            HasPosition = true,
            Latitude = lat,
            Longitude = lon,
            Source = "RMC",
            UpdatedTick = now,
        };
    }

    private static void ApplyGga(ref GnssFix fix, string[] parts, long now)
    {
        ApplyTime(ref fix, Field(parts, 1));
        string quality = QualityFromGga(Field(parts, 6));
        bool valid = Field(parts, 6) is not ("" or "0");
        bool moved = TryPosition(Field(parts, 2), Field(parts, 3), Field(parts, 4), Field(parts, 5),
            out double lat, out double lon);
        if (!valid)
        {
            MarkInvalid(ref fix, moved, lat, lon, "GGA");
            return;
        }
        if (int.TryParse(Field(parts, 7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sats))
            fix = fix with { Satellites = sats };
        if (TryDouble(Field(parts, 8), out double hdop))
            fix = fix with { Hdop = hdop };
        if (TryDouble(Field(parts, 9), out double alt))
            fix = fix with { AltitudeMeters = alt };
        fix = fix with { GgaQuality = quality };
        if (!moved) return;
        fix = fix with
        {
            View = GnssView.Fixed,
            HasPosition = true,
            Latitude = lat,
            Longitude = lon,
            Source = "GGA",
            UpdatedTick = now,
        };
    }

    private static void ApplyGll(ref GnssFix fix, string[] parts, long now)
    {
        ApplyTime(ref fix, Field(parts, 5));
        bool valid = Field(parts, 6) is "" or "A";
        bool moved = TryPosition(Field(parts, 1), Field(parts, 2), Field(parts, 3), Field(parts, 4),
            out double lat, out double lon);
        if (!valid)
        {
            MarkInvalid(ref fix, moved, lat, lon, "GLL");
            return;
        }
        if (!moved) return;
        fix = fix with
        {
            View = GnssView.Fixed,
            HasPosition = true,
            Latitude = lat,
            Longitude = lon,
            Source = "GLL",
            UpdatedTick = now,
        };
    }

    private static void ApplyGns(ref GnssFix fix, string[] parts, long now)
    {
        ApplyTime(ref fix, Field(parts, 1));
        string mode = Field(parts, 6);
        bool invalid = mode.Length > 0 && mode.All(ch => ch is 'N' or 'V');
        bool moved = TryPosition(Field(parts, 2), Field(parts, 3), Field(parts, 4), Field(parts, 5),
            out double lat, out double lon);
        if (invalid)
        {
            MarkInvalid(ref fix, moved, lat, lon, "GNS");
            return;
        }
        string quality = QualityFromGns(mode);
        if (quality.Length > 0) fix = fix with { GgaQuality = quality };
        if (int.TryParse(Field(parts, 7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sats))
            fix = fix with { Satellites = sats };
        if (TryDouble(Field(parts, 8), out double hdop))
            fix = fix with { Hdop = hdop };
        if (TryDouble(Field(parts, 9), out double alt))
            fix = fix with { AltitudeMeters = alt };
        if (!moved) return;
        fix = fix with
        {
            View = GnssView.Fixed,
            HasPosition = true,
            Latitude = lat,
            Longitude = lon,
            Source = "GNS",
            UpdatedTick = now,
        };
    }

    private static void ApplyVtg(ref GnssFix fix, string[] parts)
    {
        if (TryDouble(Field(parts, 1), out double course))
            fix = fix with { CourseDegrees = course };
        if (TryDouble(Field(parts, 7), out double kmh))
            fix = fix with { SpeedKmh = kmh };
        else if (TryDouble(Field(parts, 5), out double knots))
            fix = fix with { SpeedKmh = knots * 1.852 };
    }

    private static void ApplyGsa(ref GnssFix fix, string[] parts)
    {
        string dimensional = Field(parts, 2) switch
        {
            "3" => "3D",
            "2" => "2D",
            _ => "",
        };
        fix = fix with { GsaQuality = dimensional };
        if (parts.Length > 16 && TryDouble(parts[16], out double hdop))
            fix = fix with { Hdop = hdop };
    }

    private static void MarkInvalid(ref GnssFix fix, bool moved, double lat, double lon, string source)
    {
        if (moved)
        {
            fix = fix with
            {
                View = GnssView.Invalid,
                HasPosition = true,
                Latitude = lat,
                Longitude = lon,
                Source = source,
            };
            return;
        }
        fix = fix with { View = GnssView.Invalid, Source = source };
    }

    private static void ApplyTime(ref GnssFix fix, string text)
    {
        if (text.Length < 6) return;
        if (!int.TryParse(text.Substring(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return;
        if (!int.TryParse(text.Substring(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)) return;
        if (!int.TryParse(text.Substring(4, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) return;
        if (h is < 0 or > 23 || m is < 0 or > 59 || s is < 0 or > 60) return;
        fix = fix with { UtcSecondsOfDay = h * 3600 + m * 60 + Math.Min(s, 59) };
    }

    private static bool TryPosition(string latText, string ns, string lonText, string ew, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (!TryAngle(latText, out lat) || !TryAngle(lonText, out lon)) return false;
        if (ns == "S") lat = -lat;
        if (ew == "W") lon = -lon;
        return ns is "N" or "S" && ew is "E" or "W";
    }

    /// <summary>ddmm.mmmm 或 dddmm.mmmm。分永远占小数点前两位，所以都除以 100。</summary>
    private static bool TryAngle(string text, out double degrees)
    {
        degrees = 0;
        if (text.Length < 4) return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double packed)) return false;
        double whole = Math.Floor(packed / 100.0);
        double minutes = packed - whole * 100.0;
        if (minutes >= 60) return false;
        degrees = whole + minutes / 60.0;
        return true;
    }

    private static string QualityFromGga(string quality) => quality switch
    {
        "4" => "RTK固定",
        "5" => "RTK浮动",
        "2" => "DGPS",
        _ => "",
    };

    private static string QualityFromGns(string mode)
    {
        if (mode.Contains('R')) return "RTK固定";
        if (mode.Contains('F')) return "RTK浮动";
        if (mode.Contains('D')) return "DGPS";
        return "";
    }

    private static bool ChecksumMatches(string raw, int star)
    {
        if (star + 3 > raw.Length) return false;
        int cs = 0;
        for (int i = 1; i < star; i++) cs ^= raw[i];
        return int.TryParse(raw.Substring(star + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int got)
            && got == (cs & 0xFF);
    }

    private static string Field(string[] parts, int index) => index < parts.Length ? parts[index] : "";

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
