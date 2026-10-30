# 交接文档：sscomRe（串口/网络数据调试器 定制版）

> 行为规格的唯一依据是 [`sscom5.13.1-rewrite-spec.md`](sscom5.13.1-rewrite-spec.md)；本文件只做工程索引、架构约定和已知取舍。
> 当前版本 **v1.5.0**（2026-10-30），版本记录见 [CHANGELOG.md](CHANGELOG.md)。
> 当前状态（2026-10-29）：**M1–M9 全部实现；UI Phase 1 深/浅主题、Phase 2 三栏工作区、Phase 3A 左栏完全隐藏 + 日志头伸缩开关、Phase 3B 设置浮层、Phase 3C RTF 日志性能/即时面板/toast 已接入；连接栏方案 A（自绘端口字段/浮层、状态卡、RTS/DTR 胶囊、左栏拖宽）、连续换口重开、日志自动跟随、发送设置重排、125% DPI 对话框排版、多条字符串动态页数、定制版命名/图标、自动滚动始终跟随、日志头端口快捷下拉、多条面板方案 02、日志高亮规则（9 条预置，错误/警告默认开启，可预览）与 Ctrl+K 命令面板（工具栏按钮入口）、带颜色日志导出、持续 HTML 记录、GNSS 增强面板、暂停显示与按目录保存、VS2310 主日志观察面板已落地；`--selftest` 454/454 通过**。原程序、镜像和反编译源码未纳入版本库。

---

## 1. 项目是什么

把 `sscom5.13.1.exe`（Delphi/VCL 串口调试工具）按逆向整理出的行为规格，用 **C# WinForms / .NET Framework 4.8** 重写成 `sscomRe` 定制版。工作目录：`D:\Work\workspace\sscom-re`。

**硬约束（用户明确要求）：**

1. 只实现规格「重写时可以直接当规则用的部分」+ 正文标为「观察/推断」的对应行为。
2. **不做** STM/GD32 和 STC/IAP15 烧写；不做数据波形、English 切换、转换计算器、LUA、帮助页、广告/更新检查。
3. 不把对象偏移（如 `self+0x2C4`）和函数地址写进新程序**逻辑**；行为用自然语言/规则表达。代码注释里的原程序地址只用于溯源。
4. 规格没写数值的地方**不得猜**成原程序的规则；拿不准先问用户。
5. 界面样式可以自行设计；行为、文案、数值以规格为准。
6. 用户偏好：中文交互文案、直接不废话的汇报；大改动先给方案。

---

## 2. 仓库布局与文件地图

```
README.md                    项目说明、构建/运行/自检、已知限制
sscom5.13.1-rewrite-spec.md  行为规格（唯一依据）
sscom-spec-handoff.md        规格补全阶段的逆向任务书（历史记录）
HANDOFF.md                   本文件
REWRITE-PLAN.md              里程碑 M1–M9 状态
_analysis/                   逆向脚本与产物（见 §8，不含原程序）
SscomRe/                     工程（.NET Framework 4.8 WinForms）
  Assets/sscomRe.ico    程序/exe 多尺寸图标
  Program.cs            入口；--selftest；全局异常→crash.log
  MainForm.cs           主窗体（约 3000 行）：菜单、收发编排、ini
  MainForm.Phase2.cs    三栏工作区、连接栏（自绘端口字段/浮层）、日志头、composer 布局
  PortField.cs          连接栏端口字段（名称 + 设备描述、F2 手动输入）
  PortPopup.cs          端口选择浮层（串口描述分组 + 网络模式）
  ThemeChip.cs          标题行模式胶囊
  ThemeCheckChip.cs     RTS/DTR 胶囊复选框
  SideStatusCard.cs     连接状态卡
  ResizeSplitter.cs     左栏宽度拖动手柄
  MainForm.Log.cs       日志分色追加、搜索/暂停/复制、速率/缓冲
  MainForm.Capture.cs    持续 HTML 记录启动/停止、目录与分卷设置、状态
  LogFormatter.cs       日志文本 → 着色片段（纯逻辑）
  ThemeRichTextBox.cs   接收日志富文本框（原生滚动条跟随主题）
  Theme.cs              主题 token、DPI 尺寸、DWM、菜单/状态栏渲染、递归兜底
  ThemeButton.cs        自绘按钮与按文字映射的矢量图标
  ThemeCheckBox.cs      自绘复选框（琥珀勾选 + 暖色高亮）
  ThemeComboBox.cs      自绘下拉框（覆盖原生浅色按钮、隐藏列表滚动条）
  ThemeTextBox.cs       深色输入框、占位文字、暗色原生滚动条
  ComPortService.cs     串口层：COM 名归一化、波特率表、线路参数、DTR/RTS、ModemStatus
  NetworkService.cs     TCP 客户端/服务端/UDP + 就绪标志
  ReceiveDisplay.cs     接收显示管线（三累积、半汉字、时间戳、截断）
  SendComposer.cs       文本转义、HEX 拼字节、GBK、显示转义
  Checksums.cs          校验模式 1–7
  TerminalInput.cs      终端按键纯逻辑（缓冲、吞键、发送计数）
  MultiSendPlanner.cs   多条发送状态机（纯逻辑）
  IniDocument.cs        sscom51.ini 读写（1–3 页最多 297 行 + 设置键）
  PortSettingsDialog.cs 自建「更多串口设置」对话框
  WindowPlacement.cs    N1082–N1085 窗口几何夹取
  SaveDataDialog.cs     保存数据前选择目录
  InputNormalizer.cs    行内数字框归一化（光标规则）
  LogPortPicker.cs      日志头端口快捷下拉（复用端口浮层）
  ThemeSwitch.cs        开关式复选框（循环发送）
  LogHighlightRule.cs   日志高亮规则模型、匹配引擎、JSON/Base64 持久化
  HighlightRulesDialog.cs 高亮规则管理（整行着色、导入/导出）
  LogExport.cs          带颜色日志导出（RTF / HTML）
  HtmlLogRecorder.cs    持续 HTML 记录（分卷/追加/快照规则）
  NmeaParser.cs         NMEA 0183 拼句、定位/GSV 卫星快照（纯逻辑）
  GnssWindow.cs         GNSS 增强面板（坐标/精度/前6卫星）
  MainForm.Gnss.cs      GNSS 菜单、解析开关与小窗生命周期
  MainTraceParser.cs    VS2310 软换行重组、快照/事件/上报阶段、证据视图（纯逻辑）
  MainTraceWindow.cs    VS2310 主日志观察面板（自绘、筛选/滚动/暂停）
  MainForm.MainTrace.cs 主日志菜单、数据入口门控、窗口生命周期
  MainTraceHarness.cs   --mt-parse / --mt-ui 离线回放与截图
  CommandPalette.cs     Ctrl+K 已保存命令面板
  SelfTest.cs           --selftest 用例（当前 454 项）
```

---

## 3. 当前状态

| 里程碑 | 状态 |
|---|---|
| M1 串口骨架/打开/状态栏/界面框架 | ✅ 已实机验证 |
| M2 发送组包/校验/回显 | ✅ 自检全过 |
| M3 接收显示/文件功能 | ✅ 实机+自检 |
| M4 定时发送/发送文件/状态栏联动 | ✅ 已建 |
| M5 多条字符串 297 行（默认 1 页，可增至 3 页） | ✅ 已建 |
| M6 网络 TCP/UDP/模式联动/端口菜单 | ✅ 回环测试 |
| M7 终端仿真 + 更多串口设置 | ✅ 已建；按键顺序/退格/吞键/GBK 各有自检 |
| M8 sscom51.ini 保存/导入 | ✅ 已建；含往返与旧格式兼容自检 |
| M9 窗口几何/字体背景色/发布 | ✅ 已建；几何夹取/字体应用有自检 |
| UI Phase 1 深/浅主题（换皮，不重排） | ✅ 已建；DWM 深/浅标题栏、自绘控件、暗色菜单/下拉/滚动条、自绘多条分页、N1092 记忆 |
| UI Phase 2 三栏工作区 + 富文本日志 | ✅ 已建；左连接栏、中日志/composer、右多条抽屉、RX/TX/错误分色、搜索/暂停/复制、速率/缓冲 |
| UI Phase 3A 左栏折叠 | ✅ 已建；收起完全隐藏、日志头 `«/»` 开关、手动/自动折叠、N1093 持久化 |
| UI Phase 3B 设置浮层 | ✅ 已建；接收/发送低频项移入浮层，点外部/Esc 关闭，composer 只留常用项 |
| UI Phase 3C 性能/toast | ✅ 已建；RTF 批量日志、面板即时显隐、搜索/清除/复制 toast、`--bench-log` |

**验证口径：**

- `dotnet build -c Release` → 0 警告 0 错误
- `sscomRe_v1.5.0.exe --selftest` → `pass=454 fail=0`
- 启动冒烟 ≥5 秒无 `crash.log`
- 本次环境有 COM38/COM39，只做了接收、换口和侧栏伸缩冒烟；DTR/RTS、发送文件、定时发送与真实网口仍需在目标硬件复验

---

## 4. 构建 / 自检 / 冒烟

```bash
cd SscomRe
dotnet build -c Release
# 输出固定为 SscomRe/bin/net48/sscomRe_v1.5.0.exe（csproj 固定 OutputPath + AssemblyName）

cd bin/net48
./sscomRe_v1.5.0.exe --selftest          # 必须 pass=454 fail=0
./sscomRe_v1.5.0.exe --bench-log 2 30    # 可选：日志吞吐压测，见 §5.5

# 冒烟：跑 ≥5 秒，检查没崩
rm -f crash.log
(./sscomRe_v1.5.0.exe &) && sleep 5
ls crash.log && echo "崩了，看日志" || echo "正常"
taskkill //IM sscomRe_v1.5.0.exe //F
```

**约定：**

- `sscom51.ini` 按**当前工作目录**读写；自检和冒烟务必在 `bin/net48` 下启动。自检会自动备份/恢复现有 ini，避免测试字体、模式、窗口几何污染用户配置。
- 不要杀用户正在用的窗口；编译报 `MSB3027/文件被锁定` 说明 `bin/net48/sscomRe_v1.5.0.exe` 正在运行，先关闭再编译，不要用 `-o` 另开目录。
- 任何未处理异常都会写 exe 同目录的 `crash.log`，先看它。
- 新增逻辑尽量抽成**纯类**（如 `Checksums`、`TerminalInput`、`MultiSendPlanner`）并在 `--selftest` 加用例；网络类可写本机回环测试。
- 每次改动跑：build → selftest → 5 秒冒烟，三步都过再汇报。

---

## 5. 架构与必须遵守的不变量

### 5.1 线程模型

- 串口/网络接收事件在**后台线程**。接收线程**绝不能读控件属性**（跨线程读 `TextBox.Text` 会发 `WM_GETTEXT` 并等界面线程，把接收线程堵死——这是修过的真实 bug）。
- 接收参数走**快照字段**：`_snapHexDisplay/_snapTimestamp/_snapRecvToFile/_snapTimeoutMs`，只在界面线程由 `RefreshRecvSnapshot()` 刷新（订阅四个控件的 Changed 事件）。**新增影响接收的控件时必须加进 `RefreshRecvSnapshot`。**
- 界面更新走**合并队列**：接收线程 `EnqueueDisplay(segment, memoReplace)`，界面线程 `DrainDisplay()` 每 30ms 合并一次；截断重画优先。不要改回“每段一个 BeginInvoke”。
- 截断十六进制累积时只从 `StringBuilder` 拷出要留下的 30 万字符，不要先 `ToString` 整段。接收日志框创建后把撤销上限设为 0。

### 5.2 发送入口

- 所有发送（单条/行/循环/定时/终端）最终都落到 `WriteChannel`；普通发送先走 `SendPayload(text, hex)`：组包 → 校验 → 插入 → 门槛判定 → 写出 → 计数 → 回显。
- 通道判定：`ChannelCanSend()`（串口=已打开；网络=就绪）；写出：`WriteChannel()`（按 `_netMode` 分派）。**新发送路径必须复用这两个方法。**
- 终端仿真是**原样字节**（GBK 缓冲 + 可选 `0x0A`），**不走** `SendPayload` 的转义/校验/回显；用 `WriteChannel` + `ChannelCanSend` + 计数。
- 接收显示管线：`HandleIncoming(byte[])`（串口与三种网络共用）→ `ReceiveDisplay.Process(...)`。

### 5.3 ini 与生命周期

- `IniDocument` 只负责文本 ↔ `IniSnapshot`；`MainForm.CaptureSnapshot/ApplySnapshot` 负责控件 ↔ 快照。
- `N<k>`/`N<100+k>` 第 1 页；第 2 页 `N201–N299`/`N301–N399`；第 3 页 `N401–N499`/`N501–N599`。旧 ini 只有第 1 页时，后两页保持默认。
- `N1094` 记录可见页数（1–3，缺省 1）；第 1 页始终存在，第 2/3 页可通过页签 `＋` 增加、`×` 删除。删除中间页时其后页内容依次前移，末页清空；删除前二次确认，默认按钮为“否”。
- `N1095` 记录「自动滚动」开关（缺省 1）。勾选时 `AppendRecv/RenderLogFromText` 始终滚到最新（不再判断是否在底部），`SizeChanged/DpiChanged/Activated/Move` 也会 `ScheduleScrollRecvToLatest` 回底；关闭后才保留用户当前滚动位置。日志头的「暂停/继续」与这个开关是同一状态。
- `N1096` 保存日志高亮规则：新格式为带版本号的 UTF-8 JSON（`{"Version":2,"Rules":[...]}`）再 Base64 写入 ini，仍兼容旧的裸数组。首次启动生成 9 条预置规则，仅「错误/警告」默认开启；旧版只有 ERROR/WARN 的配置在加载时自动补齐其余预置（默认关闭），并按名称识别旧规则，不重复添加。规则的 JSON 也可从「显示 → 日志高亮规则...」导入/导出。
- `N1097` 持续 HTML 记录目录（空=程序当前目录）；`N1098` 分卷 MB（0=不分卷，默认 50）；`N1099` 浅色文档调色板（1=浅色，0=跟随主题）；`N1101` 是否包含发送回显；`N1102` 设置版本（旧配置 N1098=10 会迁移成 50，之后用户自己选 10 会保留）。勾选状态只在本次运行有效，启动时默认关闭，避免自动写文件。
- 数据字段转义：`,`→`0x02`、CR→`0x03`、LF→`0x04`；设置行格式 `N<键>=,<值>`。
- 关窗：停定时器 → `SaveConfig()` → 断网络/关串口。保存几何时最大化用 `RestoreBounds`，不保存屏幕外边界。
- 多条数据/顺序/延时框按键走约 400ms 防抖再写 ini；勾选、注释和「保存参数」仍马上写。关窗会停掉防抖定时器再按当前控件内容写一次。
- 导入 ini：选文件 → 覆盖写到当前目录 `sscom51.ini` → `LoadConfig()` 全量重载；读写失败文案沿用规格。
- 已打开串口的线路参数（数据位/停止位/校验/流控/缓冲/超时）不会即时重配，**下次打开生效**（WinForms SerialPort 限制，对话框里有提示）。
- 已打开串口时从自绘端口浮层、「通讯端口」菜单或 F2 手动输入换到另一个 COM 口：`ShouldReopenSerial` 判断后关旧口并 `OpenPort()`。所有用户选择都经 `SchedulePortReopen` **BeginInvoke 延后**到 ComboBox 文本更新之后再读当前选择；早期版本在 SelectionChangeCommitted 里同步读 Text，会读到上一个端口，出现“选 COM36 还是 COM38 数据、再选 COM37 才变 COM36”的慢一拍。程序化刷新/导入 ini 不触发。`_portOpenIntent` 在用户打开端口时置真，失败后下一次选择仍会尝试；用户显式关串口或切网络时清零。换口期间旧端口的 `DataReceived` 异常不再弹框（`Close` 先清 `_port`，只对当前端口报错）。

### 5.4 网络

- 就绪标志：TCP 客户端连上、服务端接到连接、UDP 绑定成功 → ready；断开/出错/切模式 → 清零。网络模式发送前必须 `Ready`。
- 服务端按规格**只向第 0 个连接发送**；额外连接最多保留 8 个，只读走数据，避免句柄泄漏。
- TCP 客户端连接超时 5 秒（实现补充）；UDP 收到数据会把发送方 IP 回填「远程IP」。
- 网络模式关串口；模式切换停两个发送定时器并取消勾选。

### 5.5 布局 / DPI / 性能（Phase 2 三栏，别改回旧底栏）

程序是 PerMonitorV2，但窗体没有 `AutoScaleDimensions`，布局值是物理像素，而字体按当前 DPI 渲染。所有随窗口伸缩的行都必须按实际宽度/内容计算，不要写死总宽度。

- 根布局 4 行：工具栏 / 三栏主体（`_bodyLayout` = 左 `_sidePanel` + 右 `_split`）/ 进度条 / 状态栏。旧底部 2×4 网格、`_bottomGrid`、`_serialRows`/`_netRows` 已删除，不要再加回来。
- 左栏：竖排连接栏，`SidebarWidth() = max(240, Theme.Scale(this,280))`；串口和网络字段分别是 `_serialSection`/`_netSection`，由 `UpdateModePanels` 显隐。端口选择由自绘 `PortField` + `PortPopup` 显示，`_portCombo` 只作为隐藏数据模型（Items/Text/SelectedIndex），不参与绘制；收起时列宽归 0 且 `_sidePanel.Visible=false`，不保留 rail。端口字段显示名称 + 弱化描述，浮层串口分组带描述、网络模式在底部；F2 手动输入。刷新按钮在「连接 串口」标题行右侧，端口/波特率字段等宽，RTS/DTR 为等宽胶囊，状态卡独立显示已打开/线路参数。
- 中心：`_split.Panel1` 内是日志头 + 富文本日志 + composer；`_split.Panel2` 是多条抽屉。默认抽屉宽约 340 逻辑像素，用户拖动后记录宽；N1086 旧值 480–580 仍按旧固定宽处理。
- 工具栏：打开文件 + 路径框（`WrapRowPanel.StretchControl` 填满中段）+ 右 发送文件/停止/清发送区/最前/保存参数/扩展；清除窗口、暂停显示和保存数据在发送区设置行（与自动滚动同一行）。发送区收起按钮在 composer 设置行最右侧。
- composer：`_settingsRow1`（HEX显示/HEX发送/定时发送/加回车换行 + 右侧「接收设置/发送设置/━」）+ `_sendArea`；低频项在 `_recvSettingsFlyout`/`_sendSettingsFlyout` 中，composer 只写两行绝对高度。`ComposerHeight` 供中心行高和 `UpdateMinimumHeight` 用。`━` 只隐藏 `_sendArea`，保留设置行，按钮始终可见；标题在 `━/■` 间切换。发送区**不做**拖动改高。
- 设置浮层：入口在 composer 右侧；接收设置含时间戳/超时/接收文件，发送设置含帧尾位置/校验/CRC。浮层是窗体子控件（不是 Form），参与主题重绘；`ShowFlyout` 按锚点向上定位、`SizeFlyout` 每次按当前 DPI 更新行宽和高度；`FlyoutDismissFilter` 处理点外部，`ProcessCmdKey` 处理 Esc，`WM_ACTIVATEAPP` 处理切到其它应用。浮层内下拉框展开时不要关闭浮层。
- 动效（`MainForm.Motion.cs`）：目前没有面板位移动画；侧栏、发送区、右抽屉都即时显隐，避免每帧重排富文本日志。画布变化时冻结日志重绘并只做一次布局；验证发送区收起/展开两条路径。
- toast（`Toast.cs`）：非模态、2.2 秒自动消失，显示在日志底部居中；搜索无结果、清除接收区、复制纯数据使用；保存数据的成功/失败弹窗按规格保留模态，不要改成 toast。
- 日志性能：`AppendFormatted` 一次构造 RTF 后 `SelectedRtf` 单次插入，批量追加时 `WM_SETREDRAW` 关闭重绘；`DrainDisplay` 不再每 30ms 刷整条状态栏。`--bench-log [MiB] [chunkKiB] [--hex] [--shot path]` 用真实窗体路径压测。2 MiB 文本 avg 15.8ms/30KiB、max 76.7ms（截断重画）；1 MiB HEX avg 52ms、max 465ms，HEX 高吞吐仍可能掉帧，但原始缓冲/计数不受影响。
- 日志头：`LogBarHeight = max(44, Theme.Scale(this,44))`；标题/副标题固定高度 + `RowFlowPanel` 垂直居中；搜索/暂停/清除/保存放在右侧 `RowFlowPanel`，搜索框宽度随 DPI 缩放。日志头的 TableLayoutPanel 必须有显式 Percent 行，否则默认 100px 会压住日志。
- `WrapRowPanel`：宽度固定为父宽、按 `GetPreferredSize(width)` 自动增高、按视觉行垂直居中；`MarkRightGroup`/`AlignRightGroup` 用于右侧组；`StretchControl` 用于路径框填充。设置入口在右侧组；`RowFlowPanel` 的 `OnLayout` 按“视觉行”分别垂直居中，浮层内换行时不能把所有子控件拉回同一行。
- 窗口最小尺寸基准 820×560，按 `Theme.Scale` 随 DPI 放大，并且不超过当前屏幕工作区。`UpdateMinimumHeight()` 用 `ComposerHeight`（发送区收起时只算设置行）保证日志/发送区/状态栏不被挤出；`ScheduleResponsiveRelayout()` 在缩放后延迟终排并更新侧栏宽度/composer 行高/日志头响应式。
- 多条字符串最多 297 行不用 TableLayoutPanel：`MultiRowsPanel` 自绘布局（固定行高/列宽，一次定位）；默认只显示第 1 页，第 2/3 页按需显示。首次展开前 `_prewarmTimer` 在 OnShown 后分批创建句柄（每 20ms 8 个）。
- 高 DPI：200% 窄屏三栏会拥挤；`UpdateLogHeaderResponsive` 在中心宽度 <620 隐藏搜索框、<420 隐藏副标题。改动布局时在 100% 和 200% 下都要看；当前已在 192 DPI 自检、125%/200% 窗口截图核对，100%/150% 尚未单独回归。

### 5.6 深/浅主题 / 自绘控件（Phase 1）

- 调色板：`Theme.Current` 指向 `DarkPalette` 或 `LightPalette`；`Theme.Canvas` 等属性都转发到当前 palette，自绘控件绘制时读实时值，所以切换只需重绘。
- 启动：`MainForm` 构造最开始用 `IniDocument.ReadThemePreference` 读 `N1092`，`Theme.SetLight` 后再 `BuildUi`，避免先深色再整树换色。
- 运行时：菜单「显示 → 浅色主题」→ `ApplyTheme(bool)` → `Theme.ApplyTheme(form, light)`：切换 palette、DWM（`on=0/1`）、颜色映射、菜单/状态栏渲染器、原生控件主题，最后 `StatusTick` 刷新计数底色。菜单点击会 `AutoSave`，启动/导入不保存。
- `N1092` 是重写版新增键（`0` 深色、`1` 浅色），原版不认识但会忽略；`IniSnapshot.ThemeLight` 默认 false。
- 颜色映射：`Theme.Recolor` 把显式写成旧 palette 颜色的面板/标签换新；`StyleRecursive` 刷新自绘控件和原生控件。新增容器若用了 palette 外的手写颜色，切换时不会自动更新，别这么写。
- 原生控件主题：`TryDarkNative` 在深色用 `DarkMode_Explorer`，浅色用 `null` 恢复系统浅色滚动条；`RefreshNative` 在切换时遍历已创建句柄。
- 状态栏分档底色用 `Theme.StatusBackground`，发送计数颜色用 `Theme.StatusSend`，校验区底色用 `Theme.VerifyField`；不要再写死 RGB。
- 切换主题会重置接收区背景/文字颜色为主题默认值（字号/字形保留）；`ApplySnapshot` 导入 ini 时先切主题、再套 ini 里的颜色，所以导入的 ini 颜色仍生效。
- 入口：`Theme.ApplyWindow(this)` + `Theme.StyleRecursive(this)` 在 `MainForm` 构造末尾调用；前者设窗口底色、DWM 深/浅标题栏/圆角、窗口图标，后者给原生控件兜底。
- **`ToolStrip.RenderMode` 必须在 `Renderer` 之前设置**：先设 Renderer 再设 RenderMode 会被系统默认渲染器覆盖（菜单勾选/白边问题就是这么来的）。菜单用 `ThemeMenuRenderer`，状态栏用 `ThemeStatusRenderer`。
- 菜单勾选：原生勾选列在深色下是白底，已设 `ShowImageMargin=false`/`ShowCheckMargin=false`，用 `✓  ` 文本前缀表示勾选（`Theme.ApplyMenuCheckText`，监听 `CheckedChanged`）。菜单项 Text 会变，逻辑判断请用 `Tag`/`Checked`，不要用 Text 比对。
- 下拉框：原生下拉按钮是浅色方块，`ThemeComboBox` 在 `WM_PAINT` 之后用 `GetComboBoxInfo` 的 `rcButton` 覆盖箭头和边框；`OnDropDown` 时隐藏列表原生滚动条并按项数设置高度。不要改回标准 ComboBox。
- 按钮：`ThemeButton` 按 Text 映射矢量图标（清除/打开/发送文件/停止/清发送区/保存/扩展/更多设置/刷新/±/━/■）；高度用 `Font.Height + 12`，留出 FlowLayoutPanel 的 Margin，避免 200% DPI 被行裁切。
- 复选框：`ThemeCheckBox` 勾选用琥珀色，`Highlighted` 控制暖色标签底；`StatusTick` 的 `Highlight` 只改这个属性，不要再设 `BackColor=Yellow`。
- 文本框：`ThemeTextBox` 提供 `PlaceholderText`（路径框“未选择文件”），并在句柄创建时尝试 `SetWindowTheme(..., "DarkMode_Explorer")` 让原生滚动条变暗。
- 多条字符串不再用 `TabControl`（原生页框在深色下始终有浅色边），改为 `pageHost` + `pageBar` 自绘页签；`_multiPageCount`/`_multiPage`/`_multiPages`/`_multiTabs`/`_multiCloseTabs` 管理页数、选中页与显隐，`_multiAddTab` 在 3 页时禁用。删除页只清控件数据，不销毁 99 行控件；自检用内部页数/数据钩子验证增删与移位。
- 多条面板方案 02（v1.2.0）：根为 5 行 `TableLayoutPanel`（标题/工具条/页签/表格卡/页脚），表格卡是自绘 `CardPanel`，页签用 `ThemeButton.Segment(SegmentSelected)`，循环发送用 `ThemeSwitch`，行按钮用 `ThemeButton.Soft + AccentText`。`MultiRowsPanel` 自绘斑马纹/悬停行/表头强调线/圆角字段底和聚焦描边，单元格文本框统一 `BorderStyle=None` 并用 `Rectangle.Inflate(bounds,2,2)` 画出圆角边框；改行高/列宽时要同步这段绘制。
- 日志头端口下拉 `LogPortPicker` 与连接栏 `PortField` 共用 `PortPopup`：`TogglePortPopup(anchor)` 记录 `_portPopupAnchor`，关闭时同时复位两个控件的 `Opened`；`PortPopupDismissFilter` 必须放行 `IsPortPopupAnchor` 里的两个锚点，否则点标题会先关再开。
- 日志高亮在 `LogHighlighter.Style` 内按字符级 owner 实现“列表靠前优先”；规则背景 A<255 时先与日志底色混合再写 RTF。有启用规则时 `AppendFormatted` 会先用 `HasAnyMatch` 做整批预筛，纯数据无命中仍走 `AppendText` 快路径；新增依赖日志文本的操作要先考虑分块重画是否完成。
- 高亮规则预览：选中规则即使关闭也按开启效果预览；候选行依次取预置示例/关键字示例、最近 40 行日志命中、通用示例，最多显示 3 行。`HighlightRulesDialog.PreviewStyledLines` 是纯逻辑并有自检，改动预览时不要退回固定 ERROR 示例。
- 日志导出：发送区设置行的「保存数据 ▾」是 `_saveMenu`（ContextMenuStrip，主题切换时在 `ApplyTheme` 里重新 `StyleContextMenu`）；“保存数据”走 `SaveData()`（先选目录，TXT+DAT），另两项走 `SaveColoredLog(rtf)` → `LogExport.ToRtf/ToHtml`，按当前显示文本与当前规则重新着色。导出前必须 `CompletePendingRenderLog()`；RTF 写 ASCII（非 ASCII 走 `\uNNNN?`），HTML 写带 BOM 的 UTF-8。
- 持续 HTML 记录在 `HtmlLogRecorder`：`AppendRecv` 负责接收与显示回显（普通 `Append` 永远跳过 TX 行），发送数据在 `WriteChannel` 成功后调用 `AppendTx` 单独写入，因此不依赖“加时间戳”是否开启显示回显。每行只给与默认色不同的片段写 `<span>`；按字节阈值在行边界分卷，每卷写完整头/尾，异常退出时浏览器也能自动闭合。规则/调色板/HEX 模式在开始记录时快照，修改后需取消并重新勾选。记录状态与失败检测在 `StatusTick` 的 `UpdateHtmlCaptureStatus` 中，误删这条会看不到写入错误。
- 暂停显示：`_displayPaused` 只停住 `_recvBox` 的增量刷新，接收/解析继续，新文本进入 `_displayHold`（截断时 `_displayHoldBase` 保存整屏）；恢复时补画，`LogicalWindowText()` 让保存数据包含暂停期间的内容。暂停显示/清除窗口/保存数据与自动滚动在同一行；不要改回“暂停=停止滚动”，也不要在暂停时停止接收。
- GNSS：`NmeaParser` 支持 GGA/RMC/GLL/GNS/VTG/GSA/GSV/ZDA；GSV 以（星座, PRN）合并，6 秒无更新过期，`SatellitesInView` 按 SNR 降序；GSA 提供 PDOP/HDOP/VDOP，GGA 提供水准面差距/差分龄期/基准站。`GnssWindow` 客户区 420×620、自定义绘制；卫星只显示前 6 颗，信号条颜色按 SNR 强度（≥40 / 25–39 / <25），星座徽标为 G/C/E/R；坐标卡同时显示十进制度与 DMS。**参与数来自 GGA/GNS，可见数只统计 GSV**：设备未输出全部星座 GSV（常见 QZSS/北斗）时参与数会大于 GSV 可见数，面板显示“GSV 未覆盖全部”，不要用可见数去 clamp 参与数。GPGSV 里 PRN 193–202 / 120–158 会按 QZSS / SBAS 归类。
- VS2310 主日志：`MainTraceParser` 先按逻辑消息前缀切分，再**直接拼接**片段；不要按 CRLF 分行或给片段补空格——设备约每 200 字节插入的软换行会断在字段名/JSON key 中间。接收时间只到串口块级别，面板用“同批”表达，不伪造毫秒。上报按“编码 → 提交 → API → 业务成功日志 → 云端”分开，未观测不推断失败；本机发送走 `NoteTx`，不能计入设备下发。`HandleIncoming` 只在面板首次打开后 feed（`_mainTraceEnabled`），连接变化 `Reset()`；面板 600×780，125% 客户区 750×975。真实 COM38 回放：333/333 快照、588 条逻辑消息、124 条事件、8/8 业务成功、SOC_ERR=2 ×12、本机 TX 2。
- 按目录保存：`SaveData()` 先 `SaveDataDialog.Choose`（默认上次目录，之后才是 CWD），再写 `SaveWindows<时间戳>.TXT`（GBK，含暂停 hold 内容）和 `SAVE<时间戳>.DAT`（原始字节）；目录不可写用 MessageBox，成功提示保留模态。目录只在本次运行记忆，未写 ini。
- Ctrl+K 命令面板只读已保存命令：可见页读控件，未建页读 `_multiRowSource`；`CommandPaletteDismissFilter` 负责点击外部关闭，`Ctrl+K`/Esc 在 `ProcessCmdKey` 中切换。顶部工具栏「命令面板」按钮是唯一鼠标入口（`BuildToolBar`），面板不包含清空/删除类操作。
- composer 设置行高度按 `RowHeightOf` 额外 +4px（`UpdateComposerGridRows`），给自绘下拉框/按钮留 Margin。
- 禁止在深色控件上再用 `SystemColors.Control/Window/WindowText` 或 `Color.White/Yellow`；所有颜色走 `Theme` token。

### 5.7 Phase 2 工作区 / 富文本日志

- 根布局：工具栏 → 三栏主体（`_bodyLayout`：左 `_sidePanel` + 右 `_split`）→ 进度条 → 状态栏。`_split.Panel1` 是中心工作区（日志头 + 富文本日志 + composer），Panel2 是多条抽屉。
- 左栏：`BuildSidebar`/`BuildSerialSection`/`BuildNetSection`；串口/网络字段用 `_serialSection`/`_netSection` 显隐；`UpdateModePanels` 不再碰旧的三行数组。端口由 `PortField` 显示、`PortPopup` 选择，隐藏的 `_portCombo` 只做数据模型（`RefreshPortList`/`ApplyLoadedPort` 仍操作它的 Items/Text/SelectedIndex）。刷新按钮属于标题行；`PortField` 与 `_baudCombo` 整行等宽。`RefreshPortList` 用 `ComPortService.PortDescriptions()` 生成显示项，菜单也用同一文本；`ApplyLoadedPort` 必须**同时显式写 Text 和 SelectedIndex**，因为隐藏 ComboBox 没有句柄时 SelectedIndex 不会更新 Text。
- 左栏折叠：`SetSidebarCollapsed(collapsed,persist,manual,automatic)` → `ApplySidebarState()`，收起时 `_bodyLayout` 列宽 0、侧栏和内容 `Visible=false`，展开时恢复 `SidebarWidth()`；不再有 rail。日志头最左侧的 `_sidebarToggle` 是唯一开关（展开 `«`、收起 `»`），始终留在绿色标题行里。手动操作持久化 N1093（`ScheduleAutoSave` 防抖，不阻塞点击），`_sideUserCollapsed` 保存用户偏好（自动折叠不写）。`ScheduleResponsiveRelayout` 在宽 < `SidebarWidth()+720` 时自动折叠，变宽自动展开；`_sideManualOverride` 一旦手动操作，本次运行不再自动切换。
- N1093 与 N1092 一样是重写版新增 ini 键（0 展开、1 折叠），原版忽略。
- composer：`BuildComposer` 使用 `_settingsRow1` + `_sendArea` 两个控件；低频设置项在接收/发送浮层中（见 §5.5），`UpdateComposerGridRows` 写行高；`━` 按钮在设置行最右侧，只折叠 `_sendArea`，设置行保留。左栏和中心之间用 `ResizeSplitter` 拖动改宽（本次运行内有效，未写 ini）。
- 接收框：`ThemeRichTextBox`（继承 RichTextBox），字段仍是 `_recvBox`；线程模型不变，所有富文本操作只在界面线程的 `DrainDisplay` 路径执行。
- 分色：`LogFormatter.Parse` 把文本拆成 Timestamp/Rx/Tx/Data/Hex；`AppendFormatted` 用 `SelectionColor/SelectionBackColor` 逐段追加。TX 行暖底来自 `Theme.TxWash`；错误/警告等关键字颜色**只由高亮规则产生**，日志层没有内置错误着色，关闭规则即恢复普通文字。
- 重画：主题/字体/颜色变化用 `RenderLogAll()`，它基于**当前可见文本**重解析，因此发送回显和终端本地输入也会保留；切换 HEX/文本显示用 `RenderLogBuffer()`，基于 `_receive.HexText/TextAccum`。不要用 `_receive` 缓冲去重画主题，否则会丢发送回显。
- 滚动：`_autoFollow` 为真且原来在底部时跟随；暂停/向上翻看时保持原选择/滚动位置。`RecvAtBottom` 用最后一个字符的 `GetPositionFromCharIndex().Y` 是否在客户区内判断，**不要用 `GetScrollInfo` 的 nMax/nPage**（不同 RichEdit 版本会差一行，导致日志永远不跟随）。`EM_GETSCROLLPOS`/`EM_SETSCROLLPOS` 兼容 RichTextBox。
- 搜索：`_searchBox` + `SearchNext(forward)` + `_recvBox.Find`；当前匹配用 `Theme.SearchHighlight` 作为 SelectionBackColor，下一次搜索/重画前清理。
- 复制：富文本框右键菜单有“复制/复制纯数据（去掉时间戳和收/发标记）/全选/清除窗口”；`LogFormatter.StripMeta` 是纯逻辑，已有自检。
- 速率：`_rateTimer` 每 500ms 用 S/R 计数差算 B/s，状态栏显示行内 RX/TX 文本速率和缓冲占用（用户已要求移除 RX 曲线）。`OnMainFormClosing` 必须停表。
- 窄窗：`UpdateLogHeaderResponsive` 在中心宽度 <620 时隐藏搜索框，<420 时隐藏日志副标题；右抽屉仍由用户手动展开。
- 删除旧底栏后不要再把 `_settingsRow` 放回 `_rootLayout`；composer 只属于中心工作区。

---

## 6. 已踩过的坑（别改回去）

| 坑 | 结论 |
|---|---|
| 控件字段忘了赋值（`_statusStrip`）→ 定时器首拍空引用崩溃 | 新增字段统一在 Build 方法里赋值；冒烟必须跑 ≥3 秒 |
| 行内数字框每键无条件写回 → 光标重置、多位数输入被打乱 | 用 `InputNormalizer.NormalizeInt`（仅变化时写回、恢复光标、空串不打扰） |
| 循环发送混合跳过（部分顺序 0 + 部分空数据）→ 原程序死循环 | `MultiSendPlanner` 每拍最多扫两遍（有注释与测试） |
| 显示“分多次打印/变慢” | 快照字段 + 合并刷新（§5.1） |
| 多条面板顺序框默认空 | 原程序默认 `"0"`；`0 不发送`，全 0 勾选后约 1 秒自动取消勾选 |
| 校验下拉序号 = 模式（0=None…7=ADD16）；帧尾下拉序号 = 保留字节数（0..4） | 不要另外做映射表 |
| 模式 2（CCITT）初值 0 → 向量 0x31C3（不是 CRC-16/CCITT-FALSE 的 0x29B1） | 已写在 `Checksums` 注释 |
| `SendPayload` 的“未打开串口先提示再自动打开”只适用于串口模式 | 网络模式不弹提示 |
| 保存数据 = 双文件（`SAVE*.DAT` 原始 + `SaveWindows*.TXT` 窗口内容），先选目录；接收落文件 = 追加 | 已按规格实现；落文件名串口用端口名、网络用 `TCPCLIENT/TCPSERVER/UDP` |
| 字体对话框传入已释放的 Font → “参数无效” | `LivingRecvFont` 修复；同字体重复设置不能释放控件正在用的对象 |
| 自检直接跑在主窗体上，Close 会把测试字体/模式/窗口几何写进用户的 ini | 自检现在备份/恢复 ini；新增 UI 用例不要在恢复逻辑之外写文件 |
| 布局测试写死 1200×720 → 200% DPI 下窗口过矮而误报 | 按 `DeviceDpi/96` 缩放测试窗口尺寸 |
| 先设 `ToolStrip.Renderer` 再设 `RenderMode` → 自定义渲染器被系统默认覆盖 | 必须 `RenderMode` 先、`Renderer` 后 |
| 深色菜单保留原生勾选列 → 左侧出现白色块，勾选框发蓝 | 关掉 Image/CheckMargin，用 `✓` 文本前缀表示勾选 |
| 标准 ComboBox 的原生下拉按钮是白块，列表滚动条是浅色 | `ThemeComboBox` 在 WM_PAINT 后覆盖按钮；OnDropDown 隐藏列表滚动条 |
| 原生 TabControl 的页框/标签条在深色下始终有浅色边 | 多条面板改用自绘分段按钮 + 页面 Panel，不再用 TabControl |
| 自绘按钮和原生下拉框比行高多 2–4px → 200% DPI 下被行裁切 | 按钮高度用 `Font.Height+12`；底栏设置行额外 +4px |
| `Theme.MonoFont` 返回共享 Font → `ApplyRecvFont` 释放旧字体时影响发送框 | 每次返回新 Font 实例，不共享 |
| 运行时只改 `Theme.Current` 不重绘 → 控件还是旧色 | 必须走 `ApplyTheme` → `Theme.ApplyTheme`（含颜色映射和原生控件主题刷新） |
| 接收框从 TextBox 换成 RichTextBox 后自检仍按 TextBox 找 → “找到接收框”失败 | 自检 UI 用例按 `RichTextBox` 找接收框；单行发送框仍是 TextBox |
| 主题/字体变化用 `_receive` 缓冲重画 → 发送回显和本地输入丢失 | 用 `RenderLogAll()`（当前可见文本）；只有切 HEX/文本显示用 `RenderLogBuffer()` |
| 日志头的 TableLayoutPanel 没有显式 RowStyle → 子控件按默认 100px 排，标题压到日志上 | 日志头必须加 `RowStyles.Add(Percent 100)`，标签固定高度交给 RowFlowPanel 居中 |
| 新状态栏速率条用独立 Timer，但 sleep 阻塞 UI 线程时不自增 | 真机正常运行；测试/截图时用 `Application.DoEvents` 泵消息，或直接调用 `RateTick` |
| 搜索高亮用 SelectionBackColor，重画/下一次搜索前必须清掉 | `ClearSearchHighlight` 在 `RenderLogFromText` 和下一次 `SearchNext` 开头调用 |
| 浅色主题沿用 `on=1` 的 DWM 属性 → 标题栏仍是深色 | `ApplyChrome` 按 `Current.IsDark` 写 0/1；切换时重设 |
| 浅色主题下原生滚动条仍是深色 | `TryDarkNative` 在浅色传 `null` 恢复系统主题，并由 `RefreshNative` 遍历已有句柄 |
| 状态栏发送色/校验区底色写死深色 RGB → 浅色下看不清 | 用 `Theme.StatusSend`、`Theme.StatusBackground`、`Theme.VerifyField` |
| 左栏收起/展开做 140ms 列宽动画 → 每帧重排富文本日志，实测延迟和卡顿；点击时同步 `AutoSave()` 还会打断动画 | 侧栏改为即时显隐；N1093 用 `ScheduleAutoSave` 防抖；收起后不保留 rail，开关放日志头 |
| 右抽屉 140ms 逐帧改分割线 → 每帧重排日志，左右栏都开时卡顿 | 改为即时、日志冻结 + 单次布局；首次展开的多条面板仍有一次性缓存成本 |
| ThemeCheckChip 透明圆角背景 + 取消勾选 → 圆角外残留上一帧琥珀色 | 控件用不透明 `Surface2` 先清整块再画胶囊；`Theme.StyleOne` 单独处理 ThemeCheckChip |
| 固定坐标对话框不做 DPI 缩放 → 125%/150% 下字体变大但行距不变，输入框被下一行遮住 | `PortSettingsDialog`/输入注释框按 owner 的 `Theme.Scale` 手动缩放并设 `AutoScaleMode.None`；125% 实测行距/按钮位置正常。新对话框不要只依赖 WinForms AutoScale |
| `RecvAtBottom` 用 `GetScrollInfo` 的 `nMax - nPage + 1` → RichEdit 差一行时误判不在底部，日志从此不再自动下滑 | 改为 `GetPositionFromCharIndex(TextLength - 1)` 判断最后字符可见性；自检覆盖“底部/上翻”两条路径 |
| 串口已打开时换端口：在 SelectionChangeCommitted 里同步读 `_portCombo.Text` → 读到上一个端口，出现慢一拍 | 统一走 `SchedulePortReopen`，`BeginInvoke` 到消息泵处理完选择后再读 Text；自绘字段/菜单/F2 也走同一路径；程序化刷新和 ini 导入不触发 |
| 隐藏的 `_portCombo` 没有句柄 → 只设 SelectedIndex 不会刷新 Text，端口字段丢描述 | 每次程序化选端口都显式写 Text 再设 SelectedIndex；`SyncPortField()` 在 RefreshPortList/ApplyLoadedPort/DetectMode 末尾兜底 |
| 自绘端口浮层比窗口高 → 会被窗口底部裁掉或遮住状态栏 | `PortPopup` 行高按字体压缩，定位时把底限定在状态栏之上；端口多到超高的场景仍需滚动方案 |
| 换口失败后 `_port.IsOpen=false` → 下一次选择不再自动打开 | 用 `_portOpenIntent` 记录用户“保持打开”的意图，失败后仍继续尝试；显式关串口/切网络清零 |
| 快速换口时旧端口的 `DataReceived` 读异常 → 弹模态框挡住后续选择 | `Close` 先清 `_port`，读异常只在 `ReferenceEquals(_port, sp)` 时上报，旧端口异常静默 |
| 端口下拉只显示 COMx，多设备难区分；描述文本若直接进 ini/比较会污染逻辑 | 显示 `COM38 · USB 串行设备`；`NormalizePortName` 用于打开/比较/保存，`SelectedSerialPortName()` 只给状态和 ini |

---

## 7. 规格疑点与实现取舍

- `N1058`（多条发送间隔）：原程序用隐藏中转框 `Edit35`，重写没有该控件，**保存固定写 `1000`，读入忽略**，避免覆盖主发送间隔。
- `N1080`：`1` 和 `2` 都映射 TCPClient（照规格原文的怪癖）；`3` 是 UDP；其他整串是端口名。
- `N1100`：固定写 `C`；English 切换不在范围内。
- 发送区的 `━` 现在只收起发送文本框，保留设置行和按钮；原版是设置区+发送区一起折叠。这是为了把按钮放进发送区后仍能恢复。
- 「更多串口设置」是自建对话框（原版是 CPort 组件自带对话框）；字段按规格可确认的项实现，流控 `Custom` 打开时按 `None` 处理。对话框按 owner 的 DPI 手动缩放，不再依赖 WinForms `AutoScaleMode`（125% 下实测 AutoScale 不缩放代码创建的固定坐标窗体）。
- 校验模式 7（ADD16）的字节序按规格标为推断：低字节在前。
- 打开文件预览用原字节直接按 Latin-1 式字符累积文本，再经 `ReceiveDisplay.SetPreviewBuffers` 替换预览缓冲；原始接收缓冲和接收计数不动。
- 网络服务端本地端口非法时选择“提示后不启动”（规格此处原程序响应未细读）。
- 标题栏继续用 DWM 深/浅原生方案（用户确认），完全自绘标题栏不在本期；浅色模式下系统文件/字体/颜色对话框观感正常，深色模式下这些原生对话框仍是浅色。
- 保存数据的目录选择、成功/失败提示按规格保留模态 MessageBox；toast 只用于搜索无结果、清除接收区、复制纯数据这类规格未规定弹窗的 UI 反馈。
- 1 MiB/s 的 HEX 显示比文本模式重约 3 倍；当前压测 avg 52ms/30KiB，可能掉帧，但原始接收缓冲、计数和保存数据不受影响。文本模式 avg 15.8ms，已满足 30ms 预算。
- 本机 COM38/COM39 只做了接收与换口冒烟；DTR/RTS、发送文件、定时发送与真实网口仍需在目标硬件复验。TCP/UDP 本机回环自检仍在 `--selftest` 中。
- 接收/发送低频项和 toast 已实现；发送区只收起文本框、左栏宽度可拖动（本次运行内不持久化）；右抽屉首次展开仍有一次性热缓存（本机约 0.8s），之后约 0.1s；剩余 100%/150% DPI 截图与真实硬件复验。
- 200% DPI + 窄屏（如 1440×900）下三栏会拥挤；日志头会自动收起搜索框/副标题，推荐 100%–150% 或宽度足够的窗口。
- 富文本日志按批构造 RTF、`SelectedRtf` 单次插入；不要改回逐段 `SelectionColor`/`AppendText`，长日志会退化成 O(n²)。HEX 1 MiB/s 仍是已知瓶颈，必要时再做按需降级绘制。

---

## 8. 溯源工具

- **规格优先**：先查 `sscom5.13.1-rewrite-spec.md`；疑点再回原程序。
- `_analysis/README.md` 有脚本用法、产物清单和镜像 SHA-256。
- 快捷命令（在仓库根，先把 `sscom5.13.1.unpacked.exe` 放回根目录）：
  - `python _analysis/tool.py d <地址> -n <条数>`
  - `python _analysis/strrefs.py <起始> <结束>`
  - `python _analysis/dfm.py`
- Ghidra headless 脚本在 `_analysis/ghidra/scripts/`；项目数据库（`.rep`/`.gpr`）是本地生成物，已忽略。
- 方法表起点 `0x5054b0`、字段表起点 `0x504a2a`（读控件/字段名时用）。
- 原程序、解包镜像、`_analysis/decomp/` 都**不要提交**（`.gitignore` 已排除）。

---

## 9. 后续可选项

详细计划见 [`REWRITE-PLAN.md`](REWRITE-PLAN.md) 的 Phase 3 段。按用户优先级从高到低：

1. ✅ Phase 3A：左侧栏完全隐藏、日志头 `«/»` 开关、N1093、窄窗自动折叠。
2. ✅ Phase 3B：接收/发送低频设置浮层，composer 只留常用项。
3. ✅ Phase 3C：RTF 日志性能、即时面板切换、搜索/清除/复制 toast；`--bench-log` 可复现。
4. 在真实串口/网口上复验三栏布局：接收分色、搜索/暂停、发送回显、发送文件、定时发送、TCP 服务端多连接、UDP；本机 COM38/COM39 只做过接收与换口冒烟。
5. 补 100%/150% DPI 截图回归；当前已核对 125%/200% 与 192 DPI 自检。
6. 完全自绘标题栏（用户当前选择保留原生 DWM，不要擅自改）。
7. 规格「还不能当规则用的部分」里的烧写器、波形等——**用户已排除，不要做**。
8. 若需要单文件分发，可在 csproj 增加 Costura/ILMerge 方案；当前分发 exe + config 即可，目标机只需 .NET Framework 4.8。

---

## 10. 汇报约定

- 每完成一个里程碑：更新 `REWRITE-PLAN.md`；向用户汇报「做了什么 / 怎么验证的 / 还差什么」。
- 遇到规格没写的数值或行为：**停下问用户**，不要猜；把问题写清楚（哪个控件、哪个场景、候选方案）。
- 用户会实机测试并反馈现象；先复现/定位再改，改完给可验证的检查步骤。
- 任何成功声明前先跑 build + selftest + 冒烟，并贴出结果。
