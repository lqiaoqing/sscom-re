using System.Windows.Forms;

namespace SscomRe;

/// <summary>接收日志用富文本框：深色背景、隐藏原生边框、跟随主题的原生滚动条。</summary>
internal sealed class ThemeRichTextBox : RichTextBox
{
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
    }
}
