using System.Net;
using System.Text;

namespace SscomRe;

/// <summary>
/// 开发自检：`SscomRe.exe --selftest` 时运行并把结果写到标准输出（重定向可收集）。
/// 覆盖组包与校验算法的关键用例；不参与正常启动。
/// </summary>
internal static class SelfTest
{
    private static int _pass;
    private static int _fail;

    public static int Run()
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { /* WinExe 没有控制台句柄时保持默认编码 */ }

        // —— ComposeText ——
        Check("text 纯文本", SendComposer.ComposeText("abc", false), "61 62 63");
        Check("text 转义\\r\\n", SendComposer.ComposeText("a\\r\\nb", false), "61 0D 0A 62");
        Check("text \\xHH", SendComposer.ComposeText("\\x41\\x42", false), "41 42");
        Check("text \\0", SendComposer.ComposeText("\\0", false), "00");
        Check("text 未知转义保留两字符", SendComposer.ComposeText("\\q", false), "5C 71");
        Check("text 结尾反斜杠保留", SendComposer.ComposeText("a\\", false), "61 5C");
        Check("text 加回车换行", SendComposer.ComposeText("abc", true), "61 62 63 0D 0A");
        Check("text 空串加换行", SendComposer.ComposeText("", true), "0D 0A");
        Check("text GBK 中文", SendComposer.ComposeText("中", false), "D6 D0");

        // —— ComposeHex ——
        Check("hex 基本", SendComposer.ComposeHex("12 FF 00 30", out _, out _), "12 FF 00 30");
        Check("hex 小写", SendComposer.ComposeHex("aabb", out _, out _), "AA BB");
        Check("hex 跨空白拼字节", SendComposer.ComposeHex("1 2 F", out _, out _), "12");
        Check("hex 落单半字节丢弃", SendComposer.ComposeHex("12 3", out _, out _), "12");
        Check("hex 换行分隔", SendComposer.ComposeHex("12\r\n34", out _, out _), "12 34");
        Check("hex 非法字符整段不发", SendComposer.ComposeHex("12 GG", out _, out _), "null");
        Check("hex 空串", SendComposer.ComposeHex("", out _, out _), "");
        var preview = SendComposer.FilePreview([0xD6, 0xD0, 0x00, 0x41]);
        CheckText("文件预览 HEX", preview.Hex, "D6D00041");
        CheckText("文件预览 GBK 文本", preview.Text, "中A");

        // —— 校验算法（1..7 的已知向量，"123456789"） ——
        var ascii = Encoding.ASCII.GetBytes("123456789");
        Check("mode1 ModbusCRC16", Checksums.Compute(ascii, 1, 0, 0), "37 4B");
        // 注：初值 0 + 多项式 0x1021 的校验值（XMODEM 参数族）为 0x31C3
        Check("mode2 CCITT-CRC16", Checksums.Compute(ascii, 2, 0, 0), "C3 31");
        Check("mode3 CRC32", Checksums.Compute(ascii, 3, 0, 0), "26 39 F4 CB");
        Check("mode4 0-ADD8", Checksums.Compute(ascii, 4, 0, 0), "23");
        Check("mode5 ADD8", Checksums.Compute(ascii, 5, 0, 0), "DD");
        Check("mode6 XOR8", Checksums.Compute(ascii, 6, 0, 0), "31");
        Check("mode7 ADD16", Checksums.Compute(ascii, 7, 0, 0), "DD 01");
        Check("mode0 不加校验", Checksums.Compute(ascii, 0, 0, 0), "");
        // 范围：去掉帧首 1 字节、帧尾 1 字节，只算中间
        Check("范围 head/tail", Checksums.Compute([0xAA, 0x10, 0x20, 0xBB], 5, 1, 1), "30");
        Check("范围 越界按空", Checksums.Compute([0x01], 5, 5, 5), "00");

        // —— 接收显示 ——
        var r1 = new ReceiveDisplay();
        string d1 = r1.Process([0x31, 0x32], hexDisplay: false, timestampEnabled: false, timeoutMs: 20, out var m1);
        CheckText("接收文本显示", d1, "12");
        CheckText("接收文本累积", r1.TextAccum, "12");
        CheckText("接收十六进制累积", r1.HexText, "31 32 ");
        CheckText("接收计数", r1.Counter.ToString(), "2");
        CheckText("无截断时 memoReplace 为空", m1 is null ? "" : m1, "");

        var r2 = new ReceiveDisplay();               // 半个汉字跨包
        string d2a = r2.Process([0xD6], hexDisplay: false, timestampEnabled: false, timeoutMs: 20, out _);
        string d2b = r2.Process([0xD0], hexDisplay: false, timestampEnabled: false, timeoutMs: 20, out _);
        CheckText("半字节 第一包显示为空", d2a, "");
        CheckText("半字节 第二包补全", d2b, "中");

        var r3 = new ReceiveDisplay();               // 转义：\0、丢 CR、LF→CRLF
        string d3 = r3.Process([0x00, 0x41, 0x0D, 0x0A], hexDisplay: false, timestampEnabled: false, timeoutMs: 20, out _);
        CheckText("接收转义显示", d3, "\\0A\r\n");

        var r4 = new ReceiveDisplay();               // HEX 显示模式
        string d4 = r4.Process([0x41, 0x0A], hexDisplay: true, timestampEnabled: false, timeoutMs: 20, out _);
        CheckText("接收 HEX 显示", d4, "41 0A ");
        CheckText("接收 HEX 累积(截断前)", r4.HexText, "41 0A ");

        var r5 = new ReceiveDisplay { Limit = 5 };   // 截断
        string d5 = r5.Process([1, 2, 3, 4, 5, 6], hexDisplay: false, timestampEnabled: false, timeoutMs: 20, out var m5);
        _ = d5;
        CheckText("截断后计数归 100000", r5.Counter.ToString(), "100000");
        CheckText("截断触发整屏重画", m5 is not null ? "yes" : "no", "yes");
        var big = new ReceiveDisplay { Limit = 1 };
        var zeros = new byte[20000];
        for (int i = 0; i < 6; i++) big.Process(zeros, false, false, 20, out _);
        CheckText("截断后文本累积有界", big.TextAccum.Length <= 220000 ? "yes" : "no", "yes");

        // —— 多条发送状态机（排序后的表：顺序号快照 + 数据 + 行号） ——
        {
            // 全 0 顺序：一拍扫完 99 次跳过 → 自动停
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 99; i++) rows.Add((0, "", i));
            var p = new MultiSendPlanner { StartFlag = true };
            CheckText("全0顺序自动停", p.Tick(rows).Kind.ToString(), "Stop");
        }
        {
            // 单行有序（排在表尾）：每拍重复发该行
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 98; i++) rows.Add((0, "", i));
            rows.Add((1, "abc", 98));
            var p = new MultiSendPlanner { StartFlag = true };
            var s1 = p.Tick(rows);
            var s2 = p.Tick(rows);
            CheckText("单行循环 第1拍", $"{s1.Kind}:{s1.Row}", "Send:98");
            CheckText("单行循环 第2拍", $"{s2.Kind}:{s2.Row}", "Send:98");
        }
        {
            // 两行有序：按顺序号轮流发送
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 97; i++) rows.Add((0, "", i));
            rows.Add((1, "a", 97));
            rows.Add((2, "b", 98));
            var p = new MultiSendPlanner { StartFlag = true };
            var s1 = p.Tick(rows);
            var s2 = p.Tick(rows);
            var s3 = p.Tick(rows);
            CheckText("两行轮流 第1拍", $"{s1.Kind}:{s1.Row}", "Send:97");
            CheckText("两行轮流 第2拍", $"{s2.Kind}:{s2.Row}", "Send:98");
            CheckText("两行轮流 第3拍(回卷)", $"{s3.Kind}:{s3.Row}", "Send:97");
        }
        {
            // 有序但数据空 + 98 个零：混合跳过（原程序会卡死）——重写限扫两遍，本拍不发
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 98; i++) rows.Add((0, "", i));
            rows.Add((1, "", 98));
            var p = new MultiSendPlanner { StartFlag = true };
            var s = p.Tick(rows);
            CheckText("混合跳过不死循环", s.Kind.ToString(), "None");
        }
        {
            // 单行有序但顺序号为 0 的行更多：确认 99 边界在跳过分支内触发
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 99; i++) rows.Add((0, "data", i));   // 全 0 但都有数据
            var p = new MultiSendPlanner { StartFlag = true };
            CheckText("全0(有数据)也停", p.Tick(rows).Kind.ToString(), "Stop");
        }
        {
            // 三页合成一张表：前 100 行顺序号为 0 不能把后面的有效行停掉
            var rows = new List<(int, string, int)>();
            for (int i = 0; i < 100; i++) rows.Add((0, "", i));
            rows.Add((1, "go", 100));
            var p = new MultiSendPlanner { StartFlag = true };
            var s = p.Tick(rows);
            CheckText("超过99行仍能发到后面", $"{s.Kind}:{s.Row}", "Send:100");
        }

        // —— 行内数字框归一化（顺序/延时） ——
        {
            var (t1, _) = InputNormalizer.NormalizeInt("10", 2, 0, 0, 99);      // 多位数原样保留（回归点）
            CheckText("归一化 多位数保留", t1, "10");
            var (t2, _) = InputNormalizer.NormalizeInt("100", 3, 0, 0, 99);     // 超上限夹取
            CheckText("归一化 顺序超限夹99", t2, "99");
            var (t3, c3) = InputNormalizer.NormalizeInt("", 0, 0, 0, 99);       // 编辑中暂空：不打扰
            CheckText("归一化 空串保持为空", $"{t3}|{c3}", "|0");
            var (t4, _) = InputNormalizer.NormalizeInt("abc", 3, 1000, 1, 99999);
            CheckText("归一化 非数字给默认1000", t4, "1000");
            var (t5, _) = InputNormalizer.NormalizeInt("2000", 4, 1000, 1, 99999);
            CheckText("归一化 延时2000保留", t5, "2000");
            var (t6, c6) = InputNormalizer.NormalizeInt("05", 2, 0, 0, 99);     // 前导零去除后光标保持在末尾
            CheckText("归一化 前导零去且光标贴尾", $"{t6}|{c6}", "5|1");
        }

        // 诊断信息：WinForms 给 TextBox 赋值时光标行为（决定“仅变化时写回”的策略）
        {
            using var f = new Form();
            var tb = new TextBox();
            f.Controls.Add(tb);
            _ = f.Handle;                                   // 强制创建句柄
            tb.Text = "12";
            tb.SelectionStart = 2;
            tb.Text = "12";                                 // 同值赋值
            Console.WriteLine($"  info 无焦点 同值赋值后 SelectionStart={tb.SelectionStart}");
            tb.Text = "99";                                 // 变值赋值（新文本更长）
            Console.WriteLine($"  info 无焦点 变值赋值后 SelectionStart={tb.SelectionStart}");
        }
        {
            // 带焦点（更接近真实输入场景）：光标在末尾时写回同值/不同长度值
            using var f = new Form
            {
                ShowInTaskbar = false,
                Opacity = 0,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            var tb = new TextBox();
            f.Controls.Add(tb);
            f.Show();
            tb.Focus();
            Application.DoEvents();

            tb.Text = "1";
            tb.SelectionStart = 1;                          // 模拟刚敲完 “1”，光标在末尾
            tb.Text = "1";                                  // 旧代码：同值也无条件写回
            int sameValue = tb.SelectionStart;
            tb.Text = "10";                                 // 新文本比光标位置长
            int longerValue = tb.SelectionStart;
            tb.Text = "5";                                  // 新文本比光标位置短
            int shorterValue = tb.SelectionStart;
            f.Close();
            Console.WriteLine($"  info 有焦点 同值={sameValue} 变长={longerValue} 变短={shorterValue}");
        }

        // —— 网络回环（TCP 服务端 / TCP 客户端 / UDP，走本机回环真连） ——
        {
            // A) TCP 服务端：原始 TcpClient 连进来 → Ready；发数据 → DataReceived
            var svc = new NetworkService();
            byte[]? gotA = null;
            using var readyA = new ManualResetEventSlim(false);
            using var dataA = new ManualResetEventSlim(false);
            svc.ReadyChanged += () => { if (svc.Ready) readyA.Set(); };
            svc.DataReceived += b => { gotA = b; dataA.Set(); };
            svc.StartServer(0);
            using var rawClient = new System.Net.Sockets.TcpClient();
            rawClient.Connect("127.0.0.1", svc.BoundPort);
            bool readyOk = readyA.Wait(2000);
            byte[] ping = Encoding.ASCII.GetBytes("ping");
            rawClient.GetStream().Write(ping, 0, ping.Length);
            bool dataOk = dataA.Wait(2000);
            CheckText("网络 服务端就绪", readyOk ? "yes" : "no", "yes");
            CheckText("网络 服务端收数据", gotA is null ? "null" : Encoding.ASCII.GetString(gotA), "ping");
            // 服务端发送（只发第 0 个连接）→ 原始客户端收到
            var serverGot = new byte[4];
            svc.Send(Encoding.ASCII.GetBytes("pong"), NetMode.TcpServer, "", 0);
            int n = rawClient.GetStream().Read(serverGot, 0, 4);
            CheckText("网络 服务端发数据", Encoding.ASCII.GetString(serverGot, 0, n), "pong");
            svc.Dispose();
        }
        {
            // B) TCP 客户端：原始 TcpListener 监听 → 连上置就绪 → 发送到达对端
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var svc = new NetworkService();
            using var readyB = new ManualResetEventSlim(false);
            svc.ReadyChanged += () => { if (svc.Ready) readyB.Set(); };
            svc.ConnectClient("127.0.0.1", port);
            bool readyOk = readyB.Wait(3000);
            CheckText("网络 客户端就绪", readyOk ? "yes" : "no", "yes");
            byte[]? gotB = null;
            if (readyOk)
            {
                svc.Send(Encoding.ASCII.GetBytes("hello"), NetMode.TcpClient, "", 0);
                using var accepted = listener.AcceptTcpClient();
                var buf = new byte[8];
                accepted.GetStream().ReadTimeout = 2000;
                int n = accepted.GetStream().Read(buf, 0, 8);
                gotB = new byte[n];
                Buffer.BlockCopy(buf, 0, gotB, 0, n);
            }
            CheckText("网络 客户端发数据", gotB is null ? "null" : Encoding.ASCII.GetString(gotB), "hello");
            svc.Dispose();
            listener.Stop();
        }
        {
            // C) UDP：绑 0 → 原始 UdpClient 发来 → DataReceived + 学会来源 IP；回发到达
            var svc = new NetworkService();
            byte[]? gotC = null;
            string learned = "";
            using var dataC = new ManualResetEventSlim(false);
            svc.DataReceived += b => { gotC = b; dataC.Set(); };
            svc.RemoteIpLearned += ip => learned = ip;
            svc.StartUdp(0);
            int port = svc.BoundPort;
            using var rawUdp = new System.Net.Sockets.UdpClient(0);
            rawUdp.Send(Encoding.ASCII.GetBytes("u"), 1, new IPEndPoint(IPAddress.Loopback, port));
            bool dataOk = dataC.Wait(2000);
            CheckText("网络 UDP 收数据", gotC is null ? "null" : Encoding.ASCII.GetString(gotC), "u");
            CheckText("网络 UDP 来源IP", learned, "127.0.0.1");
            svc.Send(Encoding.ASCII.GetBytes("r"), NetMode.Udp, "127.0.0.1", ((IPEndPoint)rawUdp.Client.LocalEndPoint).Port);
            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] echo = rawUdp.Receive(ref from);
            CheckText("网络 UDP 回发", Encoding.ASCII.GetString(echo), "r");
            svc.Dispose();
        }

        // —— 终端仿真（TerminalInput.Apply，界面按键走同一个入口） ——
        {
            var enter = new TermSwitches(true, false, false, false);
            var term = new TerminalInput();
            term.Apply('a', enter, true);
            term.Apply('b', enter, true);
            var bs = term.Apply('\b', enter, true);
            Check("终端 退格不发送", bs.Payload, "null");
            CheckText("终端 退格 ab 变成 a", term.Buffer, "a");

            var empty = new TerminalInput();
            empty.Apply('\b', enter, true);
            CheckText("终端 空缓冲退格仍空", empty.Buffer, "");

            var blocked = new TerminalInput();
            var sw = blocked.Apply('A', new TermSwitches(false, true, false, false), false);
            Check("终端 不能发时不发送", sw.Payload, "null");
            CheckText("终端 不能发时吞键", sw.Swallow ? "yes" : "no", "yes");

            var keySend = new TerminalInput();
            var ka = keySend.Apply('A', new TermSwitches(false, true, false, false), true);
            Check("终端 按键即发 A", ka.Payload, "41");
            CheckText("终端 按键即发计数", ka.CountDelta.ToString(), "1");
            CheckText("终端 无回显吞键", ka.Swallow ? "yes" : "no", "yes");

            var echoed = new TerminalInput();
            var ke = echoed.Apply('A', new TermSwitches(false, true, true, false), true);
            CheckText("终端 有回显不吞键", ke.Swallow ? "yes" : "no", "no");

            var cr = new TerminalInput();
            var kcr = cr.Apply('\r', new TermSwitches(false, true, true, false), true);
            Check("终端 按键即发回车", kcr.Payload, "0D");
            CheckText("终端 按键即发回车计数", kcr.CountDelta.ToString(), "1");

            var crlf = new TerminalInput();
            var kcrlf = crlf.Apply('\r', new TermSwitches(false, true, true, true), true);
            Check("终端 按键即发回车补0A", kcrlf.Payload, "0D 0A");
            CheckText("终端 补0A计数仍为1", kcrlf.CountDelta.ToString(), "1");

            var line = new TerminalInput();
            line.Apply('中', enter, true);
            var sent = line.Apply('\r', enter, true);
            Check("终端 整行GBK", sent.Payload, "D6 D0 0D");
            CheckText("终端 整行计数等于GBK长度", sent.CountDelta.ToString(), "3");
            CheckText("终端 整行不吞回车", sent.Swallow ? "yes" : "no", "no");

            var lineLf = new TerminalInput();
            var lineLfOpt = new TermSwitches(true, false, false, true);
            lineLf.Apply('中', lineLfOpt, true);
            var sentLf = lineLf.Apply('\r', lineLfOpt, true);
            Check("终端 整行补0A", sentLf.Payload, "D6 D0 0D 0A");
            CheckText("终端 整行补0A计数", sentLf.CountDelta.ToString(), "4");

            var ctrl = new TerminalInput();
            var sel = ctrl.Apply('\u0001', enter, true);
            CheckText("终端 Ctrl+A 全选", sel.SelectAll ? "yes" : "no", "yes");
            Check("终端 Ctrl+A 不发送", sel.Payload, "null");
            CheckText("终端 Ctrl+A 不改缓冲", ctrl.Buffer, "");
            var copy = ctrl.Apply('\u0003', enter, true);
            Check("终端 Ctrl+C 不发送", copy.Payload, "null");
            CheckText("终端 Ctrl+C 不吞", copy.Swallow ? "yes" : "no", "no");
            var paste = ctrl.Apply('\u0016', enter, true);
            Check("终端 Ctrl+V 不发送", paste.Payload, "null");
            CheckText("终端 Ctrl+V 不吞", paste.Swallow ? "yes" : "no", "no");
            var cut = ctrl.Apply('\u0018', enter, true);
            Check("终端 Ctrl+X 不发送", cut.Payload, "null");
            CheckText("终端 Ctrl+X 不吞", cut.Swallow ? "yes" : "no", "no");
            var lf = ctrl.Apply('\n', enter, true);
            Check("终端 换行不发送", lf.Payload, "null");
            CheckText("终端 换行不改缓冲", ctrl.Buffer, "");

            var slash = new TerminalInput();
            var sl = slash.Apply('\\', new TermSwitches(false, true, true, false), true);
            Check("终端 反斜杠原样字节", sl.Payload, "5C");

            var menu = new TermSwitches(false, false, false, false).ToggleEnter();
            CheckText("终端 菜单回车发送互斥", $"{menu.EnterSend},{menu.KeySend}", "True,False");
            menu = menu.ToggleEnter();
            CheckText("终端 菜单再点回车发送", $"{menu.EnterSend},{menu.KeySend}", "False,True");
            menu = menu.ToggleKey();
            CheckText("终端 菜单按键发送互斥", $"{menu.EnterSend},{menu.KeySend}", "True,False");
            menu = menu.ToggleEcho().ToggleAddLf();
            CheckText("终端 回显和补换行各自翻转", $"{menu.EnterSend},{menu.KeySend},{menu.LocalEcho},{menu.AddLf}", "True,False,True,True");
        }

        // —— 波特率夹取、下次打开参数、状态栏线路名 ——
        {
            var high = ComPortService.ClampCustomBaud(99_000_000);
            CheckText("波特率过高夹取", high.Value.ToString(), "50000000");
            CheckText("波特率过高提示", high.Message ?? "", ComPortService.BaudTooHighMessage);
            var low = ComPortService.ClampCustomBaud(10);
            CheckText("波特率过低夹取", low.Value.ToString(), "110");
            CheckText("波特率过低提示", low.Message ?? "", ComPortService.BaudTooLowMessage);
            var inRange = ComPortService.ClampCustomBaud(250000);
            CheckText("波特率范围内无提示", inRange.Message is null ? "none" : inRange.Message, "none");

            var svc = new ComPortService
            {
                DataBits = 7,
                StopBitsIndex = 2,
                ParityIndex = 1,
                FlowIndex = 0,
                InputBufferSize = 80_000,
                ReadTimeoutMs = 50,
                WriteTimeoutMs = 10,
                BaudIndex = 0,
                CustomBaud = 99_000_000,
            };
            var next = svc.ParametersForNextOpen();
            CheckText("下次打开波特率", next.BaudRate.ToString(), "50000000");
            CheckText("下次打开数据位", next.DataBits.ToString(), "7");
            CheckText("下次打开停止位", next.StopBits.ToString(), "Two");
            CheckText("下次打开校验", next.Parity.ToString(), "Odd");
            CheckText("下次打开流控", next.Handshake.ToString(), "RequestToSend");
            CheckText("下次打开缓冲", next.ReadBufferSize.ToString(), "80000");
            CheckText("下次打开读超时", next.ReadTimeout.ToString(), "50");
            CheckText("下次打开写超时", next.WriteTimeout.ToString(), "10");
            CheckText("状态栏线路名", svc.LineDescription(), "7,2,Odd,HardWare");
            svc.FlowIndex = 3;
            CheckText("自定义流控无额外握手", svc.ParametersForNextOpen().Handshake.ToString(), "None");
            CheckText("自定义流控名字", svc.LineDescription(), "7,2,Odd,Custom");
            svc.Dispose();

            ComPortService.ApplyBaudText("250000", out int customIndex, out int customValue);
            CheckText("自定义波特率下标", customIndex.ToString(), "0");
            CheckText("自定义波特率值", customValue.ToString(), "250000");
            ComPortService.ApplyBaudText("115200", out int tableIndex, out _);
            CheckText("表内波特率下标", tableIndex.ToString(), "13");
            ComPortService.ApplyBaudText("0", out int zeroIndex, out int zeroValue);
            CheckText("波特率0选115200", $"{zeroIndex},{zeroValue}", "13,115200");
        }

        // —— 窗口几何夹取 ——
        {
            var def = WindowPlacement.Clamp(null, null, null, null, 1920, 1080);
            CheckText("几何默认", $"{def.Width},{def.Height},{def.Left},{def.Top}", "700,600,250,180");
            CheckText("几何宽小于400", WindowPlacement.Clamp(100, 600, 250, 180, 1920, 1080).Width.ToString(), "400");
            CheckText("几何宽装不下", WindowPlacement.Clamp(3000, 600, 250, 180, 1920, 1080).Width.ToString(), "600");
            CheckText("几何高小于300", WindowPlacement.Clamp(700, 10, 250, 180, 1920, 1080).Height.ToString(), "300");
            CheckText("几何高装不下", WindowPlacement.Clamp(700, 2000, 250, 180, 1920, 1080).Height.ToString(), "400");
            CheckText("几何left装不下", WindowPlacement.Clamp(700, 600, 1800, 180, 1920, 1080).Left.ToString(), "50");
            CheckText("几何top装不下", WindowPlacement.Clamp(700, 600, 250, 1000, 1920, 1080).Top.ToString(), "50");
        }

        // —— sscom51.ini 往返 ——
        {
            var snap = new IniSnapshot
            {
                PortValue = "3",
                Baud = "250000",
                TailReserve = 3,
                AscText = "abcdefg",
                HexText = "12 FF 00 30",
            };
            snap.Data[0] = "a,b\r\n中文";
            snap.Comment[0] = "注,释\r\n";
            snap.Order[0] = "2";
            snap.Delay[0] = "1500";
            snap.Hex[0] = true;
            snap.FontName = "Consolas";
            snap.FontSize = "16";
            snap.FontStyle = (int)FontStyle.Bold | (int)FontStyle.Underline;
            snap.FontColor = Color.FromArgb(255, 20, 30, 40).ToArgb();
            snap.Data[99] = "第二页";
            snap.Hex[99] = true;
            snap.Comment[99] = "注2";
            snap.Order[99] = "4";
            snap.Data[198] = "第三页";
            snap.Delay[198] = "2500";

            string t1 = IniDocument.Save(snap);
            CheckText("ini 含HEX注释", t1.Contains(IniDocument.CommentHex) ? "yes" : "no", "yes");
            CheckText("ini 含Nx注释", t1.Contains(IniDocument.CommentNx) ? "yes" : "no", "yes");
            CheckText("ini 以end结尾", t1.TrimEnd().EndsWith(";end") ? "yes" : "no", "yes");
            CheckText("ini 转义逗号", t1.Contains("0x02") ? "yes" : "no", "yes");
            CheckText("ini 转义换行", t1.Contains("0x03") && t1.Contains("0x04") ? "yes" : "no", "yes");
            CheckText("ini 写N1100为C", t1.Contains("N1100=,C\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写N1058为1000", t1.Contains("N1058=,1000\r\n") ? "yes" : "no", "yes");

            var loaded = IniDocument.Load(t1);
            string t2 = IniDocument.Save(loaded);
            if (t1 == t2)
            {
                CheckText("ini 往返文本一致", "yes", "yes");
            }
            else
            {
                int n = Math.Min(t1.Length, t2.Length);
                int at = 0;
                while (at < n && t1[at] == t2[at]) at++;
                string slice1 = t1.Substring(at, Math.Min(60, t1.Length - at)).Replace("\r", "\\r").Replace("\n", "\\n");
                string slice2 = t2.Substring(at, Math.Min(60, t2.Length - at)).Replace("\r", "\\r").Replace("\n", "\\n");
                CheckText("ini 往返文本一致", slice1, slice2);
            }
            CheckText("ini 还原数据", loaded.Data[0], "a,b\r\n中文");
            CheckText("ini 还原注释", loaded.Comment[0], "注,释\r\n");
            CheckText("ini 还原顺序", loaded.Order[0], "2");
            CheckText("ini 第2页数据", loaded.Data[99], "第二页");
            CheckText("ini 第2页HEX", loaded.Hex[99] ? "H" : "A", "H");
            CheckText("ini 第2页注释", loaded.Comment[99], "注2");
            CheckText("ini 第2页顺序", loaded.Order[99], "4");
            CheckText("ini 第3页数据", loaded.Data[198], "第三页");
            CheckText("ini 第3页延时", loaded.Delay[198], "2500");
            CheckText("ini 含第2页键", t1.Contains("\r\nN201=H,第二页\r\n") && t1.Contains("\r\nN301=4,注2,1000\r\n") ? "yes" : "no", "yes");
            CheckText("ini 含第3页键", t1.Contains("\r\nN401=A,第三页\r\n") && t1.Contains("\r\nN501=0,1,2500\r\n") ? "yes" : "no", "yes");
            var legacy = IniDocument.Load("N1=A,hello\r\nN101=2,cmt,1500\r\n");
            CheckText("旧ini第1页仍在", legacy.Data[0], "hello");
            CheckText("旧ini第2页仍是空", legacy.Data[99], "");
            CheckText("ini 还原字体", $"{loaded.FontName},{loaded.FontSize},{loaded.FontStyle},{loaded.FontColor}",
                "Consolas,16,5," + Color.FromArgb(255, 20, 30, 40).ToArgb().ToString());
            CheckText("ini 网络模式", loaded.PortValue, "3");
            CheckText("ini 自定义波特率", loaded.Baud, "250000");
            CheckText("ini 帧尾档", loaded.TailReserve.ToString(), "3");
            CheckText("ini ASC文本", loaded.AscText, "abcdefg");
            CheckText("ini HEX文本", loaded.HexText, "12 FF 00 30");

            var mode1 = IniDocument.ParsePortMode("1");
            var mode2 = IniDocument.ParsePortMode("2");
            var mode3 = IniDocument.ParsePortMode("3");
            CheckText("N1080=1 选TCPClient", mode1.Kind.ToString(), "TcpClient");
            CheckText("N1080=2 选TCPClient", mode2.Kind.ToString(), "TcpClient");
            CheckText("N1080=3 选UDP", mode3.Kind.ToString(), "Udp");

            string crafted = t1.Replace("N1056=,1000", "N1056=,2500").Replace("N1058=,1000", "N1058=,999");
            var interval = IniDocument.Load(crafted);
            CheckText("N1058不覆盖定时间隔", interval.TimerInterval, "2500");
            string rewritten = IniDocument.Save(interval);
            CheckText("重存N1056仍是2500", rewritten.Contains("N1056=,2500") ? "yes" : "no", "yes");
            CheckText("重存N1058仍是1000", rewritten.Contains("N1058=,1000") && !rewritten.Contains("N1058=,999") ? "yes" : "no", "yes");

            string dir = Path.Combine(Path.GetTempPath(), "sscomre-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "sscom51.ini");
                IniDocument.WriteFile(path, snap);
                var fromFile = IniDocument.Load(IniDocument.ReadFile(path));
                CheckText("ini 文件往返", IniDocument.Save(fromFile) == t1 ? "yes" : "no", "yes");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 临时目录删不掉不影响结果 */ }
            }
        }

        // —— 主窗体：接收区占大头，选项保持一行，发送框是矮条 ——
        {
            string ini = Path.Combine(AppContext.BaseDirectory, "sscom51.ini");
            byte[]? iniBackup = File.Exists(ini) ? File.ReadAllBytes(ini) : null;
            using var form = new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-24000, -24000);
            form.Show();
            // 布局尺寸是物理像素，字体随 DPI 放大；按 96 DPI 的等效尺寸开窗，测试才与缩放无关。
            int scale = Math.Max(96, form.DeviceDpi);
            form.Size = new Size(1200 * scale / 96, 720 * scale / 96);
            Application.DoEvents();
            TextBox? send = null;
            TextBox? recv = null;
            ComboBox? verify = null;
            CheckBox? stamp = null;
            foreach (var c in AllControls(form))
            {
                if (c is TextBox tb && tb.Multiline && tb.Text == "abcdefg") send = tb;
                else if (c is TextBox tb2 && tb2.Multiline) recv = tb2;
                if (c is ComboBox box && box.Items.Contains("ModbusCRC16")) verify = box;
                if (c is CheckBox cb && cb.Text == "加时间戳和分包显示") stamp = cb;
            }
            CheckText("找到发送框", send is null ? "no" : "yes", "yes");
            CheckText("找到接收框", recv is null ? "no" : "yes", "yes");
            if (send is not null && recv is not null)
            {
                CheckText("接收区高于发送区", recv.Height > send.Height * 3 ? "yes" : "no", "yes");
                CheckText("发送框约三行", send.Height >= form.Font.Height * 2 && send.Height <= form.Font.Height * 6 ? "yes" : "no", "yes");
            }
            if (stamp is not null)
                CheckText("时间戳文字不被拆开", stamp.Width >= stamp.PreferredSize.Width - 2 ? "yes" : "no", "yes");
            if (verify is not null)
            {
                int right = verify.PointToScreen(new Point(verify.Width, 0)).X;
                int limit = form.PointToScreen(new Point(form.ClientSize.Width, 0)).X + 4;
                CheckText("校验框在窗口内", right <= limit ? "yes" : "no", "yes");
            }
            int sendAt720 = send?.Height ?? 0;
            form.Size = new Size(1200 * scale / 96, 980 * scale / 96);
            Application.DoEvents();
            CheckText("拉高窗口发送框不跟着长", send is not null && Math.Abs(send.Height - sendAt720) <= 4 ? "yes" : "no", "yes");

            string? fontMenu = null;
            string? backMenu = null;
            if (form.MainMenuStrip is not null)
            {
                foreach (ToolStripItem top in form.MainMenuStrip.Items)
                {
                    if (top is not ToolStripMenuItem display || display.Text != "显示") continue;
                    foreach (ToolStripItem sub in display.DropDownItems)
                    {
                        if (sub is not ToolStripMenuItem item) continue;
                        if (item.Text.StartsWith("字体")) fontMenu = item.Text;
                        if (item.Text == "背景颜色") backMenu = item.Text;
                    }
                }
            }
            CheckText("显示菜单有字体设置", fontMenu ?? "", "字体/颜色/大小/...");
            CheckText("背景颜色仍在", backMenu ?? "", "背景颜色");

            if (recv is not null)
            {
                using var face = new Font(form.Font.FontFamily, 16F, FontStyle.Bold | FontStyle.Italic);
                form.ApplyRecvFont(face, Color.FromArgb(255, 10, 20, 30));
                CheckText("接收框字号", recv.Font.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), "16");
                CheckText("接收框字体名", recv.Font.Name, form.Font.Name);
                CheckText("接收框字形", ((int)recv.Font.Style).ToString(), ((int)(FontStyle.Bold | FontStyle.Italic)).ToString());
                CheckText("接收框文字颜色", recv.ForeColor.ToArgb().ToString(), Color.FromArgb(255, 10, 20, 30).ToArgb().ToString());
                CheckText("窗体字体未改", form.Font.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), "9");
                using var again = new Font(form.Font.FontFamily, 16F, FontStyle.Bold | FontStyle.Italic);
                form.ApplyRecvFont(again, Color.FromArgb(255, 10, 20, 30));
                int height;
                try { height = recv.Font.Height; }
                catch (ArgumentException) { height = -1; }
                CheckText("同样字体再设一次仍然有效", height > 0 ? "yes" : "no", "yes");
                CheckText("同样字体再设一次字号仍在", recv.Font.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), "16");
            }

            form.Width = 820;
            Application.DoEvents();
            foreach (var c in AllControls(form))
                if (c is Button expand && expand.Text == "扩展")
                    expand.PerformClick();
            Application.DoEvents();
            foreach (var c in AllControls(form))
            {
                if (c is not SplitContainer sc || sc.Panel2Collapsed) continue;
                int total = sc.ClientSize.Width;
                int p2 = sc.Panel2.Width;
                CheckText("扩展面板窄于一半", p2 * 2 < total ? "yes" : $"{p2}/{total}", "yes");
                int quarter = Math.Max(total / 4, 460);
                CheckText("扩展面板大约四分之一", p2 <= quarter ? "yes" : $"{p2}/{total}", "yes");
            }
            Control? grid = null;
            foreach (var c in AllControls(form))
                if (c is Label header && header.Text == "延时ms" && header.Parent is not null)
                    grid = header.Parent;
            if (grid is null)
            {
                CheckText("找到多条表", "no", "yes");
            }
            else
            {
                var cells = new List<Control>();
                foreach (Control c in grid.Controls)
                    if (c.Tag is Point && c.Width > 0 && c.Height > 0) cells.Add(c);
                int overlaps = 0;
                string sample = "";
                for (int i = 0; i < cells.Count && overlaps == 0; i++)
                {
                    Rectangle a = cells[i].Bounds;
                    a.Inflate(-1, -1);
                    for (int j = i + 1; j < cells.Count; j++)
                    {
                        Rectangle b = cells[j].Bounds;
                        b.Inflate(-1, -1);
                        if (a.IntersectsWith(b))
                        {
                            overlaps++;
                            sample = cells[i].Text + " | " + cells[j].Text;
                            break;
                        }
                    }
                }
                CheckText("多条栏目互不遮挡", overlaps == 0 ? "yes" : sample, "yes");
                bool headerInside = true;
                foreach (Control c in cells)
                {
                    if (c is not Label || c.Tag is not Point cell) continue;
                    if (c.Bottom > (cell.X + 1) * (grid.Height / 100) + 1) headerInside = false;
                }
                CheckText("表头落在自己的行里", headerInside ? "yes" : "no", "yes");
                int shortSend = 0;
                foreach (Control c in cells)
                    if (c is Button b && b.Tag is Point p && p.Y == 2 && b.Height < b.Font.Height + 8)
                        shortSend++;
                CheckText("点击发送按钮容得下字", shortSend == 0 ? "yes" : shortSend.ToString(), "yes");
                int pages = 0;
                int dataBoxes = 0;
                foreach (var c in AllControls(form))
                {
                    if (c is TabPage tp && tp.Text.StartsWith("第")) pages++;
                    if (c is TextBox tb && tb.Tag is Point cell && cell.Y == 1) dataBoxes++;
                }
                CheckText("多条三页", pages.ToString(), "3");
                CheckText("三页共297条", dataBoxes.ToString(), "297");
            }
            form.Close();
            // Close 会保存测试中改过的字体/模式/几何；收尾时还原用户原配置。
            if (iniBackup is not null) File.WriteAllBytes(ini, iniBackup);
            else if (File.Exists(ini)) File.Delete(ini);
        }

        Console.WriteLine($"SELFTEST pass={_pass} fail={_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void CheckText(string name, string got, string expected)
    {
        if (got == expected)
        {
            _pass++;
            Console.WriteLine($"  ok  {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL  {name}: got=[{got.Replace("\r", "\\r").Replace("\n", "\\n")}] want=[{expected.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        }
    }

    private static void Check(string name, byte[]? actual, string expected)
    {
        string got = actual is null ? "null" : string.Join(" ", actual.Select(b => b.ToString("X2")));
        if (got == expected)
        {
            _pass++;
            Console.WriteLine($"  ok  {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL  {name}: got=[{got}] want=[{expected}]");
        }
    }
}
