using System.Text;

namespace SscomRe;

/// <summary>
/// 接收显示流水线（规格「接收和显示」，对应原程序 displayCOMdata）。
/// 维护三个累积：原始字节（→保存数据的 .DAT）、带空格的十六进制文本、转义文本；
/// 负责半字节（半个汉字）跨包处理、时间戳分包、截断阈值和整屏重画内容。
/// </summary>
public sealed class ReceiveDisplay
{
    private readonly List<byte> _raw = [];          // 0x50de40 原始接收
    private readonly StringBuilder _hex = new();    // 0x50de38 十六进制文本（每字节两个大写字符加空格）
    private readonly StringBuilder _text = new();   // 0x50de3c 转义文本（文本模式下按半字节裁剪）
    private byte[] _held = [];                      // 0x50de48 跨包拆开的字节
    private bool _halfPending;                      // 0x4ed578
    private long _lastTick;                         // 0x50ea54
    private readonly object _gate = new();          // 接收线程与界面线程共用这一份累积

    /// <summary>已收到的字节数（0x4ed540，参与截断判断）。</summary>
    private long _counter;
    public long Counter { get { lock (_gate) return _counter; } }

    /// <summary>显示缓冲上限（0x4ed548，从“显示缓冲上限”菜单来，默认 1000000）。</summary>
    public int Limit { get; set; } = 1_000_000;

    /// <summary>原始接收字节（副本，供保存数据 / 写文件用）。</summary>
    public byte[] RawBytes()
    {
        lock (_gate) return _raw.ToArray();
    }

    public string HexText
    {
        get { lock (_gate) return _hex.ToString(); }
    }

    public string TextAccum
    {
        get { lock (_gate) return _text.ToString(); }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _raw.Clear();
            _hex.Clear();
            _text.Clear();
            _held = [];
            _halfPending = false;
            _counter = 0;
            _lastTick = 0;
        }
    }

    /// <summary>打开文件预览：用新构建的十六进制/文本累积替换两个预览缓冲（原始缓冲不动）。</summary>
    public void SetPreviewBuffers(string hexText, string textAccum)
    {
        lock (_gate)
        {
            _hex.Clear().Append(hexText);
            _text.Clear().Append(textAccum);
        }
    }

    /// <summary>
    /// 处理一段收到的数据。返回要追加到接收框的文本段（可能带时间头）；
    /// <paramref name="memoReplace"/> 非空时表示触发了截断，接收框要整屏替换成该内容。
    /// </summary>
    public string Process(byte[] data, bool hexDisplay, bool timestampEnabled, int timeoutMs, out string? memoReplace)
    {
        memoReplace = null;
        lock (_gate)
        {
            return ProcessLocked(data, hexDisplay, timestampEnabled, timeoutMs, out memoReplace);
        }
    }

    private string ProcessLocked(byte[] data, bool hexDisplay, bool timestampEnabled, int timeoutMs, out string? memoReplace)
    {
        memoReplace = null;

        _counter += data.Length;                                  // 计数加在开头
        _raw.AddRange(data);                                      // 原始累积
        string hexOfSeg = SendComposer.HexText(data);             // "XX XX "
        _hex.Append(hexOfSeg);                                    // 十六进制累积

        byte[] escapedBytes = SendComposer.EscapeForDisplayBytes(data);   // 0x00→\0、丢 CR、LF→CRLF
        string display;
        if (hexDisplay)
        {
            display = hexOfSeg;
            _text.Append(SendComposer.DecodeGbk(escapedBytes));   // HEX 模式：累积未裁剪的转义文本
        }
        else
        {
            byte[] trimmed = TrimHalfGb(escapedBytes);            // 文本模式：半字节处理（字节级）
            display = SendComposer.DecodeGbk(trimmed);
            _text.Append(display);                                // 累积裁剪后的转义文本
        }

        if (timestampEnabled)
        {
            long tick = unchecked((uint)Environment.TickCount);
            long delta = unchecked((uint)(tick - _lastTick));
            _lastTick = tick;
            if (timeoutMs < delta)                                // 文本里的整数小于实际间隔才加时间头
            {
                var now = DateTime.Now;
                display = $"\r\n[{now:HH:mm:ss.fff}]收←◆" + display;
            }
        }

        if (Limit < _counter)
        {
            // 截断（0x4eb14c 的 Copy 语义：1 基起点，起点钳到 1）。
            // 只从 StringBuilder 拷出要留下的一段，不要先 ToString 整段再 Substring。
            var newRaw = DelphiCopyBytes(_raw, _counter - 100_000 + 1, 100_000);
            string newHex = DelphiCopy(_hex, _counter - 300_000 + 1, 300_000);
            _raw.Clear();
            _raw.AddRange(newRaw);
            _hex.Clear().Append(newHex);
            // 文本累积原来不截断，长时间接收会一直涨。按留下的原始字节重算，和切换到文本显示时看到的内容一致。
            _text.Clear().Append(SendComposer.EscapeForDisplay(newRaw));
            _counter = 100_000;
            memoReplace = hexDisplay ? newHex : SendComposer.DecodeGbk(_raw.ToArray());
        }
        return display;
    }

    /// <summary>
    /// 文本模式的半字节处理（字节级）：接上上次拆开的字节；结尾是汉字首字节（≥0x80）且扫描不完整时，
    /// 把它留下次用并从显示段里去掉（0x4ed578 / 0x50de48）。
    /// </summary>
    private byte[] TrimHalfGb(byte[] escapedBytes)
    {
        if (escapedBytes.Length == 0) return escapedBytes;        // 空段不扫描，标志保持
        byte[] bytes = escapedBytes;

        if (_halfPending && _held.Length > 0)
        {
            var joined = new byte[_held.Length + bytes.Length];
            Buffer.BlockCopy(_held, 0, joined, 0, _held.Length);
            Buffer.BlockCopy(bytes, 0, joined, _held.Length, bytes.Length);
            bytes = joined;
        }

        byte last = bytes[bytes.Length - 1];
        if (last >= 0x80)
        {
            int idx = 0;
            while (idx < bytes.Length)
                idx += bytes[idx] < 0x80 ? 1 : 2;
            if (idx != bytes.Length)                              // 扫过头：尾部有一个拆开的汉字首字节
            {
                _held = new[] { last };
                _halfPending = true;
                var trimmed = new byte[bytes.Length - 1];
                Buffer.BlockCopy(bytes, 0, trimmed, 0, trimmed.Length);
                return trimmed;                                   // 把最后一个字节截掉
            }
        }
        _halfPending = false;
        _held = [];
        return bytes;
    }

    // ———— Delphi Copy 等价实现（1 基起点，起点小于 1 钳到 1，起点越界给空） ————

    private static string DelphiCopy(StringBuilder s, long start1, long count)
    {
        if (start1 < 1) start1 = 1;
        if (start1 > s.Length) return "";
        long len = Math.Min(count, s.Length - start1 + 1);
        if (len <= 0) return "";
        return s.ToString((int)(start1 - 1), (int)len);
    }

    private static byte[] DelphiCopyBytes(List<byte> src, long start1, long count)
    {
        if (start1 < 1) start1 = 1;
        if (start1 > src.Count) return [];
        long len = Math.Min(count, src.Count - start1 + 1);
        if (len <= 0) return [];
        return src.GetRange((int)(start1 - 1), (int)len).ToArray();
    }
}
