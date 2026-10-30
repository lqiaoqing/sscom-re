using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SscomRe;

/// <summary>开发诊断：把 sscomRe 的 HTML 日志回放给 MainTraceParser，打印解析结果。</summary>
internal static class MainTraceHarness
{
    private static readonly Regex StylePattern = new("<style[\\s\\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex PrefixPattern = new(
        @"^\[(\d\d:\d\d:\d\d\.\d\d\d)\](收←◆|发→◇)", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        int at = Array.IndexOf(args, "--mt-parse");
        int ui = Array.IndexOf(args, "--mt-ui");
        if (ui >= 0 && ui + 2 < args.Length && File.Exists(args[ui + 1]))
        {
            MainTraceParser uiParser = ParseFile(args[ui + 1]);
            if (args.Contains("--unobserved"))
            {
                Feed(uiParser, "encode report ok(900):{\"sta\":{\"doorStatus\":1}}\r\n");
                Feed(uiParser, "mqtt publish enter ok topic=VS2310/866784060478296/report len=900\r\n");
                Feed(uiParser, "mqtt client 0 publish \"VS2310/866784060478296/report\" \"{}\" ok, msg_id=1, result=0\r\n");
                Feed(uiParser, "#19:07:31 VS2310:[1.13.16,4.0.15,48MHz,WORKING,IDLE] MOD:[CHN-CT,EUTRAN,POWERSAVE,INQ_IP,1] MQTT:[Idle 16] QHTTP:[Idle 1673] SIG:100 GPS:[ON,2,0.5,22.70459,113.81208,0km/h,N,IDL,2m] f_spd:[0km/h] BAT:[0,4.17V,0mA,100%,9787mAH,27.6C] DOOR:[CLOSE,2] RPD:[1] MCU_BLE:[11313,FORCE] SOC_BLE:[21319,PERIOD] TPMS:[1] THS:[1] SOC:[10078] SOC_ERR:[0] LDR:[792] TH:[N/A,N/A]  MEM:32\r\n");
            }
            if (args.Contains("--down-demo"))
            {
                Feed(uiParser, "receive new cmd: {\"cmd\":\"SnapShot\"}\r\n");
                Feed(uiParser, "receive new cfg: {\"setting\":{\"report_interval\":300}}\r\n");
                Feed(uiParser, "receive new upgrade: {\"upgrade\":\"demo\"}\r\n");
                Feed(uiParser, "#19:07:31 VS2310:[1.13.16,4.0.15,48MHz,WORKING,IDLE] MOD:[CHN-CT,EUTRAN,POWERSAVE,INQ_IP,1] MQTT:[Idle 16] QHTTP:[Idle 1673] SIG:100 GPS:[ON,2,0.5,22.70459,113.81208,0km/h,N,IDL,2m] f_spd:[0km/h] BAT:[0,4.17V,0mA,100%,9787mAH,27.6C] DOOR:[CLOSE,2] RPD:[1] MCU_BLE:[11313,FORCE] SOC_BLE:[21319,PERIOD] TPMS:[1] THS:[1] SOC:[10078] SOC_ERR:[0] LDR:[792] TH:[N/A,N/A]  MEM:32\r\n");
            }
            if (args.Contains("--up-demo"))
            {
                for (int i = 0; i < 5; i++)
                {
                    Feed(uiParser, "encode report ok(" + (900 + i) + "):{\"sta\":{\"doorStatus\":0}}\r\n");
                    Feed(uiParser, "mqtt publish enter ok topic=VS2310/866784060478296/report len=" + (900 + i) + "\r\n");
                    Feed(uiParser, "mqtt client 0 publish \"t\" \"{}\" ok, msg_id=" + i + ", result=0\r\n");
                    Feed(uiParser, "gps payload pubilsh success\r\n");
                }
                Feed(uiParser, "#19:07:31 VS2310:[1.13.16,4.0.15,48MHz,WORKING,IDLE] MOD:[CHN-CT,EUTRAN,POWERSAVE,INQ_IP,1] MQTT:[Idle 16] QHTTP:[Idle 1673] SIG:100 GPS:[ON,2,0.5,22.70459,113.81208,0km/h,N,IDL,2m] f_spd:[0km/h] BAT:[0,4.17V,0mA,100%,9787mAH,27.6C] DOOR:[CLOSE,2] RPD:[1] MCU_BLE:[11313,FORCE] SOC_BLE:[21319,PERIOD] TPMS:[1] THS:[1] SOC:[10078] SOC_ERR:[0] LDR:[792] TH:[N/A,N/A]  MEM:32\r\n");
            }
            if (args.Contains("--light")) Theme.SetLight(true);
            Application.EnableVisualStyles();
            using var window = new MainTraceWindow(() => uiParser.Snapshot(),
                () => "COM38 · 921600 · Main Trace");
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(-20000, -20000);
            window.Show();
            Application.DoEvents();
            window.RefreshForTest();
            if (args.Contains("--down")) window.ShowDownForTest();
            Application.DoEvents();
            using Bitmap bitmap = window.RenderForTest();
            bitmap.Save(args[ui + 2], System.Drawing.Imaging.ImageFormat.Png);
            window.Close();
            Console.WriteLine("saved=" + args[ui + 2] + " size=" + bitmap.Width + "x" + bitmap.Height);
            return 0;
        }
        if (at < 0 || at + 1 >= args.Length || !File.Exists(args[at + 1]))
        {
            Console.Error.WriteLine("用法: --mt-parse <ReceivedHtml-....html> 或 --mt-ui <html> <png>");
            return 2;
        }

        MainTraceParser parser = ParseFile(args[at + 1]);
        MainTraceView view = parser.Snapshot();
        Console.WriteLine($"model={view.Snapshot.Model} fw={view.Snapshot.Firmware} bl={view.Snapshot.Bootloader}");
        Console.WriteLine($"states={view.Snapshot.MainState}/{view.Snapshot.SubState} modem={view.Snapshot.ModemState} mqtt={view.Snapshot.MqttSummary} qhttp={view.Snapshot.QhttpSummary}");
        Console.WriteLine($"clock={view.Snapshot.DeviceTime} offset={view.Snapshot.ClockOffsetText} gps={view.Snapshot.GpsText} coord={view.Snapshot.CoordinatesText}");
        Console.WriteLine($"bat={view.Snapshot.BatteryText} cur={view.Snapshot.CurrentText} cap={view.Snapshot.CapacityText} door=[{view.Snapshot.DoorState}] socErr=[{view.Snapshot.SocError}] rawLen={view.Snapshot.Raw.Length}");
        Console.WriteLine("rawTail=" + (view.Snapshot.Raw.Length > 180 ? view.Snapshot.Raw.Substring(view.Snapshot.Raw.Length - 180) : view.Snapshot.Raw));
        Console.WriteLine($"snapshots={view.SnapshotCount} malformed={view.MalformedSnapshots} logical={view.LogicalMessageCount} events={view.EventCount} tx={view.TxCount} downlink={view.DownlinkCount}");
        Console.WriteLine($"socErrSnapshots={view.SocErrorSnapshots} doorInvalid={view.DoorInvalidSnapshots} socSummary={view.SocErrorSummary}");
        Console.WriteLine($"reports={view.ReportCount} success={view.ReportSuccessCount} unobserved={view.ReportUnobservedCount}");
        MainTraceReport? last = view.LastReport;
        if (last is not null)
            Console.WriteLine($"lastReport bytes={last.Bytes} json={last.JsonComplete} outcome={last.Outcome} text={last.OutcomeText} api={last.ApiResult} topic={last.Topic} door={last.DoorStatus} band={last.Band} lac={last.Lac} cid={last.Cid} ths={last.ThsText} doors={last.DoorCount}");
        Console.WriteLine("reportsDetail=");
        foreach (MainTraceReport report in view.Reports)
            Console.WriteLine($"  {report.EncodedAt:HH:mm:ss.fff} {report.Bytes}B json={report.JsonComplete} success={report.BusinessSuccess} api=[{report.ApiResult}] failed={report.Failed} topic={report.Topic}");
        Console.WriteLine("alerts=" + string.Join(" | ", view.Alerts));
        Console.WriteLine("links=" + string.Join(" | ",
            view.LinkStages.Select(stage => stage.Label + ":" + stage.Value + "/" + stage.State)));
        Console.WriteLine("eventsTop=");
        foreach (MainTraceEvent ev in view.Events.Take(8))
            Console.WriteLine($"  {ev.Time:HH:mm:ss.fff} [{ev.Tag}] {ev.Text} ({ev.Source})");
        return 0;
    }

    private static MainTraceParser ParseFile(string path)
    {
        string html = File.ReadAllText(path);
        string text = StylePattern.Replace(html, "");
        text = TagPattern.Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        var parser = new MainTraceParser();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string trim = line.TrimEnd();
            if (trim.Length == 0) continue;
            Match match = PrefixPattern.Match(trim);
            if (match.Success)
            {
                string body = trim.Substring(match.Length);
                if (match.Groups[2].Value == "发→◇")
                    parser.NoteTx(Encoding.UTF8.GetBytes(body));
                else
                    Feed(parser, body + "\r\n");
            }
            else
            {
                Feed(parser, trim + "\r\n");
            }
        }
        return parser;
    }

    private static void Feed(MainTraceParser parser, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        const int chunk = 137;
        for (int offset = 0; offset < bytes.Length; offset += chunk)
        {
            int length = Math.Min(chunk, bytes.Length - offset);
            var part = new byte[length];
            Buffer.BlockCopy(bytes, offset, part, 0, length);
            parser.Push(part);
        }
    }
}
