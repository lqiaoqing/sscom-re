# 交接文档：SscomRe（SSCOM 5.13.1 的 C# 重写）

> 行为规格的唯一依据是 [`sscom5.13.1-rewrite-spec.md`](sscom5.13.1-rewrite-spec.md)；本文件只做工程索引、架构约定和已知取舍。
> 当前状态（2026-10-23）：**M1–M9 全部实现，UI Phase 1 深/浅双主题已接入，`--selftest` 185/185 通过**。原程序、镜像和反编译源码未纳入版本库。

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
  MainForm.cs           主窗体（约 3000 行）：菜单、布局、收发编排、ini
  Theme.cs              深色 token、DPI 尺寸、DWM、菜单/状态栏渲染、递归兜底
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
  SelfTest.cs           --selftest 用例（当前 185 项）
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

**验证口径：**

- `dotnet build -c Release` → 0 警告 0 错误
- `SscomRe.exe --selftest` → `pass=185 fail=0`
- 启动冒烟 ≥5 秒无 `crash.log`
- 本次环境无真实 COM 口，串口/网络的实机收发热路径仍需在目标硬件上复验

---

## 4. 构建 / 自检 / 冒烟

```bash
cd SscomRe
dotnet build -c Release
# 输出固定为 SscomRe/bin/net48/SscomRe.exe（csproj 固定 OutputPath）

cd bin/net48
./SscomRe.exe --selftest          # 必须 pass=185 fail=0

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

### 5.5 布局 / DPI / 性能（2026-10-23 按原版重排，别改回去）

程序是 PerMonitorV2，但窗体没有 `AutoScaleDimensions`，布局值是物理像素，而字体按当前 DPI 渲染。所有随窗口伸缩的行都必须按实际宽度/内容计算，不要写死总宽度。

- 底部是 2 列 4 行网格（`_bottomGrid`）：左列行 0 端口号、行 1 打开/刷新/更多串口设置、行 2 RTS/DTR/波特率（网络模式为远程/本地/连接断开）；右列行 0 选项、行 1 时间戳/超时/帧尾/校验、行 2–3 发送框+发送。左右行由同一个网格行高对齐。
- 工具栏：左 清除窗口/打开文件 + 路径框（`WrapRowPanel.StretchControl` 填满中段）+ 右 发送文件/停止/清发送区/最前/保存参数/扩展/━。
- 设置里的“HEX发送/定时发送/加回车换行”用 `MakeGroup` 成组，窄窗时整组折行；不要给需要折行的 `FlowLayoutPanel` 设 `AutoSize=true`。
- `WrapRowPanel`：宽度固定为父宽、按 `GetPreferredSize(width)` 自动增高、按视觉行垂直居中；`MarkRightGroup`/`AlignRightGroup` 用于右侧组；`StretchControl` 用于路径框填充。
- 左栏宽度由当前模式三行的实际内容测量（`UpdateLeftColumnWidth`）；`UpdateBottomGridRows` 把两行选项的实际高度写入网格行高；`BottomContentHeight` 计算底栏总高。不要用网格自身高度反推根行高（会形成布局循环）。
- 窗口最小尺寸基准 820×560；`UpdateMinimumHeight()` 按当前折行高度保证底栏和状态栏不被挤出窗口；`ScheduleResponsiveRelayout()` 在缩放后延迟终排。
- 多条字符串 297 行不用 TableLayoutPanel：`MultiRowsPanel` 自绘布局（固定行高/列宽，一次定位），分 3 页。行按钮调宽只改 `SendColumnWidth` 后 `PerformLayout()`。
- 首次展开前 `_prewarmTimer` 在 OnShown 后分批创建多面板句柄（每 20ms 8 个）；改回 TableLayoutPanel 会回到秒级展开。
- 接收框 `WordWrap = true`、`ScrollBars.Vertical`；追加走 `_displayTimer`（30ms 合并）；在底部时跟随，向上翻看历史时保持滚动/光标。
- 高 DPI：物理像素布局 + 放大字体在 200% 缩放下会把底栏撑得很大，接收区变窄；自检已按 `DeviceDpi` 缩放测试窗口，但界面本身没有做完整的 DPI 自适应布局。改动布局时在 100% 和 200% 下都要看。

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
- 底栏两行设置区的高度额外 +4px（`UpdateBottomGridRows`/`BottomContentHeight`），给自绘下拉框留 Margin。
- 接收框仍是普通 `TextBox`，日志是单色；要 RX/TX 分色需要换成 `RichTextBox` 并重做追加/截断/保存/滚动（Phase 2，不要偷偷改）。
- 禁止在深色控件上再用 `SystemColors.Control/Window/WindowText` 或 `Color.White/Yellow`；所有颜色走 `Theme` token。

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
- Phase 1 主题只换皮：接收日志仍是普通 `TextBox`（单色），RX/TX 分色等富文本效果留给 Phase 2。
- 标题栏用 DWM 深/浅原生方案，完全自绘标题栏留到 Phase 2；浅色模式下系统文件/字体/颜色对话框观感正常，深色模式下这些原生对话框仍是浅色。
- 打开/保存文件、字体、颜色、MessageBox 是系统原生浅色对话框，未做深色包装。
- 200% DPI + 窄屏（如 1440×900）下底栏会换行并超出窗口；设计窗口宽度约 2800 物理像素（或 100%–150% 缩放）时正常。

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

按用户优先级从高到低：

1. **UI Phase 2**：左侧连接/会话栏 + 主接收区 + 右侧多条发送抽屉的布局重排（视觉稿方向已确认）。
2. 接收日志富文本化（`RichTextBox`，RX 青绿 / TX 琥珀），同时保持 30ms 合并刷新、截断重画、保存/打开文件行为。
3. 完全自绘标题栏（保留拖动/吸附/最大化），或继续用原生 DWM 深色。
4. 在真实串口/网口上复验：打开/关闭、DTR/RTS、发送文件、定时发送、TCP 服务端多连接、UDP。
5. 高 DPI 布局自适应（200% 缩放下底栏过高）；目前只保证 100%–150% 或宽窗口可用。
6. 规格「还不能当规则用的部分」里的烧写器、波形等——**用户已排除，不要做**。
7. 若需要单文件分发，可在 csproj 增加 Costura/ILMerge 方案；当前分发 exe + config 即可，目标机只需 .NET Framework 4.8。

---

## 10. 汇报约定

- 每完成一个里程碑：更新 `REWRITE-PLAN.md`；向用户汇报「做了什么 / 怎么验证的 / 还差什么」。
- 遇到规格没写的数值或行为：**停下问用户**，不要猜；把问题写清楚（哪个控件、哪个场景、候选方案）。
- 用户会实机测试并反馈现象；先复现/定位再改，改完给可验证的检查步骤。
- 任何成功声明前先跑 build + selftest + 冒烟，并贴出结果。
