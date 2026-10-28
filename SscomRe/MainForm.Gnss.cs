namespace SscomRe;

public sealed partial class MainForm
{
    private readonly NmeaParser _nmea = new();
    private volatile bool _snapGnss;
    private GnssWindow? _gnssWindow;
    private ToolStripMenuItem? _gnssItem;

    private void ToggleGnssWindow()
    {
        if (_gnssWindow is { IsDisposed: false, Visible: true })
        {
            _gnssWindow.Activate();
            return;
        }
        _nmea.Reset();
        _snapGnss = true;
        _gnssWindow = new GnssWindow(() => _nmea.Snapshot());
        _gnssWindow.FormClosed += (_, _) =>
        {
            _snapGnss = false;
            _nmea.Reset();
            if (_gnssItem is not null) _gnssItem.Checked = false;
            _gnssWindow = null;
        };
        if (_gnssItem is not null) _gnssItem.Checked = true;
        _gnssWindow.Show(this);
        PlaceGnssWindow();
    }

    private void PlaceGnssWindow()
    {
        if (_gnssWindow is null) return;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int x = Right - _gnssWindow.Width - 28;
        int y = Top + (MainMenuStrip?.Height ?? 0) + 48;
        if (x < area.Left) x = area.Left + 16;
        if (y < area.Top) y = area.Top + 16;
        if (x + _gnssWindow.Width > area.Right) x = Math.Max(area.Left, area.Right - _gnssWindow.Width - 16);
        if (y + _gnssWindow.Height > area.Bottom) y = Math.Max(area.Top, area.Bottom - _gnssWindow.Height - 16);
        _gnssWindow.Location = new Point(x, y);
    }

    internal void OpenGnssForTest() => ToggleGnssWindow();
    internal void CloseGnssForTest() => _gnssWindow?.Close();
    internal bool GnssOpenForTest => _gnssWindow is { IsDisposed: false, Visible: true };
    internal bool GnssParsingForTest => _snapGnss;
    internal string GnssTitleForTest => _gnssWindow?.Text ?? "";
    internal string GnssSummaryForTest => _gnssWindow?.SummaryForTest ?? "";

    internal void FeedGnssForTest(byte[] data)
    {
        _snapGnss = true;
        _nmea.Push(data);
        _gnssWindow?.RefreshForTest();
    }
}
