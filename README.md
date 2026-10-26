# SscomRe — SSCOM 5.13.1 的 C# 重写版

按逆向整理出的行为规格，用 **C# WinForms / .NET Framework 4.8** 重写 `sscom5.13.1.exe`（Delphi/VCL 串口网络调试工具）的日常收发功能。

- 行为唯一依据：[sscom5.13.1-rewrite-spec.md](sscom5.13.1-rewrite-spec.md)
- 当前状态：M1–M9 全部实现；Phase 1 深/浅双主题、Phase 2 三栏工作区、Phase 3A 左栏折叠、Phase 3B 设置浮层、Phase 3C 日志性能/短动效/toast 已接入；`--selftest` 198/198 通过
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
- 低频发送项收在「发送设置」浮层：帧尾位置、校验算法与 CRC 值；composer 只留 HEX 发送、定时发送和回车换行
- 单条定时发送；发送文件（256 字节分块 + 5 档块间延时 + 停止按钮 + 进度条）
- 多条字符串：3 页 × 99 行，共 297 行；每行 HEX、数据、顺序号、延时（ms）、注释、点击发送；顺序号排序后循环发送，`+`/`−` 调发送列宽
- 终端仿真：在接收框直接输入；支持「回车后发送输入行」「按键立即发送」「本地回显」「回车补 0x0A」四个开关（前两个互斥）
- 发送回显：勾选「加时间戳和分包显示」时，以 `[HH:MM:SS.mmm]发→◇` 追加到接收框

### 接收

- 文本 / HEX 两种显示方式，可随时切换（带时间戳丢失警告）
- 低频接收项收在「接收设置」浮层：时间戳/分包、超时时间、接收数据到文件
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

### 界面主题（Phase 1）

- 两套主题：深色专业风、浅色现代风；菜单「显示 → 浅色主题」切换，勾选即浅色
- 深色：深海军蓝画布 + 青绿主操作 + 琥珀“已启用/勾选”；浅色：冷白灰 + 深青主操作 + 琥珀勾选
- 切换即时生效，写入 `sscom51.ini` 的 `N1092`（`0`=深色、`1`=浅色），下次启动保持
- 全自绘控件：按钮（含矢量图标）、复选框、下拉框、输入框、菜单、状态栏、多条分页
- 窗口使用 DWM 深/浅标题栏与 Windows 11 圆角（Win10 自动降级），保留原生拖动/吸附/最大化
- 接收区/发送区等宽字体；菜单勾选改用 `✓` 前缀，避免原生浅色勾选列
- 多条字符串由原生 TabControl 改为自绘分页按钮，去掉系统浅色页框
- 字体、字号、字形、文字颜色、背景颜色仍可调，并随 `sscom51.ini` 保存；切换主题会把接收区背景/文字颜色重置为主题默认值，字号和字形保留

设计 token：

| 用途 | 深色 | 浅色 |
|---|---|---|
| 画布 | `#0B1119` | `#EEF3F8` |
| 面板 | `#101620` | `#FFFFFF` |
| 边框 | `#2C3A4E` | `#C7D4E2` |
| 主文字 | `#DBE4EE` | `#17232E` |
| 次文字 | `#A7B5C6` | `#4F6275` |
| 强调/连接 | `#2FC9B9` | `#0B8A7E` |
| 已启用/发送 | `#F0B45A` | `#E0A340` |

### 工作区（Phase 2）

- 三栏布局：左侧连接栏（端口/波特率/RTS/DTR/网络字段）、中间日志与发送区、右侧可折叠多条字符串抽屉
- 左栏可折叠为窄图标 rail：保留连接状态、端口缩写、打开/关闭和展开入口；`N1093` 记忆，窄窗自动折叠，手动操作后本次运行不再自动切换
- 接收日志改为富文本：时间戳灰、RX 青绿、TX 琥珀、错误红、HEX 暗青；TX 行带暖色底、错误行带红色底
- 日志搜索（Enter / Shift+Enter 前后查找）、暂停跟随、右键复制/复制纯数据/全选/清除
- 状态栏显示 RX/TX 实时速率、RX sparkline 和缓冲占用
- 发送区仍在底部，包含 HEX 显示/发送、加时间戳/回车、定时发送、帧首/帧尾/校验等全部原控件
- 标题栏保留原生 DWM 深/浅方案；菜单、工具栏、连接栏和抽屉使用现有主题 token

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

- 当前结果：`SELFTEST pass=198 fail=0`
- 覆盖组包/转义、7 种校验、接收显示与截断、多条发送状态机、ini 往返、终端按键、窗口几何、主窗体布局、设置浮层开关、搜索 toast、composer 收起/展开动效等
- 自检会临时创建主窗体并触发自动保存；开始前会备份现有 `sscom51.ini`，结束后原样恢复（没有则删除）
- 布局检查按 `DeviceDpi` 缩放测试窗口，100%–200% 缩放下都可复现

正常退出码为 0，失败时返回 1 并打印 `FAIL` 行。自检输出使用系统代码页（中文 Windows 为 GBK）；在 Git Bash 下若显示乱码，可用 `| iconv -f GBK -t UTF-8` 查看。

## 日志压测

```bash
cd SscomRe/bin/net48
./SscomRe.exe --bench-log 2 30       # 2 MiB，30 KiB/块，文本显示
./SscomRe.exe --bench-log 1 30 --hex # HEX 显示
./SscomRe.exe --bench-log 1 30 --shot bench.png  # 额外保存一张分色日志截图
```

压测走真实窗体的 `HandleIncoming → DrainDisplay → RTF 插入` 路径，统计每块的平均/最大界面耗时；默认目标为 30 KiB/块，对应 1 MiB/s 接收：

- 文本 2 MiB：avg 15.8ms、max 76.7ms（max 为截断整屏重画），吞吐约 1.8 MiB/s。
- HEX 1 MiB：avg 52ms、max 465ms。HEX 文本量约为原始数据 3 倍，1 MiB/s 下可能掉帧；原始接收缓冲、计数和保存数据不受影响。
- `--bench-log` 会备份/恢复 `sscom51.ini`；默认在屏幕外运行，屏幕上的绘制开销会另加约 10–15ms。

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
  Program.cs                  入口；--selftest / --bench-log；全局异常写 crash.log
  MainForm.cs                 主窗体、菜单、收发编排、ini 读写
  MainForm.Phase2.cs          三栏工作区、连接栏、日志头、发送区和设置浮层布局
  MainForm.Log.cs             RTF 分色追加、搜索/暂停/复制、速率与缓冲
  MainForm.Motion.cs          toast 与左栏/抽屉/composer 短动效
  LogBenchmark.cs             --bench-log 高吞吐压测和截图
  Toast.cs                    非模态短提示控件
  LogFormatter.cs             日志文本 → 着色片段（纯逻辑）
  Theme.cs                    主题 token、DWM、菜单/状态栏渲染
  ThemeRichTextBox.cs         接收日志富文本框
  Sparkline.cs                状态栏 RX 速率曲线
  ThemeButton.cs              自绘按钮与矢量图标
  ThemeCheckBox.cs            自绘复选框（琥珀勾选）
  ThemeComboBox.cs            自绘下拉框（覆盖原生浅色按钮/滚动条）
  ThemeTextBox.cs             深色输入框与占位文字
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
- 界面按物理像素布局、字体按 DPI 渲染；高 DPI（如 200%）下底栏占比明显变大，建议最大化窗口或降低显示缩放
- 标题栏使用原生 DWM 深/浅方案（保证拖动/吸附/最大化稳定）；完全自绘标题栏不在本期
- 接收/发送低频项已收进设置浮层；左栏/composer/抽屉有 140ms 短动效；搜索无结果、清除和复制有 toast。保存数据的成功/失败仍按规格使用模态弹窗
- 文本日志 1 MiB/s 压测平均 15.8ms/30KiB；HEX 显示约 52ms，1 MiB/s 下可能掉帧（原始缓冲与计数不受影响）
- 100%/150% DPI 尚未单独截图回归；已核对 125%/200% 窗口和 192 DPI 自检
- 本机没有 COM 口；真实串口/网口的收发热路径仍需在目标硬件上复验
- 高 DPI 窄屏（如 1440×900 @200%）下三栏会拥挤；左栏会自动折叠成 rail，日志搜索框和副标题也会自动收起；推荐 100%–150% 或宽窗口
- 打开/保存文件、字体、颜色和 MessageBox 使用系统原生对话框
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
