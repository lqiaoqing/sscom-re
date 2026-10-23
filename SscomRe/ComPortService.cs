using System.IO.Ports;

namespace SscomRe;

/// <summary>
/// 串口服务层。行为按 sscom5.13.1-rewrite-spec.md「打开串口」一节的规则实现：
/// - 端口名含大小写敏感的 "COM" 时，取其后的十进制整数再拼回 "COM"+数字（COM03 → COM3）；
///   不含 "COM" 时端口名为空，不打开（走网络模式）。
/// - 打开即独占（System.IO.Ports 在 Windows 上以 CreateFile 独占打开）。
/// - 打开成功后才应用 DTR / RTS（对应原程序打开后按复选框调 SetDTR / SetRTS 的次序）。
/// - 波特率走 26 项表，下标 0 为自定义整数，自定义范围 110..50000000。
/// </summary>
public sealed class ComPortService : IDisposable
{
    /// <summary>波特率表（含首项“自定义”占位，与规格中的显示顺序一致）。</summary>
    public static readonly string[] BaudItems =
    {
        "自定义", "110", "300", "600", "1200", "2400", "4800", "9600", "14400", "19200",
        "38400", "56000", "57600", "115200", "128000", "230400", "256000", "460800",
        "500000", "512000", "600000", "750000", "921600", "1000000", "1500000", "2000000",
    };

    // 规格里四张名称表（状态栏描述用），内容照原文。
    public static readonly string[] DataBitsItems = { "5", "6", "7", "8" };
    public static readonly string[] StopBitsItems = { "1", "1.5", "2" };
    public static readonly string[] ParityItems = { "None", "Odd", "Even", "Mark", "Space" };

    /// <summary>流控名称，按 CPort 枚举顺序：0=HardWare、1=SoftWare、2=None、3=Custom。</summary>
    public static readonly string[] FlowItems = { "HardWare", "SoftWare", "None", "Custom" };

    private static readonly int[] BaudValues =
    {
        0, 110, 300, 600, 1200, 2400, 4800, 9600, 14400, 19200,
        38400, 56000, 57600, 115200, 128000, 230400, 256000, 460800,
        500000, 512000, 600000, 750000, 921600, 1000000, 1500000, 2000000,
    };

    public const int CustomBaudMin = 110;
    public const int CustomBaudMax = 50_000_000;
    public const string BaudTooHighMessage = "The highest baudrate is 50M BPS";
    public const string BaudTooLowMessage = "The lowest baudrate is 110 BPS";

    private SerialPort? _port;

    public bool IsOpen => _port is { IsOpen: true };
    public string OpenedPortName { get; private set; } = "";

    // —— 线路参数（打开时生效，规格：下拉下标走表、夹取范围等） ——
    public int BaudIndex { get; set; } = 13;                 // 默认 115200
    public int CustomBaud { get; set; } = 115200;            // 下标 0 时用
    public int DataBits { get; set; } = 8;                   // 5..8
    public int StopBitsIndex { get; set; }                   // 0=1位, 1=1.5位, 2=2位
    public int ParityIndex { get; set; }                     // 0..4 ↔ None/Odd/Even/Mark/Space
    public int FlowIndex { get; set; } = 2;                  // 0..3 ↔ HardWare/SoftWare/None/Custom
    public bool DtrWanted { get; set; }
    public bool RtsWanted { get; set; }
    public int InputBufferSize { get; set; } = 100_000;      // 资源里 Buffer.InputSize=100000
    public int ReadTimeoutMs { get; set; } = -1;             // 数据走 DataReceived；-1 为无限等待
    public int WriteTimeoutMs { get; set; } = 3000;

    public static Parity ParityOf(int index) => index switch
    {
        1 => Parity.Odd,
        2 => Parity.Even,
        3 => Parity.Mark,
        4 => Parity.Space,
        _ => Parity.None,
    };

    public static Handshake HandshakeOf(int index) => index switch
    {
        0 => Handshake.RequestToSend,                        // HardWare
        1 => Handshake.XOnXOff,                              // SoftWare
        _ => Handshake.None,                                 // None / Custom（自定义流控留待“更多串口设置”）
    };

    /// <summary>收到一段数据的回调（对应原程序 RxChar 事件的数据段）。</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>打开 / 设置失败等错误信息（英文 / 中文两条，交给界面选语言展示）。</summary>
    public event Action<string, string>? ErrorRaised;

    /// <summary>
    /// 端口名变换：含大小写敏感的 "COM" 时取后随整数（atoi 语义，遇非数字停止，无数字按 0），
    /// 拼回 "COM" + 十进制；不含则返回空串。
    /// </summary>
    public static string NormalizePortName(string text)
    {
        int i = text.IndexOf("COM", StringComparison.Ordinal);
        if (i < 0) return "";
        int j = i + 3;
        int value = 0;
        bool any = false;
        while (j < text.Length && text[j] >= '0' && text[j] <= '9')
        {
            value = value * 10 + (text[j] - '0');
            any = true;
            j++;
            if (value > 9999) { value = 9999; break; }       // 防御上限，避免离谱输入
        }
        _ = any;
        return "COM" + value;
    }

    public readonly record struct BaudClamp(int Value, string? Message);

    /// <summary>自定义波特率夹到 110..50000000。越界时带上规格里的英文提示。</summary>
    public static BaudClamp ClampCustomBaud(int value)
    {
        if (value > CustomBaudMax) return new BaudClamp(CustomBaudMax, BaudTooHighMessage);
        if (value < CustomBaudMin) return new BaudClamp(CustomBaudMin, BaudTooLowMessage);
        return new BaudClamp(value, null);
    }

    public static int BaudValueOf(int index, int customBaud)
    {
        if (index <= 0) return ClampCustomBaud(customBaud).Value;
        if (index >= BaudValues.Length) index = BaudValues.Length - 1;
        return BaudValues[index];
    }

    /// <summary>
    /// 按 N1081 的数字选波特率。对上表就选该项；0 或解析失败选 115200；其余走自定义。
    /// </summary>
    public static void ApplyBaudText(string text, out int baudIndex, out int customBaud)
    {
        int fallback = Array.IndexOf(BaudValues, 115200);
        if (fallback < 0) fallback = 1;
        baudIndex = fallback;
        customBaud = 115200;
        if (!int.TryParse(text.Trim(), out int value) || value == 0)
            return;
        for (int i = 1; i < BaudValues.Length; i++)
        {
            if (BaudValues[i] == value)
            {
                baudIndex = i;
                customBaud = value;
                return;
            }
        }
        baudIndex = 0;
        customBaud = ClampCustomBaud(value).Value;
    }

    /// <summary>下次 Open 会用的线路参数。已经打开的端口不会因为改这些字段而重配。</summary>
    public readonly record struct SerialOpenParameters(
        int BaudRate,
        int DataBits,
        StopBits StopBits,
        Parity Parity,
        Handshake Handshake,
        int ReadBufferSize,
        int ReadTimeout,
        int WriteTimeout);

    public SerialOpenParameters ParametersForNextOpen() => new(
        BaudValueOf(BaudIndex, CustomBaud),
        Fx.Clamp(DataBits, 5, 8),
        StopBitsIndex switch
        {
            1 => StopBits.OnePointFive,
            2 => StopBits.Two,
            _ => StopBits.One,
        },
        ParityOf(ParityIndex),
        HandshakeOf(FlowIndex),
        Fx.Clamp(InputBufferSize, 4096, 1_000_000),
        ReadTimeoutMs,
        WriteTimeoutMs);

    /// <summary>状态栏四段：数据位,停止位,校验,流控。名字来自当前索引，不写死。</summary>
    public string LineDescription()
    {
        string bits = DataBits is >= 5 and <= 8 ? DataBitsItems[DataBits - 5] : DataBits.ToString();
        return $"{bits},{Item(StopBitsItems, StopBitsIndex)},{Item(ParityItems, ParityIndex)},{Item(FlowItems, FlowIndex)}";
    }

    private static string Item(string[] items, int index) =>
        (uint)index < (uint)items.Length ? items[index] : items[items.Length - 1];

    /// <summary>打开串口。portText 为界面上的端口显示文本。失败抛异常，Already-open 先关闭。</summary>
    public void Open(string portText)
    {
        if (IsOpen) Close();

        string name = NormalizePortName(portText);
        if (name.Length == 0)
            throw new InvalidOperationException("端口名为空，按规则不打开串口。");

        var line = ParametersForNextOpen();
        var sp = new SerialPort(name)
        {
            ReadBufferSize = line.ReadBufferSize,
            ReadTimeout = line.ReadTimeout,
            WriteTimeout = line.WriteTimeout,
            BaudRate = line.BaudRate,
            DataBits = line.DataBits,
            StopBits = line.StopBits,
            Parity = line.Parity,
            Handshake = line.Handshake,
        };
        sp.DataReceived += OnDataReceived;

        try
        {
            sp.Open();                        // 独占、OPEN_EXISTING、重叠 I/O
        }
        catch (Exception ex)
        {
            sp.DataReceived -= OnDataReceived;
            sp.Dispose();
            throw new IOException($"无法打开串口 {name}：{ex.Message}", ex);
        }

        // 打开成功后再按复选框应用 DTR / RTS（与原程序次序一致）
        try
        {
            sp.DtrEnable = DtrWanted;
            sp.RtsEnable = RtsWanted;
        }
        catch (Exception ex)
        {
            // 打开成功但 DTR/RTS 设置失败：保留已打开状态，向上报告（界面按原行为提示并复位勾选）
            ErrorRaised?.Invoke(
                $"Unable to set DTR/RTS!!!\r\n{ex.Message}",
                $"DTR/RTS 无法设置!请检查后重新打开!");
        }

        _port = sp;
        OpenedPortName = name;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var sp = _port;
        if (sp is not { IsOpen: true }) return;
        try
        {
            int n = sp.BytesToRead;
            if (n <= 0) return;
            var buf = new byte[n];
            int read = sp.Read(buf, 0, n);
            if (read > 0)
            {
                if (read != buf.Length) Array.Resize(ref buf, read);
                DataReceived?.Invoke(buf);
            }
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke(ex.Message, ex.Message);
        }
    }

    public void Close()
    {
        var sp = _port;
        _port = null;
        OpenedPortName = "";
        if (sp == null) return;
        try { sp.DataReceived -= OnDataReceived; } catch { /* ignore */ }
        try { if (sp.IsOpen) sp.Close(); } catch { /* ignore */ }
        try { sp.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>把勾选状态写入已打开的串口（对应 CheckBox1/2Click 的行为；失败抛异常给界面处理）。</summary>
    public void ApplyDtr(bool value)
    {
        DtrWanted = value;
        if (_port is { IsOpen: true }) _port.DtrEnable = value;
    }

    public void ApplyRts(bool value)
    {
        RtsWanted = value;
        if (_port is { IsOpen: true }) _port.RtsEnable = value;
    }

    public void Write(byte[] data)
    {
        var sp = _port;
        if (sp is not { IsOpen: true } || data.Length == 0) return;
        sp.Write(data, 0, data.Length);
    }

    /// <summary>读取 CTS/DSR/RLSD（调制解调器状态）；未打开或读取失败返回 null。</summary>
    public (bool Cts, bool Dsr, bool Rlsd)? ModemStatus()
    {
        var sp = _port;
        if (sp is not { IsOpen: true }) return null;
        try
        {
            return (sp.CtsHolding, sp.DsrHolding, sp.CDHolding);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => Close();
}
