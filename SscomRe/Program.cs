namespace SscomRe;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--selftest"))
        {
            try
            {
                Environment.Exit(SelfTest.Run());
            }
            catch (Exception ex)
            {
                // 自检里的未处理异常要能落到 stderr，避免只看到退出码。
                Console.Error.WriteLine(ex);
                Environment.Exit(2);
            }
            return;
        }
        if (args.Contains("--bench-log"))
        {
            Environment.Exit(LogBenchmark.Run(args));
            return;
        }
        if (args.Contains("--mt-parse") || args.Contains("--mt-ui"))
        {
            Environment.Exit(MainTraceHarness.Run(args));
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 未处理异常：写日志（exe 同目录 crash.log）后再照常弹默认款对话框
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            LogCrash(e.Exception);
            using var dlg = new ThreadExceptionDialog(e.Exception);
            dlg.ShowDialog();
        };
        Application.Run(new MainForm());
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch
        {
            // 日志失败就算了，别影响崩溃对话框
        }
    }
}