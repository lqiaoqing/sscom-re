namespace SscomRe;

public sealed partial class MainForm
{
    private readonly MainTraceParser _mainTrace = new();
    private MainTraceWindow? _mainTraceWindow;
    private ToolStripMenuItem? _mainTraceItem;
    private bool _mainTraceEnabled;

    private void ToggleMainTraceWindow()
    {
        if (_mainTraceWindow is { IsDisposed: false, Visible: true })
        {
            _mainTraceWindow.Activate();
            return;
        }

        _mainTraceEnabled = true;
        _mainTraceWindow = new MainTraceWindow(() => _mainTrace.Snapshot(), MainTraceSourceLabel);
        _mainTraceWindow.FormClosed += (_, _) =>
        {
            if (_mainTraceItem is not null) _mainTraceItem.Checked = false;
            _mainTraceWindow = null;
        };
        if (_mainTraceItem is not null) _mainTraceItem.Checked = true;
        _mainTraceWindow.Show(this);
        PlaceMainTraceWindow();
    }

    private void PlaceMainTraceWindow()
    {
        if (_mainTraceWindow is null) return;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int x = Right - _mainTraceWindow.Width - 28;
        int y = Top + (MainMenuStrip?.Height ?? 0) + 48;
        if (x < area.Left) x = area.Left + 16;
        if (y < area.Top) y = area.Top + 16;
        if (x + _mainTraceWindow.Width > area.Right)
            x = Math.Max(area.Left, area.Right - _mainTraceWindow.Width - 16);
        if (y + _mainTraceWindow.Height > area.Bottom)
            y = Math.Max(area.Top, area.Bottom - _mainTraceWindow.Height - 16);
        _mainTraceWindow.Location = new Point(x, y);
    }

    private string MainTraceSourceLabel()
    {
        if (_netMode == NetMode.Serial)
        {
            string port = _port.OpenedPortName;
            if (port.Length == 0) port = SelectedSerialPortName();
            if (port.Length == 0) port = "COM";
            string baud = _baudCombo.Text.Trim();
            return port + (baud.Length > 0 ? " · " + baud : "") + " · Main Trace";
        }
        return "网络 · " + _netMode + " · Main Trace";
    }

    internal void OpenMainTraceForTest() => ToggleMainTraceWindow();
    internal void CloseMainTraceForTest() => _mainTraceWindow?.Close();
    internal bool MainTraceOpenForTest => _mainTraceWindow is { IsDisposed: false, Visible: true };
    internal string MainTraceTitleForTest => _mainTraceWindow?.Text ?? "";
    internal string MainTraceSummaryForTest => _mainTraceWindow?.SummaryForTest ?? "";
    internal bool MainTracePausedForTest => _mainTraceWindow?.PausedForTest ?? false;
    internal void ToggleMainTracePauseForTest() => _mainTraceWindow?.TogglePauseForTest();
    internal string MainTraceSourceLabelForTest => MainTraceSourceLabel();
    internal Bitmap? RenderMainTraceForTest() => _mainTraceWindow?.RenderForTest();

    internal void FeedMainTraceForTest(byte[] data)
    {
        _mainTrace.Push(data);
        _mainTraceWindow?.RefreshForTest();
    }

    internal void FeedIncomingForTest(byte[] data)
    {
        _mainTraceEnabled = true;
        HandleIncoming(data);
    }

    internal void RefreshMainTraceForTest() => _mainTraceWindow?.RefreshForTest();

    internal void NoteMainTraceTxForTest(byte[] data)
    {
        _mainTrace.NoteTx(data);
        _mainTraceWindow?.RefreshForTest();
    }

    internal void ResetMainTraceForTest() => _mainTrace.Reset();

    internal string MainTraceParserSummaryForTest
    {
        get
        {
            MainTraceView view = _mainTrace.Snapshot();
            return view.SnapshotCount + "|" + view.Snapshot.MainState + "|" + view.Snapshot.Model;
        }
    }
}
