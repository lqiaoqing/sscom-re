using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>轻量 toast。</summary>
public sealed partial class MainForm
{
    private ToastPanel _toast = null!;
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2200 };
    private bool _composerShown = true;

    private void BuildToast()
    {
        _toast = new ToastPanel { Visible = false };
        // 挂在日志区上。若挂到窗体，显示时会带动主菜单重新停靠，菜单栏会抖一下。
        _logHost.Controls.Add(_toast);
    }

    private void WireMotion()
    {
        _toastTimer.Tick += (_, _) => HideToast();
        FormClosed += (_, _) => _toastTimer.Stop();
    }

    internal void ShowToast(string message, ToastTone tone = ToastTone.Info)
    {
        if (_toast is null || !IsHandleCreated || _recvBox is null) return;
        int margin = Theme.Scale(this, 12);
        int max = Math.Min(Theme.Scale(this, 460),
            Math.Max(Theme.Scale(this, 180), _recvBox.Width - margin * 2));
        _toast.SetMessage(message, tone, max);
        PositionToast();
        _toast.Visible = true;
        _toast.BringToFront();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    internal void HideToast()
    {
        _toastTimer.Stop();
        if (_toast is { Visible: true }) _toast.Visible = false;
    }

    internal void ToggleComposerForTest() => ToggleBottomPanels();
    internal bool ComposerVisibleForTest => _sendArea?.Visible == true;

    private void PositionToast()
    {
        Control host = _toast.Parent ?? _logHost;
        if (host is null || !host.IsHandleCreated) return;
        int margin = Theme.Scale(this, 12);
        int x = Math.Max(margin, (host.ClientSize.Width - _toast.Width) / 2);
        int y = host.ClientSize.Height - _toast.Height - margin;
        _toast.Location = new Point(x, Math.Max(margin, y));
    }

}
