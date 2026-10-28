using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// 深色下拉框：原生控件负责弹出和编辑，列表项、箭头按钮和边框由这里绘制。
/// 原生的下拉按钮是浅色方块，必须在 WM_PAINT 之后盖掉。
/// </summary>
internal sealed class ThemeComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;

    public ThemeComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        IntegralHeight = false;
        DropDownHeight = 260;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TryDarkNative(Handle);
        SyncItemHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        SyncItemHeight();
    }

    /// <summary>
    /// 关闭状态下的高度。OwnerDraw 的选择区按 ItemHeight 排，比 PreferredHeight 更高；
    /// AutoSize 行若按 PreferredHeight 分配，底边会被父面板裁掉。
    /// </summary>
    internal static int ClosedHeightFor(Control scaleSource)
    {
        int item = Math.Max(Theme.Scale(scaleSource, 20), scaleSource.Font.Height + Theme.Scale(scaleSource, 8));
        // 96 DPI 实测：ItemHeight 24 时控件总高 30，边框约占 6px。
        int chrome = SystemInformation.BorderSize.Height * 4 + 2;
        return item + chrome;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size size = base.GetPreferredSize(proposedSize);
        return new Size(size.Width, Math.Max(size.Height, ClosedHeightFor(this)));
    }

    private void SyncItemHeight()
    {
        int wanted = Math.Max(Theme.Scale(this, 20), Font.Height + Theme.Scale(this, 8));
        if (ItemHeight != wanted) ItemHeight = wanted;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Raised : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        string text = GetItemText(Items[e.Index]);
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, selected ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT) PaintChrome();
    }

    private void PaintChrome()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        try
        {
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            if (!GetComboBoxInfo(Handle, ref info)) return;
            using Graphics g = Graphics.FromHwnd(Handle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle button = Rectangle.FromLTRB(
                info.rcButton.Left, info.rcButton.Top, info.rcButton.Right, info.rcButton.Bottom);
            if (button.Width > 0 && button.Height > 0)
            {
                using (var back = new SolidBrush(Theme.Field))
                    g.FillRectangle(back, button);
                using (var sep = new Pen(Theme.Border))
                    g.DrawLine(sep, button.Left, button.Top + 2, button.Left, button.Bottom - 3);

                int centerX = button.Left + button.Width / 2;
                int centerY = button.Top + button.Height / 2;
                int size = Math.Max(3, Math.Min(button.Width, button.Height) / 5);
                using var chevron = new Pen(Focused ? Theme.Accent : Theme.Muted, Math.Max(1.2F, Theme.Scale(this, 1) * 0.9F))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                g.DrawLines(chevron, new[]
                {
                    new Point(centerX - size, centerY - size / 2),
                    new Point(centerX, centerY + size / 2),
                    new Point(centerX + size, centerY - size / 2),
                });
            }

            using var border = new Pen(Focused ? Theme.Accent : Theme.Border, 1);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
        catch
        {
            // 句柄正在销毁时忽略绘制失败
        }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        try
        {
            int visible = Math.Max(1, Math.Min(Items.Count, 10));
            int wanted = visible * ItemHeight + Theme.Scale(this, 4);
            if (DropDownHeight != wanted) DropDownHeight = wanted;

            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            if (GetComboBoxInfo(Handle, ref info) && info.hwndList != IntPtr.Zero)
            {
                Theme.TryDarkNative(info.hwndList);
                // 深色主题下原生的浅色滚动条很扎眼；列表仍可用滚轮/键盘滚动。
                ShowScrollBar(info.hwndList, 1, false);
            }
        }
        catch
        {
            // 列表窗口尚未建立时忽略
        }
    }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
    protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndEdit;
        public IntPtr hwndList;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);
}
