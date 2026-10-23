using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>深色输入框。单行用 FixedSingle 边框，多行区域由外层面板负责边框。</summary>
internal sealed class ThemeTextBox : TextBox
{
    private const int WM_PAINT = 0x000F;

    public string PlaceholderText { get; set; } = "";

    public ThemeTextBox()
    {
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        BorderStyle = BorderStyle.FixedSingle;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TryDarkNative(Handle);
    }

    protected override void OnReadOnlyChanged(EventArgs e)
    {
        base.OnReadOnlyChanged(e);
        if (ReadOnly && BackColor == Theme.Field)
            ForeColor = Theme.Muted;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (PlaceholderText.Length > 0) Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (PlaceholderText.Length > 0) Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (PlaceholderText.Length > 0) Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || PlaceholderText.Length == 0 || Text.Length > 0 || Focused || Multiline) return;
        try
        {
            using Graphics g = Graphics.FromHwnd(Handle);
            Rectangle rect = ClientRectangle;
            rect.Inflate(-Theme.Scale(this, 4), 0);
            TextRenderer.DrawText(g, PlaceholderText, Font, rect, Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        catch
        {
            // 句柄正在销毁时忽略绘制失败
        }
    }
}
