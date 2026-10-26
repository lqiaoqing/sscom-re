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
            Environment.Exit(SelfTest.Run());
            return;
        }
        if (args.Contains("--bench-log"))
        {
            Environment.Exit(LogBenchmark.Run(args));
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