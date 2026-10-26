# SscomRe — SSCOM 5.13.1 的 C# 重写版

按逆向整理出的行为规格，用 **C# WinForms / .NET Framework 4.8** 重写 `sscom5.13.1.exe`（Delphi/VCL 串口网络调试工具）的日常收发功能。

- 行为唯一依据：[sscom5.13.1-rewrite-spec.md](sscom5.13.1-rewrite-spec.md)
- 当前状态：M1–M9 全部实现；`--selftest` 186/186 通过
- 目标系统：Windows 10/11（自带 .NET Framework 4.8）
- 原版程序、解包镜像和反编译源码**不在本仓库中**，详见 [_analysis/README.md](_analysis/README.md)

## 功能

### 串口

- 枚举本机串口并排序；端口名按 `COM` + 十进制整数归一化（`COM03` → `COM3`）
- 26 档波特率（110–2 Mbps）+ 自定义波特率（110–50,000,000，越界夹取并提示）
- 数据位 5/6/7/8、停止位 1/1.5/2、校验 None/Odd/Even/Mark/Space、流控 HardWare/SoftWare/None/Custom
- 输入缓冲、读/写超时可在「更多串口设置」中修改，**下次打开串口生效**
- DTR / RTS 勾选与运行时切换；CTS / DSR / RLSD 状态显示
- 串口独占打开；打开失败、DTR/RTS 失败按规格提示并复位相关状态

### 发送

- 文本发送：`\0 \a \b \t \n \v \f \r \\ \xHH` 转义；可选自动追加 `\r\n`
- HEX 发送：两字符拼一字节，允许空格/CR/LF；非法字符整段不发并报错
- 校验模式 0–7：Modbus CRC16、CCITT CRC16、CRC32、0-ADD8、ADD8、XOR8、ADD16；可设帧首跳过字节和帧尾保留字节，CRC32 值显示在旁侧小框
- 单条定时发送；发送文件（256 字节分块 + 5 档块间延时 + 停止按钮 + 进度条）
- 多条字符串：3 页 × 99 行，共 297 行；每行 HEX、数据、顺序号、延时（ms）、注释、点击发送；顺序号排序后循环发送，`+`/`−` 调发送列宽
- 终端仿真：在接收框直接输入；支持「回车后发送输入行」「按键立即发送」「本地回显」「回车补 0x0A」四个开关（前两个互斥）
- 发送回显：勾选「加时间戳和分包显示」时，以 `[HH:MM:SS.mmm]发→◇` 追加到接收框

### 接收

- 文本 / HEX 两种显示方式，可随时切换（带时间戳丢失警告）
- 时间戳分包显示；GBK 半个汉字跨包拼接
- 显示缓冲上限 200K / 500K / 1M / 2M / 5M；状态栏底色随接收量分档
- 清除窗口；保存数据（原始 `SAVE*.DAT` + 窗口内容 `SaveWindows*.TXT`）
- 接收数据到文件（按模式/端口名生成 `ReceivedTofile-*.DAT`，逐段追加）
- 打开文件预览前 4000 字节；状态栏 `S:` / `R:` 计数与串口参数描述
- 接收框字体、字号、字形、文字颜色、背景颜色可调

### 网络

- TCP 客户端、TCP 服务端、UDP 三种模式与端口下拉/菜单联动
- 就绪标志语义：客户端连上、服务端接到连接、UDP 绑定成功后才允许发送
- 服务端按规格**只向第 0 个连接发送**；其余连接只读走数据，避免句柄泄漏
- 模式切换时停定时器、断连接、切换串口/网络设置栏；网络模式字段按协议启用/禁用
- UDP 收到数据时把发送方 IP 回填到「远程IP」

### 参数与界面

- 保存参数 / 导入 ini：兼容原版 `sscom51.ini` 的键位与转义（`,`/CR/LF → `0x02`/`0x03`/`0x04`），支持三页多条字符串
- 关窗自动保存；启动时读取当前工作目录下的 `sscom51.ini`
- 窗口几何记忆（按规格夹取）；多条面板宽度、发送列宽记忆；启动时立即打开串口/连接网络
- 菜单「显示缓冲上限」「发送文件设置」「终端仿真设置」「更多串口设置」「关于」

## 构建

需要 .NET SDK（已验证 **10.0.401**；其他能编译 `net48` 的 SDK 也可）和 .NET Framework 4.8 目标包。

```bash
cd SscomRe
dotnet build -c Release
```

输出固定为：

```
SscomRe/bin/net48/SscomRe.exe
SscomRe/bin/net48/SscomRe.exe.config
SscomRe/bin/net48/SscomRe.pdb
```

`OutputPath` 在 csproj 中固定，不再生成 `Debug/`、`Release/` 等额外目录。

## 运行

双击 `SscomRe.exe`，或在命令行中：

```bash
cd SscomRe/bin/net48
./SscomRe.exe
```

注意：`sscom51.ini` 按**当前工作目录**读写。从仓库根启动会读写根目录的 ini；从 `bin/net48` 启动会读写该目录的 ini。从资源管理器双击时当前目录就是 exe 目录，与原版行为一致。

## 自检

```bash
cd SscomRe/bin/net48
./SscomRe.exe --selftest
```

- 当前结果：`SELFTEST pass=186 fail=0`
- 覆盖组包/转义、7 种校验、接收显示与截断、多条发送状态机、ini 往返、终端按键、窗口几何、主窗体布局等
- 自检会临时创建主窗体并触发自动保存；开始前会备份现有 `sscom51.ini`，结束后原样恢复（没有则删除）
- 布局检查按 `DeviceDpi` 缩放测试窗口，100%–200% 缩放下都可复现

正常退出码为 0，失败时返回 1 并打印 `FAIL` 行。自检输出使用系统代码页（中文 Windows 为 GBK）；在 Git Bash 下若显示乱码，可用 `| iconv -f GBK -t UTF-8` 查看。

## 发布

目标机只需 Windows 10/11 自带的 .NET Framework 4.8：

1. `dotnet build -c Release`
2. 拷贝 `SscomRe.exe` 和 `SscomRe.exe.config` 到目标目录
3. 在该目录运行（ini 与保存/接收文件都写在运行目录）

不需要安装 .NET 运行时，也不提供单文件发布。

## 仓库结构

```
sscom5.13.1-rewrite-spec.md   行为规格（唯一依据）
sscom-spec-handoff.md         规格补全阶段的逆向交接（历史记录）
HANDOFF.md                    实现/维护交接：架构、不变量、构建验证
REWRITE-PLAN.md               里程碑与实现状态
_analysis/                    逆向分析脚本、DFM/Ghidra 产物（不含原程序）
SscomRe/                      工程源码（.NET Framework 4.8 WinForms）
  Program.cs                  入口；--selftest；全局异常写 crash.log
  MainForm.cs                 主窗体、菜单、收发编排、ini 读写
  ComPortService.cs           串口层
  NetworkService.cs           TCP 客户端/服务端/UDP
  ReceiveDisplay.cs           接收显示管线
  SendComposer.cs             文本/HEX 组包与转义
  Checksums.cs                校验 0–7
  TerminalInput.cs            终端按键纯逻辑
  MultiSendPlanner.cs         多条循环发送状态机
  IniDocument.cs              sscom51.ini 读写
  PortSettingsDialog.cs       更多串口设置
  WindowPlacement.cs          窗口几何夹取
  InputNormalizer.cs          行内数字框归一化
  SelfTest.cs                 --selftest 用例
```

## 已知限制 / 未实现

以下为规格中标注「未知」或用户明确排除的部分，**不属于缺陷**：

- STM/GD32、STC/IAP15 烧写器
- 数据波形、English 界面切换、转换计算器、LUA 脚本、帮助页、广告/更新检查
- 「更多串口设置」为自建对话框，不是原版 CPort 组件对话框；流控 `Custom` 在打开时按 `None` 处理
- `N1058`（多条发送间隔）固定写 `1000`，读入时忽略（原程序用隐藏中转框，重写无此控件）
- `N1080` 的 `1` 和 `2` 都映射为 TCPClient（照规格原文的怪癖）
- 界面按物理像素布局、字体按 DPI 渲染。最小窗口随 DPI 放大，但不超过屏幕工作区；底栏放不下时在底栏内部滚动，避免把接收区挤扁
- 自检不依赖真实串口/网络硬件；实机串口收发仍需在目标机器上复验

## 故障排查

- **编译报 `MSB3027` / 文件被锁定**：`SscomRe.exe` 正在运行，关闭窗口后重编，不要用 `-o` 换输出目录
- **启动即闪退**：查看 exe 同目录的 `crash.log`
- **端口下拉只有 TCPClient**：本机没有枚举到串口；按规格自动切到 TCP 客户端，插好设备后点「通讯端口 → 刷新串口列表」
- **保存参数失败**：确认 exe 所在目录（或当前工作目录）可写，检查 `sscom51.ini`
- **提示 ini 保存失败**：文案沿用原版「无法保存ini文件!请拷贝exe文件到且它目录试试.」

## 文档

- [sscom5.13.1-rewrite-spec.md](sscom5.13.1-rewrite-spec.md)：逐条标注观察/推断/未知的行为规格
- [HANDOFF.md](HANDOFF.md)：接手开发必须先读的工程约定、线程模型、已踩过的坑
- [REWRITE-PLAN.md](REWRITE-PLAN.md)：M1–M9 里程碑与实现范围
- [sscom-spec-handoff.md](sscom-spec-handoff.md)：规格补全阶段的逆向任务书（历史记录）
- [_analysis/README.md](_analysis/README.md)：分析产物的用途、来源与复现方法
