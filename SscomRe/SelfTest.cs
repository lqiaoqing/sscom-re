using System.Net;
using System.Text;
using System.Threading;

namespace SscomRe;

/// <summary>
/// 开发自检：`sscomRe_v<版本>.exe --selftest` 时运行并把结果写到标准输出（重定向可收集）。
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

        // —— 接收日志分色 ——
        var logLines = LogFormatter.Parse(
            "[22:31:05.128]收←◆AT+VERSION?\n[22:31:06.002]发→◇AT+BAUD?\n帧校验失败：期望 37 4B", false);
        CheckText("日志行数", logLines.Count.ToString(), "3");
        CheckText("日志 RX 片段", $"{logLines[0].Spans[0].Kind},{logLines[0].Spans[1].Kind}", "Timestamp,Rx");
        CheckText("日志 TX 行", logLines[1].Tx ? "yes" : "no", "yes");
        CheckText("日志错误行归为数据",
            logLines[2].Spans[logLines[2].Spans.Count - 1].Kind.ToString(), "Data");
        CheckText("日志去元数据", LogFormatter.StripMeta("[22:31:05.128]收←◆AT\nOK"), "AT\nOK");
        CheckText("日志 HEX 片段", LogFormatter.Parse("D6 D0", true)[0].Spans[0].Kind.ToString(), "Hex");

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

        // 起点落在缓冲中间时，只按 Delphi Copy 留下 30 万字符，不依赖先物化整段。
        {
            var wide = new ReceiveDisplay { Limit = 300_000 };
            var chunk = new byte[300_010];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)(i * 17);
            wide.Process(chunk, true, false, 20, out var replaced);
            string expect = SendComposer.HexText(chunk).Substring(10, 300_000); // 1 基起点 11
            string got = wide.HexText;
            CheckText("大块截断计数仍归 100000", wide.Counter.ToString(), "100000");
            CheckText("大块截断十六进制长度", got.Length.ToString(), "300000");
            CheckText("大块截断十六进制起点", got == expect ? "yes" : "no", "yes");
            CheckText("大块截断重画等于十六进制", replaced == got ? "yes" : "no", "yes");
        }

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

            CheckText("换串口自动重开", MainForm.ShouldReopenSerial(true, NetMode.Serial, "COM3", "COM4") ? "yes" : "no", "yes");
            CheckText("同串口不重开", MainForm.ShouldReopenSerial(true, NetMode.Serial, "COM3", "COM03") ? "yes" : "no", "no");
            CheckText("关闭状态不重开", MainForm.ShouldReopenSerial(false, NetMode.Serial, "", "COM4") ? "yes" : "no", "no");
            CheckText("失败后仍想开", MainForm.ShouldReopenSerial(true, NetMode.Serial, "", "COM4") ? "yes" : "no", "yes");
            CheckText("网络模式不重开串口", MainForm.ShouldReopenSerial(true, NetMode.TcpClient, "COM3", "COM4") ? "yes" : "no", "no");
            CheckText("空端口不重开", MainForm.ShouldReopenSerial(true, NetMode.Serial, "COM3", "TCPClient") ? "yes" : "no", "no");
            CheckText("端口显示带描述", ComPortService.FormatPortChoice("COM38", "USB 串行设备"), "COM38 · USB 串行设备");
            CheckText("端口显示无描述", ComPortService.FormatPortChoice("COM38", ""), "COM38");
            CheckText("描述文本还原端口", ComPortService.NormalizePortName("COM38 · USB 串行设备"), "COM38");
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
            snap.ThemeLight = true;
            snap.SideCollapsed = true;
            snap.MultiPageCount = 2;
            snap.AutoScroll = false;
            snap.HighlightRulesJson = "{\"v\":1}";
            snap.RecvHtmlDir = @"D:\logs";
            snap.RecvHtmlSplitMb = 25;
            snap.RecvHtmlLight = false;
            snap.RecvHtmlIncludeTx = false;
            snap.RecvHtmlSettingsVersion = 2;

            string t1 = IniDocument.Save(snap);
            CheckText("ini 含HEX注释", t1.Contains(IniDocument.CommentHex) ? "yes" : "no", "yes");
            CheckText("ini 含Nx注释", t1.Contains(IniDocument.CommentNx) ? "yes" : "no", "yes");
            CheckText("ini 以end结尾", t1.TrimEnd().EndsWith(";end") ? "yes" : "no", "yes");
            CheckText("ini 转义逗号", t1.Contains("0x02") ? "yes" : "no", "yes");
            CheckText("ini 转义换行", t1.Contains("0x03") && t1.Contains("0x04") ? "yes" : "no", "yes");
            var rawCtl = IniDocument.Load("N1=A,a\u0002b\r\n");
            CheckText("ini 原始STX按逗号读入", rawCtl.Data[0], "a,b");
            var ctlSnap = new IniSnapshot();
            ctlSnap.Data[0] = "a\u0002b";
            string ctlText = IniDocument.Save(ctlSnap);
            CheckText("ini 原始STX保存为转义", ctlText.Contains("\u0002") ? "yes" : "no", "no");
            CheckText("ini 原始STX转义为0x02", ctlText.Contains("N1=A,a0x02b\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写N1100为C", t1.Contains("N1100=,C\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写N1058为1000", t1.Contains("N1058=,1000\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写浅色主题", t1.Contains("N1092=,1\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写连接栏折叠", t1.Contains("N1093=,1\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写多条页数", t1.Contains("N1094=,2\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写自动滚动关闭", t1.Contains("N1095=,0\r\n") ? "yes" : "no", "yes");
            CheckText("ini 写高亮规则", t1.Contains("\r\nN1096=,") ? "yes" : "no", "yes");
            CheckText("ini 写HTML记录设置",
                t1.Contains("\r\nN1097=,D:\\logs\r\n") && t1.Contains("\r\nN1098=,25\r\n") &&
                t1.Contains("\r\nN1099=,0\r\n") && t1.Contains("\r\nN1101=,0\r\n") &&
                t1.Contains("\r\nN1102=,2\r\n") ? "yes" : "no", "yes");

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
            CheckText("ini 还原多条页数", loaded.MultiPageCount.ToString(), "2");
            CheckText("ini 还原自动滚动关闭", loaded.AutoScroll ? "yes" : "no", "no");
            CheckText("ini 还原高亮规则", loaded.HighlightRulesJson, "{\"v\":1}");
            CheckText("ini 还原HTML记录设置",
                loaded.RecvHtmlDir + "|" + loaded.RecvHtmlSplitMb + "|" + loaded.RecvHtmlLight + "|" +
                loaded.RecvHtmlIncludeTx + "|" + loaded.RecvHtmlSettingsVersion,
                @"D:\logs|25|False|False|2");
            CheckText("ini 新档默认自动滚动", new IniSnapshot().AutoScroll ? "yes" : "no", "yes");
            CheckText("HTML记录默认分卷50", new IniSnapshot().RecvHtmlSplitMb.ToString(), "50");
            CheckText("旧10MB默认迁移到50", MainForm.NormalizeRecvHtmlSplitMb(10, 0).ToString(), "50");
            CheckText("已保存10MB保持10", MainForm.NormalizeRecvHtmlSplitMb(10, 2).ToString(), "10");
            CheckText("旧ini默认自动滚动", IniDocument.Load("").AutoScroll ? "yes" : "no", "yes");
            var legacy = IniDocument.Load("N1=A,hello\r\nN101=2,cmt,1500\r\n");
            CheckText("旧ini第1页仍在", legacy.Data[0], "hello");
            CheckText("旧ini第2页仍是空", legacy.Data[99], "");
            CheckText("旧ini默认一页", legacy.MultiPageCount.ToString(), "1");
            CheckText("ini 新档默认一页", new IniSnapshot().MultiPageCount.ToString(), "1");
            CheckText("页数上限夹到三", IniDocument.Load("N1094=,9\r\n").MultiPageCount.ToString(), "3");
            CheckText("页数下限夹到一", IniDocument.Load("N1094=,0\r\n").MultiPageCount.ToString(), "1");
            CheckText("ini 还原字体", $"{loaded.FontName},{loaded.FontSize},{loaded.FontStyle},{loaded.FontColor}",
                "Consolas,16,5," + Color.FromArgb(255, 20, 30, 40).ToArgb().ToString());
            CheckText("ini 还原浅色主题", loaded.ThemeLight ? "yes" : "no", "yes");
            CheckText("ini 还原连接栏折叠", loaded.SideCollapsed ? "yes" : "no", "yes");
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
                CheckText("ini 主题偏好读取", IniDocument.ReadThemePreference(path) ? "yes" : "no", "yes");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 临时目录删不掉不影响结果 */ }
            }
        }

        // —— 日志高亮规则 ——
        {
            var palette = new LogHighlightPalette(
                Color.White, Color.Black, Color.Gray, Color.Teal, Color.Goldenrod,
                Color.Teal, Color.LightGoldenrodYellow);
            const string sample = "[10:00:00.000]收←◆ hello ERROR world";
            var line = LogFormatter.Parse(sample, false)[0];

            var highlighter = new LogHighlighter();
            highlighter.SetRules(new[]
            {
                new LogHighlightRule
                {
                    Name = "err", Pattern = "ERROR", IsRegex = false, Enabled = true,
                    ForeColor = Color.Red.ToArgb(),
                },
            });
            StyledLogLine styled = highlighter.Style(line, palette);
            CheckText("高亮规则命中", styled.Matched ? "yes" : "no", "yes");
            CheckText("高亮只改匹配文字",
                styled.Runs.First(run => run.Text.Contains("ERROR")).Fore.ToArgb().ToString(),
                Color.Red.ToArgb().ToString());

            highlighter.SetRules(new[]
            {
                new LogHighlightRule
                {
                    Name = "整行", Pattern = "ERROR", IsRegex = false, Enabled = true,
                    WholeLine = true, BackColor = Color.FromArgb(0x55, 0, 0, 255).ToArgb(),
                    ForeColor = Color.Blue.ToArgb(),
                },
            });
            styled = highlighter.Style(line, palette);
            CheckText("高亮整行背景", styled.Runs.All(run => run.Back == styled.Back) ? "yes" : "no", "yes");

            highlighter.SetRules(new[]
            {
                new LogHighlightRule { Name = "first", Pattern = "ERROR", ForeColor = Color.Red.ToArgb() },
                new LogHighlightRule { Name = "second", Pattern = "ERROR", ForeColor = Color.Green.ToArgb() },
            });
            styled = highlighter.Style(line, palette);
            CheckText("高亮按列表优先级",
                styled.Runs.First(run => run.Text.Contains("ERROR")).Fore.ToArgb().ToString(),
                Color.Red.ToArgb().ToString());

            highlighter.SetRules(new[]
            {
                new LogHighlightRule
                {
                    Name = "只接收", Pattern = "ERROR", Rx = true, Tx = false,
                    ForeColor = Color.Red.ToArgb(),
                },
            });
            var txLine = LogFormatter.Parse("[10:00:00.000]发→◇ ERROR", false)[0];
            CheckText("高亮作用范围", highlighter.Style(txLine, palette).Matched ? "yes" : "no", "no");

            highlighter.SetRules(new[]
            {
                new LogHighlightRule
                {
                    Name = "区分大小写", Pattern = "error", CaseSensitive = true,
                    ForeColor = Color.Red.ToArgb(),
                },
            });
            CheckText("高亮区分大小写", highlighter.Style(line, palette).Matched ? "yes" : "no", "no");

            highlighter.SetRules(new[]
            {
                new LogHighlightRule { Name = "坏正则", Pattern = "(", IsRegex = true },
            });
            CheckText("坏正则有错误提示", highlighter.Errors.Count.ToString(), "1");
            CheckText("坏正则不影响其他规则", highlighter.ActiveCount.ToString(), "0");

            List<LogHighlightRule> defaults = LogHighlightRuleStore.CreateDefaults(true);
            CheckText("预置规则数量", defaults.Count.ToString(), "9");
            CheckText("预置仅错误警告开启",
                string.Join(",", defaults.Where(rule => rule.Enabled).Select(rule => rule.Name)), "错误,警告");
            CheckText("预置规则名称", string.Join(",", defaults.Select(rule => rule.Name)),
                "错误,警告,成功 / OK,连接状态,GPS / 定位,MQTT / 网络,AT 命令,心跳,调试");
            CheckText("预置规则都有标识", defaults.All(rule => rule.PresetKey.Length > 0) ? "yes" : "no", "yes");

            var off = defaults.Select(rule =>
            {
                LogHighlightRule copy = rule.Clone();
                copy.Enabled = false;
                return copy;
            }).ToList();
            highlighter.SetRules(off);
            styled = highlighter.Style(line, palette);
            CheckText("关闭全部规则后不命中", styled.Matched ? "yes" : "no", "no");
            CheckText("关闭全部规则后错误不着色",
                styled.Runs.First(run => run.Text.Contains("ERROR")).Fore.ToArgb().ToString(),
                palette.DefaultFore.ToArgb().ToString());
            CheckText("关闭全部规则后无背景",
                styled.Runs.All(run => run.Back == palette.DefaultBack) ? "yes" : "no", "yes");

            highlighter.SetRules(defaults);
            const string errorSample = "[10:00:00.000]收←◆ 帧校验失败：期望 37 4B";
            CheckText("预置错误规则命中中文错误",
                highlighter.Style(LogFormatter.Parse(errorSample, false)[0], palette).Matched ? "yes" : "no", "yes");

            string defaultsJson = LogHighlightRuleStore.ToJson(defaults);
            List<LogHighlightRule> roundtrip = LogHighlightRuleStore.FromJson(defaultsJson, out int rulesVersion);
            CheckText("高亮 JSON 版本", rulesVersion.ToString(), LogHighlightRuleStore.CurrentVersion.ToString());
            CheckText("高亮 JSON 往返规则数", roundtrip.Count.ToString(), "9");
            CheckText("高亮 JSON 保留预置标识", roundtrip[0].PresetKey, "error");
            CheckText("高亮 Base64 往返",
                LogHighlightRuleStore.FromBase64(LogHighlightRuleStore.ToBase64(defaults)).Count.ToString(), "9");

            string legacyJson = "[{\"Name\":\"ERROR\",\"Pattern\":\"ERROR\",\"Enabled\":true,\"Rx\":true,\"Tx\":true}," +
                                "{\"Name\":\"WARN\",\"Pattern\":\"WARN\",\"Enabled\":false,\"Rx\":true,\"Tx\":true}]";
            List<LogHighlightRule> legacyRules = LogHighlightRuleStore.FromJson(legacyJson, out int legacyVersion);
            CheckText("旧数组规则可读", legacyRules.Count.ToString(), "2");
            CheckText("旧数组版本为零", legacyVersion.ToString(), "0");
            List<LogHighlightRule> upgraded = LogHighlightRuleStore.Upgrade(legacyRules, true, legacyVersion);
            CheckText("升级补齐预置规则", upgraded.Count.ToString(), "9");
            CheckText("升级保留旧规则开关", upgraded[1].Enabled ? "yes" : "no", "no");
            CheckText("升级补的预置默认关闭",
                upgraded.Skip(2).All(rule => !rule.Enabled) ? "yes" : "no", "yes");
            CheckText("升级识别旧错误规则", upgraded[0].PresetKey, "error");
            CheckText("升级后不重复补齐",
                LogHighlightRuleStore.Upgrade(upgraded, true, LogHighlightRuleStore.CurrentVersion).Count.ToString(), "9");

            var previewPalette = new LogHighlightPalette(
                Color.White, Color.Black, Color.Gray, Color.Teal, Color.Goldenrod,
                Color.Teal, Color.LightGoldenrodYellow);
            bool presetsPreviewable = true;
            foreach (LogHighlightRule preset in defaults)
            {
                LogHighlightRule offPreset = preset.Clone();
                offPreset.Enabled = false;
                if (HighlightRulesDialog.PreviewStyledLines(
                        offPreset, new[] { offPreset }, null, previewPalette).Count == 0)
                    presetsPreviewable = false;
            }
            CheckText("全部预置关闭也能预览", presetsPreviewable ? "yes" : "no", "yes");

            var customKeyword = new LogHighlightRule { Name = "自定义", Pattern = "NEEDLE", Enabled = false };
            CheckText("自定义关键字关闭也能预览",
                HighlightRulesDialog.PreviewStyledLines(
                    customKeyword, new[] { customKeyword }, null, previewPalette).Count > 0 ? "yes" : "no",
                "yes");

            var customRegex = new LogHighlightRule
            {
                Name = "数字", Pattern = @"\d{3}", IsRegex = true, Enabled = false,
            };
            CheckText("自定义正则可从最近日志预览",
                HighlightRulesDialog.PreviewStyledLines(
                    customRegex, new[] { customRegex },
                    "[10:00:00.000]收←◆ value 123", previewPalette).Count > 0 ? "yes" : "no",
                "yes");
        }

        // —— 持续 HTML 记录 ——
        {
            string captureDir = Path.Combine(Path.GetTempPath(), "sscom-capture-" + Guid.NewGuid().ToString("N"));
            try
            {
                var rules = LogHighlightRuleStore.CreateDefaults(true);
                using (var recorder = new HtmlLogRecorder(captureDir, "COM38", 512,
                           Theme.DocumentPalette(), false, true, rules))
                {
                    recorder.Start();
                    recorder.Append("[10:00:00.000]收←◆ ERROR 中文 <&>\r\n[10:00:01.000]发→◇ DISPLAY-TX\r\n");
                    recorder.AppendTx("REAL-TX");
                    recorder.Append("[10:00:02.000]收←◆ " + new string('x', 600) + "\r\n");
                }
                string[] parts = Directory.GetFiles(captureDir, "*.html");
                Array.Sort(parts, StringComparer.Ordinal);
                string all = string.Join("\n", parts.Select(File.ReadAllText));
                CheckText("HTML 记录分卷", parts.Length >= 2 ? "yes" : "no", "yes");
                CheckText("HTML 记录文档头", all.Contains("<!DOCTYPE html>") ? "yes" : "no", "yes");
                CheckText("HTML 记录尾部闭合", all.TrimEnd().EndsWith("</html>") ? "yes" : "no", "yes");
                CheckText("HTML 记录转义", all.Contains("&lt;&amp;&gt;") ? "yes" : "no", "yes");
                CheckText("HTML 记录带高亮", all.Contains("<span style=") ? "yes" : "no", "yes");
                CheckText("HTML 记录含发送回显", all.Contains("发→◇") ? "yes" : "no", "yes");
                CheckText("HTML 记录发送数据", all.Contains("REAL-TX") ? "yes" : "no", "yes");
                CheckText("HTML 记录不重复显示回显", all.Contains("DISPLAY-TX") ? "yes" : "no", "no");
                CheckText("HTML 记录卷号", parts[0].Contains("-p001.html") ? "yes" : "no", "yes");
            }
            finally
            {
                try { Directory.Delete(captureDir, true); } catch { /* 临时目录删不掉不影响结果 */ }
            }

            string noTxDir = Path.Combine(Path.GetTempPath(), "sscom-capture-notx-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new HtmlLogRecorder(noTxDir, "COM1", 0,
                           Theme.DocumentPalette(), false, false, LogHighlightRuleStore.CreateDefaults(true)))
                {
                    recorder.Start();
                    recorder.Append("RX-DATA\r\n");
                    recorder.AppendTx("TX-ONLY");
                }
                string allNoTx = string.Join("\n",
                    Directory.GetFiles(noTxDir, "*.html").Select(File.ReadAllText));
                CheckText("HTML 记录可排除发送", allNoTx.Contains("TX-ONLY") ? "yes" : "no", "no");
                CheckText("HTML 记录保留接收", allNoTx.Contains("RX-DATA") ? "yes" : "no", "yes");
            }
            finally
            {
                try { Directory.Delete(noTxDir, true); } catch { /* 临时目录删不掉不影响结果 */ }
            }
        }

        // —— NMEA / GNSS ——
        {
            static string WithCs(string body)
            {
                int cs = 0;
                foreach (char ch in body) cs ^= ch;
                return "$" + body + "*" + cs.ToString("X2") + "\r\n";
            }
            static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text);
            string rmc = WithCs("GNRMC,082105.00,A,3113.82496,N,12128.42206,E,0.81,186.2,271026,,,A");
            string gga = WithCs("GNGGA,082105.00,3113.82496,N,12128.42206,E,1,14,0.8,12.4,M,0.0,M,,");
            string gsa = WithCs("GNGSA,A,3,,,,,,,,,,,,,1.2,0.8,0.9");
            string rtk = WithCs("GNGGA,082106.00,3113.82496,N,12128.42206,E,4,18,0.6,12.4,M,-2.1,M,0.8,0032");
            string gpgsv = WithCs("GPGSV,2,1,08,12,68,214,46,15,54,301,41,24,62,132,44,19,47,088,38");
            string bdgsv = WithCs("BDGSV,2,1,08,24,61,132,44,07,31,176,27,09,22,046,21,11,12,310,18");
            string gagsv = WithCs("GAGSV,1,1,04,19,47,088,38");
            string glgsv = WithCs("GLGSV,1,1,03,09,39,250,32");
            var parser = new NmeaParser();
            byte[] first = Ascii(rmc);
            var head = new byte[8];
            var tail = new byte[first.Length - 8];
            Buffer.BlockCopy(first, 0, head, 0, 8);
            Buffer.BlockCopy(first, 8, tail, 0, tail.Length);
            parser.Push(head);
            CheckText("NMEA 半句未完成", parser.Snapshot().View.ToString(), "Waiting");
            parser.Push(tail);
            GnssFix fix = parser.Snapshot();
            CheckText("NMEA 拼句后定位", fix.View.ToString(), "Fixed");
            CheckText("NMEA 纬度", Math.Abs(fix.Latitude - 31.230416).ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture), "0.0000000");
            CheckText("NMEA 经度", Math.Abs(fix.Longitude - 121.473701).ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture), "0.0000000");
            CheckText("NMEA 速度 km/h", fix.SpeedKmh is double speed && Math.Abs(speed - 0.81 * 1.852) < 0.0001 ? "yes" : "no", "yes");
            CheckText("NMEA 纬度文本", fix.FormatLatitude(), "31.230416° N");
            parser.Push(Ascii(gga + gsa));
            fix = parser.Snapshot();
            CheckText("NMEA 卫星数", fix.Satellites?.ToString() ?? "", "14");
            CheckText("NMEA 海拔", fix.AltitudeMeters is double alt && Math.Abs(alt - 12.4) < 0.001 ? "yes" : "no", "yes");
            CheckText("NMEA 质量 3D", fix.Quality, "3D");
            CheckText("NMEA 时间", fix.FormatUtc(), "08:21:05 UTC");
            CheckText("NMEA DOP",
                (fix.Pdop is double pd && Math.Abs(pd - 1.2) < 0.001 ? "1.2" : "?") + "/" +
                (fix.Hdop is double hd && Math.Abs(hd - 0.8) < 0.001 ? "0.8" : "?") + "/" +
                (fix.Vdop is double vd && Math.Abs(vd - 0.9) < 0.001 ? "0.9" : "?"),
                "1.2/0.8/0.9");
            parser.Push(Ascii(rtk));
            fix = parser.Snapshot();
            CheckText("NMEA 质量 RTK", fix.Quality, "RTK固定");
            CheckText("NMEA 质量显示", fix.FormatQuality(), "RTK 固定解");
            CheckText("NMEA 大地水准面",
                fix.GeoidSeparationMeters is double geoid && Math.Abs(geoid + 2.1) < 0.001 ? "yes" : "no", "yes");
            CheckText("NMEA 差分龄期",
                fix.DifferentialAgeSeconds is double diff && Math.Abs(diff - 0.8) < 0.001 ? "yes" : "no", "yes");
            CheckText("NMEA 基准站", fix.BaseStationId, "0032");
            CheckText("NMEA 纬度 DMS", fix.FormatLatitudeDms(), "31° 13′ 49.50″ N");
            CheckText("NMEA 航向方位", fix.FormatCourseCardinal(), "南");
            CheckText("NMEA 速度节", fix.FormatSpeedKnots(), "0.8 kn");

            parser.Push(Ascii(gpgsv + bdgsv + gagsv + glgsv));
            fix = parser.Snapshot();
            CheckText("GSV 可见卫星", fix.VisibleSatellites.ToString(), "23");
            CheckText("GSV 卫星明细", fix.SatellitesInView.Count.ToString(), "10");
            CheckText("GSV 按 SNR 降序",
                string.Join(",", fix.SatellitesInView.Take(6).Select(sat => sat.Snr?.ToString() ?? "-")),
                "46,44,44,41,38,38");
            CheckText("GSV 星座代码",
                fix.SatellitesInView[0].Code == "G" &&
                fix.SatellitesInView.Take(6).Any(sat => sat.Code == "C") &&
                fix.SatellitesInView.Take(6).Any(sat => sat.Code == "E") ? "yes" : "no", "yes");
            CheckText("GSV 仰角方位",
                fix.SatellitesInView[0].Elevation + "/" + fix.SatellitesInView[0].Azimuth, "68/214");
            var qzss = new NmeaParser();
            qzss.Push(Ascii(WithCs("GPGSV,1,1,01,194,45,120,40")));
            GnssFix qzssFix = qzss.Snapshot();
            CheckText("GSV QZSS 识别", qzssFix.SatellitesInView[0].Code, "J");
            CheckText("GSV QZSS 名称", qzssFix.SatellitesInView[0].DisplayName, "QZSS");
            double kept = parser.Snapshot().Latitude;
            parser.Push(Ascii("$GNRMC,082107.00,A,0000.00000,N,00000.00000,E,0.1,1.0,271026,,,A*00\r\n"));
            fix = parser.Snapshot();
            CheckText("NMEA 坏校验不改位置", Math.Abs(fix.Latitude - kept) < 0.0000001 ? "yes" : "no", "yes");
            CheckText("NMEA 坏校验有标记", fix.ChecksumFailed ? "yes" : "no", "yes");
            parser.Push(Ascii("\xB5\x62\x00" + rmc));
            CheckText("NMEA 噪声后仍能定位", parser.Snapshot().View.ToString(), "Fixed");
            CheckText("NMEA 好句清掉校验失败", parser.Snapshot().ChecksumFailed ? "yes" : "no", "no");
            string lost = WithCs("GNRMC,082108.00,V,3113.82496,N,12128.42206,E,0.10,10.0,271026,,,A");
            parser.Push(Ascii(lost));
            fix = parser.Snapshot();
            CheckText("NMEA 无效仍留坐标", fix.View == GnssView.Invalid && fix.HasPosition ? "yes" : "no", "yes");
            CheckText("NMEA 无效年龄后缀", fix.FormatAge(fix.SentenceTick).EndsWith("前") ? "yes" : "no", "yes");
        }

        // —— 主窗体：接收区占大头，选项保持一行，发送框是矮条 ——
        {
            string ini = Path.Combine(AppContext.BaseDirectory, "sscom51.ini");
            byte[]? iniBackup = File.Exists(ini) ? File.ReadAllBytes(ini) : null;
            using var form = new MainForm();
            CheckText("标题显示版本", form.Text.Contains(MainForm.AppVersion) ? "yes" : "no", "yes");
            CheckText("版本号格式", System.Text.RegularExpressions.Regex.IsMatch(MainForm.AppVersion, @"^\d+\.\d+\.\d+$") ? "yes" : "no", "yes");
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-24000, -24000);
            form.Show();
            // 布局尺寸是物理像素，字体随 DPI 放大；按 96 DPI 的等效尺寸开窗，测试才与缩放无关。
            int scale = Math.Max(96, form.DeviceDpi);
            form.Size = new Size(1200 * scale / 96, 720 * scale / 96);
            Application.DoEvents();
            TextBox? send = null;
            RichTextBox? recv = null;
            ComboBox? verify = null;
            CheckBox? stamp = null;
            foreach (var c in AllControls(form))
            {
                if (c is TextBox { Tag: "send" } tb) send = tb;
                if (c is RichTextBox rich) recv = rich;
                if (c is ComboBox box && box.Items.Contains("ModbusCRC16")) verify = box;
                if (c is CheckBox cb && cb.Text == "加时间戳和分包显示") stamp = cb;
            }
            CheckText("找到发送框", send is null ? "no" : "yes", "yes");
            CheckText("找到接收框", recv is null ? "no" : "yes", "yes");
            string[] portItems = form.PortComboItemsForTest;
            CheckText("端口/波特率字段等宽",
                form.PortFieldWidthForTest == form.BaudComboWidthForTest ? "yes" : "no", "yes");
            var descriptions = ComPortService.PortDescriptions();
            bool hasDescribedPort = false;
            bool descriptionsFormed = true;
            foreach (string item in portItems)
            {
                string portName = ComPortService.NormalizePortName(item);
                if (!descriptions.TryGetValue(portName, out string? description) ||
                    string.IsNullOrWhiteSpace(description)) continue;
                hasDescribedPort = true;
                if (item != ComPortService.FormatPortChoice(portName, description))
                    descriptionsFormed = false;
            }
            CheckText("端口描述格式",
                !hasDescribedPort || descriptionsFormed ? "yes" : "no", "yes");
            if (recv is not null)
            {
                string chunkText = new('x', 70_000);
                form.RenderLogForTest(chunkText);
                CheckText("分块重画进入排队", form.RenderLogBusyForTest ? "yes" : "no", "yes");
                form.CompletePendingRenderLogForTest();
                CheckText("分块重画可同步收尾", form.RenderLogBusyForTest ? "yes" : "no", "no");
                CheckText("同步收尾后内容完整", form.LogTextLengthForBenchmark.ToString(), chunkText.Length.ToString());
                form.RenderLogForTest("");
                Application.DoEvents();

                var rulesOff = LogHighlightRuleStore.CreateDefaults(Theme.IsLight)
                    .Select(rule =>
                    {
                        LogHighlightRule copy = rule.Clone();
                        copy.Enabled = false;
                        return copy;
                    }).ToList();
                form.SetHighlightRulesForTest(rulesOff);
                form.RenderLogForTest("[10:00:00.000]收←◆ ERROR timeout");
                form.CompletePendingRenderLogForTest();
                int errorAt = recv.Text.IndexOf("ERROR", StringComparison.Ordinal);
                recv.Select(errorAt, 5);
                CheckText("关闭规则后渲染不着色",
                    recv.SelectionColor.ToArgb().ToString(), recv.ForeColor.ToArgb().ToString());
                CheckText("关闭规则后渲染无底色",
                    recv.SelectionBackColor.ToArgb().ToString(), recv.BackColor.ToArgb().ToString());

                form.SetHighlightRulesForTest(LogHighlightRuleStore.CreateDefaults(Theme.IsLight));
                form.RenderLogForTest("[10:00:00.000]收←◆ ERROR timeout");
                form.CompletePendingRenderLogForTest();
                errorAt = recv.Text.IndexOf("ERROR", StringComparison.Ordinal);
                recv.Select(errorAt, 5);
                CheckText("开启错误规则后渲染着色",
                    recv.SelectionColor.ToArgb() != recv.ForeColor.ToArgb() ? "yes" : "no", "yes");
                form.RenderLogForTest("");
                Application.DoEvents();

                form.AppendLogForTest(string.Join("\r\n", Enumerable.Range(0, 200).Select(i => "test line " + i)));
                recv.SelectionStart = recv.TextLength;
                recv.SelectionLength = 0;
                recv.ScrollToCaret();
                Application.DoEvents();
                CheckText("日志跟随到底部", form.RecvAtBottomForTest() ? "yes" : "no", "yes");
                recv.SelectionStart = 0;
                recv.SelectionLength = 0;
                recv.ScrollToCaret();
                Application.DoEvents();
                CheckText("上翻后不在底部", form.RecvAtBottomForTest() ? "yes" : "no", "no");
                recv.SelectionStart = recv.TextLength;
                recv.SelectionLength = 0;
                recv.ScrollToCaret();
                Application.DoEvents();
                CheckText("自动滚动默认开启", form.AutoScrollEnabledForTest ? "yes" : "no", "yes");
                form.SetAutoScrollForTest(false);
                form.AppendLogForTest(string.Join("\r\n", Enumerable.Range(0, 200).Select(i => "new line " + i)));
                Application.DoEvents();
                CheckText("关闭自动滚动后不跟随", form.RecvAtBottomForTest() ? "yes" : "no", "no");
                form.SetAutoScrollForTest(true);
                Application.DoEvents();
                CheckText("重新开启自动滚动回到底部", form.RecvAtBottomForTest() ? "yes" : "no", "yes");
                recv.SelectionStart = 0;
                recv.SelectionLength = 0;
                recv.ScrollToCaret();
                Application.DoEvents();
                form.AppendLogForTest("auto-scroll-tail");
                Application.DoEvents();
                CheckText("勾选时上翻后仍回底", form.RecvAtBottomForTest() ? "yes" : "no", "yes");
                recv.SelectionStart = 0;
                recv.SelectionLength = 0;
                recv.ScrollToCaret();
                Application.DoEvents();
                Size before = form.Size;
                form.Size = new Size(form.Width - 80, form.Height - 40);
                Application.DoEvents();
                CheckText("尺寸变化后仍跟随到底部", form.RecvAtBottomForTest() ? "yes" : "no", "yes");
                form.Size = before;
                Application.DoEvents();

                const string anchor = "PAUSE-ANCHOR-9f3c";
                const string heldTail = "HELD-TAIL-9f3c";
                form.AppendLogForTest(anchor);
                Application.DoEvents();
                int frozen = recv.TextLength;
                form.SetDisplayPausedForTest(true);
                form.AppendLogForTest(heldTail);
                Application.DoEvents();
                CheckText("停止显示时窗口不刷新", recv.TextLength.ToString(), frozen.ToString());
                CheckText("停止显示时新内容记在旁边", form.LogicalWindowTextForTest().Contains(heldTail) ? "yes" : "no", "yes");
                CheckText("停止显示时画面不含新内容", recv.Text.Contains(heldTail) ? "yes" : "no", "no");
                form.SetDisplayPausedForTest(false);
                form.CompletePendingRenderLogForTest();
                Application.DoEvents();
                CheckText("继续显示后补上内容", recv.Text.Contains(heldTail) ? "yes" : "no", "yes");
                CheckText("继续显示后旧内容还在", recv.Text.Contains(anchor) ? "yes" : "no", "yes");

                form.SetDisplayPausedForTest(true);
                int frozenBig = recv.TextLength;
                form.AppendLogForTest(new string('Q', 70_000));
                CheckText("大段停止显示不进窗口", recv.TextLength.ToString(), frozenBig.ToString());
                form.SetDisplayPausedForTest(false);
                CheckText("大段恢复走分块补画", form.RenderLogBusyForTest ? "yes" : "no", "yes");
                form.CompletePendingRenderLogForTest();
                Application.DoEvents();
                CheckText("大段恢复后长度增加", recv.TextLength == frozenBig + 70_000 ? "yes" : "no", "yes");
                CheckText("大段恢复后旧内容还在", recv.Text.Contains(anchor) ? "yes" : "no", "yes");

                const string visible = "VISIBLE-MARKER-9f3c";
                form.AppendLogForTest(visible);
                form.SetDisplayPausedForTest(true);
                form.SetDisplayLimitForTest(32);
                try
                {
                    form.SimulateIncomingForTest(Enumerable.Repeat((byte)'B', 128).ToArray());
                    CheckText("超限时窗口仍停着", recv.Text.Contains(visible) ? "yes" : "no", "yes");
                    CheckText("超限后逻辑文本丢掉旧画面", form.LogicalWindowTextForTest().Contains(visible) ? "yes" : "no", "no");
                    form.SetDisplayPausedForTest(false);
                    form.CompletePendingRenderLogForTest();
                    Application.DoEvents();
                    CheckText("超限恢复后窗口换成保留内容", recv.Text.Contains(visible) ? "yes" : "no", "no");
                    CheckText("超限恢复后有新数据", recv.TextLength > 0 ? "yes" : "no", "yes");
                }
                finally
                {
                    form.SetDisplayLimitForTest(1_000_000);
                    if (form.DisplayPausedForTest) form.SetDisplayPausedForTest(false);
                    form.CompletePendingRenderLogForTest();
                    form.ClearReceiveForTest();
                    form.HideToastForTest();
                }
            }
            CheckText("搜索无结果提示", form.SearchForTest("definitely-not-in-log") ? "yes" : "no", "yes");
            form.HideToastForTest();
            if (send is not null && recv is not null)
            {
                CheckText("接收区高于发送区", recv.Height > send.Height * 3 ? "yes" : "no", "yes");
                CheckText("发送框约三行", send.Height >= form.Font.Height * 2 && send.Height <= form.Font.Height * 6 ? "yes" : "no", "yes");
            }
            if (stamp is not null)
            {
                form.OpenReceiveSettingsForTest();
                Application.DoEvents();
                CheckText("接收设置浮层可打开", stamp.Visible ? "yes" : "no", "yes");
                CheckText("时间戳文字不被拆开", stamp.Width >= stamp.PreferredSize.Width - 2 ? "yes" : "no", "yes");
                ComboBox? splitCombo = null;
                ComboBox? paletteCombo = null;
                CheckBox? echoBox = null;
                foreach (var c in AllControls(form))
                {
                    if (c is ComboBox box)
                    {
                        bool split = false;
                        foreach (object item in box.Items)
                            if (item is string text && text.IndexOf("MB", StringComparison.Ordinal) >= 0) split = true;
                        if (split) splitCombo = box;
                        else if (box.Items.Count == 2) paletteCombo = box;
                    }
                    if (c is CheckBox cb && cb.Text == "含发送回显") echoBox = cb;
                }
                if (splitCombo?.Parent is Control splitHost)
                {
                    bool splitFits = splitCombo.Top >= 0 && splitCombo.Bottom <= splitHost.ClientSize.Height;
                    CheckText("分卷下拉完整可见", splitFits ? "yes" : $"{splitCombo.Bounds}/{splitHost.ClientSize}", "yes");
                }
                if (paletteCombo?.Parent is Control paletteHost)
                {
                    bool paletteFits = paletteCombo.Top >= 0 && paletteCombo.Bottom <= paletteHost.ClientSize.Height;
                    CheckText("调色板下拉完整可见", paletteFits ? "yes" : $"{paletteCombo.Bounds}/{paletteHost.ClientSize}", "yes");
                }
                if (splitCombo is not null && echoBox is not null)
                {
                    Rectangle splitScreen = splitCombo.RectangleToScreen(splitCombo.ClientRectangle);
                    Rectangle echoScreen = echoBox.RectangleToScreen(echoBox.ClientRectangle);
                    CheckText("分卷下拉不压住回显", splitScreen.IntersectsWith(echoScreen) ? "yes" : "no", "no");
                }
                form.CloseSettingsForTest();
                Application.DoEvents();
            }
            if (verify is not null)
            {
                form.OpenSendSettingsForTest();
                Application.DoEvents();
                CheckText("发送设置浮层可打开", verify.Visible ? "yes" : "no", "yes");
                int right = verify.PointToScreen(new Point(verify.Width, 0)).X;
                int limit = form.PointToScreen(new Point(form.ClientSize.Width, 0)).X + 4;
                CheckText("校验框在窗口内", right <= limit ? "yes" : "no", "yes");
                form.CloseSettingsForTest();
                Application.DoEvents();
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

            form.Width = Math.Max(820, form.MinimumSize.Width);
            Application.DoEvents();
            form.ResetMultiPanelWidthForTest();
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
                form.SetMultiPageCountForTest(1);
                CheckText("多条默认一页", form.MultiPageCountForTest.ToString(), "1");
                CheckText("第1页可见", form.MultiPageVisibleForTest(0) ? "yes" : "no", "yes");
                CheckText("第2页默认隐藏", form.MultiPageVisibleForTest(1) ? "yes" : "no", "no");
                CheckText("第1页不能删除", form.MultiPageCloseVisibleForTest(0) ? "yes" : "no", "no");
                int visibleTabs = 0;
                int dataBoxes = 0;
                foreach (var c in AllControls(form))
                {
                    if (c is Button pageButton && pageButton.Text is "第1页" or "第2页" or "第3页" && pageButton.Visible) visibleTabs++;
                    if (c is TextBox tb && tb.Tag is Point cell && cell.Y == 1) dataBoxes++;
                }
                CheckText("默认只显示一个页签", visibleTabs.ToString(), "1");
                CheckText("默认只建当前页99行", dataBoxes.ToString(), "99");

                form.AddMultiPageForTest();
                CheckText("增加到两页", form.MultiPageCountForTest.ToString(), "2");
                CheckText("新增页自动选中", form.MultiPageSelectedForTest.ToString(), "1");
                CheckText("第2页显示", form.MultiPageVisibleForTest(1) ? "yes" : "no", "yes");
                CheckText("第2页有删除入口", form.MultiPageCloseVisibleForTest(1) ? "yes" : "no", "yes");
                form.SetMultiPageDataForTest(1, 0, "第二页");

                form.AddMultiPageForTest();
                CheckText("增加到三页", form.MultiPageCountForTest.ToString(), "3");
                form.SetMultiPageDataForTest(2, 0, "第三页");
                int builtRows = 0;
                foreach (var c in AllControls(form))
                    if (c is TextBox builtBox && builtBox.Tag is Point builtCell && builtCell.Y == 1) builtRows++;
                CheckText("三页按需建齐297行", builtRows.ToString(), "297");
                form.AddMultiPageForTest();
                CheckText("不能超过三页", form.MultiPageCountForTest.ToString(), "3");

                form.RemoveMultiPageForTest(1);
                CheckText("删除中间页", form.MultiPageCountForTest.ToString(), "2");
                CheckText("后续页内容前移", form.MultiPageDataForTest(1, 0), "第三页");
                CheckText("末页已清空", form.MultiPageDataForTest(2, 0), "");
                CheckText("删除后选中前移页", form.MultiPageSelectedForTest.ToString(), "1");
                CheckText("第3页删除入口收起", form.MultiPageCloseVisibleForTest(2) ? "yes" : "no", "no");
                form.RemoveMultiPageForTest(0);
                CheckText("第1页不可删除", form.MultiPageCountForTest.ToString(), "2");
                form.RemoveMultiPageForTest(1);
                CheckText("删除到只剩一页", form.MultiPageCountForTest.ToString(), "1");
                CheckText("只剩第1页可见", form.MultiPageVisibleForTest(0) && !form.MultiPageVisibleForTest(1) ? "yes" : "no", "yes");
            }

            form.SetMultiPageDataForTest(0, 0, "palette-command");
            form.OpenCommandPaletteForTest();
            Application.DoEvents();
            CheckText("命令面板可打开", form.CommandPaletteVisibleForTest ? "yes" : "no", "yes");
            CheckText("命令面板有已保存命令", form.CommandPaletteItemCountForTest > 0 ? "yes" : "no", "yes");
            form.SetCommandPaletteQueryForTest("palette-command");
            Application.DoEvents();
            CheckText("命令面板搜索命中", form.CommandPaletteFilteredCountForTest >= 1 ? "yes" : "no", "yes");
            form.SetCommandPaletteQueryForTest("no-such-command");
            Application.DoEvents();
            CheckText("命令面板搜索无结果", form.CommandPaletteFilteredCountForTest.ToString(), "0");
            form.CloseCommandPaletteForTest();
            Application.DoEvents();
            CheckText("命令面板可关闭", form.CommandPaletteVisibleForTest ? "yes" : "no", "no");

            Button? paletteButton = null;
            foreach (var c in AllControls(form))
                if (c is Button button && button.Text == "命令面板") paletteButton = button;
            CheckText("命令面板有按钮入口", paletteButton is null ? "no" : "yes", "yes");
            if (paletteButton is not null)
            {
                paletteButton.PerformClick();
                Application.DoEvents();
                CheckText("按钮可打开命令面板", form.CommandPaletteVisibleForTest ? "yes" : "no", "yes");
                form.CloseCommandPaletteForTest();
                Application.DoEvents();
            }

            CheckText("保存下拉三项", string.Join("|", form.SaveMenuTextsForTest),
                "保存数据（TXT + DAT，选目录）|另存为带颜色日志（RTF）...|另存为带颜色日志（HTML）...");

            form.SetHighlightRulesForTest(LogHighlightRuleStore.CreateDefaults(Theme.IsLight));
            form.RenderLogForTest("[10:00:00.000]收←◆ ERROR value <1> & ok");
            Application.DoEvents();
            form.CompletePendingRenderLogForTest();
            string exportHtml = form.ExportHtmlForTest();
            CheckText("HTML 导出含转义", exportHtml.Contains("&lt;1&gt; &amp;") ? "yes" : "no", "yes");
            CheckText("HTML 导出含高亮 span", exportHtml.Contains("<span style=") ? "yes" : "no", "yes");
            CheckText("HTML 导出是完整文档",
                exportHtml.StartsWith("<!DOCTYPE html>") && exportHtml.Contains("</html>") ? "yes" : "no", "yes");
            string exportRtf = form.ExportRtfForTest();
            CheckText("RTF 导出文档头",
                exportRtf.StartsWith(@"{\rtf1") && exportRtf.EndsWith("}") ? "yes" : "no", "yes");
            CheckText("RTF 导出带段落", exportRtf.Contains(@"\par") ? "yes" : "no", "yes");

            List<LogHighlightRule> noRules = LogHighlightRuleStore.CreateDefaults(Theme.IsLight)
                .Select(rule =>
                {
                    LogHighlightRule copy = rule.Clone();
                    copy.Enabled = false;
                    return copy;
                }).ToList();
            form.SetHighlightRulesForTest(noRules);
            form.RenderLogForTest("ERROR plain data");
            Application.DoEvents();
            form.CompletePendingRenderLogForTest();
            CheckText("关闭规则后导出无高亮 span",
                form.ExportHtmlForTest().Contains("<span style=") ? "yes" : "no", "no");

            using (var exportFont = new Font("Consolas", 9F))
            {
                var exportPalette = new LogHighlightPalette(
                    Color.White, Color.Black, Color.Gray, Color.Teal, Color.Goldenrod,
                    Color.Teal, Color.LightGoldenrodYellow);
                var emptyHighlighter = new LogHighlighter();
                CheckText("HTML 纯数据转义",
                    LogExport.ToHtml("a<b&c", false, emptyHighlighter, exportPalette, exportFont, "t")
                        .Contains("a&lt;b&amp;c") ? "yes" : "no", "yes");
                string rtfPlain = LogExport.ToRtf("plain", false, emptyHighlighter, exportPalette, exportFont);
                CheckText("RTF 纯数据无颜色段", rtfPlain.Contains(@"{\cf") ? "yes" : "no", "no");

                var fileHighlighter = new LogHighlighter();
                fileHighlighter.SetRules(LogHighlightRuleStore.CreateDefaults(true));
                string exportDir = Path.Combine(Path.GetTempPath(), "sscom-export-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(exportDir);
                    string rtfPath = Path.Combine(exportDir, "log.rtf");
                    File.WriteAllText(rtfPath,
                        LogExport.ToRtf("[10:00:00.000]收←◆ ERROR value", false, fileHighlighter, exportPalette, exportFont),
                        Encoding.ASCII);
                    string htmlPath = Path.Combine(exportDir, "log.html");
                    File.WriteAllText(htmlPath,
                        LogExport.ToHtml("[10:00:00.000]收←◆ ERROR value", false, fileHighlighter, exportPalette, exportFont, "t"),
                        new UTF8Encoding(true));
                    CheckText("RTF 文件写读", File.ReadAllText(rtfPath).StartsWith(@"{\rtf1") ? "yes" : "no", "yes");
                    CheckText("HTML 文件写读", File.ReadAllText(htmlPath).Contains("</html>") ? "yes" : "no", "yes");
                }
                finally
                {
                    try { Directory.Delete(exportDir, true); } catch { /* 临时目录删不掉不影响结果 */ }
                }
            }

            form.SetHighlightRulesForTest(LogHighlightRuleStore.CreateDefaults(Theme.IsLight));
            form.RenderLogForTest("");
            Application.DoEvents();

            string uiCaptureDir = Path.Combine(Path.GetTempPath(), "sscom-ui-capture-" + Guid.NewGuid().ToString("N"));
            try
            {
                form.StartHtmlCaptureForTest(uiCaptureDir, 10, true, true);
                CheckText("界面可启动 HTML 记录", form.HtmlCaptureActiveForTest ? "yes" : "no", "yes");
                form.AppendLogForTest("[10:00:00.000]收←◆ UI-CAP-123 <x>");
                Application.DoEvents();
                form.StopHtmlCaptureForTest();
                Application.DoEvents();
                string[] uiParts = Directory.GetFiles(uiCaptureDir, "*.html");
                string uiHtml = uiParts.Length > 0 ? File.ReadAllText(uiParts[0]) : "";
                CheckText("界面 HTML 记录落盘", uiParts.Length > 0 ? "yes" : "no", "yes");
                CheckText("界面 HTML 记录含日志", uiHtml.Contains("UI-CAP-123") ? "yes" : "no", "yes");
                CheckText("界面 HTML 记录含高亮", uiHtml.Contains("<span style=") ? "yes" : "no", "yes");
            }
            finally
            {
                try { form.StopHtmlCaptureForTest(); } catch { /* 已停止 */ }
                try { Directory.Delete(uiCaptureDir, true); } catch { /* 临时目录删不掉不影响结果 */ }
            }

            form.ToggleComposerForTest();
            Application.DoEvents();
            CheckText("发送区可收起", form.ComposerVisibleForTest ? "no" : "yes", "yes");
            form.ToggleComposerForTest();
            Application.DoEvents();
            CheckText("发送区可展开", form.ComposerVisibleForTest ? "yes" : "no", "yes");

            form.SetSidebarCollapsedForTest(false);
            Application.DoEvents();
            CheckText("侧栏默认展开", form.SidebarVisibleForTest ? "yes" : "no", "yes");
            CheckText("端口字段与模型同步", form.PortFieldTextForTest, form.PortComboTextForTest);
            form.OpenPortPopupForTest();
            Application.DoEvents();
            CheckText("端口浮层可打开", form.PortPopupVisibleForTest ? "yes" : "no", "yes");
            form.ClosePortPopupForTest();
            Application.DoEvents();
            CheckText("日志头端口有内容", form.LogPortPickerTextForTest.Length > 0 ? "yes" : "no", "yes");
            form.OpenLogPortPopupForTest();
            Application.DoEvents();
            CheckText("日志头端口浮层可打开", form.PortPopupVisibleForTest ? "yes" : "no", "yes");
            form.ClosePortPopupForTest();
            Application.DoEvents();
            CheckText("侧栏展开宽度", form.SidebarColumnWidthForTest.ToString(), form.SidebarExpandedWidthForTest.ToString());
            Button? sidebarCollapse = null;
            foreach (var c in AllControls(form))
                if (c is Button { Text: "«" } b) sidebarCollapse = b;
            CheckText("日志头有收起开关", sidebarCollapse is { Visible: true } ? "yes" : "no", "yes");
            Button? logOpen = null;
            foreach (var c in AllControls(form))
                if (c is Button { Text: "打开" or "关闭" } b) logOpen = b;
            CheckText("日志头有串口开关", logOpen is { Visible: true } ? "yes" : "no", "yes");
            if (sidebarCollapse is not null)
            {
                sidebarCollapse.PerformClick();
                CheckText("收起立即生效", form.SidebarColumnWidthForTest.ToString(), "0");
                Application.DoEvents();
                CheckText("收起后侧栏完全隐藏", form.SidebarVisibleForTest ? "no" : "yes", "yes");
                CheckText("收起后开关仍在日志头", form.SidebarToggleVisibleForTest ? "yes" : "no", "yes");
                CheckText("收起后开关变为展开", form.SidebarToggleTextForTest, "»");
                CheckText("收起后日志头仍显示连接", form.LogTitleVisibleForTest ? "yes" : "no", "yes");
                Button? sidebarExpand = null;
                foreach (var c in AllControls(form))
                    if (c is Button { Text: "»" } b) sidebarExpand = b;
                CheckText("找到日志头展开开关", sidebarExpand is { Visible: true } ? "yes" : "no", "yes");
                if (sidebarExpand is not null)
                {
                    sidebarExpand.PerformClick();
                    Application.DoEvents();
                    CheckText("展开后侧栏恢复", form.SidebarVisibleForTest ? "yes" : "no", "yes");
                    CheckText("展开后宽度恢复", form.SidebarColumnWidthForTest.ToString(), form.SidebarExpandedWidthForTest.ToString());
                }
            }
            if (form.MainMenuStrip is not null)
            {
                var names = form.MainMenuStrip.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                int toolsAt = names.IndexOf("工具");
                int helpAt = names.IndexOf("帮助");
                CheckText("工具菜单在帮助左边", toolsAt >= 0 && helpAt > toolsAt ? "yes" : string.Join(",", names), "yes");
                ToolStripMenuItem? gnss = null;
                if (toolsAt >= 0 && form.MainMenuStrip.Items[toolsAt] is ToolStripMenuItem tools)
                {
                    foreach (ToolStripItem item in tools.DropDownItems)
                        if (item is ToolStripMenuItem menu && menu.Text == "GNSS") gnss = menu;
                }
                CheckText("工具菜单有 GNSS", gnss is null ? "no" : "yes", "yes");
            }
            form.OpenGnssForTest();
            Application.DoEvents();
            CheckText("GNSS 小窗可打开", form.GnssOpenForTest ? "yes" : "no", "yes");
            CheckText("GNSS 窗口标题", form.GnssTitleForTest, "GNSS 定位");
            CheckText("打开 GNSS 开始解析", form.GnssParsingForTest ? "yes" : "no", "yes");
            void Feed(string sentence)
            {
                int check = 0;
                foreach (char ch in sentence) check ^= ch;
                form.FeedGnssForTest(System.Text.Encoding.ASCII.GetBytes(
                    "$" + sentence + "*" + check.ToString("X2") + "\r\n"));
            }
            const string rmcBody = "GNRMC,082105.00,A,3113.82496,N,12128.42206,E,0.81,186.2,271026,,,A";
            Feed(rmcBody);
            CheckText("GNSS 小窗显示纬度", form.GnssSummaryForTest.Contains("31.230416° N") ? "yes" : form.GnssSummaryForTest, "yes");
            Feed("GNGGA,082105.00,3113.82496,N,12128.42206,E,4,18,0.6,12.4,M,-2.1,M,0.8,0032");
            Feed("GNGSA,A,3,,,,,,,,,,,,,1.2,0.8,0.9");
            Feed("GPGSV,2,1,08,12,68,214,46,15,54,301,41,24,62,132,44,19,47,088,38");
            Feed("BDGSV,2,1,08,24,61,132,44,07,31,176,27,09,22,046,21,11,12,310,18");
            Feed("GAGSV,1,1,04,19,47,088,38");
            Feed("GLGSV,1,1,03,09,39,250,32");
            CheckText("GNSS 小窗显示卫星", form.GnssSummaryForTest.Contains("G12:46") ? "yes" : form.GnssSummaryForTest, "yes");
            CheckText("GNSS 小窗可见卫星数", form.GnssSummaryForTest.Contains("|23|") ? "yes" : form.GnssSummaryForTest, "yes");
            form.CloseGnssForTest();
            Application.DoEvents();
            CheckText("关闭 GNSS 停止解析", form.GnssParsingForTest ? "yes" : "no", "no");
            foreach (var c in AllControls(form))
                if (c is Button { Text: "隐藏" } hideMulti) hideMulti.PerformClick();
            int layoutScale = Math.Max(96, form.DeviceDpi);
            form.Size = new Size(1200 * layoutScale / 96, 720 * layoutScale / 96);
            Application.DoEvents();
            CheckBox? autoScroll = null;
            Button? pauseDisplay = null;
            Button? clearWindow = null;
            Button? saveData = null;
            foreach (var c in AllControls(form))
            {
                if (c is CheckBox { Text: "自动滚动" } box) autoScroll = box;
                if (c is Button { Text: "暂停显示" } pause) pauseDisplay = pause;
                if (c is Button { Text: "清除窗口" } clear) clearWindow = clear;
                if (c is Button { Text: "保存数据 ▾" } save) saveData = save;
            }
            bool SameRow(Control? a, Control? b) =>
                a is not null && b is not null &&
                Math.Abs(a.PointToScreen(Point.Empty).Y - b.PointToScreen(Point.Empty).Y) < Math.Max(8, a.Height);
            CheckText("有暂停显示", pauseDisplay is null ? "no" : "yes", "yes");
            CheckText("有清除窗口", clearWindow is null ? "no" : "yes", "yes");
            CheckText("有保存数据", saveData is null ? "no" : "yes", "yes");
            CheckBox? hexDisplay = null;
            foreach (var c in AllControls(form))
                if (c is CheckBox { Text: "HEX显示" } box) hexDisplay = box;
            CheckText("暂停显示与自动滚动同一行", SameRow(autoScroll, pauseDisplay) ? "yes" : "no", "yes");
            CheckText("清除窗口与自动滚动同一行", SameRow(autoScroll, clearWindow) ? "yes" : "no", "yes");
            CheckText("保存数据与自动滚动同一行", SameRow(autoScroll, saveData) ? "yes" : "no", "yes");
            CheckText("自动滚动与 HEX显示 同一行", SameRow(hexDisplay, autoScroll) ? "yes" : "no", "yes");
            int saveGap = saveData is null || clearWindow is null
                ? -1
                : saveData.PointToScreen(Point.Empty).X - (clearWindow.PointToScreen(Point.Empty).X + clearWindow.Width);
            CheckText("保存数据紧跟清除窗口", saveGap is >= 0 and <= 24 ? "yes" : saveGap.ToString(), "yes");
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
