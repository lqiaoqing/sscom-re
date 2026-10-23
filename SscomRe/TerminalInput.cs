namespace SscomRe;

/// <summary>
/// 终端仿真按键（规格「终端仿真开关」）。纯逻辑：调用方传入当前开关和通道是否可发，
/// 这里维护输入行缓冲并决定吞键、全选、要发出的原始字节和发送计数增量。
/// 不走文本转义、校验和发送回显。
/// </summary>
public sealed class TerminalInput
{
    /// <summary>输入行缓冲。按键先进入这里；按键即发会在发送前整段换成这一个字符。</summary>
    public string Buffer { get; private set; } = "";

    /// <summary>
    /// 处理一个按键。<paramref name="canSend"/> 为假表示串口模式未打开，或网络模式未就绪。
    /// </summary>
    public TerminalResult Apply(char key, TermSwitches switches, bool canSend)
    {
        if (key == '\u0001')
            return TerminalResult.All();
        if (key is '\u0003' or '\u0016' or '\u0018' or '\n')
            return TerminalResult.Pass();

        Buffer += key;
        if (key == '\b')
            Buffer = Buffer.Length >= 2 ? Buffer.Substring(0, Buffer.Length - 2) : "";

        if (!canSend)
            return TerminalResult.Blocked();

        // 回车后发送 且 这一键是回车 → 整行。其余走“按键立即发送”（未勾选则只留在缓冲里）。
        if (switches.EnterSend && key == '\r')
        {
            if (switches.AddLf)
                Buffer += "\n";
            byte[] line = SendComposer.EncodeGbk(Buffer);
            int count = line.Length;
            Buffer = "";
            return new TerminalResult(false, false, line, count);
        }

        if (!switches.KeySend)
            return TerminalResult.Pass();

        Buffer = key.ToString();
        if (key == '\r' && switches.AddLf)
            Buffer += "\n";
        byte[] one = SendComposer.EncodeGbk(Buffer);
        Buffer = "";
        // 规格正文：按键即发计数 +1，补了 0x0A 也不再加。本地回显未勾选才吞掉这一键。
        return new TerminalResult(!switches.LocalEcho, false, one, 1);
    }
}

/// <summary>终端四个开关。前两个互斥，后两个各自翻转。</summary>
public readonly record struct TermSwitches(bool EnterSend, bool KeySend, bool LocalEcho, bool AddLf)
{
    /// <summary>先翻转“回车后发送”，再把“按键立即发送”设成相反值。</summary>
    public TermSwitches ToggleEnter()
    {
        bool enter = !EnterSend;
        return this with { EnterSend = enter, KeySend = !enter };
    }

    /// <summary>先翻转“按键立即发送”，再把“回车后发送”设成相反值。</summary>
    public TermSwitches ToggleKey()
    {
        bool key = !KeySend;
        return this with { KeySend = key, EnterSend = !key };
    }

    public TermSwitches ToggleEcho() => this with { LocalEcho = !LocalEcho };

    public TermSwitches ToggleAddLf() => this with { AddLf = !AddLf };
}

/// <summary>一次按键的处理结果。Payload 为 null 表示这一键不发送。</summary>
public readonly record struct TerminalResult(bool Swallow, bool SelectAll, byte[]? Payload, int CountDelta)
{
    public static TerminalResult All() => new(true, true, null, 0);

    public static TerminalResult Pass() => new(false, false, null, 0);

    public static TerminalResult Blocked() => new(true, false, null, 0);
}
