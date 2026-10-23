# SSCOM 5.13.1 规格补全交接

> **状态（2026-10-23）：规格补全与 M1–M9 重写均已完成。** 本文保留为当时的任务书与逆向方法记录；文中 `D:\Work\workspace\sscom-re` 是旧工作目录，当前仓库以相对路径为准。原程序、解包镜像与反编译源码未纳入版本库，见 [README](README.md) 与 [_analysis/README.md](_analysis/README.md)。

下一个 agent 的任务：把用户确认要补的剩余行为写进规格。不要开始重写程序。不要做用户已排除的功能。

## Suggested skills

- `reverse-engineer-anything`：目标是加壳 Delphi PE 的展开镜像。需要读 native 反汇编、方法表、DFM 资源和导入跳板。REA 的 Ghidra 可用，但上一轮后半用本机 Capstone 4.0.2 扫镜像更快。打开 Ghidra 会话后结束时要 `close_binary`。
- 项目编码规则在用户规则 / 工作区 `CLAUDE.md`：规格是 UTF-8；二进制资源 Caption 是 GBK。改规格时保持 UTF-8，不要把 GB2312 源文件转成 UTF-8。

## 目标与范围

用户要把 SSCOM 5.13.1 收到能按观察行为重写的程度。当前产物是行为规格，不是实现。

**这次要做：** 把下面「剩余清单」逐项追到指令，写成 **观察 / 推断 / 未知**，补进规格正文，并把能当规则的句子加到文末清单。做完后更新「还不能当规则用的部分」。

**这次不要做：**

- 数据波形（示波器）
- English / `languageChange`
- 字体和背景色
- 转换计算器
- STM/GD32、STC/IAP15 烧写（命令字节保持未知，不要实现）
- LUA 脚本（菜单 Caption 是「尚未支持」，核对一句即可）
- 帮助 / 关于 / 更新历史 / 广告条（除非顺手碰到一句字符串）
- 不要声称打开过串口或发过网络包。全程静态。

工作量口径（上一轮估的）：去掉波形/English/字体背景色/计算器之后，大约还有**现有规格正文的四成到七成**。日常收发那一档大约两成出头；保存参数和「更多串口设置」会把总量拉到四成至七成。

## 文件

| 路径 | 用途 |
|---|---|
| `D:\Work\workspace\sscom-re\sscom5.13.1-rewrite-spec.md` | **唯一要改的文件。** UTF-8。已有打开、线路参数、校验、HEX 发送标志、99 行发送、TCP/UDP 发送、接收显示。 |
| `D:\Work\workspace\sscom-re\sscom5.13.1.unpacked.exe` | 分析用镜像。基址 `0x400000`，文件偏移 = RVA。SHA-256 `85efc6d9b7c55ea150f81ffbc62d6141d0011dc86b83dd81ff84af885e197aee`。 |
| `D:\Work\workspace\sscom-re\sscom5.13.1.exe` | 仍加壳，补规格不需要。 |
| 原界面截图（历史附件，未纳入仓库） | 串口已打开、多条字符串页；界面截图可参考 `_analysis/title_bar.png`、`_analysis/btn_row*.png`。 |

规格里的地址、字段、算法不要在交接里复述，以该文件为准。

## 硬约束

1. 结论必须标 **观察 / 推断 / 未知**。
2. 方法表记录：`word` 大小、`dword` 代码、短字符串名字。大小 = `2+4+1+名字长度`。代码属于**同一条记录里指针后面的那个名字**。早期把指针配给前一个名字，把 `displayCOMdata`（`0x40578c`）错写成 `isp255_stm32Click`。表起点 `0x5054f9`。核对身份时按这个算法重走，不要信旧段落里未更正的 ISP 指针。
3. 字段表：`dword` 偏移、`word` 类型索引、短字符串。约从 `0x504a2a`（`ComPort`）起。
4. DFM Caption 用 GBK 搜。控件名字（`CheckBox5`）和界面 Caption（`HEX显示`）不是一回事。不要用 `Button1Click` 这种名字猜功能，先对 Caption。
5. 内核32 导入：`jmp dword ptr [IAT]`，未绑定名字在 `IAT - 0x1F4`（本镜像上对过 `ReadFile` / `GetTickCount` / `GetLocalTime`）。user32 的间距不是 `0x1F4`。
6. 虚调用 `+0xB8` 在这套 VCL 里是复选框勾选字节。
7. 改规格后把新规则写进文末「可以直接当规则用的部分」。对象偏移和函数地址可以留在观察里，但告诉重写 agent：实现时不要把这些地址抄进新程序。

## 已经写进规格的（不要重做）

打开串口、`CreateFileA`、波特率表 `0x508fb4`、数据位/停止位/校验、端口名 `COM` 变换、发送组包与七种校验、99 行多条发送、TCP 客户端/服务端/UDP 发送、接收显示（`displayCOMdata` `0x40578c`：HEX 显示、时间戳分包、GBK 半字、`Memo` `EM_REPLACESEL`、计数超过 `0x4ed548` 截断）。

ISP 段已更正：`isp255_stm32Click` = `0x419954`；`STM32ISP1Click` = `0x4251d4`；`STC15ISPClick` = `0x4252a8`。`0x40578c` 是显示函数。烧写命令仍未知。

## 剩余清单（按建议顺序）

先做界面上每天都用的，再做 INI 和「更多串口设置」。每项先从 DFM Caption 对到方法，再反汇编。

### A. 日常收发（约两成出头）

1. **清除窗口 / 打开文件**  
   接收框下方两个按钮。用 GBK Caption `清除窗口`、`打开文件` 对方法。不要猜 `Button1Click`。

2. **保存数据**  
   `Button42Click` `0x40fd54`。规格里只写过文件名：格式串 `SaveWindows`（`0x4ef060`），`:` → `-`，空格 `/` `\` → `_`。要补：保存的是哪段文本、编码、对话框、是否截断。

3. **接收数据到文件**  
   `CheckBox19` `self+0x4A0`，`CheckBox19Click` `0x4219cc`。`displayCOMdata` 只在模式不是 0/1/2 时看这个勾选，调用 `0x4e2aac`（转到 `0x4e1ae4`）和 `0x4e37e4`，文件名候选全局串 `0x50de44`。串口/TCP 路径可能在 Click 或别的函数。要补：何时打开文件、写原文还是 HEX、何时关闭。

4. **定时发送（单条，不是 99 行）**  
   `CheckBox3` Caption `定时发送`，`CheckBox3Click` `0x40d7a4`。`SendStringTimer` `self+0x2C8`，`SendStringTimerTimer` `0x40da24`（过程很短）。间隔编辑框在截图里是 `2000 ms/次`。多条发送的 `CheckBox6Click` 会关掉这个定时器。

5. **发送文件 / 停止 / 清发送区**  
   菜单「发送文件设置」：`sendfile_0Click` `0x425e68`、`sendfile_1Click` `0x425ec8`、`sendfile_10Click` `0x425f28`、`sendfile_50Click` `0x425f88`、`sendfile_100Click` `0x425fe8`（约 96 字节入口，共用一个发送过程）。Caption：`连续发送`、`每发256字节延时N毫秒`。停止、清发送区用 GBK Caption 对按钮。

6. **RTS / DTR（打开之后再改，以及失败提示）**  
   打开路径已经 `SetDTR`/`SetRTS`。`CheckBox1Click` `0x407638`、`CheckBox2Click` `0x4077b0`（字段 `0x3C0` / `0x3BC`）。失败串在 `Rfreues100msTimer` 后部：`Unable to set DTR!!!`、`DTR无法设置!...`、`Unable to set RTS!!!`（约 `0x4ee2dd`、`0x4ee3a4`、`0x4ee41d`）。

7. **状态栏**  
   `Rfreues100msTimer` `0x4065a0`（大约到 `0x4077a0`，约 4KB）。`StatusBar1` `self+0x348`。已看到 `R:` `0x4ee299`、`S:` `0x4ee29c`、` Opened  ` / ` Closed  `、`已打开` / `已关闭`、`bps,`。接收计数累加在 `0x4ed540`（显示函数里）。发送计数候选 `0x4ed53c`（显示函数后部写过状态，要在定时器里确认）。CTS/DSR/RLSD：`ComPortCTSChange` `0x407928`、`ComPortDSRChange` `0x4079c0`、`ComPortRLSDChange` `0x407a58`。截图格式：`S:0  R:0`、`COM38 已打开 115200bps,8,1,None,None`、`CTS=0 DSR=0 RLSD=0`。

8. **最前**  
   `CheckBox7` Caption `最前`，`CheckBox7MouseUp` `0x428f90`。已看到走 `SetWindowPos`（`0x4ec160`）。补成规则：勾选时 HWND_TOPMOST 还是别的。

9. **隐藏**  
   GBK Caption `隐藏`。可能是托盘或藏到任务栏。对到方法后再写。

10. **运行时窗口标题**  
    资源 Caption 仍是 `sscom5.00a,...`。运行截图是 `SSCOM V5.13.1 串口/网络数据调试器,作者:大虾丁丁,2618058@qq.com. QQ群：52502449(最新版本)`。从 `FormCreate` `0x409d58` 追拼接。

11. **HEX 发送：两个字符拼成一个字节**  
    组包 `FUN_004034a4` `0x4034a4`。规格已有字符类（`0-9A-Fa-f`、空格、CR、LF）和 `0x50de34` 标志。缺：成对拼字节、奇数个半字节、非法字符是丢掉还是整段不发。循环从 `0x4040d6` 起。

12. **显示缓冲上限 200K / 500K / 1M / 2M / 5M**  
    `N200KClick` `0x425584`、`N500KClick` `0x425600`、`N1MClick` `0x42567c`、`N2MClick` `0x4256f8`、`N5MClick` `0x425774`。规格里截断比较的是 `0x4ed548`，当前值 `0xF4240`（1000000）。这些菜单几乎肯定改这个 dword。改完后，文末那条「大于 1000000」要改成「大于 `0x4ed548`，菜单写入这些档位」。

13. **刷新串口列表 / 通讯端口菜单**  
    `MenuComItemClick` `0x425a50`。菜单还有 `TCPClient1Click` `0x425834`、`TCPServer1Click` `0x4258e8`、`UDP1Click` `0x42599c`（可能只是把端口名设成 `TCPClient`/`TCPServer`/`UDP`，与已有模式字节衔接）。`Open_immediatelyClick` `0x4257f0`：启动时立即打开串口。

14. **多条字符串附加 UI**  
    已有：双击 `NdataOnDblclick` `0x40828c`、循环发送 `CheckBox6Click`。还要：`NsendOnclick` `0x408428`（点击发送列）、`Splitter1Moved` `0x407b00`（拖动加宽）、`makeTList` `0x4085fc`（`+/-` 增删行）、`NCheckBoxMouseUp` `0x407e7c`、`NEditKeyPress` `0x407eb4`、`NorderKeyUp` `0x407f6c`、`NtimeKeyUp` `0x4080f8`。导入 ini 并进第 B 项。

15. **终端三项开关（只要菜单勾选行为，不要做终端仿真引擎）**  
    `term_enter_sendClick` `0x425418`、`term_key_sendClick` `0x42549c`、`term_showtypeClick` `0x425520`、`term_0Dadd0AClick` `0x42948c`。字段 `term_enter_send` `0x468`、`term_key_send` `0x46C`、`term_0Dadd0A` `0x598`。`MemoKeyPress` `0x40cf00` 里已有两处 `WriteStr`，和这些开关的关系要接上。菜单原文：回车后发送输入行、按键立即发送、发送回车后自动加 `0x0A`。

### B. 重块（拉高到四成至七成）

16. **保存参数 / 导入 ini**  
    `saveconfigClick` `0x4133b4`，字段 `saveconfig` `0x338`。`FormCreate` / `FormClose` `0x40fc18` 多半读写同一套键。规格里有一句旧 INI 注释 `0=None，1=modbusCRC16...` 和现行七项对不上，不能当现行规则。要列出实际读写的键、节名、默认值。导入 ini 的按钮用 GBK `导入ini` 对方法。

17. **更多串口设置**  
    菜单 Caption `更多串口设置`。会开另一个窗口。这里才可能有规格仍缺的：超时五个整数的默认值、输入/输出缓冲、流控。打开路径已经调用 `ApplyBuffer` / `ApplyTimeouts`，但值从哪来还没写。

## 已知地图（规格未收录、补剩余项时用）

主窗体公开方法 141 个，表从 `0x5054f9` 走。完整名单可用上一轮的解析脚本重打，不要手抄错。

字段（与剩余项有关）：

- `0x2C4` ComPort，`0x2C8` SendStringTimer，`0x2CC` Send_N_StringTimer
- `0x344` ProgressBar1，`0x348` StatusBar1
- `0x370` timeoutCRLF，`0x374` CheckBox5（HEX显示），`0x378` timeoutText
- `0x388` CheckBox4（HEX发送），`0x38C` CheckBox3（定时发送）
- `0x3BC` / `0x3C0` CheckBox2 / CheckBox1（RTS/DTR 候选，以 Caption 为准）
- `0x4A0` CheckBox19（接收数据到文件），`0x4B0` Memo
- `0x550` CheckBox6（循环发送），`0x584` CheckBox7（最前）
- `0x4ed534` 网络模式，`0x4ed540` 接收计数，`0x4ed544` 英文界面标志，`0x4ed548` 显示截断阈值

常用 RTL：`0x4eacc0` 字面量→字符串，`0x4eaf10` 拼接，`0x4eaee8` 赋值，`0x4eaeb8` 释放，`0x4eaf9c` 相等（`sete al`），`0x4d5e34` StrToIntDef，`0x4af9f0` 取控件文本，`0x4afa20` 写控件文本，`0x47d578` `EM_REPLACESEL`。

菜单 DFM 在镜像约 `0x5330d8` 起（GBK Caption）。小工具里「数据波形」「转换计算器」这次跳过。

## 写法

- 新节跟现有章节同级，例如 `## 定时发送`、`## 状态栏`。不要另起一份文档。
- 观察写调用约定、立即数、字段、导入名。推断单独标。没读完就标未知，不要用「应该是」充规则。
- 显示截断阈值改成可配置后，同步改文末规则，避免和正文打架。
- 做完把「还不能当规则用的部分」改成：烧写器命令字节；以及你仍然没写成规则的项。用户已排除的功能写明「这次不包含」，不要写成未知缺陷。

## 完成标准

规格补全即完成。不要开始写新程序。用户没有要求提交 git。若某一项（尤其「更多串口设置」另一个窗体）静态读不完，留下未知边界和已确认的入口地址，不要编默认值。
