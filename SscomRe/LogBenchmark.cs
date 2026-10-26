using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// `--bench-log [MiB] [chunk KiB] [--hex] [--shot path]`：用真实窗体日志路径跑高吞吐压测。
/// 每块只计时界面线程的 DrainDisplay + 消息泵，接收解码仍走 HandleIncoming。
/// </summary>
internal static class LogBenchmark
{
    public static int Run(string[] args)
    {
        int mebibytes = ArgValue(args, 0, 2);
        int chunkKib = Math.Max(1, ArgValue(args, 1, 30));
        bool hex = args.Any(a => string.Equals(a, "--hex", StringComparison.OrdinalIgnoreCase));
        string? shot = ShotPath(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string ini = Path.Combine(AppContext.BaseDirectory, "sscom51.ini");
        byte[]? backup = File.Exists(ini) ? File.ReadAllBytes(ini) : null;
        try
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = shot is null ? new Point(-24000, -24000) : new Point(60, 60),
            };
            form.Show();
            int scale = Math.Max(96, form.DeviceDpi);
            int logicalWidth = shot is null ? 1200 : 1000;
            int logicalHeight = shot is null ? 720 : 700;
            form.Size = new Size(logicalWidth * scale / 96, logicalHeight * scale / 96);
            Application.DoEvents();

            form.PrepareLogBenchmark(hex);
            byte[] payload = BuildPayload(mebibytes * 1024 * 1024);
            int chunk = chunkKib * 1024;
            int chunks = (payload.Length + chunk - 1) / chunk;

            double total = 0;
            double max = 0;
            var sw = Stopwatch.StartNew();
            for (int offset = 0; offset < payload.Length; offset += chunk)
            {
                int n = Math.Min(chunk, payload.Length - offset);
                var part = new byte[n];
                Buffer.BlockCopy(payload, offset, part, 0, n);
                double ms = form.FeedLogChunkForBenchmark(part);
                total += ms;
                if (ms > max) max = ms;
            }
            sw.Stop();
            double average = total / chunks;
            double throughput = mebibytes / Math.Max(0.001, sw.Elapsed.TotalSeconds);
            Console.WriteLine(
                "BENCH " +
                $"miB={mebibytes} chunkKiB={chunkKib} hex={(hex ? "yes" : "no")} chunks={chunks} " +
                $"elapsed_ms={sw.ElapsedMilliseconds} avg_chunk_ms={average:0.0} " +
                $"max_chunk_ms={max:0.0} throughput_mib_s={throughput:0.0} " +
                $"chars={form.LogTextLengthForBenchmark}");
            Console.WriteLine(
                "BENCH BREAKDOWN " +
                $"avg_decode_ms={form.BenchDecodeMs / chunks:0.0} " +
                $"avg_drain_ms={form.BenchDrainMs / chunks:0.0} " +
                $"avg_pump_ms={form.BenchPumpMs / chunks:0.0}");
            Console.WriteLine(average <= 30
                ? "BENCH RESULT: 平均单块界面耗时 <= 30ms，可跟上 1MiB/s（max 含截断整屏重画）"
                : "BENCH RESULT: 平均单块界面耗时超过 30ms，需要继续优化");

            if (shot is not null)
            {
                form.ShowBenchmarkDemo();
                Capture(form, shot);
            }
            return 0;
        }
        finally
        {
            try
            {
                if (backup is null) File.Delete(ini);
                else File.WriteAllBytes(ini, backup);
            }
            catch
            {
                // 测试收尾失败不改变退出码
            }
        }
    }

    private static int ArgValue(string[] args, int index, int fallback)
    {
        int seen = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--shot") { i++; continue; }
            if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
            if (seen++ != index) continue;
            return int.TryParse(arg, out int value) && value > 0 ? value : fallback;
        }
        return fallback;
    }

    private static string? ShotPath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--shot") return args[i + 1];
        return null;
    }

    private static void Capture(Form form, string path)
    {
        form.TopMost = true;
        form.Activate();
        form.Refresh();
        Application.DoEvents();
        Thread.Sleep(300);

        Rectangle bounds = Rectangle.Intersect(form.Bounds, Screen.FromControl(form).Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bmp);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine($"SHOT {path} {bounds.Width}x{bounds.Height}");
    }

    private static byte[] BuildPayload(int bytes)
    {
        // 每 66 字节一行，接近实际设备的文本行；保留 CR/LF 让解析/分包路径都参与。
        byte[] line = Encoding.ASCII.GetBytes(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\r\n");
        var payload = new byte[bytes];
        for (int i = 0; i < bytes; i++) payload[i] = line[i % line.Length];
        return payload;
    }
}
