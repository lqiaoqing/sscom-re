using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SscomRe;

/// <summary>接收设置里的持续 HTML 记录：目录、分卷、调色板与状态。</summary>
public sealed partial class MainForm
{
    private void OnRecvHtmlChanged()
    {
        if (_suppressHtmlCheck) return;
        if (_recvHtmlBox.Checked) StartHtmlCapture();
        else StopHtmlCapture(showToast: true);
    }

    internal const int HtmlSettingsVersion = 2;

    /// <summary>旧版本把 10MB 当默认值；没有设置版本时迁移到新的 50MB 默认。</summary>
    internal static int NormalizeRecvHtmlSplitMb(int splitMb, int settingsVersion) =>
        settingsVersion < HtmlSettingsVersion && splitMb == 10 ? 50 : splitMb;

    private void StartHtmlCapture()
    {
        _htmlRecorder?.Dispose();
        _htmlRecorder = null;

        string dir = ResolveHtmlDir();
        HtmlLogRecorder? recorder = null;
        try
        {
            Directory.CreateDirectory(dir);
            _recvHtmlDirBox.Text = dir;
            LogHighlightPalette palette = _recvHtmlPaletteCombo.SelectedIndex == 0
                ? Theme.DocumentPalette()
                : CurrentLogPalette();
            recorder = new HtmlLogRecorder(dir, CurrentTransferName(), HtmlSplitMb() * 1024 * 1024,
                palette, _hexDisplayBox.Checked, _recvHtmlTxBox.Checked, _highlightRules);
            recorder.Start();
            _htmlRecorder = recorder;
            SetHtmlOptionsEnabled(false);
            _recvHtmlStatus.Text = "记录中：第 1 卷 · 0 B";
            _rowTip.SetToolTip(_recvHtmlStatus, recorder.CurrentFilePath);
            ShowToast("开始记录 HTML 日志");
            AutoSave();
        }
        catch (Exception ex)
        {
            try { recorder?.Dispose(); } catch { }
            _htmlRecorder = null;
            _suppressHtmlCheck = true;
            _recvHtmlBox.Checked = false;
            _suppressHtmlCheck = false;
            SetHtmlOptionsEnabled(true);
            _recvHtmlStatus.Text = "未开始记录（勾选后开始）";
            MessageBox.Show(this, "无法开始记录 HTML 日志：\r\n" + ex.Message,
                "持续记录 HTML", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopHtmlCapture(bool showToast)
    {
        HtmlLogRecorder? recorder = _htmlRecorder;
        _htmlRecorder = null;
        if (recorder is not null)
        {
            try { recorder.Dispose(); } catch { /* 关闭失败不影响取消勾选 */ }
            if (showToast) ShowToast("HTML 记录已停止");
            AutoSave();
        }
        SetHtmlOptionsEnabled(true);
        if (_recvHtmlStatus is not null)
        {
            _recvHtmlStatus.Text = "未开始记录（勾选后开始）";
            _rowTip.SetToolTip(_recvHtmlStatus, "");
        }
    }

    private void UpdateHtmlCaptureStatus()
    {
        if (_recvHtmlStatus is null) return;
        if (_htmlRecorder is null)
        {
            const string idle = "未开始记录（勾选后开始）";
            if (_recvHtmlStatus.Text != idle) _recvHtmlStatus.Text = idle;
            return;
        }
        if (_htmlRecorder.LastError is { } error)
        {
            HtmlLogRecorder failed = _htmlRecorder;
            _htmlRecorder = null;
            try { failed.Dispose(); } catch { }
            _suppressHtmlCheck = true;
            _recvHtmlBox.Checked = false;
            _suppressHtmlCheck = false;
            SetHtmlOptionsEnabled(true);
            _recvHtmlStatus.Text = "记录失败：" + error;
            ShowToast("HTML 记录已停止：" + error);
            return;
        }
        string text = "记录中：第 " + _htmlRecorder.PartCount + " 卷 · "
            + FormatBytes(_htmlRecorder.TotalBytes);
        if (_recvHtmlStatus.Text != text)
        {
            _recvHtmlStatus.Text = text;
            _rowTip.SetToolTip(_recvHtmlStatus, _htmlRecorder.CurrentFilePath);
        }
    }

    private void PickHtmlDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择 HTML 日志保存目录",
            SelectedPath = ResolveHtmlDir(),
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _recvHtmlDirBox.Text = dlg.SelectedPath;
        AutoSave();
    }

    private void OpenHtmlDir()
    {
        string dir = ResolveHtmlDir();
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast("无法打开目录：" + ex.Message);
        }
    }

    private string ResolveHtmlDir()
    {
        string dir = _recvHtmlDirBox.Text.Trim();
        return dir.Length == 0 ? Directory.GetCurrentDirectory() : dir;
    }

    private int HtmlSplitMb() => _recvHtmlSplitCombo.SelectedIndex switch
    {
        0 => 0,
        1 => 10,
        2 => 25,
        3 => 50,
        4 => 100,
        _ => 50,
    };

    private void SetHtmlOptionsEnabled(bool enabled)
    {
        _recvHtmlDirBox.Enabled = enabled;
        _recvHtmlSplitCombo.Enabled = enabled;
        _recvHtmlPaletteCombo.Enabled = enabled;
        _recvHtmlTxBox.Enabled = enabled;
        _recvHtmlPickButton.Enabled = enabled;
    }

    private string CurrentTransferName()
    {
        if (_netMode == NetMode.TcpClient) return "TCPCLIENT";
        if (_netMode == NetMode.TcpServer) return "TCPSERVER";
        if (_netMode == NetMode.Udp) return "UDP";
        string piece = _port.IsOpen ? _port.OpenedPortName : ComPortService.NormalizePortName(_portCombo.Text);
        return piece.Length == 0 ? "COM" : piece;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
    }

    internal bool HtmlCaptureActiveForTest => _htmlRecorder is not null;

    internal void StartHtmlCaptureForTest(string dir, int splitMb, bool light, bool includeTx)
    {
        _recvHtmlDirBox.Text = dir;
        _recvHtmlSplitCombo.SelectedIndex = splitMb switch
        {
            0 => 0,
            10 => 1,
            25 => 2,
            100 => 4,
            _ => 3,
        };
        _recvHtmlPaletteCombo.SelectedIndex = light ? 0 : 1;
        _recvHtmlTxBox.Checked = includeTx;
        _recvHtmlBox.Checked = true;
    }

    internal void StopHtmlCaptureForTest() => _recvHtmlBox.Checked = false;
}
