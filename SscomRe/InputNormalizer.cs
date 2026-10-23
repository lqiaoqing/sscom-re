namespace SscomRe;

/// <summary>
/// 行内数字框的归一化（对应原程序顺序框/延时框 KeyUp 里“夹取后写回”的行为）。
/// 两条实测结论决定这里的写法：
/// 1) WinForms 给 TextBox 赋“变化过的值”会把光标重置到开头（同值赋值不动光标，实测有焦点场景）；
///    所以只在文本确实变化时才写回，并显式恢复光标位置，避免多位数输入被逐位打乱。
/// 2) 文本为空时保持为空不写回——排序/发送时按默认值解析（0 / 1000），
///    与原程序“空串→默认值”在引擎侧等价，但避免用户清空重输时被中途回填打断。
/// </summary>
public static class InputNormalizer
{
    /// <summary>返回 (规范化文本, 规范化后的光标位置)。文本未变（或为空）时原样返回，调用方不要赋值。</summary>
    public static (string Text, int Caret) NormalizeInt(string text, int caret, int fallback, int min, int max)
    {
        if (text.Trim().Length == 0) return (text, caret);   // 编辑中暂空：不打扰

        int value = int.TryParse(text.Trim(), out var parsed) ? parsed : fallback;
        value = Fx.Clamp(value, min, max);
        string norm = value.ToString();
        if (norm == text) return (text, caret);              // 没变：不碰文本，光标不动
        return (norm, Math.Min(caret, norm.Length));         // 变了：光标钳到新文本长度内（通常在末尾）
    }
}
