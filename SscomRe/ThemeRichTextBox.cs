using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>接收日志用富文本框：深色背景、隐藏原生边框、跟随主题的原生滚动条。</summary>
internal sealed class ThemeRichTextBox : RichTextBox
{
    private const int EM_SETUNDOLIMIT = 0x400 + 82;   // WM_USER + 82

    public ThemeRichTextBox()
    {
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Log;
        ForeColor = Theme.Text;
        DetectUrls = false;
        HideSelection = false;
        WordWrap = true;
        ScrollBars = RichTextBoxScrollBars.Vertical;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TryDarkNative(Handle);
        // 日志只追加，不需要撤销。关掉撤销缓冲，避免持续接收时内存和排版一直涨。
        SendMessage(Handle, EM_SETUNDOLIMIT, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
