using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;

namespace SscomRe;

/// <summary>
/// 主窗体（里程碑 M1：骨架 + 串口开关 + 状态栏）。
/// 布局与文案按规格“界面结构与控件对照 / 状态栏 / 最前、扩展、━”设计，其余按钮先占位。
/// </summary>
public sealed class MainForm : Form
{
    private readonly ComPortService _port = new();
    private readonly ReceiveDisplay _receive = new();

    // 计数（规格：S:=发送计数、R:=接收计数，清除窗口时清零）
    private long _sentCount;
    private long _recvCount;

    // 显示缓冲上限（规格：默认 1000000，菜单可改 200K/500K/1M/2M/5M）
    private int _displayLimit = 1_000_000;
    private readonly List<ToolStripMenuItem> _limitItems = new();

    // 控件
    private ComboBox _portCombo = null!;
    private ComboBox _baudCombo = null!;
    private Button _openButton = null!;
    private CheckBox _rtsBox = null!;
    private CheckBox _dtrBox = null!;
    private Button _moreSettingsButton = null!;
    private TextBox _recvBox = null!;
    private TextBox _sendBox = null!;
    private Button _sendButton = null!;
    private CheckBox _hexDisplayBox = null!;
    private CheckBox _hexSendBox = null!;
    private CheckBox _newLineBox = null!;
    private CheckBox _timestampBox = null!;
    private CheckBox _recvToFileBox = null!;
    private TextBox _timeoutEdit = null!;
    private TextBox _filePathBox = null!;
    private string _recvFileName = "";
    private ComboBox _verifyBox = null!;
    private TextBox _verifyNumEdit = null!;
    private ComboBox _tailCombo = null!;
    private TextBox _crcBox = null!;
    private ToolStripStatusLabel _connStatus = null!;
    private ToolStripStatusLabel _sentStatus = null!;
    private ToolStripStatusLabel _recvStatus = null!;
    private ToolStripStatusLabel _modemStatus = null!;
    private Control _settingsBar = null!;
    private Control _sendArea = null!;
    private Button _hideToggleButton = null!;
    private Button _collapseButton = null!;
    private SplitContainer _split = null!;
    private CheckBox _topmostBox = null!;

    // —— 接收线程与界面线程之间的快照 / 待显示队列 ——
    // 接收事件在串口线程上跑：只读下面的普通字段，绝不在串口线程里读控件属性
    //（跨线程读 TextBox.Text 会发 WM_GETTEXT 等界面线程处理，界面忙时会把串口线程堵住）。
    private volatile bool _snapHexDisplay;
    private volatile bool _snapTimestamp;
    private volatile bool _snapRecvToFile;
    private int _snapTimeoutMs = 20;
    private readonly object _displayLock = new();
    private readonly System.Text.StringBuilder _pendingDisplay = new();
    private string? _pendingReplace;
    private bool _drainScheduled;

    // —— M4：定时发送 / 发送文件 / 状态栏联动 ——
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _prewarmTimer = new() { Interval = 20 };
    private readonly Queue<Control> _prewarmQueue = new();
    private bool _adjustingSplit;
    private readonly System.Windows.Forms.Timer _sendStringTimer = new();   // 单条定时发送
    private CheckBox _timerSendBox = null!;              // CheckBox3 定时发送
    private TextBox _timerIntervalEdit = null!;          // Edit1
    private ProgressBar _progressBar = null!;
    private StatusStrip _statusStrip = null!;
    private Label _timeoutLabel = null!;
    private Button _sendFileButton = null!;
    private readonly List<ToolStripMenuItem> _sendFileItems = new();        // sendfile_*
    private int _sendFileDelay = 1;                      // 资源：默认勾“每发256字节延时1毫秒”
    private volatile bool _abortFlag;                    // 0x4ED579
    private bool _sendingFile;

    // —— M5：多条字符串 99 行 ——
    private CheckBox[] _rowHex = new CheckBox[99];
    private TextBox[] _rowData = new TextBox[99];
    private Button[] _rowSend = new Button[99];
    private TextBox[] _rowOrder = new TextBox[99];
    private TextBox[] _rowTime = new TextBox[99];
    private readonly string[] _rowComments = new string[99];
    private readonly ToolTip _rowTip = new();
    private MultiRowsPanel _multiRows = null!;
    private Control _toolBar = null!;
    private Control _timerValueGroup = null!;
    private Control _timeoutGroup = null!;
    private Control _verifyIndexGroup = null!;
    private bool _relayoutScheduled;
    private bool _updatingMin;
    private CheckBox _loopSendBox = null!;               // CheckBox6 循环发送
    private Panel _multiPanel = null!;
    private readonly System.Windows.Forms.Timer _multiTimer = new() { Interval = 1000 };   // 资源无 Interval，TTimer 默认 1000
    private readonly List<(int Order, int Row)> _loopOrder = new();   // 排序后的表（顺序号快照 + 行号）
    private readonly MultiSendPlanner _planner = new();               // 一拍一行的状态机
    private int _multiBtnWidth = 120;                    // N1087 行按钮宽度

    // —— M6：网络（TCP 客户端 / 服务端 / UDP）——
    private readonly NetworkService _net = new();
    private Control _serialBar = null!;                  // 对应 PanelCom
    private Control _netBar = null!;                     // 对应 PanelNet
    private TextBox _remoteIpBox = null!;
    private TextBox _remotePortBox = null!;
    private TextBox _localPortBox = null!;
    private ComboBox _localIpCombo = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private ToolStripMenuItem _openImmediatelyItem = null!;
    private ToolStripMenuItem _commMenu = null!;
    private ToolStripMenuItem[] _protocolItems = [];
    private readonly List<ToolStripMenuItem> _comMenuItems = new();
    private NetMode _netMode;                            // 0x4ED534

    // —— M7：终端仿真 / 自定义波特率 ——
    private readonly TerminalInput _terminal = new();
    private ToolStripMenuItem _termEnterItem = null!;
    private ToolStripMenuItem _termKeyItem = null!;
    private ToolStripMenuItem _termEchoItem = null!;
    private ToolStripMenuItem _termAddLfItem = null!;
    private TextBox _customBaudBox = null!;

    // —— M8：ini。加载过程中不自动保存，也不要因为切模式而立刻连网。
    private Button _importIniButton = null!;
    private bool _loading;
    private int _multiPanelWidth = 515;                  // N1086，缺省 515
    private TableLayoutPanel _lowerBand = null!;
    private TableLayoutPanel _rootLayout = null!;
    private string _ascSendText = "abcdefg";
    private string _hexSendText = "12 FF 00 30";

    public MainForm()
    {
        Text = "串口/网络数据调试器（重写版）";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        Width = 980;
        Height = 700;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        WireEvents();
        RefreshRecvSnapshot();
        RefreshPortList();
        LoadConfig();
        UpdateLeftColumnWidth();
        UpdateStatus();
        UpdateDependentVisibility();
    }

    // ———————————————————————— UI 构建 ————————————————————————

    private void BuildUi()
    {
        SuspendLayout();

        var menu = BuildMenu();
        var toolBar = BuildToolBar();
        _toolBar = toolBar;
        var bottom = BuildBottomBand();
        var center = BuildCenter();
        var status = BuildStatusBar();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        _rootLayout = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 接收区
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 进度条
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, LineHeight())); // 工具栏，可换行
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, BottomBandHeight())); // 端口 + 选项 + 发送框
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 状态栏

        _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 8, Maximum = 100, Visible = false, Margin = new Padding(0) };
        root.Controls.Add(center, 0, 0);
        root.Controls.Add(_progressBar, 0, 1);
        root.Controls.Add(toolBar, 0, 2);
        root.Controls.Add(bottom, 0, 3);
        root.Controls.Add(status, 0, 4);

        toolBar.SizeChanged += (_, _) => { UpdateToolbarRowHeight(toolBar); ScheduleResponsiveRelayout(); };
        UpdateToolbarRowHeight(toolBar);
        UpdateBottomBandHeight();
        SizeChanged += (_, _) => ScheduleResponsiveRelayout();

        Controls.Add(root);
        Controls.Add(menu);
        MainMenuStrip = menu;
        ResumeLayout();
    }

    private int LineHeight() => Math.Max(28, Font.Height + 14);

    /// <summary>底部控制区：左侧三行端口，右侧两行选项加三行发送框。设置行换行时整体高度会增加。</summary>
    private int BottomBandHeight() => LineHeight() * 4 + 4;

    private void UpdateToolbarRowHeight(Control toolBar)
    {
        if (_rootLayout is null) return;
        int h = Math.Max(LineHeight(), toolBar.Height);
        RowStyle style = _rootLayout.RowStyles[2];
        if (Math.Abs(style.Height - h) > 0.5f) style.Height = h;
    }

    /// <summary>折行/缩放后 Toolbar、设置区和根行高可能在同一轮布局里彼此错过，延迟一次终排。</summary>
    private void ScheduleResponsiveRelayout()
    {
        if (_relayoutScheduled || !IsHandleCreated) return;
        _relayoutScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _relayoutScheduled = false;
            if (_rootLayout is null || _toolBar is null || _settingsBar is null) return;
            int toolbarHeight = Math.Max(LineHeight(), _toolBar.Height);
            int bottomHeight = Math.Max(LineHeight() * 2, _settingsBar.Height) + LineHeight() * 2 + 4;
            RowStyle toolbarRow = _rootLayout.RowStyles[2];
            if (Math.Abs(toolbarRow.Height - toolbarHeight) > 0.5f) toolbarRow.Height = toolbarHeight;
            RowStyle bottomRow = _rootLayout.RowStyles[3];
            if (Math.Abs(bottomRow.Height - bottomHeight) > 0.5f) bottomRow.Height = bottomHeight;
            _rootLayout.PerformLayout();
            UpdateMinimumHeight();
        }));
    }

    private void UpdateMinimumHeight()
    {
        if (_updatingMin || !IsHandleCreated || _toolBar is null || _settingsBar is null) return;
        _updatingMin = true;
        try
        {
            int required = (MainMenuStrip?.Height ?? 0)
                           + _toolBar.Height
                           + _settingsBar.Height + LineHeight() * 2 + 4
                           + (_statusStrip?.Height ?? 0)
                           + 130;
            int nonClient = Height - ClientSize.Height;
            int minHeight = Math.Max(520, required + nonClient);
            if (MinimumSize.Height != minHeight)
            {
                MinimumSize = new Size(MinimumSize.Width, minHeight);
                if (Height < minHeight) Height = minHeight;
            }
        }
        finally
        {
            _updatingMin = false;
        }
    }

    /// <summary>定时/时间戳/校验的附属输入只在对应开关打开时显示，默认保持下栏紧凑。</summary>
    private void UpdateDependentVisibility()
    {
        if (_timerValueGroup is null) return;
        bool verifying = _verifyBox.SelectedIndex > 0;
        SetVisible(_timerValueGroup, _timerSendBox.Checked);
        SetVisible(_timeoutGroup, _timestampBox.Checked);
        SetVisible(_verifyIndexGroup, verifying);
        SetVisible(_crcBox, verifying);
    }

    private static void SetVisible(Control control, bool visible)
    {
        if (control.Visible != visible) control.Visible = visible;
    }

    private void UpdateBottomBandHeight()
    {
        if (_rootLayout is null || _settingsBar is null) return;
        int settingsHeight = Math.Max(LineHeight() * 2, _settingsBar.Height);
        int lowerHeight = LineHeight() * 2 + 4;
        int h = settingsHeight + lowerHeight;
        RowStyle style = _rootLayout.RowStyles[3];
        if (Math.Abs(style.Height - h) > 0.5f) style.Height = h;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };

        var comm = new ToolStripMenuItem("通讯端口");
        comm.DropDownItems.Add(new ToolStripMenuItem("刷新串口列表", null, (_, _) => RefreshPortList()));
        comm.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem Protocol(string name)      // 协议项：勾自己、写下拉文本、重探模式
        {
            var item = new ToolStripMenuItem(name);
            item.Click += (_, _) =>
            {
                foreach (var it in comm.DropDownItems.OfType<ToolStripMenuItem>()) it.Checked = false;
                _portCombo.Text = name;
                item.Checked = true;
                DetectMode();
            };
            return item;
        }
        _protocolItems = [Protocol("TCPClient"), Protocol("TCPServer"), Protocol("UDP")];
        foreach (var item in _protocolItems) comm.DropDownItems.Add(item);
        comm.DropDownItems.Add(new ToolStripSeparator());
        _commMenu = comm;

        var portCfg = new ToolStripMenuItem("串口设置");
        portCfg.DropDownItems.Add(new ToolStripMenuItem("更多串口设置", null, (_, _) => ShowPortSettings()));
        _openImmediatelyItem = new ToolStripMenuItem("启动时立即打开串口") { CheckOnClick = true };
        portCfg.DropDownItems.Add(_openImmediatelyItem);

        var display = new ToolStripMenuItem("显示");
        display.DropDownItems.Add(new ToolStripMenuItem("背景颜色", null, (_, _) => PickRecvBackColor()));
        var limitMenu = new ToolStripMenuItem("显示缓冲上限");
        AddLimitItem(limitMenu, "200K", 200_000);
        AddLimitItem(limitMenu, "500K", 500_000);
        AddLimitItem(limitMenu, "1M", 1_000_000);
        AddLimitItem(limitMenu, "2M", 2_000_000);
        AddLimitItem(limitMenu, "5M", 5_000_000);
        _limitItems[2].Checked = true;                            // 默认 1M
        display.DropDownItems.Add(limitMenu);

        var send = new ToolStripMenuItem("发送");
        var sendFile = new ToolStripMenuItem("发送文件设置");
        string[] sendFileTexts = { "连续发送", "每发256字节延时1毫秒", "每发256字节延时10毫秒", "每发256字节延时50毫秒", "每发256字节延时100毫秒" };
        int[] sendFileMs = { 0, 1, 10, 50, 100 };
        for (int i = 0; i < sendFileTexts.Length; i++)
        {
            int ms = sendFileMs[i];
            var item = new ToolStripMenuItem(sendFileTexts[i]) { Tag = ms };   // 单选：点一个勾一个
            item.Click += (_, _) =>
            {
                _sendFileDelay = ms;
                foreach (var it in _sendFileItems) it.Checked = ReferenceEquals(it, item);
                AutoSave();
            };
            if (ms == 1) { item.Checked = true; _sendFileDelay = 1; }          // 资源默认勾选
            _sendFileItems.Add(item);
            sendFile.DropDownItems.Add(item);
        }
        send.DropDownItems.Add(sendFile);
        var term = new ToolStripMenuItem("终端仿真设置");
        _termEnterItem = new ToolStripMenuItem("回车后发送输入行(带回显)");
        _termKeyItem = new ToolStripMenuItem("按键立即发送该键值");
        _termEchoItem = new ToolStripMenuItem("(按键即发送)本地回显");
        _termAddLfItem = new ToolStripMenuItem("发送回车0x0d后自动加上换行符0x0a");
        _termEnterItem.Click += (_, _) =>
        {
            var next = CurrentTermSwitches().ToggleEnter();
            ApplyTermSwitches(next);
            AutoSave();
        };
        _termKeyItem.Click += (_, _) =>
        {
            var next = CurrentTermSwitches().ToggleKey();
            ApplyTermSwitches(next);
            AutoSave();
        };
        _termEchoItem.Click += (_, _) =>
        {
            var next = CurrentTermSwitches().ToggleEcho();
            ApplyTermSwitches(next);
            AutoSave();
        };
        _termAddLfItem.Click += (_, _) =>
        {
            var next = CurrentTermSwitches().ToggleAddLf();
            ApplyTermSwitches(next);
            AutoSave();
        };
        term.DropDownItems.AddRange(new ToolStripItem[] { _termEnterItem, _termKeyItem, _termEchoItem, _termAddLfItem });
        send.DropDownItems.Add(term);

        var multi = new ToolStripMenuItem("多条字符串", null, (_, _) => ToggleMultiPanel());
        var help = new ToolStripMenuItem("帮助");
        help.DropDownItems.Add(new ToolStripMenuItem("关于", null, (_, _) =>
            MessageBox.Show(this, "SSCOM 重写版（C# / .NET Framework 4.8）\n依据 sscom5.13.1-rewrite-spec.md 的行为规则实现。", "关于",
                MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new ToolStripItem[] { comm, portCfg, display, send, multi, help });
        return menu;
    }

    private void AddLimitItem(ToolStripMenuItem parent, string text, int value)
    {
        var item = new ToolStripMenuItem(text) { Tag = value };
        item.Click += (_, _) =>
        {
            _displayLimit = value;
            _receive.Limit = value;
            foreach (var it in _limitItems) it.Checked = ReferenceEquals(it, item);
        };
        _limitItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    /// <summary>底部一条：左边端口/网络，右边两行选项和发送框。高度固定，不换行拆字。</summary>
    private Control BuildBottomBand()
    {
        var band = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(2, 0, 2, 0) };

        _serialBar = BuildSerialBar();
        _netBar = BuildNetBar();
        _netBar.Visible = false;

        var lower = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        _lowerBand = lower;
        lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        left.Controls.Add(_serialBar);
        left.Controls.Add(_netBar);
        _sendArea = BuildSendArea();
        lower.Controls.Add(left, 0, 0);
        lower.Controls.Add(_sendArea, 1, 0);
        UpdateLeftColumnWidth();

        _settingsBar = BuildSettingsBar();
        _settingsBar.Dock = DockStyle.Top;
        _settingsBar.SizeChanged += (_, _) => { UpdateBottomBandHeight(); ScheduleResponsiveRelayout(); };

        band.Controls.Add(lower);
        band.Controls.Add(_settingsBar);
        return band;
    }

    /// <summary>
    /// 左栏按当前可见内容测宽。窗体是 PerMonitorV2，但布局按物理像素写死；
    /// 125%/150% 缩放下字体变宽，固定 248px 会把“波特率”下拉裁掉。
    /// </summary>
    private void UpdateLeftColumnWidth()
    {
        if (_lowerBand is null) return;
        Control bar = _netMode == NetMode.Serial ? _serialBar : _netBar;
        int need = MaxRowWidth(bar) + 6;   // 与右侧发送区留 6px 间隔（left 的右边距）
        _lowerBand.ColumnStyles[0].Width = Math.Max(236, need);
    }

    private static int FlowContentWidth(FlowLayoutPanel row, bool onlyVisible = true)
    {
        int w = row.Padding.Horizontal;
        foreach (Control c in row.Controls)
            if (!onlyVisible || c.Visible)
                w += c.Width + c.Margin.Horizontal;
        return w;
    }

    private static int MaxRowWidth(Control? root, bool onlyVisible = true)
    {
        if (root is null) return 0;
        int max = 0;
        foreach (Control child in root.Controls)
            if (child is FlowLayoutPanel row)
                max = Math.Max(max, FlowContentWidth(row, onlyVisible));
        return max;
    }

    private Control BuildSerialBar()
    {
        int unit = TextUnit();
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _portCombo = new ComboBox { Width = unit * 10, DropDownStyle = ComboBoxStyle.DropDown };
        _openButton = new Button { Text = "打开串口", AutoSize = true };
        _baudCombo = new ComboBox { Width = unit * 7, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var b in ComPortService.BaudItems) _baudCombo.Items.Add(b);
        _baudCombo.SelectedIndex = 13; // 115200
        _customBaudBox = new TextBox { Width = unit * 6, Text = _port.CustomBaud.ToString(), Visible = false };
        _customBaudBox.VisibleChanged += (_, _) => UpdateLeftColumnWidth();
        _rtsBox = new CheckBox { Text = "RTS", AutoSize = true };
        _dtrBox = new CheckBox { Text = "DTR", AutoSize = true };
        _moreSettingsButton = new Button { Text = "更多串口设置", AutoSize = true };
        _moreSettingsButton.Click += (_, _) => ShowPortSettings();

        var row1 = MakeLine();
        Put(row1, new Label { Text = "端口号", AutoSize = true });
        Put(row1, _portCombo);
        var refresh = new Button { Text = "↻", AutoSize = true };
        refresh.Click += (_, _) => RefreshPortList();
        _rowTip.SetToolTip(refresh, "刷新串口列表");
        Put(row1, refresh);
        var row2 = MakeLine();
        Put(row2, _openButton);
        Put(row2, _moreSettingsButton);
        Put(row2, _rtsBox);
        Put(row2, _dtrBox);
        Put(row2, new Label { Text = "波特率", AutoSize = true });
        Put(row2, _baudCombo);
        Put(row2, _customBaudBox);
        grid.Controls.Add(row1, 0, 0);
        grid.Controls.Add(row2, 0, 1);
        return grid;
    }

    /// <summary>网络设置栏（对应 PanelNet）：远程IP/端口、本地IP/端口、连接、断开。</summary>
    private Control BuildNetBar()
    {
        int unit = TextUnit();
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Label Lb(string text) => new() { Text = text, AutoSize = true };

        _remoteIpBox = new TextBox { Width = unit * 12 };
        _remotePortBox = new TextBox { Width = unit * 6, Text = "8080" };
        _localIpCombo = new ComboBox { Width = unit * 12, DropDownStyle = ComboBoxStyle.DropDown };
        _localPortBox = new TextBox { Width = unit * 6, Text = "8080" };
        _connectButton = new Button { Text = "连接", AutoSize = true };
        _disconnectButton = new Button { Text = "断开", AutoSize = true };
        _connectButton.Click += (_, _) => ConnectNetwork();
        _disconnectButton.Click += (_, _) => DisconnectNetwork();

        var row1 = MakeLine();
        Put(row1, Lb("远程"));
        Put(row1, _remoteIpBox);
        Put(row1, Lb("端口"));
        Put(row1, _remotePortBox);
        var row2 = MakeLine();
        Put(row2, Lb("本地"));
        Put(row2, _localIpCombo);
        Put(row2, Lb("端口"));
        Put(row2, _localPortBox);
        Put(row2, _connectButton);
        Put(row2, _disconnectButton);
        grid.Controls.Add(row1, 0, 0);
        grid.Controls.Add(row2, 0, 1);
        return grid;
    }

    private Control BuildToolBar()
    {
        var panel = MakeWrapLine();

        Button Make(string text, Action? onClick = null)
        {
            var b = new Button { Text = text, AutoSize = true };
            if (onClick != null) b.Click += (_, _) => onClick();
            return b;
        }

        Put(panel, Make("清除窗口", ClearReceive));
        Put(panel, Make("打开文件", OpenFile));
        _filePathBox = new TextBox { Width = TextUnit() * 12, ReadOnly = true, BackColor = SystemColors.Window };
        Put(panel, _filePathBox);
        _sendFileButton = Make("发送文件", SendFile);
        Put(panel, _sendFileButton);
        Put(panel, Make("停止", StopSendFile));
        Put(panel, Make("清发送区", () => _sendBox.Clear()));
        _topmostBox = new CheckBox { Text = "最前", AutoSize = true };
        Put(panel, _topmostBox);
        Put(panel, Make("保存参数", SaveConfig));
        _hideToggleButton = Make("扩展", ToggleMultiPanel);
        _collapseButton = Make("━", ToggleBottomPanels);
        Put(panel, _hideToggleButton);
        Put(panel, _collapseButton);
        return panel;
    }

    private Control BuildSettingsBar()
    {
        // 两行紧凑选项；窗口变窄时各行内部自动换行，面板高度由两行实际高度决定。
        var host = new Panel { Dock = DockStyle.Top, Margin = new Padding(0), Width = 1200 };
        var line1 = MakeWrapLine();
        var line2 = MakeWrapLine();

        CheckBox Cb(string text, bool @checked = false) =>
            new() { Text = text, AutoSize = true, Checked = @checked };
        Label Lb(string text) => new() { Text = text, AutoSize = true };

        // 接收设置（保存数据/接收数据到文件/加时间戳按规格接上）
        int unit = TextUnit();
        _hexDisplayBox = Cb("HEX显示");
        Put(line1, _hexDisplayBox);
        var saveBtn = new Button { Text = "保存数据", AutoSize = true };
        saveBtn.Click += (_, _) => SaveData();
        Put(line1, saveBtn);
        _recvToFileBox = Cb("接收数据到文件");
        Put(line1, _recvToFileBox);
        _hexSendBox = Cb("HEX发送");
        Put(line1, _hexSendBox);
        _timerSendBox = Cb("定时发送:");
        Put(line1, _timerSendBox);
        _timerIntervalEdit = new TextBox { Text = "1000", Width = unit * 4 };
        _timerValueGroup = Group(_timerIntervalEdit, Lb("ms/次"));
        Put(line1, _timerValueGroup);
        _newLineBox = Cb("加回车换行");
        Put(line1, _newLineBox);

        _timestampBox = Cb("加时间戳和分包显示", @checked: true);
        Put(line2, _timestampBox);
        _timeoutLabel = Lb("超时时间:");
        _timeoutEdit = new TextBox { Text = "20", Width = unit * 4 };
        _timeoutGroup = Group(_timeoutLabel, _timeoutEdit, Lb("ms"));
        Put(line2, _timeoutGroup);
        _verifyNumEdit = new TextBox { Text = "1", Width = unit * 3 };
        _tailCombo = new ComboBox { Width = unit * 14, DropDownWidth = unit * 42, DropDownStyle = ComboBoxStyle.DropDownList };
        _tailCombo.Items.AddRange(new object[]
        {
            "末尾 (无帧尾字节)",
            "第-1 (最后1个字节是帧尾,校验插在帧尾前面)",
            "第-2 (最后2个字节是帧尾,校验插在帧尾前面)",
            "第-3 (最后3个字节是帧尾,校验插在帧尾前面)",
            "第-4 (最后4个字节是帧尾,校验插在帧尾前面)",
        });
        _tailCombo.SelectedIndex = 0;
        _verifyIndexGroup = Group(Lb("第"), _verifyNumEdit, Lb("字节至"), _tailCombo);
        Put(line2, _verifyIndexGroup);
        _verifyBox = new ComboBox { Width = unit * 12, DropDownStyle = ComboBoxStyle.DropDownList };
        _verifyBox.Items.AddRange(new object[] { "None", "ModbusCRC16", "CCITT-CRC16", "CRC32", "0-ADD8", "ADD8", "XOR8", "ADD16" });
        _verifyBox.SelectedIndex = 0;
        _crcBox = new TextBox { Width = unit * 8, ReadOnly = true, TabStop = false };
        Put(line2, Group(Lb("加校验"), _verifyBox, _crcBox));

        host.Controls.Add(line2);
        host.Controls.Add(line1);
        void SyncHeight()
        {
            int h = Math.Max(LineHeight(), line1.Height) + Math.Max(LineHeight(), line2.Height);
            if (host.Height != h) host.Height = h;
        }
        line1.SizeChanged += (_, _) => SyncHeight();
        line2.SizeChanged += (_, _) => SyncHeight();
        SyncHeight();
        return host;
    }

    /// <summary>单行，不换行。控件保持自身宽度，避免把「加时间戳」拆成两行。</summary>
    private FlowLayoutPanel MakeLine() => new RowFlowPanel
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        AutoSize = false,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(2, 1, 2, 0),
    };

    /// <summary>可换行的一行：宽度不够时自动折到下一行，高度随内容增长。</summary>
    private FlowLayoutPanel MakeWrapLine() => new WrapRowPanel
    {
        Dock = DockStyle.Top,
        WrapContents = true,
        AutoSize = false,
        Height = LineHeight(),
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(2, 1, 2, 1),
        Width = 1200,   // 构造期先按宽窗口排一次，避免在默认 200px 宽度下反复折行
    };

    /// <summary>不换行行内的垂直居中（固定行高区域用）。</summary>
    private sealed class RowFlowPanel : FlowLayoutPanel
    {
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int h = ClientSize.Height - Padding.Vertical;
            foreach (Control c in Controls)
            {
                int y = Padding.Top + Math.Max(0, (h - c.Height) / 2);
                if (c.Top != y) c.Top = y;
            }
        }
    }

    /// <summary>
    /// 可换行行：宽度固定为父容器宽度（AutoSize=false），按首选高度自动增高，
    /// 再按“视觉行”垂直居中。AutoSize=true 会让宽度保持内容宽度而不换行，导致右侧被裁。
    /// </summary>
    private sealed class WrapRowPanel : FlowLayoutPanel
    {
        private bool _adjusting;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (_adjusting)
            {
                base.OnLayout(levent);
                return;
            }
            _adjusting = true;
            try
            {
                base.OnLayout(levent);
                if (ClientSize.Width > 0)
                {
                    int desired = GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
                    int min = Math.Max(28, Font.Height + 14);
                    if (desired < min) desired = min;
                    if (Height != desired) Height = desired;
                }
                CenterLines();
            }
            finally
            {
                _adjusting = false;
            }
        }

        private void CenterLines()
        {
            int count = Controls.Count;
            int start = 0;
            while (start < count)
            {
                int end = start + 1;
                int prevLeft = Controls[start].Left;
                while (end < count && Controls[end].Left > prevLeft)
                {
                    prevLeft = Controls[end].Left;
                    end++;
                }
                int top = int.MaxValue, bottom = int.MinValue;
                for (int i = start; i < end; i++)
                {
                    Control c = Controls[i];
                    top = Math.Min(top, c.Top - c.Margin.Top);
                    bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
                }
                int rowHeight = Math.Max(1, bottom - top);
                for (int i = start; i < end; i++)
                {
                    Control c = Controls[i];
                    int available = rowHeight - c.Margin.Vertical;
                    int y = top + c.Margin.Top + Math.Max(0, (available - c.Height) / 2);
                    if (c.Top != y) c.Top = y;
                }
                start = end;
            }
        }
    }

    private static void Put(FlowLayoutPanel line, Control control)
    {
        control.Margin = new Padding(3, 2, 2, 2);
        if (control is Label or CheckBox)
            control.AutoSize = true;
        if (control is Button button)
        {
            button.AutoSize = true;
            button.Margin = new Padding(2, 1, 2, 1);
        }
        line.Controls.Add(control);
    }

    /// <summary>把“标签+输入+单位”绑成一组，折行时不会被拆散。</summary>
    private static FlowLayoutPanel Group(params Control[] controls)
    {
        var group = new RowFlowPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        foreach (Control control in controls)
        {
            control.Margin = new Padding(3, 2, 2, 2);
            if (control is Button button)
                button.Margin = new Padding(2, 1, 2, 1);
            group.Controls.Add(control);
        }
        return group;
    }

    private int TextUnit() => Math.Max(7, TextRenderer.MeasureText("0", Font).Width);

    private Control BuildCenter()
    {
        _recvBox = new TextBox
        {
            Multiline = true,
            ReadOnly = false,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
        };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        _split.Panel1.Controls.Add(_recvBox);
        _split.Panel2.Controls.Add(BuildMultiPanel());
        _split.Panel2Collapsed = true;   // 规格：多条面板初始隐藏
        _split.Resize += (_, _) =>
        {
            if (!_split.Panel2Collapsed) ApplyMultiWidth();
        };
        _split.SplitterMoved += (_, _) =>
        {
            if (!_split.Panel2Collapsed && !_adjustingSplit) _multiPanelWidth = _split.Panel2.Width;
        };
        return _split;
    }

    /// <summary>多条字符串面板（规格「多条字符串」）：顶部循环发送开关 + 99 行列区（HEX/数据/点击发送/顺序/延时）。</summary>
    private Control BuildMultiPanel()
    {
        _multiPanel = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control, Width = Math.Max(400, _multiPanelWidth) };

        // 顶部条：循环发送、调宽按钮、（导入ini 待 M8）
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2), WrapContents = false };
        _loopSendBox = new CheckBox { Text = "循环发送", AutoSize = true, Padding = new Padding(2, 5, 6, 0) };
        top.Controls.Add(_loopSendBox);
        var minus = new Button { Text = "−", Width = 26, Height = 22, Margin = new Padding(2, 1, 0, 0) };
        minus.Click += (_, _) => AdjustMultiButtonWidth(-10);
        var plus = new Button { Text = "＋", Width = 26, Height = 22, Margin = new Padding(2, 1, 0, 0) };
        plus.Click += (_, _) => AdjustMultiButtonWidth(+10);
        top.Controls.Add(new Label { Text = "点击发送按钮宽度:", AutoSize = true, Padding = new Padding(6, 7, 0, 0) });
        top.Controls.Add(minus);
        top.Controls.Add(plus);
        _importIniButton = new Button { Text = "导入ini", Width = 64, Height = 22, Margin = new Padding(8, 1, 0, 0) };
        _importIniButton.Click += (_, _) => ImportIni();
        top.Controls.Add(_importIniButton);

        // 行表格：0 行是表头，1..99 是数据行。自绘布局，一次定位全部控件，避免 TableLayoutPanel 逐行重排。
        int rowHeight = Math.Max(24, LineHeight() - 2);
        int hexCol = Math.Max(36, TextRenderer.MeasureText("HEX", Font).Width + 16);
        int orderCol = Math.Max(48, TextRenderer.MeasureText("顺序", Font).Width + 12);
        int delayCol = Math.Max(64, TextRenderer.MeasureText("延时ms", Font).Width + 12);
        int sendCol = Math.Max(_multiBtnWidth, TextRenderer.MeasureText("点击发送", Font).Width + 16);
        _multiRows = new MultiRowsPanel
        {
            Dock = DockStyle.Top,
            Height = rowHeight * 100,
            RowHeight = rowHeight,
            HexColumnWidth = hexCol,
            SendColumnWidth = sendCol,
            OrderColumnWidth = orderCol,
            DelayColumnWidth = delayCol,
            MinimumSize = new Size(hexCol + sendCol + orderCol + delayCol + 80, rowHeight * 100),
        };
        _multiRows.SuspendLayout();
        void AddHeader(string text, int col)
        {
            var label = HeaderLabel(text);
            label.Tag = new Point(0, col);
            _multiRows.Controls.Add(label);
        }
        AddHeader("HEX", 0);
        AddHeader("字符串(双击注释)", 1);
        AddHeader("点击发送", 2);
        AddHeader("顺序", 3);
        AddHeader("延时ms", 4);

        for (int k = 0; k < 99; k++)
        {
            int row = k;                                   // 闭包捕获
            var cb = new CheckBox { Checked = false, AutoSize = false, Width = 26, Height = 20, Tag = new Point(row + 1, 0) };
            cb.MouseUp += (_, _) => AutoSave();            // 规格：行 HEX 复选框 MouseUp 自动保存
            _rowHex[row] = cb;
            _multiRows.Controls.Add(cb);

            var data = new TextBox { Tag = new Point(row + 1, 1) };
            data.KeyPress += (_, e) =>
            {
                if (e.KeyChar == (char)1) { data.SelectAll(); e.Handled = true; }   // Ctrl+A 全选
                else AutoSave();                                                     // 其余键自动保存
            };
            data.DoubleClick += (_, _) => EditRowComment(row);
            _rowData[row] = data;
            _multiRows.Controls.Add(data);

            var send = new Button { Text = (row + 1).ToString(), Tag = new Point(row + 1, 2) };
            send.Click += (_, _) => SendRow(row);
            _rowSend[row] = send;
            _multiRows.Controls.Add(send);

            var order = new TextBox { Text = "0", Tag = new Point(row + 1, 3) };
            _rowTip.SetToolTip(order, "自动发送时按顺序号从小到大,0不发送");
            order.KeyUp += (_, _) =>
            {
                var (t, c) = InputNormalizer.NormalizeInt(order.Text, order.SelectionStart, 0, 0, 99);
                if (t != order.Text)
                {
                    order.Text = t;                       // 仅变化时写回（避免光标被重置导致多位数输入颠倒）
                    order.SelectionStart = c;
                }
                AutoSave();
            };
            _rowOrder[row] = order;
            _multiRows.Controls.Add(order);

            var time = new TextBox { Text = "1000", Tag = new Point(row + 1, 4) };
            time.KeyUp += (_, _) =>
            {
                var (t, c) = InputNormalizer.NormalizeInt(time.Text, time.SelectionStart, 1000, 1, 99999);
                if (t != time.Text)
                {
                    time.Text = t;                        // 仅变化时写回（同样为保住光标）
                    time.SelectionStart = c;
                }
                AutoSave();
            };
            _rowTime[row] = time;
            _multiRows.Controls.Add(time);

            _rowComments[row] = (row + 1).ToString();      // 注释默认行号（按钮面板与提示同源）
            _rowTip.SetToolTip(data, _rowComments[row]);
        }

        var scroll = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Window };
        scroll.Controls.Add(_multiRows);
        _multiRows.ResumeLayout(false);

        _multiPanel.Controls.Add(scroll);
        _multiPanel.Controls.Add(top);
        return _multiPanel;
    }

    private static Label HeaderLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(2, 3, 0, 3),
    };

    /// <summary>滚动区双缓冲，减少多行面板展开/滚动时的闪烁。</summary>
    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() => DoubleBuffered = true;
    }

    /// <summary>
    /// 99 行 × 5 列的轻量容器：按固定行高和列宽一次性定位控件，
    /// 避免 TableLayoutPanel 在展开时对 495 个控件反复测量/重排。
    /// </summary>
    private sealed class MultiRowsPanel : Panel
    {
        public int RowHeight { get; set; } = 27;
        public int HexColumnWidth { get; set; } = 40;
        public int SendColumnWidth { get; set; } = 120;
        public int OrderColumnWidth { get; set; } = 56;
        public int DelayColumnWidth { get; set; } = 72;

        public MultiRowsPanel() => DoubleBuffered = true;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int width = ClientSize.Width;
            int dataWidth = Math.Max(80, width - HexColumnWidth - SendColumnWidth - OrderColumnWidth - DelayColumnWidth);
            int xData = HexColumnWidth;
            int xSend = xData + dataWidth;
            int xOrder = xSend + SendColumnWidth;
            int xDelay = xOrder + OrderColumnWidth;

            foreach (Control c in Controls)
            {
                if (c.Tag is not Point cell) continue;
                int row = cell.X, col = cell.Y;
                int x = col switch { 0 => 0, 1 => xData, 2 => xSend, 3 => xOrder, _ => xDelay };
                int colWidth = col switch { 0 => HexColumnWidth, 1 => dataWidth, 2 => SendColumnWidth, 3 => OrderColumnWidth, _ => DelayColumnWidth };
                int y = row * RowHeight;
                if (c is Label)
                {
                    c.Location = new Point(x + 2, y + Math.Max(0, (RowHeight - c.Height) / 2));
                    continue;
                }
                if (col == 0 && c is CheckBox)
                {
                    int boxWidth = Math.Min(c.Width, Math.Max(1, colWidth - 2));
                    c.SetBounds(x + (colWidth - boxWidth) / 2,
                        y + Math.Max(0, (RowHeight - c.Height) / 2), boxWidth, c.Height);
                    continue;
                }
                int w = Math.Max(1, colWidth - 2);
                int h = c.Height;
                c.SetBounds(x + 1, y + Math.Max(0, (RowHeight - h) / 2), w, h);
            }
        }
    }

    private Control BuildSendArea()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        _sendBox = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Text = "abcdefg",            // 规格：发送框默认 ASC 内容
        };
        _sendButton = new Button
        {
            Text = "发  送",
            Dock = DockStyle.Right,
            Width = Math.Max(72, TextRenderer.MeasureText("发送", Font).Width + 28),
        };
        host.Controls.Add(_sendBox);
        host.Controls.Add(_sendButton);
        return host;
    }

    private StatusStrip BuildStatusBar()
    {
        var strip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusStrip = strip;
        _connStatus = new ToolStripStatusLabel(" 已关闭 ") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _sentStatus = new ToolStripStatusLabel("S:0") { AutoSize = true };
        _recvStatus = new ToolStripStatusLabel("R:0") { AutoSize = true };
        _modemStatus = new ToolStripStatusLabel("CTS=- DSR=- RLSD=-") { AutoSize = true, Margin = new Padding(0, 3, 8, 2) };
        strip.Items.AddRange(new ToolStripItem[] { _connStatus, _sentStatus, _recvStatus, _modemStatus });
        return strip;
    }

    // ———————————————————————— 行为 ————————————————————————

    private void WireEvents()
    {
        _openButton.Click += (_, _) => ToggleOpen();
        _sendButton.Click += (_, _) => SendCurrent();
        _recvBox.KeyPress += OnRecvKeyPress;
        _customBaudBox.Leave += (_, _) => CommitCustomBaud(notify: true);
        _baudCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_baudCombo.SelectedIndex >= 0)
                _port.BaudIndex = _baudCombo.SelectedIndex;
            _customBaudBox.Visible = _baudCombo.SelectedIndex <= 0;
            if (!_loading) UpdateStatus();
        };
        _hexSendBox.CheckedChanged += (_, _) => OnHexSendModeChanged();
        _timerSendBox.CheckedChanged += (_, _) => UpdateDependentVisibility();
        _timestampBox.CheckedChanged += (_, _) => UpdateDependentVisibility();
        _verifyBox.SelectedIndexChanged += (_, _) => UpdateDependentVisibility();
        _filePathBox.TextChanged += (_, _) => _rowTip.SetToolTip(_filePathBox, _filePathBox.Text);
        _sendBox.TextChanged += (_, _) => RememberSendText();
        _rtsBox.Click += (_, _) => ApplyModemLine(_rtsBox, v => _port.ApplyRts(v), "RTS");
        _dtrBox.Click += (_, _) => ApplyModemLine(_dtrBox, v => _port.ApplyDtr(v), "DTR");
        _topmostBox.CheckedChanged += (_, _) => TopMost = _topmostBox.Checked; // 规格：勾选 HWND_TOPMOST，取消 HWND_NOTOPMOST
        _recvToFileBox.CheckedChanged += (_, _) => OnRecvToFileChanged();
        _hexDisplayBox.Click += (_, _) => OnHexDisplayClicked();
        // 快照在界面线程更新（订阅顺序在业务处理之后，取消警告时翻回也能反映）
        _hexDisplayBox.CheckedChanged += (_, _) => RefreshRecvSnapshot();
        _timestampBox.CheckedChanged += (_, _) => RefreshRecvSnapshot();
        _recvToFileBox.CheckedChanged += (_, _) => RefreshRecvSnapshot();
        _timeoutEdit.TextChanged += (_, _) => RefreshRecvSnapshot();
        _portCombo.TextChanged += (_, _) => DetectMode();     // 下拉文本变化 → 重探模式（面板/定时器联动）
        _net.ReadyChanged += () => Post(UpdateStatus);
        _net.DataReceived += OnNetData;
        _net.RemoteIpLearned += ip => Post(() =>
        {
            if (!_remoteIpBox.Focused) _remoteIpBox.Text = ip;   // 规格：UDP 收到数据写回“远程IP”
        });
        _net.ErrorRaised += msg => Post(() =>
            MessageBox.Show(this, msg, "网络", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        _port.DataReceived += OnPortData;
        _port.ErrorRaised += (en, cn) => Post(() =>
            MessageBox.Show(this, cn, "串口", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        _timerSendBox.CheckedChanged += (_, _) => OnTimerSendChanged();
        _sendStringTimer.Tick += (_, _) => OnSendTimerTick();
        _loopSendBox.CheckedChanged += (_, _) => OnLoopSendChanged();
        _multiTimer.Tick += (_, _) => LoopSendTick();
        _statusTimer.Tick += (_, _) => StatusTick();
        _statusTimer.Start();
        _displayTimer.Tick += (_, _) => DrainDisplay();
        _prewarmTimer.Tick += (_, _) => PrewarmTick();
        FormClosing += OnMainFormClosing;
        FormClosed += (_, _) => _port.Dispose();
    }

    private void OnPortData(byte[] data) => HandleIncoming(data);
    private void OnNetData(byte[] data) => HandleIncoming(data);

    /// <summary>串口与三种网络共用的接收管线（对应 displayCOMdata 的显示部分）。</summary>
    private void HandleIncoming(byte[] data)
    {
        // 接收数据到文件（规格：勾选时每段数据都打开目标文件追加写出再关闭，所有模式都写）
        if (_snapRecvToFile && _recvFileName.Length > 0)
            AppendRecvFile(_recvFileName, data);

        Interlocked.Add(ref _recvCount, data.Length);

        // 只用快照字段，不在接收线程里读控件
        string segment = _receive.Process(data, _snapHexDisplay, _snapTimestamp,
            _snapTimeoutMs, out string? memoReplace);
        EnqueueDisplay(segment, memoReplace);
    }

    /// <summary>把显示段投进待显示队列；同一轮只安排一次界面刷新，多段合并成一次追加。</summary>
    private void EnqueueDisplay(string segment, string? memoReplace)
    {
        bool needSchedule;
        lock (_displayLock)
        {
            if (memoReplace is not null)
            {
                _pendingReplace = memoReplace;      // 截断重画优先，丢弃已积压的增量
                _pendingDisplay.Clear();
            }
            else
            {
                _pendingDisplay.Append(segment);
            }
            needSchedule = !_drainScheduled;
            _drainScheduled = true;
        }
        if (needSchedule)
            Post(StartDisplayFlush);
    }

    private void StartDisplayFlush()
    {
        if (!_displayTimer.Enabled)
            _displayTimer.Start();
    }

    /// <summary>最多 30ms 合并刷新一次，避免高频小段逐条重绘造成跳动。</summary>
    private void DrainDisplay()
    {
        string text;
        string? replace;
        lock (_displayLock)
        {
            text = _pendingDisplay.ToString();
            _pendingDisplay.Clear();
            replace = _pendingReplace;
            _pendingReplace = null;
        }
        if (replace is not null)
        {
            _recvBox.Text = replace;                // 截断后的整屏重画
            _recvBox.SelectionStart = _recvBox.TextLength;
        }
        else if (text.Length > 0)
        {
            AppendRecv(text);
        }
        lock (_displayLock)
        {
            if (_pendingDisplay.Length == 0 && _pendingReplace is null)
            {
                _drainScheduled = false;
                _displayTimer.Stop();
            }
        }
        UpdateStatus();
    }

    /// <summary>
    /// 追加接收内容：用户在底部时跟随滚动；向上翻看历史或正在输入时保持原光标/滚动位置，
    /// 不再像 AppendText 后强制 SelectionStart 那样把视图拽回末尾。
    /// </summary>
    private void AppendRecv(string text)
    {
        if (text.Length == 0) return;
        if (!_recvBox.IsHandleCreated)
        {
            _recvBox.AppendText(text);
            return;
        }

        bool atBottom = RecvAtBottom();
        int selStart = _recvBox.SelectionStart;
        int selLen = _recvBox.SelectionLength;
        Point scroll = GetRecvScroll();

        _recvBox.AppendText(text);

        if (atBottom)
        {
            _recvBox.SelectionStart = _recvBox.TextLength;
            _recvBox.SelectionLength = 0;
        }
        else
        {
            int start = Math.Min(selStart, _recvBox.TextLength);
            int len = Math.Min(selLen, Math.Max(0, _recvBox.TextLength - start));
            _recvBox.Select(start, len);
            SetRecvScroll(scroll);
        }
    }

    private bool RecvAtBottom()
    {
        var si = new SCROLLINFO
        {
            cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)),
            fMask = SIF_ALL,
        };
        if (!GetScrollInfo(_recvBox.Handle, SB_VERT, ref si))
            return true;
        int max = Math.Max(si.nMin, si.nMax - si.nPage + 1);
        return si.nPos >= max;
    }

    private Point GetRecvScroll()
    {
        var p = Point.Empty;
        if (_recvBox.IsHandleCreated)
            SendMessage(_recvBox.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p);
        return p;
    }

    private void SetRecvScroll(Point p)
    {
        if (_recvBox.IsHandleCreated)
            SendMessage(_recvBox.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p);
    }

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO si);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin, nMax, nPage, nPos, nTrackPos;
    }

    private const int SB_VERT = 1;
    private const uint SIF_ALL = 0x17;
    private const int EM_GETSCROLLPOS = 0x04DD;
    private const int EM_SETSCROLLPOS = 0x04DE;

    /// <summary>在界面线程刷新接收参数快照（串口线程只读这些普通字段）。</summary>
    private void RefreshRecvSnapshot()
    {
        _snapHexDisplay = _hexDisplayBox.Checked;
        _snapTimestamp = _timestampBox.Checked;
        _snapRecvToFile = _recvToFileBox.Checked;
        _snapTimeoutMs = RecvTimeoutMs();
    }

    private int RecvTimeoutMs()
    {
        var s = _timeoutEdit?.Text ?? "";
        return int.TryParse(s.Trim(), out var v) ? v : 1;    // 转换失败用 1（规格）
    }

    private void AppendRecvFile(string path, byte[] data)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(data, 0, data.Length);
        }
        catch
        {
            // 原程序打开失败时也只是写不进去，不弹窗
        }
    }

    private void OnRecvToFileChanged()
    {
        if (!_recvToFileBox.Checked)
            return;
        string piece = _port.IsOpen
            ? _port.OpenedPortName
            : "COM";
        string name = Path.Combine(Directory.GetCurrentDirectory(),
            "ReceivedTofile-" + piece + SendComposer.FileTimestamp() + ".DAT");
        _recvFileName = name;
        MessageBox.Show(this,
            "之后接收到的数据都将存到：\r\n" + name,
            "接收数据到文件", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnHexDisplayClicked()
    {
        // 规格：加时间戳勾选且接收框里出现时间戳标记（← 或 →）时先警告，点“否”翻回复选框
        if (_timestampBox.Checked && (_recvBox.Text.Contains('←') || _recvBox.Text.Contains('→')))
        {
            var answer = MessageBox.Show(this,
                "注意！！！！！！！！\r\n已显示的时间戳和发送数据的显示可能会丢失！\r\n时间戳显示将会从新的数据开始。\r\n\r\n按“是”切换显示方式且时间戳丢失，按“取消”返回原状态.",
                "警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                _hexDisplayBox.Checked = !_hexDisplayBox.Checked;
                return;
            }
        }
        // 切换生效：清空接收框后整体重画对应累积
        _recvBox.Text = _hexDisplayBox.Checked ? _receive.HexText : _receive.TextAccum;
        _recvBox.SelectionStart = _recvBox.TextLength;
    }

    private void ClearReceive()
    {
        _recvBox.Clear();
        Interlocked.Exchange(ref _sentCount, 0);   // 规格：清除窗口把两个计数清 0
        Interlocked.Exchange(ref _recvCount, 0);
        _receive.Clear();                     // 三个累积串与半字节状态一并清空
        UpdateStatus();
    }

    /// <summary>保存数据：接收窗口内容另存为 SaveWindows 时间戳 .TXT；原始接收缓冲写成 SAVE 时间戳 .DAT。</summary>
    private void SaveData()
    {
        string dir = Directory.GetCurrentDirectory();      // 规格：当前目录
        string dt = SendComposer.FileTimestamp();
        string datPath = Path.Combine(dir, "SAVE" + dt + ".DAT");
        string txtPath = Path.Combine(dir, "SaveWindows" + dt + ".TXT");

        try
        {
            File.WriteAllBytes(txtPath, SendComposer.EncodeGbk(_recvBox.Text));   // 窗口内容（原程序用 Lines.SaveToFile）
        }
        catch
        {
            // 原程序这一步没有显式处理，失败就跳过
        }

        try
        {
            File.WriteAllBytes(datPath, _receive.RawBytes());
        }
        catch
        {
            MessageBox.Show(this, "当前目录不可写!请拷贝exe文件到且它目录试试.", "保存数据",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(this,
            "收到的原始数据已保存在当前目录!,文件名:\r\n" + datPath
            + "\r\n接收窗口显示的内容保存在:\r\n" + txtPath,
            "保存数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>打开文件：选文件写进路径框，读前 4000 字节做预览（十六进制/文本按 HEX显示）。</summary>
    private void OpenFile()
    {
        using var dlg = new OpenFileDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _filePathBox.Text = dlg.FileName;

        byte[] data;
        long fullSize;
        try
        {
            using var fs = File.OpenRead(dlg.FileName);
            fullSize = fs.Length;
            int n = (int)Math.Min(fullSize, 4000);           // 0xFA0
            data = new byte[n];
            int read = n > 0 ? fs.Read(data, 0, n) : 0;
            if (read < n) Array.Resize(ref data, read);
        }
        catch
        {
            return;                                          // 打不开：静默返回
        }

        // 逐字节：十六进制（两个字符无空格）、文本（0x00 丢弃）
        var hex = new System.Text.StringBuilder(data.Length * 2);
        var text = new System.Text.StringBuilder(data.Length);
        foreach (byte b in data)
        {
            hex.Append(b.ToString("X2"));
            if (b != 0) text.Append((char)b);
        }
        _receive.SetPreviewBuffers(hex.ToString(), text.ToString());

        string header = "文件大小:" + fullSize
                        + "字节,下面是预览的前" + data.Length + "字节内容:\r\n\r\n";
        AppendRecv(header + (_hexDisplayBox.Checked ? hex.ToString() : text.ToString()));
    }

    private void ToggleOpen()
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }
        else
        {
            try
            {
                _port.BaudIndex = _baudCombo.SelectedIndex;
                if (_baudCombo.SelectedIndex <= 0) CommitCustomBaud(notify: true);
                _port.DtrWanted = _dtrBox.Checked;
                _port.RtsWanted = _rtsBox.Checked;
                _port.Open(_portCombo.Text);
                // 打开成功后恢复“发送文件/发送”（RTS/DTR 失败分支曾把它们禁用）
                _sendFileButton.Enabled = true;
                _sendButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开串口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        UpdateStatus();
    }

    private void ApplyModemLine(CheckBox box, Action<bool> apply, string name)
    {
        if (!_port.IsOpen) return;
        try
        {
            apply(box.Checked);
        }
        catch (Exception ex)
        {
            // 规格（DTR/RTS 失败分支）：取消勾选循环发送、停多条定时（M5 接入后补）、置停止标志、
            // 关闭串口、禁用“发送文件”和“发送”、弹错误框、最后取消勾选自己
            _abortFlag = true;
            _port.Close();
            _sendFileButton.Enabled = false;
            _sendButton.Enabled = false;
            MessageBox.Show(this,
                $"Unable to set {name}!!!\r\n1.The USB is loose？\r\n2.The Baudrate is too high？\r\n3.The mainboard COM Should be Lower than 1152...\r\n\r\n{ex.Message}",
                name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            box.Checked = false;
            UpdateStatus();
        }
    }

    // ———————————————————————— M4：发送文件 / 定时发送 / 状态栏联动 ————————————————————————

    /// <summary>停止按钮：置中止标志（0x4ED579）。</summary>
    private void StopSendFile() => _abortFlag = true;

    /// <summary>
    /// 发送文件（规格「发送文件」）：按 256 字节分块发送，块间按延时菜单等待，可被“停止”中止。
    /// 在后台读文件和写出，界面线程只更新进度，避免一次读完全文件和 DoEvents 重入。
    /// </summary>
    private void SendFile()
    {
        if (_sendingFile) return;
        if (_netMode == NetMode.Serial)
        {
            if (!_port.IsOpen)
            {
                ToggleOpen();                       // 原程序先点“打开串口”再继续
                if (!_port.IsOpen) return;
            }
        }
        else if (!_net.Ready)
        {
            return;                                 // 网络模式未就绪：不发（规格门槛）
        }

        string path = _filePathBox.Text;
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch
        {
            MessageBox.Show(this, "文件不存在或者已经被别的程序打开!", "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long length = stream.Length;
        int delay = _sendFileDelay;
        int baud = ComPortService.BaudValueOf(_baudCombo.SelectedIndex, _port.CustomBaud);
        long secs = length == 0 ? 0 : Math.Max(1, (long)Math.Floor(length * 10.0 / Math.Max(1, baud)));
        // 快照说明行（规格：CN 版含“文件大小: ”前缀与延时提示；secs 为按 10 位/字节的估算，推断）
        AppendRecv("文件大小: " + length
                   + "\r\n波特率 " + baud + "bps\r\n需要时间:大约 " + secs
                   + " 秒（每发送256字节已经被插入了" + delay + "ms延时，在发送菜单里设置）\r\n请稍候...\r\n");

        _sendingFile = true;
        _abortFlag = false;                     // 规格：开始前把 0x4ED579 清 0
        _progressBar.Value = 0;
        _progressBar.Visible = true;
        _sendFileButton.Enabled = false;
        _openButton.Enabled = false;

        var mode = _netMode;
        string host = _remoteIpBox.Text.Trim();
        int remotePort = ParsePort(_remotePortBox.Text, 8080);
        ThreadPool.QueueUserWorkItem(_ => SendFileWorker(stream, length, delay, mode, host, remotePort));
    }

    private void SendFileWorker(FileStream stream, long length, int delay, NetMode mode, string host, int remotePort)
    {
        var buf = new byte[256];
        long sent = 0;
        bool aborted = false;
        Exception? error = null;
        try
        {
            while (sent < length)
            {
                if (_abortFlag) { aborted = true; break; }
                int n = stream.Read(buf, 0, 256);
                if (n <= 0) break;
                var chunk = new byte[n];
                Buffer.BlockCopy(buf, 0, chunk, 0, n);
                try
                {
                    WriteChannelSnapshot(chunk, mode, host, remotePort);
                }
                catch (Exception ex)
                {
                    error = ex;
                    break;
                }
                sent += n;
                Interlocked.Add(ref _sentCount, n);
                long shown = sent;
                Post(() => UpdateFileSendProgress(shown, length));
                if (delay > 0 && sent < length && !_abortFlag)
                    Thread.Sleep(delay);
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            try { stream.Dispose(); } catch { /* 读完即关 */ }
        }

        bool abortedCopy = aborted;
        Exception? errorCopy = error;
        Post(() => FinishFileSend(abortedCopy, errorCopy));
    }

    private void UpdateFileSendProgress(long sent, long length)
    {
        if (IsDisposed || length <= 0) return;
        _progressBar.Value = (int)Math.Min(100, sent * 100 / length);
        UpdateStatus();
    }

    private void FinishFileSend(bool aborted, Exception? error)
    {
        if (IsDisposed) return;
        if (error is not null)
        {
            MessageBox.Show(this, error.Message, "发送文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (aborted)
        {
            AppendRecv("用户中止！\r\n");
        }
        else
        {
            AppendRecv("发送完毕！");
            _progressBar.Value = 100;
        }
        _progressBar.Visible = false;
        _sendFileButton.Enabled = true;
        _openButton.Enabled = true;
        _abortFlag = true;                      // 规格：收尾置 0x4ED579 = 1
        _sendingFile = false;
        UpdateStatus();
    }

    /// <summary>后台发送用的通道写出。参数在界面线程取好，这里不再读控件。</summary>
    private void WriteChannelSnapshot(byte[] data, NetMode mode, string host, int remotePort)
    {
        if (mode == NetMode.Serial)
        {
            if (_port.IsOpen) _port.Write(data);
        }
        else if (_net.Ready)
        {
            _net.Send(data, mode, host, remotePort);
        }
    }

    /// <summary>后台线程投回界面。窗口已经关掉时直接丢掉，避免 BeginInvoke 抛异常。</summary>
    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // 句柄已销毁，或窗口正在关闭
        }
    }

    /// <summary>定时发送勾选处理（规格 CheckBox3Click）：勾选时校验间隔并启停定时器；勾选后间隔框禁用。</summary>
    private void OnTimerSendChanged()
    {
        _timerIntervalEdit.Enabled = !_timerSendBox.Checked;   // 勾选时编辑框不可用
        if (!_timerSendBox.Checked)
        {
            _sendStringTimer.Stop();
            return;
        }
        string text = _timerIntervalEdit.Text;
        if (text.Length == 0)
        {
            MessageBox.Show(this, "请设置时钟间隔!", "定时发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _timerSendBox.Checked = false;
            _sendStringTimer.Stop();
            return;
        }
        int interval = int.TryParse(text.Trim(), out var v) ? v : 1;   // 转换失败用 1
        if (interval == 0)
        {
            MessageBox.Show(this, "请设置非0数据!", "定时发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _timerSendBox.Checked = false;
            _sendStringTimer.Stop();
            return;
        }
        _sendStringTimer.Interval = interval;
        _sendStringTimer.Start();
    }

    /// <summary>定时发送到时：条件满足时等效点一次“发送”（规格 SendStringTimerTimer）。</summary>
    private void OnSendTimerTick()
    {
        if (!_timerSendBox.Checked) return;
        if (ChannelCanSend())               // 串口已开，或网络模式已就绪（规格 0x4ED535）
            SendCurrent();
    }

    /// <summary>100ms 周期：调制解调器状态、状态栏配色、校验组底色、超时标签可用性。</summary>
    private void StatusTick()
    {
        // CTS / DSR / RLSD 段（原程序由三个事件维护，这里按 100ms 轮询等价显示）
        if (_port.IsOpen)
        {
            var modem = _port.ModemStatus();
            if (modem is { } m)
                _modemStatus.Text = $"CTS={(m.Cts ? 1 : 0)} DSR={(m.Dsr ? 1 : 0)} RLSD={(m.Rlsd ? 1 : 0)}";
        }
        else
        {
            _modemStatus.Text = "CTS=- DSR=- RLSD=-";
        }

        // 状态栏底色随接收计数分档（规格：20 万/50 万/100 万/200 万）
        long n = _receive.Counter;
        _statusStrip.BackColor = n switch
        {
            >= 2_000_000 => Color.FromArgb(255, 99, 71),    // 0x4763FF
            >= 1_000_000 => Color.FromArgb(255, 160, 122),  // 0x7AA0FF
            >= 500_000 => Color.FromArgb(255, 218, 185),    // 0xB9DAFF
            >= 200_000 => Color.FromArgb(255, 235, 205),    // 0xCDEBFF
            _ => SystemColors.Control,
        };

        // 复选框高亮（规格：勾选时黄，否则系统色；加回车换行在 HEX发送 勾选时不亮）
        Highlight(_hexDisplayBox, _hexDisplayBox.Checked);
        Highlight(_hexSendBox, _hexSendBox.Checked);
        Highlight(_timestampBox, _timestampBox.Checked);
        Highlight(_newLineBox, _newLineBox.Checked && !_hexSendBox.Checked);
        Highlight(_timerSendBox, _timerSendBox.Checked);
        Highlight(_recvToFileBox, _recvToFileBox.Checked);

        // 校验组：模式 None(0) 时白底、否则黄底（标签跟随）
        bool verifying = _verifyBox.SelectedIndex > 0;
        _verifyNumEdit.BackColor = verifying ? Color.Yellow : Color.White;
        _tailCombo.BackColor = verifying ? Color.Yellow : Color.White;
        _verifyBox.BackColor = verifying ? Color.Yellow : Color.White;
        _crcBox.BackColor = verifying ? Color.Yellow : Color.White;

        _timeoutLabel.Enabled = _timestampBox.Checked;           // 规格：超时标签随“加时间戳”
        UpdateDependentVisibility();
        _sentStatus.Text = $"S:{Interlocked.Read(ref _sentCount)}";
        _recvStatus.Text = $"R:{Interlocked.Read(ref _recvCount)}";
    }

    private static void Highlight(Control c, bool on) =>
        c.BackColor = on ? Color.Yellow : SystemColors.Control;

    // ———————————————————————— M5：多条字符串 ————————————————————————

    /// <summary>行内改动触发的自动保存（原程序这些交互都会点一次“保存参数”，M8 接入后转调）。</summary>
    private void AutoSave()
    {
        if (_loading) return;
        SaveConfig();
    }

    private void AdjustMultiButtonWidth(int delta)
    {
        _multiBtnWidth = Fx.Clamp(_multiBtnWidth + delta, 20, 400);
        _multiRows.SendColumnWidth = Fx.Clamp(_multiBtnWidth, 20, 400);   // 规格：全部 99 个行按钮宽度 ±10 后重排
        _multiRows.PerformLayout();
        AutoSave();
    }

    /// <summary>行内“点击发送”按钮：数据空则什么都不做；否则用该行的 HEX 开关发送。</summary>
    private void SendRow(int row)
    {
        string data = _rowData[row].Text;
        if (data.Length == 0) return;
        SendPayload(data, _rowHex[row].Checked);
    }

    /// <summary>双击数据框输入注释（标题=行号）；确定后写入提示与行按钮标题并自动保存。</summary>
    private void EditRowComment(int row)
    {
        if (!InputComment((row + 1).ToString(), _rowComments[row], out string result)) return;
        _rowComments[row] = result;
        _rowSend[row].Text = result;
        _rowTip.SetToolTip(_rowData[row], result);
        AutoSave();
    }

    private bool InputComment(string title, string initial, out string result)
    {
        result = initial;
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(300, 100),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label { Text = "注释:", AutoSize = true, Location = new Point(10, 12) };
        var box = new TextBox { Text = initial, Location = new Point(10, 34), Width = 276 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(136, 64), Width = 70 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(216, 64), Width = 70 };
        dlg.Controls.AddRange(new Control[] { label, box, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        result = box.Text;
        return true;
    }

    /// <summary>
    /// 循环发送勾选处理（规格 CheckBox6Click）：先关单条定时器；读 99 行顺序号做稳定升序排序；
    /// 打开多条定时器（首拍等待用定时器原有间隔 1000）。
    /// </summary>
    private void OnLoopSendChanged()
    {
        if (_loopSendBox.Checked)
        {
            _sendStringTimer.Stop();                       // 规格：先把 SendStringTimer 关掉
            var pairs = new List<(int Order, int Row)>(99);
            for (int k = 0; k < 99; k++)
                pairs.Add((ParseIntOr0(_rowOrder[k].Text), k));
            _loopOrder.Clear();
            _loopOrder.AddRange(pairs.OrderBy(t => t.Order));   // 冒泡“大于才交换”= 稳定升序
            _planner.StartFlag = true;                     // 0x4ED64E = 1
            _multiTimer.Start();
        }
        else
        {
            _multiTimer.Stop();
        }
    }

    /// <summary>
    /// 多条发送一拍（规格 Send_N_StringTimerTimer）：状态机找行 → 先把该行延时写进定时器 → 再按该行 HEX 开关发送。
    /// 连续跳过 99 次自动取消“循环发送”。
    /// </summary>
    private void LoopSendTick()
    {
        if (!_loopSendBox.Checked) { _multiTimer.Stop(); return; }
        if (!ChannelCanSend()) return;                      // 串口未开或网络未就绪：本拍不动

        var input = new List<(int Order, string Data, int Row)>(99);
        foreach (var (order, row) in _loopOrder)
            input.Add((order, _rowData[row].Text, row));    // 顺序号用快照，数据实时读

        MultiSendPlanner.Step step = _planner.Tick(input);
        switch (step.Kind)
        {
            case MultiSendPlanner.Kind.Stop:
                _loopSendBox.Checked = false;               // 触发停表
                return;
            case MultiSendPlanner.Kind.None:
                return;
            case MultiSendPlanner.Kind.Send:
                int iv = int.TryParse(_rowTime[step.Row].Text.Trim(), out var p) ? p : 1000;
                _multiTimer.Interval = Fx.Clamp(iv, 1, 99999);   // 先改间隔（WinForms 间隔必须 ≥1）

                SendPayload(_rowData[step.Row].Text, _rowHex[step.Row].Checked);   // 再发送该行

                for (int k = 0; k < 99; k++) _rowSend[k].ForeColor = Color.Black;
                _rowSend[step.Row].ForeColor = Color.Red;   // 当前行按钮变红
                return;
        }
    }

    // ———————————————————————— M6：网络 ————————————————————————

    /// <summary>
    /// 模式探测（规格 FUN_00418188）：下拉文本恰为 TCPClient/TCPServer/UDP 时是 1/2/3，否则 0；
    /// 切换时停两个发送定时器、置停止标志、关掉套接字、清就绪位，并按模式切面板。
    /// </summary>
    private void DetectMode()
    {
        NetMode mode = _portCombo.Text switch
        {
            "TCPClient" => NetMode.TcpClient,
            "TCPServer" => NetMode.TcpServer,
            "UDP" => NetMode.Udp,
            _ => NetMode.Serial,
        };
        if (mode == _netMode)
        {
            UpdateModePanels();
            return;
        }
        _netMode = mode;

        // 规格：停循环发送/定时发送（都取消勾选）、置停止标志、关两个套接字、0x4ED535 = 0
        _loopSendBox.Checked = false;
        _timerSendBox.Checked = false;
        _abortFlag = true;
        _net.Disconnect();

        if (_netMode != NetMode.Serial)
        {
            _port.Close();                       // 网络模式：关串口
            FillLocalIpCombo();
            UpdateNetworkFieldEnable();
            if (_openImmediatelyItem.Checked && !_loading)    // 加载过程中不连，留给启动后的自动打开
                ConnectNetwork();
        }
        UpdateModePanels();
        UpdateStatus();
    }

    private void UpdateModePanels()
    {
        _serialBar.Visible = _netMode == NetMode.Serial;
        _netBar.Visible = _netMode != NetMode.Serial;
        UpdateLeftColumnWidth();
    }

    /// <summary>按模式禁用字段（规格：模式1 禁本地端口；模式2 禁远程IP/端口；模式3 全开）。</summary>
    private void UpdateNetworkFieldEnable()
    {
        _localPortBox.Enabled = _netMode is NetMode.TcpServer or NetMode.Udp;
        _remoteIpBox.Enabled = _netMode is NetMode.TcpClient or NetMode.Udp;
        _remotePortBox.Enabled = _netMode is NetMode.TcpClient or NetMode.Udp;
    }

    /// <summary>填本机 IP 列表（规格：gethostbyname + inet_ntoa 逐条填入本地下拉，选中第 0 项）。</summary>
    private void FillLocalIpCombo()
    {
        if (_localIpCombo.Items.Count > 0) return;
        try
        {
            var ips = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
            foreach (var ip in ips) _localIpCombo.Items.Add(ip);
            if (_localIpCombo.Items.Count > 0) _localIpCombo.SelectedIndex = 0;
        }
        catch
        {
            // 取本机地址失败就不填，允许手输
        }
    }

    /// <summary>连接（规格 ConnectClick）：按模式填主机/端口后启动对应套接字。</summary>
    private void ConnectNetwork()
    {
        switch (_netMode)
        {
            case NetMode.TcpClient:
            {
                string host = _remoteIpBox.Text.Trim();
                int port = ParsePort(_remotePortBox.Text, 8080);     // 失败默认 8080
                _net.ConnectClient(host, port);
                break;
            }
            case NetMode.TcpServer:
            {
                if (!int.TryParse(_localPortBox.Text.Trim(), out int port) || port is < 1 or > 65535)
                {
                    // 规格：服务端本地端口转换失败不替换成 8080；重写取“提示后不启动”（原程序此处响应未细读）
                    MessageBox.Show(this, "本地端口无效，请填写 1-65535。", "TCP服务端",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _net.StartServer(port);
                break;
            }
            case NetMode.Udp:
            {
                int port = ParsePort(_localPortBox.Text, 8080);      // 失败默认 8080
                _net.StartUdp(port);
                break;
            }
        }
        _connectButton.Enabled = false;                              // 连接后禁用（规格 ConnectClick 尾部）
        _disconnectButton.Enabled = true;
        UpdateStatus();
    }

    private void DisconnectNetwork()
    {
        _net.Disconnect();                                           // 内含清就绪（0x4ED535 = 0）
        _connectButton.Enabled = true;
        _disconnectButton.Enabled = true;
        UpdateStatus();
    }

    private static int ParsePort(string text, int fallback) =>
        int.TryParse(text.Trim(), out var v) && v is >= 0 and <= 65535 ? v : fallback;

    /// <summary>按当前通道写出（串口 / TCP 客户端 / 服务端第 0 个连接 / UDP）。</summary>
    private void WriteChannel(byte[] data)
    {
        if (_netMode == NetMode.Serial)
        {
            if (_port.IsOpen) _port.Write(data);
        }
        else if (_net.Ready)
        {
            _net.Send(data, _netMode, _remoteIpBox.Text.Trim(), ParsePort(_remotePortBox.Text, 8080));
        }
    }

    /// <summary>网络模式下的发送门槛（规格：模式 0 或 就绪标志非 0 才发）。</summary>
    private bool ChannelCanSend() =>
        _netMode == NetMode.Serial ? _port.IsOpen : _net.Ready;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyMultiWidth();
        // 构造期窗体不可见，Control.Visible 全为 false，左栏测宽要在显示后校正
        UpdateLeftColumnWidth();
        UpdateMinimumHeight();
        StartMultiHandlePrewarm();
        // 规格：勾了“启动时立即打开串口”，启动后自动打开（串口）/连接（网络）。加载 ini 在这之前。
        if (!_openImmediatelyItem.Checked) return;
        if (_netMode == NetMode.Serial) ToggleOpen();
        else ConnectNetwork();
    }

    /// <summary>
    /// 单条发送：组包 → 校验 → 插帧尾前 → 发送 → 计数 → 回显（规格「发送」「校验」「状态栏」）。
    /// </summary>
    private void SendCurrent() => SendPayload(_sendBox.Text, _hexSendBox.Checked);

    /// <summary>
    /// 发送入口（规格 SendData）：组包 → 校验 → 插帧尾前 → 发送 → 计数 → 回显。
    /// 单条发送、行发送、循环发送共用；<paramref name="text"/> 为空/HEX 的规则与外层一致。
    /// </summary>
    private void SendPayload(string text, bool hex)
    {
        if (_sendingFile) return;                                   // 发送文件进行中，避免和后台发送叠在一起
        // 串口未打开：接收框追加提示并尝试打开（对齐原行为；网络模式不需要）
        if (_netMode == NetMode.Serial && !_port.IsOpen)
        {
            AppendRecv("\r\n正在打开串口...\r\n");
            ToggleOpen();
            if (!_port.IsOpen) return;
        }

        if (hex && text.Length == 0) return;                        // HEX 空串：不发
        if (!hex && !_newLineBox.Checked && text.Length == 0) return; // 文本空且不加换行：不发

        byte[] payload;
        if (hex)
        {
            var composed = SendComposer.ComposeHex(text, out var errCn, out _);
            if (composed is null)
            {
                // 规格：非法字符整段不发并报错（原程序同时关掉循环发送/定时发送，M4/M5 接入后补）
                MessageBox.Show(this, errCn, "HEX 发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            payload = composed;
        }
        else
        {
            payload = SendComposer.ComposeText(text, _newLineBox.Checked);
        }

        int mode = Math.Max(0, _verifyBox.SelectedIndex);           // 0..7，名称与模式一一对应
        int headSkip = Math.Max(0, ParseIntOr0(_verifyNumEdit.Text) - 1);
        int tailSkip = Math.Max(0, _tailCombo.SelectedIndex);       // 0..4

        byte[] checksum = Checksums.Compute(payload, mode, headSkip, tailSkip);
        if (checksum.Length > 0)
        {
            int pos = Fx.Clamp(payload.Length - tailSkip, 0, payload.Length);
            payload = InsertAt(payload, checksum, pos);
        }
        if (mode == 3 && checksum.Length == 4)
            _crcBox.Text = SendComposer.HexText(checksum).Trim();   // 规格：CRC32 值显示在校验旁小框

        if (!ChannelCanSend()) return;                              // 规格发送门槛：模式 0 或 就绪标志非 0 才发
        try
        {
            WriteChannel(payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Interlocked.Add(ref _sentCount, payload.Length);            // 发送计数 = 实际发出的字节数

        if (_timestampBox.Checked)
        {
            // 规格：勾选加时间戳时，接收框追加 `\r\n[HH:MM:SS.mmm]发→◇` + 内容；
            // 内容是 HEX 文本（HEX显示 勾选）或转义文本
            string content = _hexDisplayBox.Checked
                ? SendComposer.HexText(payload)
                : SendComposer.EscapeForDisplay(payload);
            AppendRecv("\r\n[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]发→◇" + content);
        }
        UpdateStatus();
    }

    private static int ParseIntOr0(string s) => int.TryParse(s.Trim(), out var v) ? v : 0;

    private static byte[] InsertAt(byte[] src, byte[] ins, int index)
    {
        var result = new byte[src.Length + ins.Length];
        Buffer.BlockCopy(src, 0, result, 0, index);
        Buffer.BlockCopy(ins, 0, result, index, ins.Length);
        Buffer.BlockCopy(src, index, result, index + ins.Length, src.Length - index);
        return result;
    }

    private void RefreshPortList()
    {
        // 规格 Label23Click：枚举合并排序 → 重建下拉（端口 + 三协议）→ 重建端口菜单 → 按模式收尾
        var current = _portCombo.Text;
        var ports = SerialPort.GetPortNames().Distinct().OrderBy(ParseComNumber).ToList();

        _portCombo.Items.Clear();
        foreach (var name in ports) _portCombo.Items.Add(name);
        _portCombo.Items.Add("TCPClient");
        _portCombo.Items.Add("TCPServer");
        _portCombo.Items.Add("UDP");
        if (!string.IsNullOrEmpty(current)) _portCombo.Text = current;
        else if (_portCombo.Items.Count > 0) _portCombo.SelectedIndex = 0;

        // 重建“通讯端口”菜单的端口项（Tag=下标，点击=选端口；等价原程序动态菜单）
        foreach (var it in _comMenuItems) _commMenu.DropDownItems.Remove(it);
        _comMenuItems.Clear();
        for (int i = 0; i < ports.Count; i++)
        {
            string caption = ports[i];
            var item = new ToolStripMenuItem(caption) { Tag = i };
            item.Click += (_, _) =>
            {
                foreach (var it in _protocolItems) it.Checked = false;
                foreach (var it in _comMenuItems) it.Checked = false;
                item.Checked = true;
                _portCombo.Text = caption;
                DetectMode();
            };
            _comMenuItems.Add(item);
            _commMenu.DropDownItems.Add(item);
        }

        // 按模式收尾
        if (_netMode == NetMode.Serial)
        {
            if (ports.Count > 0)
            {
                string normalized = ComPortService.NormalizePortName(_portCombo.Text);
                int match = ports.FindIndex(p => ComPortService.NormalizePortName(p) == normalized);
                if (match >= 0)
                {
                    _portCombo.SelectedIndex = match;                 // 选中匹配端口
                    if (match < _comMenuItems.Count) _comMenuItems[match].Checked = true;
                }
            }
            else
            {
                _portCombo.Text = "TCPClient";                        // 无端口 → 切到 TCP 客户端（规格：模式设 1）
            }
        }
        else
        {
            _portCombo.Text = _netMode switch
            {
                NetMode.TcpClient => "TCPClient",
                NetMode.TcpServer => "TCPServer",
                _ => "UDP",
            };
        }
        DetectMode();
    }

    private static int ParseComNumber(string name)
    {
        var n = ComPortService.NormalizePortName(name);
        return n.Length > 3 && int.TryParse(n.Substring(3), out var v) ? v : 9999;
    }

    private void StartMultiHandlePrewarm()
    {
        if (!_split.Panel2Collapsed) return;
        _prewarmQueue.Clear();
        void Collect(Control c)
        {
            _prewarmQueue.Enqueue(c);
            foreach (Control child in c.Controls) Collect(child);
        }
        Collect(_multiPanel);
        _prewarmTimer.Start();
    }

    private void PrewarmTick()
    {
        for (int i = 0; i < 8 && _prewarmQueue.Count > 0; i++)
        {
            Control c = _prewarmQueue.Dequeue();
            try { _ = c.Handle; } catch { }
        }
        if (_prewarmQueue.Count == 0) _prewarmTimer.Stop();
    }

    private void ToggleMultiPanel()
    {
        _prewarmTimer.Stop();
        _prewarmQueue.Clear();
        // 规格：切换多条面板；标题在 显示态=隐藏 / 隐藏态=扩展 之间切换
        _split.Panel2Collapsed = !_split.Panel2Collapsed;
        _hideToggleButton.Text = _split.Panel2Collapsed ? "扩展" : "隐藏";
        if (!_split.Panel2Collapsed) ApplyMultiWidth();
    }

    private void ToggleBottomPanels()
    {
        // 规格：━ 按钮隐藏/恢复设置区与信息条；标题在 ■ / ━ 间切换
        bool show = !_settingsBar.Visible;
        _settingsBar.Visible = show;
        _sendArea.Visible = show;
        _collapseButton.Text = show ? "━" : "■";
    }

    private void UpdateStatus()
    {
        if (_netMode == NetMode.Serial)
        {
            bool open = _port.IsOpen;
            string state = open ? " 已打开 " : " 已关闭 ";
            string portName = open ? _port.OpenedPortName : (_portCombo.Text.Length > 0 ? _portCombo.Text : "");
            int baud = ComPortService.BaudValueOf(_baudCombo.SelectedIndex, _port.CustomBaud);
            string baudText = _baudCombo.SelectedIndex <= 0 ? baud.ToString() : _baudCombo.Text;
            _connStatus.Text = $"{portName}{state}{baudText}bps,{_port.LineDescription()}";
        }
        else
        {
            // 规格：网络模式面板 3 是整个全局串 0x4ED538（下拉文本 + 本机 IP 文本）
            _connStatus.Text = $"{_portCombo.Text} {_localIpCombo.Text}  {(_net.Ready ? "已连接" : "未连接")}";
        }
        _sentStatus.Text = $"S:{Interlocked.Read(ref _sentCount)}";
        _recvStatus.Text = $"R:{Interlocked.Read(ref _recvCount)}";
        _openButton.Text = _port.IsOpen ? "关闭串口" : "打开串口";
    }

    private TermSwitches CurrentTermSwitches() => new(
        _termEnterItem.Checked,
        _termKeyItem.Checked,
        _termEchoItem.Checked,
        _termAddLfItem.Checked);

    private void ApplyTermSwitches(TermSwitches switches)
    {
        _termEnterItem.Checked = switches.EnterSend;
        _termKeyItem.Checked = switches.KeySend;
        _termEchoItem.Checked = switches.LocalEcho;
        _termAddLfItem.Checked = switches.AddLf;
    }

    private void OnRecvKeyPress(object? sender, KeyPressEventArgs e)
    {
        TerminalResult result = _terminal.Apply(e.KeyChar, CurrentTermSwitches(), ChannelCanSend());
        if (result.SelectAll)
            _recvBox.SelectAll();
        if (result.Swallow)
            e.Handled = true;
        if (result.Payload is not { Length: > 0 }) return;
        try
        {
            WriteChannel(result.Payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "终端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Interlocked.Add(ref _sentCount, result.CountDelta);
        UpdateStatus();
    }

    private void ShowPortSettings()
    {
        PortSettingsDialog.Show(this, _port);
        UpdateStatus();
    }

    private void CommitCustomBaud(bool notify)
    {
        int raw = int.TryParse(_customBaudBox.Text.Trim(), out int v) ? v : 115200;
        ComPortService.BaudClamp clamped = ComPortService.ClampCustomBaud(raw);
        if (notify && clamped.Message is not null)
            MessageBox.Show(this, clamped.Message, "波特率", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _port.CustomBaud = clamped.Value;
        string shown = clamped.Value.ToString();
        if (_customBaudBox.Text != shown)
            _customBaudBox.Text = shown;
    }

    private void PickRecvBackColor()
    {
        using var dlg = new ColorDialog { Color = _recvBox.BackColor, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _recvBox.BackColor = dlg.Color;
        AutoSave();
    }

    private void RememberSendText()
    {
        if (_loading) return;
        if (_hexSendBox.Checked) _hexSendText = _sendBox.Text;
        else _ascSendText = _sendBox.Text;
    }

    private void OnHexSendModeChanged()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (_hexSendBox.Checked)
            {
                _ascSendText = _sendBox.Text;
                _sendBox.Text = _hexSendText;
            }
            else
            {
                _hexSendText = _sendBox.Text;
                _sendBox.Text = _ascSendText;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        _abortFlag = true;
        _loopSendBox.Checked = false;
        _timerSendBox.Checked = false;
        _sendStringTimer.Stop();
        _multiTimer.Stop();
        SaveConfig();
        try { _net.Disconnect(); } catch { /* 关闭时断开失败不影响退出 */ }
        try { _port.Close(); } catch { /* 同上 */ }
    }

    private static string IniPath() => IniDocument.CurrentPath();

    private void SaveConfig()
    {
        if (_loading) return;
        try
        {
            IniDocument.WriteFile(IniPath(), CaptureSnapshot());
        }
        catch
        {
            MessageBox.Show(this, IniDocument.SaveFailedMessage, "保存参数",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadConfig()
    {
        string path = IniPath();
        IniSnapshot snap;
        if (!File.Exists(path))
        {
            snap = new IniSnapshot();
        }
        else
        {
            try
            {
                snap = IniDocument.Load(IniDocument.ReadFile(path));
            }
            catch
            {
                snap = new IniSnapshot();
            }
        }
        ApplySnapshot(snap);
    }

    private void ImportIni()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "ini 文件|*.ini|所有文件|*.*",
            Title = "导入ini",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dlg.FileName);
        }
        catch
        {
            MessageBox.Show(this, IniDocument.ReadFailedMessage, "导入ini",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            File.WriteAllBytes(IniPath(), bytes);
        }
        catch
        {
            MessageBox.Show(this, IniDocument.SaveFailedMessage, "导入ini",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LoadConfig();
    }

    private IniSnapshot CaptureSnapshot()
    {
        // 最大化/最小化时不要保存屏幕外的实际边界，否则下次启动会被 WindowPlacement 夹成窄窗
        Rectangle savedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (savedBounds.Width <= 0 || savedBounds.Height <= 0) savedBounds = Bounds;
        var snap = new IniSnapshot
        {
            SendFileDelayMs = _sendFileDelay,
            FilePath = _filePathBox.Text,
            AscText = _ascSendText,
            HexText = _hexSendText,
            HexSend = _hexSendBox.Checked,
            TimerInterval = _timerIntervalEdit.Text,
            AddCrLf = _newLineBox.Checked,
            HexDisplay = _hexDisplayBox.Checked,
            VerifyMode = Math.Max(0, _verifyBox.SelectedIndex),
            Dtr = _dtrBox.Checked,
            Rts = _rtsBox.Checked,
            OpenImmediately = _openImmediatelyItem.Checked,
            Timestamp = _timestampBox.Checked,
            TimeoutMs = _timeoutEdit.Text,
            MemoBackColor = _recvBox.BackColor.ToArgb(),
            DisplayLimit = _displayLimit,
            RemoteIp = _remoteIpBox.Text,
            RemotePort = _remotePortBox.Text,
            LocalPort = _localPortBox.Text,
            TermEnter = _termEnterItem.Checked,
            TermKey = _termKeyItem.Checked,
            TermEcho = _termEchoItem.Checked,
            VerifyFrom = _verifyNumEdit.Text,
            TailReserve = Math.Max(0, _tailCombo.SelectedIndex),
            TermAddLf = _termAddLfItem.Checked,
            PortValue = _netMode switch
            {
                NetMode.TcpClient => "1",
                NetMode.TcpServer => "2",
                NetMode.Udp => "3",
                _ => _portCombo.Text.Length > 0 ? _portCombo.Text : "COM1",
            },
            Baud = ComPortService.BaudValueOf(_baudCombo.SelectedIndex, _port.CustomBaud).ToString(),
            FormWidth = savedBounds.Width,
            FormHeight = savedBounds.Height,
            FormLeft = savedBounds.Left,
            FormTop = savedBounds.Top,
            MultiPanelWidth = _multiPanelWidth,
            MultiButtonWidth = _multiBtnWidth,
            FontName = _recvBox.Font.Name,
            FontSize = _recvBox.Font.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontColor = _recvBox.ForeColor.ToArgb(),
            Language = "C",
        };
        if (_hexSendBox.Checked) snap.HexText = _sendBox.Text;
        else snap.AscText = _sendBox.Text;
        for (int i = 0; i < 99; i++)
        {
            snap.Order[i] = _rowOrder[i].Text;
            snap.Comment[i] = _rowComments[i];
            snap.Delay[i] = _rowTime[i].Text;
            snap.Hex[i] = _rowHex[i].Checked;
            snap.Data[i] = _rowData[i].Text;
        }
        return snap;
    }

    private void ApplySnapshot(IniSnapshot snap)
    {
        _loading = true;
        try
        {
            for (int i = 0; i < 99; i++)
            {
                _rowOrder[i].Text = snap.Order[i];
                _rowComments[i] = snap.Comment[i];
                _rowSend[i].Text = snap.Comment[i].Length > 0 ? snap.Comment[i] : (i + 1).ToString();
                _rowTip.SetToolTip(_rowData[i], snap.Comment[i]);
                _rowTime[i].Text = snap.Delay[i];
                _rowHex[i].Checked = snap.Hex[i];
                _rowData[i].Text = snap.Data[i];
            }

            _sendFileDelay = snap.SendFileDelayMs;
            foreach (var it in _sendFileItems)
                it.Checked = (int)it.Tag! == snap.SendFileDelayMs;
            if (!_sendFileItems.Any(it => it.Checked))
            {
                _sendFileDelay = 1;
                foreach (var it in _sendFileItems)
                    it.Checked = (int)it.Tag! == 1;
            }
            _filePathBox.Text = snap.FilePath;
            _ascSendText = snap.AscText;
            _hexSendText = snap.HexText;
            _hexSendBox.Checked = snap.HexSend;
            _sendBox.Text = snap.HexSend ? snap.HexText : snap.AscText;
            _timerIntervalEdit.Text = snap.TimerInterval;
            _newLineBox.Checked = snap.AddCrLf;
            _hexDisplayBox.Checked = snap.HexDisplay;
            _verifyBox.SelectedIndex = Fx.Clamp(snap.VerifyMode, 0, _verifyBox.Items.Count - 1);
            _dtrBox.Checked = snap.Dtr;
            _rtsBox.Checked = snap.Rts;
            _port.DtrWanted = snap.Dtr;
            _port.RtsWanted = snap.Rts;
            _openImmediatelyItem.Checked = snap.OpenImmediately;
            _timestampBox.Checked = snap.Timestamp;
            _timeoutEdit.Text = snap.TimeoutMs;
            _recvBox.BackColor = Color.FromArgb(snap.MemoBackColor);
            try
            {
                _recvBox.ForeColor = Color.FromArgb(snap.FontColor);
                if (snap.FontName.Length > 0 &&
                    float.TryParse(snap.FontSize, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float size) &&
                    size > 0)
                    _recvBox.Font = new Font(snap.FontName, size);
            }
            catch
            {
                // 字体名在本机不存在时保持当前字体，键仍然按当前字体写回
            }

            int limit = snap.DisplayLimit;
            ToolStripMenuItem? limitItem = _limitItems.FirstOrDefault(it => (int)it.Tag! == limit);
            if (limitItem is null)
            {
                limit = 500_000;
                limitItem = _limitItems.First(it => (int)it.Tag! == limit);
            }
            _displayLimit = limit;
            _receive.Limit = limit;
            foreach (var it in _limitItems) it.Checked = ReferenceEquals(it, limitItem);

            _remoteIpBox.Text = snap.RemoteIp;
            _remotePortBox.Text = snap.RemotePort;
            _localPortBox.Text = snap.LocalPort;
            ApplyTermSwitches(new TermSwitches(snap.TermEnter, snap.TermKey, snap.TermEcho, snap.TermAddLf));
            _verifyNumEdit.Text = snap.VerifyFrom;
            _tailCombo.SelectedIndex = Fx.Clamp(snap.TailReserve, 0, _tailCombo.Items.Count - 1);

            ComPortService.ApplyBaudText(snap.Baud, out int baudIndex, out int customBaud);
            _port.BaudIndex = baudIndex;
            _port.CustomBaud = customBaud;
            _baudCombo.SelectedIndex = Fx.Clamp(baudIndex, 0, _baudCombo.Items.Count - 1);
            _customBaudBox.Text = customBaud.ToString();
            _customBaudBox.Visible = _baudCombo.SelectedIndex <= 0;

            _multiPanelWidth = snap.MultiPanelWidth > 0 ? snap.MultiPanelWidth : 515;
            _multiBtnWidth = snap.MultiButtonWidth > 0 ? snap.MultiButtonWidth : 120;
            ApplyLoadedPort(snap.PortValue);
            ApplyWindowGeometry(snap);
        }
        finally
        {
            _loading = false;
            RefreshRecvSnapshot();
        }
    }

    private void ApplyLoadedPort(string portValue)
    {
        var (kind, portName) = IniDocument.ParsePortMode(portValue);
        foreach (var it in _protocolItems) it.Checked = false;
        foreach (var it in _comMenuItems) it.Checked = false;
        switch (kind)
        {
            case PortKind.TcpClient:
                _portCombo.Text = "TCPClient";
                _protocolItems[0].Checked = true;
                break;
            case PortKind.Udp:
                _portCombo.Text = "UDP";
                _protocolItems[2].Checked = true;
                break;
            default:
                string name = portName.Length > 0 ? portName : "COM1";
                _portCombo.Text = name;
                int match = -1;
                for (int i = 0; i < _comMenuItems.Count; i++)
                {
                    if (ComPortService.NormalizePortName(_comMenuItems[i].Text ?? "") ==
                        ComPortService.NormalizePortName(name))
                    {
                        match = i;
                        break;
                    }
                }
                if (match >= 0) _comMenuItems[match].Checked = true;
                break;
        }
    }

    private void ApplyWindowGeometry(IniSnapshot snap)
    {
        Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 800, 600);
        WindowPlacement.Clamped g = WindowPlacement.Clamp(
            snap.FormWidth, snap.FormHeight, snap.FormLeft, snap.FormTop, bounds.Width, bounds.Height);
        StartPosition = FormStartPosition.Manual;
        Width = g.Width;
        Height = g.Height;
        Left = bounds.Left + g.Left;
        Top = bounds.Top + g.Top;
    }

    private void ApplyMultiWidth()
    {
        if (_adjustingSplit) return;
        _adjustingSplit = true;
        try
        {
            _multiRows.SendColumnWidth = Fx.Clamp(_multiBtnWidth, 20, 400);
            _multiRows.PerformLayout();
            int total = _split.ClientSize.Width;
            if (total < 300) return;
            int want2 = Fx.Clamp(_multiPanelWidth, 120, total - 180);
            int dist = total - _split.SplitterWidth - want2;
            int min = _split.Panel1MinSize;
            int max = total - _split.SplitterWidth - _split.Panel2MinSize;
            if (max < min) return;
            _split.SplitterDistance = Fx.Clamp(dist, min, max);
        }
        catch
        {
            // 句柄还没就绪时跳过，下次显示再套宽度
        }
        finally
        {
            _adjustingSplit = false;
        }
    }
}
