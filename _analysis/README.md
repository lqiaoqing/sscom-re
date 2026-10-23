# _analysis — 逆向分析资料

这里保存把 `sscom5.13.1.exe` 的行为整理成规格时用的脚本、DFM/Ghidra 产物和截图。**编译/运行 `SscomRe` 不需要本目录**；它只用于溯源和复核规格。

## 目标镜像

所有脚本默认读取仓库根目录的：

```
sscom5.13.1.unpacked.exe
SHA-256: 85efc6d9b7c55ea150f81ffbc62d6141d0011dc86b83dd81ff84af885e197aee
```

这是从加壳的 `sscom5.13.1.exe` 展开、把节的文件偏移改成等于 RVA、并把入口改到 `0x1000` 后的派生镜像：

- 基址 `0x400000`
- 文件偏移 = RVA（`VA - 0x400000`）
- 入口来自壳尾 `0x147415` 的 `popad` 之后 `push 0x401000; ret`

**原程序和镜像都不在版本库中**（`.gitignore` 排除了 `sscom5.13.1.exe`、`sscom5.13.1.unpacked.exe` 和 `_analysis/decomp/`）。需要复核时把镜像放回仓库根目录；脚本已改为相对路径。反编译产物请本地生成，不要提交。

## 脚本

需要 Python 3 和 `capstone`（仅 `tool.py` 用）：

```bash
pip install capstone
```

| 文件 | 用途 |
|---|---|
| `tool.py` | Capstone 反汇编工具。`d <地址> -n <条数>` 反汇编；`x <地址> -l <字节数>` 十六进制；`f <模式...>` 搜索；`ref <值>` 找代码里的 dword 引用；`iat <关键字>` 查导入名；`mt <地址>` 解析 Delphi 方法表 |
| `strrefs.py` | 扫描函数机器码里的字符串立即数（`mov/push/cmp`），输出地址与 GBK/ASCII 字符串 |
| `dfm.py` | 解析 `.rsrc` 里的二进制 DFM：控件树、Caption、OnClick 等属性 |
| `dfm2.py` | 在 DFM 区域做滑窗扫描，按对象名/Caption/OnClick 三元组提取 |
| `imports.py` | 从 `.idata` 重建 IAT 槽位 → 导入名，输出 `iat_map.json` |
| `dumpstr.py` | 按给定 VA 列表导出 GBK 字符串（示例脚本，按需改地址） |

例：

```bash
python _analysis/tool.py d 0x40578c -n 80 --detail
python _analysis/strrefs.py 0x40578c 0x4065a0
python _analysis/dfm.py > _analysis/form_tree_new.txt
```

## 已保存的产物

| 文件 | 内容 |
|---|---|
| `form_tree.txt` | 主窗体 DFM 控件树、Caption、可见性、矩形 |
| `check_defaults.txt` | 菜单/复选框/编辑框的默认 Checked、Text、Max 等 |
| `method_table.txt` | 主窗体 144 个方法表记录（地址、名字、大小） |
| `iat_map.json` | IAT 槽位到导入函数名的映射（重建结果） |
| `ghidra/out/functions.csv` | Ghidra 识别出的函数入口、名字、大小 |
| `ghidra/out/xrefs.txt` | 关键全局地址的读写引用 |
| `addrs_batch*.txt`、`ghidra/addrs_batch*.txt` | 批量反编译/分析用的地址清单 |
| `pdeb*.txt`、`idata_scan.txt`、`sd_esc.txt` | 展开镜像、`.idata`、字符串转义的探查记录 |
| `senddata_dis.txt`、`saveconfig_dis.txt`、`savedata_dis.txt`、`maketlist_dis.txt` | 关键函数的反汇编摘录 |
| `title_bar.png`、`btn_row*.png` | 原版界面截图（核对布局/文案用） |

`ghidra/scripts/` 是 Ghidra headless 脚本：

- `DumpInventory.java`：函数清单
- `DumpXrefs.java`：交叉引用
- `DumpDecomp.java <outDir> <addrListFile>`：按地址批量反编译成 `.c`

Ghidra 项目数据库（`.rep`）和项目描述文件（`.gpr`）都是本地生成物，已忽略。新建项目后把 `sscom5.13.1.unpacked.exe` 导入，基址设为 `0x400000` 即可。

## 使用约定

- 规格结论以 [../sscom5.13.1-rewrite-spec.md](../sscom5.13.1-rewrite-spec.md) 为准；这里的地址只用于溯源，**不要**把偏移/函数地址写进重写程序逻辑。
- DFM 里的 Caption 是 GBK；控件名（如 `CheckBox5`）和界面文案（如 `HEX显示`）不是一回事，先对 Caption 再对方法。
- 静态分析没有打开过串口，也没有把任何字节发到线上；所有动态结论都需要在目标机器上复验。
- 原程序的烧写器（STM/GD32、STC/IAP15）命令字节仍未读出，规格中标注为未知，重写不包含。
