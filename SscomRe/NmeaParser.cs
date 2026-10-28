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

/// <summary>GSV 里的一颗卫星；仰角/方位/信噪比缺失时为 null。</summary>
internal readonly record struct GnssSatellite(string Constellation, int Prn, int? Elevation, int? Azimuth, int? Snr)
{
    public string Code => Constellation switch
    {
        "GPS" => "G",
        "BeiDou" => "C",
        "Galileo" => "E",
        "GLONASS" => "R",
        "QZSS" => "J",
        "NavIC" => "I",
        "SBAS" => "S",
        _ => "M",
    };

    public string DisplayName => Constellation switch
    {
        "GPS" => "GPS",
        "BeiDou" => "北斗",
        "Galileo" => "Galileo",
        "GLONASS" => "GLONASS",
        "QZSS" => "QZSS",
        "NavIC" => "NavIC",
        "SBAS" => "SBAS",
        _ => "GNSS",
    };
}

/// <summary>实时定位卡片用的一帧快照。坐标是十进制度，速度已换成 km/h。</summary>
internal readonly record struct GnssFix
{
    public GnssView View { get; init; }
    public bool HasPosition { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? AltitudeMeters { get; init; }
    public double? GeoidSeparationMeters { get; init; }
    public double? SpeedKmh { get; init; }
    public double? CourseDegrees { get; init; }
    public int? Satellites { get; init; }
    public double? Hdop { get; init; }
    public double? Pdop { get; init; }
    public double? Vdop { get; init; }
    public double? DifferentialAgeSeconds { get; init; }
    public string BaseStationId { get; init; } = "";
    public int VisibleSatellites { get; init; }
    public IReadOnlyList<GnssSatellite> SatellitesInView { get; init; } = Array.Empty<GnssSatellite>();
    public string GgaQuality { get; init; }
    public string GsaQuality { get; init; }
    public string Talker { get; init; }
    public string Source { get; init; }
    public bool ChecksumFailed { get; init; }
    public long UpdatedTick { get; init; }
    public long SentenceTick { get; init; }
    public int UtcSecondsOfDay { get; init; }

    public GnssFix()
    {
        GgaQuality = "";
        GsaQuality = "";
        Talker = "";
        Source = "";
        BaseStationId = "";
        SatellitesInView = Array.Empty<GnssSatellite>();
    }

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
    public string FormatLatitudeDms() => FormatDms(Latitude, 'N', 'S');
    public string FormatLongitudeDms() => FormatDms(Longitude, 'E', 'W');

    public string FormatAltitude() =>
        AltitudeMeters is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "—";

    public string FormatGeoid() =>
        GeoidSeparationMeters is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "—";

    public string FormatSpeed() =>
        SpeedKmh is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " km/h" : "—";

    public string FormatSpeedKnots() =>
        SpeedKmh is double v ? (v / 1.852).ToString("0.0", CultureInfo.InvariantCulture) + " kn" : "—";

    public string FormatCourse() =>
        CourseDegrees is double v ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°" : "—";

    public string FormatCourseCardinal() => CourseDegrees is double v
        ? new[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" }[
            (int)Math.Round(((v % 360) + 360) % 360 / 45.0) % 8]
        : "—";

    public string FormatDiffAge() =>
        DifferentialAgeSeconds is double v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "—";

    public string FormatBaseStation() => BaseStationId.Length > 0 ? "基准 " + BaseStationId : "基准 —";

    public string FormatHdop() => Hdop is double v ? "HDOP " + v.ToString("0.0#", CultureInfo.InvariantCulture) : "HDOP —";
    public string FormatVdop() => Vdop is double v ? "VDOP " + v.ToString("0.0#", CultureInfo.InvariantCulture) : "VDOP —";
    public string FormatPdop() => Pdop is double v ? "PDOP " + v.ToString("0.0#", CultureInfo.InvariantCulture) : "PDOP —";

    public string FormatQuality() => Quality switch
    {
        "RTK固定" => "RTK 固定解",
        "RTK浮动" => "RTK 浮动解",
        "DGPS" => "DGPS",
        "3D" => "3D 定位",
        "2D" => "2D 定位",
        _ => "",
    };

    public string FormatUtc()
    {
        if (UtcSecondsOfDay < 0) return "";
        int h = UtcSecondsOfDay / 3600;
        int m = UtcSecondsOfDay % 3600 / 60;
        int s = UtcSecondsOfDay % 60;
        return $"{h:00}:{m:00}:{s:00} UTC";
    }

    public string FormatLocalTime()
    {
        if (UtcSecondsOfDay < 0) return "";
        int offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
        int seconds = (UtcSecondsOfDay + offset) % 86400;
        if (seconds < 0) seconds += 86400;
        return $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
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

    private static string FormatDms(double value, char positive, char negative)
    {
        char hemi = value >= 0 ? positive : negative;
        double abs = Math.Abs(value);
        int degrees = (int)abs;
        double minutesFull = (abs - degrees) * 60;
        int minutes = (int)minutesFull;
        double seconds = Math.Round((minutesFull - minutes) * 60, 2);
        if (seconds >= 60)
        {
            seconds = 0;
            minutes++;
        }
        if (minutes >= 60)
        {
            minutes = 0;
            degrees++;
        }
        return $"{degrees}° {minutes:00}′ {seconds:00.00}″ {hemi}";
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
    private readonly Dictionary<(string Constellation, int Prn), (GnssSatellite Satellite, long SeenTick)> _satellites = new();
    private readonly Dictionary<string, int> _visibleTotals = new();
    private readonly Dictionary<string, long> _constellationSeen = new();
    private static readonly long SatelliteTtlTicks = Stopwatch.Frequency * 6;
    private bool _collecting;
    private GnssFix _fix = GnssFix.Waiting;

    public GnssFix Snapshot()
    {
        lock (_gate)
        {
            PruneSatellites(Stopwatch.GetTimestamp());
            _fix = _fix with
            {
                SatellitesInView = SatelliteSnapshot(),
                VisibleSatellites = VisibleCount(),
            };
            return _fix;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _buf.Clear();
            _collecting = false;
            _satellites.Clear();
            _visibleTotals.Clear();
            _constellationSeen.Clear();
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
            case "GSV": ApplyGsv(ref next, parts, talker, now); break;
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
        if (TryDouble(Field(parts, 11), out double geoid))
            fix = fix with { GeoidSeparationMeters = geoid };
        if (TryDouble(Field(parts, 13), out double diffAge))
            fix = fix with { DifferentialAgeSeconds = diffAge };
        string baseId = Field(parts, 14);
        if (baseId.Length > 0)
            fix = fix with { BaseStationId = baseId };
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
        if (TryDouble(Field(parts, 15), out double pdop))
            fix = fix with { Pdop = pdop };
        if (TryDouble(Field(parts, 16), out double hdop))
            fix = fix with { Hdop = hdop };
        if (TryDouble(Field(parts, 17), out double vdop))
            fix = fix with { Vdop = vdop };
    }

    private void ApplyGsv(ref GnssFix fix, string[] parts, string talker, long now)
    {
        string constellation = ConstellationFromTalker(talker);
        if (int.TryParse(Field(parts, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
            _visibleTotals[constellation] = total;
        _constellationSeen[constellation] = now;

        for (int i = 4; i + 3 < parts.Length; i += 4)
        {
            if (!int.TryParse(Field(parts, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int prn) || prn <= 0)
                continue;
            int? elevation = NullableInt(Field(parts, i + 1));
            int? azimuth = NullableInt(Field(parts, i + 2));
            int? snr = NullableInt(Field(parts, i + 3));
            string satelliteConstellation = NormalizeConstellation(constellation, prn);
            _satellites[(satelliteConstellation, prn)] =
                (new GnssSatellite(satelliteConstellation, prn, elevation, azimuth, snr), now);
        }

        PruneSatellites(now);
        fix = fix with
        {
            SatellitesInView = SatelliteSnapshot(),
            VisibleSatellites = VisibleCount(),
        };
    }

    private void PruneSatellites(long now)
    {
        var stale = new List<(string Constellation, int Prn)>();
        foreach (var pair in _satellites)
            if (now - pair.Value.SeenTick > SatelliteTtlTicks)
                stale.Add(pair.Key);
        foreach (var key in stale) _satellites.Remove(key);

        var staleSystems = new List<string>();
        foreach (var pair in _constellationSeen)
            if (now - pair.Value > SatelliteTtlTicks)
                staleSystems.Add(pair.Key);
        foreach (string system in staleSystems)
        {
            _constellationSeen.Remove(system);
            _visibleTotals.Remove(system);
        }
    }

    private GnssSatellite[] SatelliteSnapshot() => _satellites.Values
        .Select(pair => pair.Satellite)
        .OrderByDescending(sat => sat.Snr ?? -1)
        .ThenByDescending(sat => sat.Elevation ?? -1)
        .ThenBy(sat => sat.Constellation, StringComparer.Ordinal)
        .ThenBy(sat => sat.Prn)
        .ToArray();

    private int VisibleCount()
    {
        int total = 0;
        foreach (int value in _visibleTotals.Values) total += value;
        return Math.Max(total, _satellites.Count);
    }

    private static string ConstellationFromTalker(string talker) => talker switch
    {
        "GP" => "GPS",
        "GL" => "GLONASS",
        "GA" => "Galileo",
        "GB" or "BD" => "BeiDou",
        "GQ" => "QZSS",
        "GI" => "NavIC",
        _ => "GNSS",
    };

    /// <summary>部分接收机把 QZSS/SBAS 混在 GPGSV 里，用 PRN 区间纠正星座。</summary>
    private static string NormalizeConstellation(string constellation, int prn)
    {
        if (constellation != "GPS") return constellation;
        if (prn is >= 193 and <= 202) return "QZSS";
        if (prn is >= 120 and <= 158) return "SBAS";
        return constellation;
    }

    private static int? NullableInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

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
