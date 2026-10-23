using System.Text;

namespace SscomRe;

/// <summary>
/// 单条发送组包（规格「发送：文本、HEX、校验」）。
/// 文本与 HEX 两条路径都先组出字节串，再按需加校验、发送（发送与回显在 MainForm）。
/// </summary>
public static class SendComposer
{
    public const string HexErrorCn = "不是有效HEX字符组合，请输入(\"0-9\",\"A-F\",\"a-f\",\" \")!\r\n每两个字符之间空一个空格.";
    public const string HexErrorEn = "Input valid HEX data，Input(\"0-9\",\"A-F\",\"a-f\",\" \")!\r\nFor Example: FF 81 55 Aa";

    private static readonly Encoding Gbk = Encoding.GetEncoding(936);

    /// <summary>
    /// 文本模式组包。转义表（对字节流逐字节处理）：\0→0x00、\a→0x07、\b→0x08、\t→0x09、\n→0x0A、
    /// \v→0x0B、\f→0x0C、\r→0x0D、\\→0x5C、\xHH→一个字节（两个十六进制字符）；
    /// 反斜杠在结尾时保留；未知转义保留“反斜杠 + 该字符”两个字节。
    /// addCrLf（加回车换行）为真时在末尾追加 0x0D 0x0A。
    /// 说明：原程序对 \xHH 不校验十六进制字符（未定义行为），这里非法半字节按 0 处理。
    /// </summary>
    public static byte[] ComposeText(string input, bool addCrLf)
    {
        byte[] raw = Gbk.GetBytes(input);   // 原程序按 ANSI 字节处理，中文按 GBK 两字节
        var outp = new List<byte>(raw.Length + 2);

        for (int i = 0; i < raw.Length; i++)
        {
            byte c = raw[i];
            if (c != (byte)'\\')
            {
                outp.Add(c);
                continue;
            }

            if (i + 1 >= raw.Length)        // 结尾的反斜杠：保留
            {
                outp.Add(c);
                break;
            }

            byte n = raw[i + 1];
            switch (n)
            {
                case (byte)'0': outp.Add(0x00); i++; break;
                case (byte)'a': outp.Add(0x07); i++; break;
                case (byte)'b': outp.Add(0x08); i++; break;
                case (byte)'t': outp.Add(0x09); i++; break;
                case (byte)'n': outp.Add(0x0A); i++; break;
                case (byte)'v': outp.Add(0x0B); i++; break;
                case (byte)'f': outp.Add(0x0C); i++; break;
                case (byte)'r': outp.Add(0x0D); i++; break;
                case (byte)'\\': outp.Add(0x5C); i++; break;
                case (byte)'x':
                    if (i + 3 < raw.Length)  // 需要再有两个字符
                    {
                        outp.Add((byte)((Nibble(raw[i + 2]) << 4) + Nibble(raw[i + 3])));
                        i += 3;
                    }
                    else
                    {
                        i = raw.Length;      // 不足两个字符：该前缀丢弃（观察）
                    }
                    break;
                default:                // 未知转义：反斜杠和字符都保留
                    outp.Add(c);
                    outp.Add(n);
                    i++;
                    break;
            }
        }

        if (addCrLf)
        {
            outp.Add(0x0D);
            outp.Add(0x0A);
        }
        return outp.ToArray();
    }

    private static int Nibble(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        _ => 0,                                  // 原为未定义（内部换算表），这里按 0
    };

    /// <summary>
    /// HEX 模式组包。整段只允许 0-9 A-F a-f、空格、CR、LF；出现其它字符 → 整段不发并给错误文案（返回 null）。
    /// 合法时跳过空白/换行，按两个字符拼一个字节；末尾落单的半字节丢弃。
    /// </summary>
    public static byte[]? ComposeHex(string input, out string errorCn, out string errorEn)
    {
        errorCn = HexErrorCn;
        errorEn = HexErrorEn;

        foreach (char ch in input)
        {
            if (!IsHexChar(ch))
                return null;
        }

        var outp = new List<byte>(input.Length / 2 + 1);
        int idx = 0;
        while (idx < input.Length)
        {
            while (idx < input.Length && IsBlank(input[idx])) idx++;
            if (idx >= input.Length) break;
            byte c1 = (byte)input[idx++];

            while (idx < input.Length && IsBlank(input[idx])) idx++;
            if (idx >= input.Length) break;      // 落单半字节丢弃
            byte c2 = (byte)input[idx++];

            outp.Add((byte)((Nibble(c1) << 4) + Nibble(c2)));
        }
        return outp.ToArray();
    }

    private static bool IsHexChar(char ch) =>
        ch is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f' or ' ' or '\r' or '\n';

    private static bool IsBlank(char ch) => ch is ' ' or '\r' or '\n';

    /// <summary>字节串按 GBK 解码（接收/回显文本用）。</summary>
    public static string DecodeGbk(byte[] data) => Gbk.GetString(data);

    /// <summary>字符串按 GBK 编码（文件名、写文件用）。</summary>
    public static byte[] EncodeGbk(string text) => Gbk.GetBytes(text);

    /// <summary>接收/回显用转义字节流：0x00 → “\0”，丢掉 0x0D，0x0A → CRLF，其余原样（字节级，半字节处理用）。</summary>
    public static byte[] EscapeForDisplayBytes(byte[] data)
    {
        var outp = new List<byte>(data.Length);
        foreach (byte b in data)
        {
            switch (b)
            {
                case 0x00:
                    outp.Add(0x5C);
                    outp.Add(0x30);
                    break;
                case 0x0D:
                    break;
                case 0x0A:
                    outp.Add(0x0D);
                    outp.Add(0x0A);
                    break;
                default:
                    outp.Add(b);
                    break;
            }
        }
        return outp.ToArray();
    }

    /// <summary>接收/回显用转义文本：0x00 → “\0”，丢掉 0x0D，0x0A → CRLF，其余按 GBK 原样。</summary>
    public static string EscapeForDisplay(byte[] data) => DecodeGbk(EscapeForDisplayBytes(data));

    /// <summary>文件名用时间戳（对应原程序把 DateTimeToStr 结果里的 : 空格 / \ 换成 - 或 _）。</summary>
    public static string FileTimestamp() => DateTime.Now.ToString("yyyy_MM_dd_HH-mm-ss");

    /// <summary>
    /// 打开文件预览：十六进制用无空格的两个大写字符；文本按 GBK 解码并丢弃 0x00。
    /// </summary>
    public static (string Hex, string Text) FilePreview(byte[] data)
    {
        const string digits = "0123456789ABCDEF";
        var hex = new StringBuilder(data.Length * 2);
        var textBytes = new List<byte>(data.Length);
        foreach (byte b in data)
        {
            hex.Append(digits[b >> 4]).Append(digits[b & 0x0F]);
            if (b != 0) textBytes.Add(b);
        }
        return (hex.ToString(), DecodeGbk(textBytes.ToArray()));
    }

    /// <summary>字节串 → 两个大写十六进制字符加一个空格（与接收显示同一格式）。</summary>
    public static string HexText(byte[] data)
    {
        const string digits = "0123456789ABCDEF";
        var sb = new StringBuilder(data.Length * 3);
        foreach (byte b in data)
        {
            sb.Append(digits[b >> 4]);
            sb.Append(digits[b & 0x0F]);
            sb.Append(' ');
        }
        return sb.ToString();
    }
}
