using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>轻量 toast 与面板短动效。动效只保留一个 15ms 计时器，结束后立即停止。</summary>
public sealed partial class MainForm
{
    private ToastPanel _toast = null!;
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2200 };
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 15 };
    private readonly Stopwatch _animClock = new();
    private Action<int>? _animStep;
    private Action? _animDone;
    private int _animFrom;
    private int _animTo;
    private int _animMs;
    private bool _composerShown = true;
    private bool _animatingComposer;
    private bool _animatingSplit;

    /// <summary>自检可关闭动效，布局断言仍按最终状态执行。</summary>
    internal bool AnimationsEnabled { get; set; } = true;

    private void BuildToast()
    {
        _toast = new ToastPanel { Visible = false };
        Controls.Add(_toast);
    }

    private void WireMotion()
    {
        _toastTimer.Tick += (_, _) => HideToast();
        _animTimer.Tick += (_, _) => AnimTick();
        FormClosed += (_, _) =>
        {
            _toastTimer.Stop();
            _animTimer.Stop();
        };
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
    internal bool ComposerVisibleForTest => _composerPanel?.Visible == true;
    internal bool AnimationRunningForTest => _animTimer.Enabled;

    private void PositionToast()
    {
        if (_toast is null || _recvBox is null || !_recvBox.IsHandleCreated) return;
        Point log = PointToClient(_recvBox.PointToScreen(Point.Empty));
        int margin = Theme.Scale(this, 12);
        int x = log.X + Math.Max(margin, (_recvBox.Width - _toast.Width) / 2);
        int y = log.Y + _recvBox.Height - _toast.Height - margin;
        int maxX = Math.Max(margin, ClientSize.Width - _toast.Width - margin);
        int maxY = Math.Max(margin, ClientSize.Height - _toast.Height - margin);
        _toast.Location = new Point(Math.Min(Math.Max(margin, x), maxX),
            Math.Min(Math.Max(margin, y), maxY));
    }

    private void AnimateValue(int from, int to, int durationMs, Action<int> step, Action? done = null)
    {
        if (!AnimationsEnabled || from == to || durationMs <= 0 || !IsHandleCreated)
        {
            step(to);
            done?.Invoke();
            return;
        }
        FinishAnimation();
        _animFrom = from;
        _animTo = to;
        _animMs = durationMs;
        _animStep = step;
        _animDone = done;
        _animClock.Restart();
        _animTimer.Start();
    }

    private void AnimTick()
    {
        if (_animStep is null)
        {
            _animTimer.Stop();
            return;
        }
        double t = Math.Min(1.0, _animClock.Elapsed.TotalMilliseconds / _animMs);
        double eased = 1 - Math.Pow(1 - t, 3);
        int value = (int)Math.Round(_animFrom + (_animTo - _animFrom) * eased);
        _animStep(value);
        if (t >= 1.0) FinishAnimation();
    }

    private void FinishAnimation()
    {
        if (_animStep is null)
        {
            _animTimer.Stop();
            return;
        }
        _animTimer.Stop();
        Action<int> step = _animStep;
        Action? done = _animDone;
        _animStep = null;
        _animDone = null;
        step(_animTo);
        done?.Invoke();
    }

    private void AnimateSidebar(int from, int to, bool collapsed)
    {
        AnimateValue(from, to, 140, width =>
        {
            if (_bodyLayout is null) return;
            _bodyLayout.ColumnStyles[0].Width = width;
            _sidePanel?.PerformLayout();
            _bodyLayout.PerformLayout();
        }, () =>
        {
            if (_sideContent is not null) _sideContent.Visible = !collapsed;
            if (_railPanel is not null) _railPanel.Visible = collapsed;
            if (_sidePanel is not null) _sidePanel.AutoScroll = !collapsed;
            _sidePanel?.PerformLayout();
            _bodyLayout?.PerformLayout();
            UpdateSidebarRail();
        });
    }

    private void AnimateComposer(bool show)
    {
        if (_centerLayout is null || _centerLayout.RowStyles.Count < 3 || _composerPanel is null)
        {
            if (_composerPanel is not null) _composerPanel.Visible = show;
            UpdateComposerLayout();
            return;
        }
        FinishAnimation();
        _animatingComposer = true;
        int from = (int)Math.Round(_centerLayout.RowStyles[2].Height);
        UpdateComposerGridRows();
        int to = show ? _composerHeight : 0;
        if (show) _composerPanel.Visible = true;
        AnimateValue(from, to, 140, height =>
        {
            _centerLayout.RowStyles[2].Height = height;
            _centerLayout.PerformLayout();
        }, () =>
        {
            _animatingComposer = false;
            _composerPanel.Visible = show;
            _composerShown = show;
            UpdateComposerLayout();
            UpdateMinimumHeight();
        });
    }

    private void AnimateMultiPanel(bool show)
    {
        if (_split is null || !IsHandleCreated)
        {
            if (_split is not null) _split.Panel2Collapsed = !show;
            if (show) ApplyMultiWidth();
            return;
        }
        FinishAnimation();
        _animatingSplit = true;
        int total = _split.ClientSize.Width;
        int minPanel2 = Math.Max(80, _split.Panel2MinSize);
        if (show)
        {
            _adjustingSplit = true;
            _split.Panel2Collapsed = false;
            _adjustingSplit = false;
            ApplyMultiWidth();
            int target = _split.SplitterDistance;
            int start = Math.Min(target,
                Math.Max(_split.Panel1MinSize, total - _split.SplitterWidth - minPanel2));
            _adjustingSplit = true;
            _split.SplitterDistance = start;
            AnimateValue(start, target, 140, distance =>
            {
                try { _split.SplitterDistance = distance; } catch { }
            }, () =>
            {
                _adjustingSplit = false;
                _animatingSplit = false;
                ApplyMultiWidth();
            });
        }
        else
        {
            int start = _split.SplitterDistance;
            int target = Math.Max(_split.Panel1MinSize,
                total - _split.SplitterWidth - minPanel2);
            _adjustingSplit = true;
            AnimateValue(start, target, 140, distance =>
            {
                try { _split.SplitterDistance = distance; } catch { }
            }, () =>
            {
                _split.Panel2Collapsed = true;
                _adjustingSplit = false;
                _animatingSplit = false;
            });
        }
    }
}
