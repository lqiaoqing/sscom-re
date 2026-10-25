# 交接文档：SscomRe（SSCOM 5.13.1 的 C# 重写）

> 行为规格的唯一依据是 [`sscom5.13.1-rewrite-spec.md`](sscom5.13.1-rewrite-spec.md)；本文件只做工程索引、架构约定和已知取舍。
> 当前状态（2026-10-23）：**M1–M9 全部实现；UI Phase 1 深/浅主题、Phase 2 三栏工作区与富文本日志已接入，`--selftest` 193/193 通过**。原程序、镜像和反编译源码未纳入版本库。

---

## 1. 项目是什么

把 `sscom5.13.1.exe`（Delphi/VCL 串口调试工具）按逆向整理出的行为规格，用 **C# WinForms / .NET Framework 4.8** 重写成 `SscomRe`。工作目录：`D:\QWorkspace\Projects\Tools\sscom-re`。

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
  Program.cs            入口；--selftest；全局异常→crash.log
  MainForm.cs           主窗体（约 3000 行）：菜单、收发编排、ini
  MainForm.Phase2.cs    三栏工作区、连接栏、日志头、composer 布局
  MainForm.Log.cs       日志分色追加、搜索/暂停/复制、速率/缓冲
  LogFormatter.cs       日志文本 → 着色片段（纯逻辑）
  ThemeRichTextBox.cs   接收日志富文本框（原生滚动条跟随主题）
  Sparkline.cs          状态栏 RX 速率曲线
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
  IniDocument.cs        sscom51.ini 读写（三页 297 行 + 设置键）
  PortSettingsDialog.cs 自建「更多串口设置」对话框
  WindowPlacement.cs    N1082–N1085 窗口几何夹取
  InputNormalizer.cs    行内数字框归一化（光标规则）
  SelfTest.cs           --selftest 用例（当前 193 项）
```

---

## 3. 当前状态

| 里程碑 | 状态 |
|---|---|
| M1 串口骨架/打开/状态栏/界面框架 | ✅ 已实机验证 |
| M2 发送组包/校验/回显 | ✅ 自检全过 |
| M3 接收显示/文件功能 | ✅ 实机+自检 |
| M4 定时发送/发送文件/状态栏联动 | ✅ 已建 |
| M5 多条字符串 297 行（3 页） | ✅ 已建 |
| M6 网络 TCP/UDP/模式联动/端口菜单 | ✅ 回环测试 |
| M7 终端仿真 + 更多串口设置 | ✅ 已建；按键顺序/退格/吞键/GBK 各有自检 |
| M8 sscom51.ini 保存/导入 | ✅ 已建；含往返与旧格式兼容自检 |
| M9 窗口几何/字体背景色/发布 | ✅ 已建；几何夹取/字体应用有自检 |
| UI Phase 1 深/浅主题（换皮，不重排） | ✅ 已建；DWM 深/浅标题栏、自绘控件、暗色菜单/下拉/滚动条、自绘多条分页、N1092 记忆 |
| UI Phase 2 三栏工作区 + 富文本日志 | ✅ 已建；左连接栏、中日志/composer、右多条抽屉、RX/TX/错误分色、搜索/暂停/复制、速率/缓冲 |
| UI Phase 3A 左栏折叠 | ✅ 已建；窄图标 rail、手动/自动折叠、N1093 持久化 |

**验证口径：**

- `dotnet build -c Release` → 0 警告 0 错误
- `SscomRe.exe --selftest` → `pass=193 fail=0`
- 启动冒烟 ≥5 秒无 `crash.log`
- 本次环境无真实 COM 口，串口/网络的实机收发热路径仍需在目标硬件上复验

---

## 4. 构建 / 自检 / 冒烟

```bash
cd SscomRe
dotnet build -c Release
# 输出固定为 SscomRe/bin/net48/SscomRe.exe（csproj 固定 OutputPath）

cd bin/net48
./SscomRe.exe --selftest          # 必须 pass=193 fail=0

# 冒烟：跑 ≥5 秒，检查没崩
rm -f crash.log
(./SscomRe.exe &) && sleep 5
ls crash.log && echo "崩了，看日志" || echo "正常"
taskkill //IM SscomRe.exe //F
```

**约定：**

- `sscom51.ini` 按**当前工作目录**读写；自检和冒烟务必在 `bin/net48` 下启动。自检会自动备份/恢复现有 ini，避免测试字体、模式、窗口几何污染用户配置。
- 不要杀用户正在用的窗口；编译报 `MSB3027/文件被锁定` 说明 `bin/net48/SscomRe.exe` 正在运行，先关闭再编译，不要用 `-o` 另开目录。
- 任何未处理异常都会写 exe 同目录的 `crash.log`，先看它。
- 新增逻辑尽量抽成**纯类**（如 `Checksums`、`TerminalInput`、`MultiSendPlanner`）并在 `--selftest` 加用例；网络类可写本机回环测试。
- 每次改动跑：build → selftest → 5 秒冒烟，三步都过再汇报。

---

## 5. 架构与必须遵守的不变量

### 5.1 线程模型

- 串口/网络接收事件在**后台线程**。接收线程**绝不能读控件属性**（跨线程读 `TextBox.Text` 会发 `WM_GETTEXT` 并等界面线程，把接收线程堵死——这是修过的真实 bug）。
- 接收参数走**快照字段**：`_snapHexDisplay/_snapTimestamp/_snapRecvToFile/_snapTimeoutMs`，只在界面线程由 `RefreshRecvSnapshot()` 刷新（订阅四个控件的 Changed 事件）。**新增影响接收的控件时必须加进 `RefreshRecvSnapshot`。**
- 界面更新走**合并队列**：接收线程 `EnqueueDisplay(segment, memoReplace)`，界面线程 `DrainDisplay()` 每 30ms 合并一次；截断重画优先。不要改回“每段一个 BeginInvoke”。

### 5.2 发送入口

- 所有发送（单条/行/循环/定时/终端）最终都落到 `WriteChannel`；普通发送先走 `SendPayload(text, hex)`：组包 → 校验 → 插入 → 门槛判定 → 写出 → 计数 → 回显。
- 通道判定：`ChannelCanSend()`（串口=已打开；网络=就绪）；写出：`WriteChannel()`（按 `_netMode` 分派）。**新发送路径必须复用这两个方法。**
- 终端仿真是**原样字节**（GBK 缓冲 + 可选 `0x0A`），**不走** `SendPayload` 的转义/校验/回显；用 `WriteChannel` + `ChannelCanSend` + 计数。
- 接收显示管线：`HandleIncoming(byte[])`（串口与三种网络共用）→ `ReceiveDisplay.Process(...)`。

### 5.3 ini 与生命周期

- `IniDocument` 只负责文本 ↔ `IniSnapshot`；`MainForm.CaptureSnapshot/ApplySnapshot` 负责控件 ↔ 快照。
- `N<k>`/`N<100+k>` 第 1 页；第 2 页 `N201–N299`/`N301–N399`；第 3 页 `N401–N499`/`N501–N599`。旧 ini 只有第 1 页时，后两页保持默认。
- 数据字段转义：`,`→`0x02`、CR→`0x03`、LF→`0x04`；设置行格式 `N<键>=,<值>`。
- 关窗：停定时器 → `SaveConfig()` → 断网络/关串口。保存几何时最大化用 `RestoreBounds`，不保存屏幕外边界。
- 导入 ini：选文件 → 覆盖写到当前目录 `sscom51.ini` → `LoadConfig()` 全量重载；读写失败文案沿用规格。
- 已打开串口的线路参数（数据位/停止位/校验/流控/缓冲/超时）不会即时重配，**下次打开生效**（WinForms SerialPort 限制，对话框里有提示）。

### 5.4 网络

- 就绪标志：TCP 客户端连上、服务端接到连接、UDP 绑定成功 → ready；断开/出错/切模式 → 清零。网络模式发送前必须 `Ready`。
- 服务端按规格**只向第 0 个连接发送**；额外连接最多保留 8 个，只读走数据，避免句柄泄漏。
- TCP 客户端连接超时 5 秒（实现补充）；UDP 收到数据会把发送方 IP 回填「远程IP」。
- 网络模式关串口；模式切换停两个发送定时器并取消勾选。

### 5.5 布局 / DPI / 性能（Phase 2 三栏，别改回旧底栏）

程序是 PerMonitorV2，但窗体没有 `AutoScaleDimensions`，布局值是物理像素，而字体按当前 DPI 渲染。所有随窗口伸缩的行都必须按实际宽度/内容计算，不要写死总宽度。

- 根布局 4 行：工具栏 / 三栏主体（`_bodyLayout` = 左 `_sidePanel` + 右 `_split`）/ 进度条 / 状态栏。旧底部 2×4 网格、`_bottomGrid`、`_serialRows`/`_netRows` 已删除，不要再加回来。
- 左栏：竖排连接栏，`SidebarWidth() = max(240, Theme.Scale(this,280))`；串口和网络字段分别是 `_serialSection`/`_netSection`，由 `UpdateModePanels` 显隐；端口/协议统一由 `_portCombo` 选。
- 中心：`_split.Panel1` 内是日志头 + 富文本日志 + composer；`_split.Panel2` 是多条抽屉。默认抽屉宽约 340 逻辑像素，用户拖动后记录宽；N1086 旧值 480–580 仍按旧固定宽处理。
- 工具栏：打开文件 + 路径框（`WrapRowPanel.StretchControl` 填满中段）+ 右 发送文件/停止/清发送区/最前/保存参数/扩展/━；清除窗口和保存数据移到日志头。
- composer：`_settingsRow1`（HEX显示/接收落文件/HEX发送/定时/加回车）+ `_settingsRow2`（时间戳/超时/帧首/帧尾/校验/CRC）+ `_sendArea`；`UpdateComposerGridRows` 写三行绝对高度，`ComposerHeight` 供中心行高和 `UpdateMinimumHeight` 用。`━` 通过隐藏 `_composerPanel` 折叠 composer。
- 日志头：`LogBarHeight = max(44, Theme.Scale(this,44))`；标题/副标题固定高度 + `RowFlowPanel` 垂直居中；搜索/暂停/清除/保存放在右侧 `RowFlowPanel`，搜索框宽度随 DPI 缩放。日志头的 TableLayoutPanel 必须有显式 Percent 行，否则默认 100px 会压住日志。
- `WrapRowPanel`：宽度固定为父宽、按 `GetPreferredSize(width)` 自动增高、按视觉行垂直居中；`MarkRightGroup`/`AlignRightGroup` 用于右侧组；`StretchControl` 用于路径框填充。设置里的“HEX发送/定时发送/加回车换行”仍用 `MakeGroup` 成组折行。
- 窗口最小尺寸基准 820×560；`UpdateMinimumHeight()` 用 `ComposerHeight` 保证日志/发送区/状态栏不被挤出；`ScheduleResponsiveRelayout()` 在缩放后延迟终排并更新侧栏宽度/composer 行高/日志头响应式。
- 多条字符串 297 行不用 TableLayoutPanel：`MultiRowsPanel` 自绘布局（固定行高/列宽，一次定位），分 3 页；首次展开前 `_prewarmTimer` 在 OnShown 后分批创建句柄（每 20ms 8 个）。
- 高 DPI：200% 窄屏三栏会拥挤；`UpdateLogHeaderResponsive` 在中心宽度 <620 隐藏搜索框、<420 隐藏副标题。改动布局时在 100% 和 200% 下都要看。

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
- 按钮：`ThemeButton` 按 Text 映射矢量图标（清除/打开/发送文件/停止/清发送区/保存/扩展/更多设置/刷新/±/━）；高度用 `Font.Height + 12`，留出 FlowLayoutPanel 的 Margin，避免 200% DPI 被行裁切。
- 复选框：`ThemeCheckBox` 勾选用琥珀色，`Highlighted` 控制暖色标签底；`StatusTick` 的 `Highlight` 只改这个属性，不要再设 `BackColor=Yellow`。
- 文本框：`ThemeTextBox` 提供 `PlaceholderText`（路径框“未选择文件”），并在句柄创建时尝试 `SetWindowTheme(..., "DarkMode_Explorer")` 让原生滚动条变暗。
- 多条字符串 3 页不再用 `TabControl`（原生页框在深色下始终有浅色边），改为 `pageHost` + `pageBar` 自绘分段按钮；`_multiPages`/`_multiTabs` 管理显隐。自检按“第1/2/3页”按钮计数，不按 `TabPage`。
- 发送区的两行选项高度额外 +4px（`UpdateComposerGridRows`），给自绘下拉框留 Margin。
- 禁止在深色控件上再用 `SystemColors.Control/Window/WindowText` 或 `Color.White/Yellow`；所有颜色走 `Theme` token。

### 5.7 Phase 2 工作区 / 富文本日志

- 根布局：工具栏 → 三栏主体（`_bodyLayout`：左 `_sidePanel` + 右 `_split`）→ 进度条 → 状态栏。`_split.Panel1` 是中心工作区（日志头 + 富文本日志 + composer），Panel2 是多条抽屉。
- 左栏：`BuildSidebar`/`BuildSerialSection`/`BuildNetSection`；共用 `_portCombo`，串口/网络字段用 `_serialSection`/`_netSection` 显隐；`UpdateModePanels` 不再碰旧的三行数组。
- 左栏折叠：`BuildRail` 生成窄 rail（展开 `»`、状态点、模式/端口缩写、打开/关闭）；`_sideContent` 与 `_railPanel` 二选一显示。`SetSidebarCollapsed(collapsed,persist,manual,automatic)` 写 `_bodyLayout` 列宽；手动操作持久化 N1093，`_sideUserCollapsed` 保存用户偏好（自动折叠不写）。`ScheduleResponsiveRelayout` 在宽 < `SidebarWidth()+720` 时自动折叠，变宽自动展开；`_sideManualOverride` 一旦手动操作，本次运行不再自动切换。轨道宽度 `RailWidth()`、rail 标签/按钮 `UpdateSidebarRail`。
- N1093 与 N1092 一样是重写版新增 ini 键（0 展开、1 折叠），原版忽略。
- composer：`BuildComposer` 使用 `_settingsRow1`/`_settingsRow2`/`_sendArea` 三个控件，`UpdateComposerGridRows` 写行高；`━` 按钮通过 `_composerPanel.Visible` 折叠整个 composer。
- 接收框：`ThemeRichTextBox`（继承 RichTextBox），字段仍是 `_recvBox`；线程模型不变，所有富文本操作只在界面线程的 `DrainDisplay` 路径执行。
- 分色：`LogFormatter.Parse` 把文本拆成 Timestamp/Rx/Tx/Error/Data/Hex；`AppendFormatted` 用 `SelectionColor/SelectionBackColor` 逐段追加。TX 行暖底、错误行红底，来源是 `Theme.TxWash/ErrorWash`。
- 重画：主题/字体/颜色变化用 `RenderLogAll()`，它基于**当前可见文本**重解析，因此发送回显和终端本地输入也会保留；切换 HEX/文本显示用 `RenderLogBuffer()`，基于 `_receive.HexText/TextAccum`。不要用 `_receive` 缓冲去重画主题，否则会丢发送回显。
- 滚动：`_autoFollow` 为真且原来在底部时跟随；暂停时保持原选择/滚动位置。`RecvAtBottom`/`EM_GETSCROLLPOS`/`EM_SETSCROLLPOS` 兼容 RichTextBox。
- 搜索：`_searchBox` + `SearchNext(forward)` + `_recvBox.Find`；当前匹配用 `Theme.SearchHighlight` 作为 SelectionBackColor，下一次搜索/重画前清理。
- 复制：富文本框右键菜单有“复制/复制纯数据（去掉时间戳和收/发标记）/全选/清除窗口”；`LogFormatter.StripMeta` 是纯逻辑，已有自检。
- 速率：`_rateTimer` 每 500ms 用 S/R 计数差算 B/s，`Sparkline` 画最近 24 个 RX 样本；状态栏另有 RX/TX 文本速率和缓冲占用。`OnMainFormClosing` 必须停表。
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
| 保存数据 = 双文件（`SAVE*.DAT` 原始 + `SaveWindows*.TXT` 窗口内容）；接收落文件 = 追加 | 已按规格实现；落文件名串口用端口名、网络用 `TCPCLIENT/TCPSERVER/UDP` |
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

---

## 7. 规格疑点与实现取舍

- `N1058`（多条发送间隔）：原程序用隐藏中转框 `Edit35`，重写没有该控件，**保存固定写 `1000`，读入忽略**，避免覆盖主发送间隔。
- `N1080`：`1` 和 `2` 都映射 TCPClient（照规格原文的怪癖）；`3` 是 UDP；其他整串是端口名。
- `N1100`：固定写 `C`；English 切换不在范围内。
- 「更多串口设置」是自建对话框（原版是 CPort 组件自带对话框）；字段按规格可确认的项实现，流控 `Custom` 打开时按 `None` 处理。
- 校验模式 7（ADD16）的字节序按规格标为推断：低字节在前。
- 打开文件预览用原字节直接按 Latin-1 式字符累积文本，再经 `ReceiveDisplay.SetPreviewBuffers` 替换预览缓冲；原始接收缓冲和接收计数不动。
- 网络服务端本地端口非法时选择“提示后不启动”（规格此处原程序响应未细读）。
- 标题栏继续用 DWM 深/浅原生方案（用户确认），完全自绘标题栏不在本期；浅色模式下系统文件/字体/颜色对话框观感正常，深色模式下这些原生对话框仍是浅色。
- 接收/发送设置目前仍在 composer 的两行里直接展示，没有做悬浮设置面板；折叠动画也未加入。
- 200% DPI + 窄屏（如 1440×900）下三栏会拥挤；日志头会自动收起搜索框/副标题，推荐 100%–150% 或宽度足够的窗口。
- 富文本日志按段设置 SelectionColor/SelectionBackColor；极高波特率下若出现重绘瓶颈，优先把追加改成批量/自定义绘制，不要退回单色 TextBox。

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

1. ✅ Phase 3A：左侧栏 rail 折叠、N1093、窄窗自动折叠。
2. Phase 3B：接收/发送低频设置浮层，composer 只留常用项。
3. Phase 3C：高波特率日志压测、短动效、toast 和 DPI 回归。
4. 在真实串口/网口上复验三栏布局：接收分色、搜索/暂停、发送回显、发送文件、定时发送、TCP 服务端多连接、UDP。
5. 完全自绘标题栏（用户当前选择保留原生 DWM，不要擅自改）。
6. 规格「还不能当规则用的部分」里的烧写器、波形等——**用户已排除，不要做**。
7. 若需要单文件分发，可在 csproj 增加 Costura/ILMerge 方案；当前分发 exe + config 即可，目标机只需 .NET Framework 4.8。

---

## 10. 汇报约定

- 每完成一个里程碑：更新 `REWRITE-PLAN.md`；向用户汇报「做了什么 / 怎么验证的 / 还差什么」。
- 遇到规格没写的数值或行为：**停下问用户**，不要猜；把问题写清楚（哪个控件、哪个场景、候选方案）。
- 用户会实机测试并反馈现象；先复现/定位再改，改完给可验证的检查步骤。
- 任何成功声明前先跑 build + selftest + 冒烟，并贴出结果。
