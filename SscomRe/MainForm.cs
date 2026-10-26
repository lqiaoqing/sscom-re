using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;

namespace SscomRe;

/// <summary>
/// 主窗体：串口/网络收发、多条字符串、终端仿真、ini 参数与界面编排。
/// 布局与文案按规格“界面结构与控件对照 / 状态栏 / 最前、扩展、━”设计。
/// </summary>
public sealed partial class MainForm : Form
{
    private readonly ComPortService _port = new();
    private readonly ReceiveDisplay _receive = new();

    // 计数（规格：S:=发送计数、R:=接收计数，清除窗口时清零）
    private long _sentCount;
    private long _recvCount;

    // 显示缓冲上限（规格：默认 1000000，菜单可改 200K/500K/1M/2M/5M）
    private int _displayLimit = 1_000_000;
    private readonly List<ToolStripMenuItem> _limitItems = new();
    private ThemeTextBox _searchBox = null!;
    private ThemeButton _pauseButton = null!;
    private bool _autoFollow = true;
    private int _searchStart = -1;
    private int _searchLength;
    private ToolStripStatusLabel _rxRateLabel = null!;
    private ToolStripStatusLabel _txRateLabel = null!;
    private ToolStripStatusLabel _bufferLabel = null!;
    private readonly System.Windows.Forms.Timer _rateTimer = new() { Interval = 500 };
    private long _lastSentSample;
    private long _lastRecvSample;

    // 控件
    private ComboBox _portCombo = null!;
    private PortField _portField = null!;
    private PortPopup _portPopup = null!;
    private PortPopupDismissFilter? _portPopupFilter;
    private ComboBox _baudCombo = null!;
    private Button _openButton = null!;
    private CheckBox _rtsBox = null!;
    private CheckBox _dtrBox = null!;
    private Button _moreSettingsButton = null!;
    private RichTextBox _recvBox = null!;
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
    private WrapRowPanel _settingsRow1 = null!;
    private Control _sendArea = null!;
    private ThemeButton _recvSettingsButton = null!;
    private ThemeButton _sendSettingsButton = null!;
    private Panel _recvSettingsFlyout = null!;
    private Panel _sendSettingsFlyout = null!;
    private Panel? _openFlyout;
    private Control? _openFlyoutAnchor;
    private Control? _flyoutClosedAnchor;
    private DateTime _flyoutClosedAt;
    private FlyoutDismissFilter? _flyoutFilter;
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
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new() { Interval = 400 };
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

    // —— M5：多条字符串，3 页 × 99 行 ——
    private readonly CheckBox[] _rowHex = new CheckBox[IniDocument.MultiRowCount];
    private readonly TextBox[] _rowData = new TextBox[IniDocument.MultiRowCount];
    private readonly Button[] _rowSend = new Button[IniDocument.MultiRowCount];
    private readonly TextBox[] _rowOrder = new TextBox[IniDocument.MultiRowCount];
    private readonly TextBox[] _rowTime = new TextBox[IniDocument.MultiRowCount];
    private readonly string[] _rowComments = new string[IniDocument.MultiRowCount];
    private readonly ToolTip _rowTip = new();
    private readonly MultiRowsPanel[] _multiGrids = new MultiRowsPanel[IniDocument.MultiPageCount];
    private readonly Panel[] _multiPages = new Panel[IniDocument.MultiPageCount];
    private readonly ThemeButton[] _multiTabs = new ThemeButton[IniDocument.MultiPageCount];
    private Control _toolBar = null!;
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
    private TextBox _remoteIpBox = null!;
    private TextBox _remotePortBox = null!;
    private TextBox _localPortBox = null!;
    private ComboBox _localIpCombo = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private ToolStripMenuItem _openImmediatelyItem = null!;
    private ToolStripMenuItem _lightThemeItem = null!;
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
    private int _multiPanelWidth;                       // N1086。0 表示还没拖过，按窗口大约四分之一
    private bool _multiWidthCustom;
    private bool _userMovingSplitter;
    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _bodyLayout = null!;
    private Panel _sidePanel = null!;
    private Control _sideContent = null!;
    private ResizeSplitter _sideSplitter = null!;
    private int _sideWidth;
    private ThemeButton _sidebarToggle = null!;
    private bool _sideCollapsed;
    private bool _sideAutoCollapsed;
    private bool _sideManualOverride;
    private bool _sideUserCollapsed;
    private TableLayoutPanel _serialSection = null!;
    private TableLayoutPanel _netSection = null!;
    private ThemeChip _sideModeLabel = null!;
    private SideStatusCard _sideStatusCard = null!;
    private Label _logTitleLabel = null!;
    private Label _logSubLabel = null!;
    private bool _portOpenIntent;
    private Panel _composerPanel = null!;
    private TableLayoutPanel _composerGrid = null!;
    private TableLayoutPanel _centerLayout = null!;
    private string _ascSendText = "abcdefg";
    private string _hexSendText = "12 FF 00 30";

    public MainForm()
    {
        Text = $"串口/网络数据调试器（重写版）v{AppVersion}";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        Width = 980;
        Height = 700;
        MinimumSize = new Size(Theme.Scale(this, 820), Theme.Scale(this, 560));
        StartPosition = FormStartPosition.CenterScreen;

        // 建控件前先定主题，避免启动时先画深色再整体换色。
        Theme.SetLight(IniDocument.ReadThemePreference(IniPath()));

        BuildUi();
        _rateTimer.Tick += (_, _) => RateTick();
        WireEvents();
        RefreshRecvSnapshot();
        RefreshPortList();
        LoadConfig();
        UpdateStatus();
        Theme.ApplyWindow(this);
        Theme.StyleRecursive(this);
        RefreshLogHeaderLayout();
    }

    // ———————————————————————— UI 构建 ————————————————————————

    private void BuildUi()
    {
        SuspendLayout();

        var menu = BuildMenu();
        var toolBar = BuildToolBar();
        _toolBar = toolBar;
        var side = BuildSidebar();
        var center = BuildCenterPhase2();
        var status = BuildStatusBar();

        _sideWidth = SidebarWidth();
        _bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Canvas,
        };
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _sideWidth));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SideSplitterThickness()));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _sideSplitter = new ResizeSplitter { Orientation = Orientation.Vertical, Dock = DockStyle.Fill, BackColor = Theme.Surface2 };
        _sideSplitter.Dragged += ResizeSidebarBy;
        _rowTip.SetToolTip(_sideSplitter, "拖动调整连接栏宽度");
        side.Dock = DockStyle.Fill;
        center.Dock = DockStyle.Fill;
        _bodyLayout.Controls.Add(side, 0, 0);
        _bodyLayout.Controls.Add(_sideSplitter, 1, 0);
        _bodyLayout.Controls.Add(center, 2, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Canvas,
        };
        _rootLayout = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, LineHeight())); // 工具栏
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));           // 三栏主体
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));               // 进度条
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));               // 状态栏

        _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 8, Maximum = 100, Visible = false, Margin = new Padding(0), BackColor = Theme.Surface2 };
        root.Controls.Add(toolBar, 0, 0);
        root.Controls.Add(_bodyLayout, 0, 1);
        root.Controls.Add(_progressBar, 0, 2);
        root.Controls.Add(status, 0, 3);

        toolBar.SizeChanged += (_, _) => { UpdateToolbarRowHeight(toolBar); ScheduleResponsiveRelayout(); };
        UpdateToolbarRowHeight(toolBar);
        SizeChanged += (_, _) => { HideToast(); CloseFlyout(); ClosePortPopup(false); ScheduleResponsiveRelayout(); };
        Move += (_, _) => { HideToast(); CloseFlyout(); ClosePortPopup(false); };
        menu.MenuActivate += (_, _) => { CloseFlyout(); ClosePortPopup(false); };

        Controls.Add(root);
        Controls.Add(menu);
        MainMenuStrip = menu;
        BuildSettingsFlyouts();
        BuildPortPopup();
        BuildToast();
        ResumeLayout();
    }

    private int LineHeight() => Math.Max(28, Font.Height + 14);

    internal static string AppVersion
    {
        get
        {
            Version? version = typeof(MainForm).Assembly.GetName().Version;
            return version is null ? "1.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private int SidebarWidth() => Math.Max(240, Theme.Scale(this, 280));
    private int SideSplitterThickness() => Math.Max(7, Theme.Scale(this, 8));
    private int MaxSidebarWidth() => Math.Max(240, Theme.Scale(this, 480));

    private void ResizeSidebarBy(int delta)
    {
        if (_sideCollapsed || _bodyLayout is null) return;
        int min = Math.Max(200, Theme.Scale(this, 220));
        int maxByWindow = ClientSize.Width - Theme.Scale(this, 600) - SideSplitterThickness() - Theme.Scale(this, 16);
        int max = Math.Min(MaxSidebarWidth(), Math.Max(min, maxByWindow));
        int wanted = Fx.Clamp(_sideWidth + delta, min, max);
        if (wanted == _sideWidth) return;
        _sideWidth = wanted;
        _sideManualOverride = true;
        _sideAutoCollapsed = false;
        _sideUserCollapsed = false;
        _bodyLayout.ColumnStyles[0].Width = wanted;
        _bodyLayout.PerformLayout();
        FitSidebar(_sideContent);
        UpdateComposerLayout();
        UpdateMinimumHeight();
    }

    private void UpdateToolbarRowHeight(Control toolBar)
    {
        if (_rootLayout is null) return;
        int h = Math.Max(LineHeight(), toolBar.Height);
        RowStyle style = _rootLayout.RowStyles[0];
        if (Math.Abs(style.Height - h) > 0.5f) style.Height = h;
    }

    /// <summary>折行/缩放后工具栏、侧栏和发送区可能在同一轮布局里彼此错过，延迟一次终排。</summary>
    private void ScheduleResponsiveRelayout()
    {
        if (_relayoutScheduled || !IsHandleCreated) return;
        _relayoutScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _relayoutScheduled = false;
            if (_rootLayout is null || _toolBar is null || _sidePanel is null) return;
            int toolbarHeight = Math.Max(LineHeight(), _toolBar.Height);
            RowStyle toolbarRow = _rootLayout.RowStyles[0];
            if (Math.Abs(toolbarRow.Height - toolbarHeight) > 0.5f) toolbarRow.Height = toolbarHeight;

            if (!_sideManualOverride)
            {
                bool narrow = ClientSize.Width < _sideWidth + 720;
                if (narrow && !_sideCollapsed)
                    SetSidebarCollapsed(true, persist: false, manual: false, automatic: true);
                else if (!narrow && _sideAutoCollapsed)
                    SetSidebarCollapsed(false, persist: false, manual: false, automatic: true);
            }
            if (_bodyLayout is not null)
            {
                float wanted = _sideCollapsed ? 0 : _sideWidth;
                if (Math.Abs(_bodyLayout.ColumnStyles[0].Width - wanted) > 0.5f)
                    _bodyLayout.ColumnStyles[0].Width = wanted;
                float splitter = _sideCollapsed ? 0 : SideSplitterThickness();
                if (Math.Abs(_bodyLayout.ColumnStyles[1].Width - splitter) > 0.5f)
                    _bodyLayout.ColumnStyles[1].Width = splitter;
                if (_sideSplitter is not null) _sideSplitter.Visible = !_sideCollapsed;
            }
            UpdateComposerLayout();
            _rootLayout.PerformLayout();
            _bodyLayout?.PerformLayout();
            UpdateMinimumHeight();
        }));
    }

    private void UpdateMinimumHeight()
    {
        if (_updatingMin || !IsHandleCreated || _toolBar is null || _sidePanel is null) return;
        _updatingMin = true;
        try
        {
            int required = (MainMenuStrip?.Height ?? 0)
                           + _toolBar.Height
                           + ComposerHeight()
                           + (_statusStrip?.Height ?? 0)
                           + Theme.Scale(this, 220);     // 日志区最小高度
            int nonClient = Math.Max(0, Height - ClientSize.Height);
            int minHeight = Math.Max(Theme.Scale(this, 560), required + nonClient);
            int minWidth = Theme.Scale(this, 820);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            if (work.Height > 0)
                minHeight = Math.Min(minHeight, Math.Max(Theme.Scale(this, 300), work.Height - 8));
            if (work.Width > 0)
                minWidth = Math.Min(minWidth, Math.Max(Theme.Scale(this, 400), work.Width - 8));
            if (MinimumSize.Width != minWidth || MinimumSize.Height != minHeight)
                MinimumSize = new Size(minWidth, minHeight);
            if (Height < minHeight) Height = minHeight;
        }
        finally
        {
            _updatingMin = false;
        }
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
        display.DropDownItems.Add(new ToolStripMenuItem("字体/颜色/大小/...", null, (_, _) => PickRecvFont()));
        display.DropDownItems.Add(new ToolStripMenuItem("背景颜色", null, (_, _) => PickRecvBackColor()));
        _lightThemeItem = new ToolStripMenuItem("浅色主题")
        {
            CheckOnClick = true,
            Checked = Theme.IsLight,
        };
        _lightThemeItem.Click += (_, _) =>
        {
            ApplyTheme(_lightThemeItem.Checked);
            AutoSave();
        };
        display.DropDownItems.Add(_lightThemeItem);
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
            MessageBox.Show(this,
                $"SSCOM 重写版（C# / .NET Framework 4.8）\n版本 v{AppVersion}\n依据 sscom5.13.1-rewrite-spec.md 的行为规则实现。",
                $"关于 SscomRe v{AppVersion}", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new ToolStripItem[] { comm, portCfg, display, send, multi, help });
        Theme.StyleMenu(menu);
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

    private Control BuildToolBar()
    {
        var panel = MakeWrapLine();
        Put(panel, ActionButton("打开文件", OpenFile));
        _filePathBox = new ThemeTextBox
        {
            Width = TextUnit() * 12,
            ReadOnly = true,
            BackColor = Theme.Field,
            ForeColor = Theme.Muted,
            PlaceholderText = "未选择文件",
        };
        Put(panel, _filePathBox);
        panel.StretchControl = _filePathBox;

        panel.MarkRightGroup();
        _sendFileButton = ActionButton("发送文件", SendFile);
        Put(panel, _sendFileButton);
        Put(panel, ActionButton("停止", StopSendFile));
        Put(panel, ActionButton("清发送区", () => _sendBox.Clear()));
        _topmostBox = new ThemeCheckBox { Text = "最前", AutoSize = true };
        Put(panel, _topmostBox);
        Put(panel, ActionButton("保存参数", SaveConfig));
        _hideToggleButton = ActionButton("扩展", ToggleMultiPanel);
        Put(panel, _hideToggleButton);
        return panel;
    }

    private WrapRowPanel BuildOptionsRow()
    {
        var row = MakeWrapLine();
        CheckBox Cb(string text, bool @checked = false) =>
            new ThemeCheckBox { Text = text, AutoSize = true, Checked = @checked };
        Label Lb(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent };
        int unit = TextUnit();

        _hexDisplayBox = Cb("HEX显示");
        Put(row, _hexDisplayBox);
        _hexSendBox = Cb("HEX发送");
        Put(row, _hexSendBox);
        _timerSendBox = Cb("定时发送:");
        Put(row, _timerSendBox);
        _timerIntervalEdit = new ThemeTextBox { Text = "1000", Width = unit * 4 };
        Put(row, _timerIntervalEdit);
        Put(row, Lb("ms/次"));
        _newLineBox = Cb("加回车换行");
        Put(row, _newLineBox);

        row.MarkRightGroup();
        _recvSettingsButton = SettingButton("接收设置 ▾");
        _sendSettingsButton = SettingButton("发送设置 ▾");
        Put(row, _recvSettingsButton);
        Put(row, _sendSettingsButton);
        _collapseButton = new ThemeButton
        {
            Text = "━",
            Kind = ThemeButton.ButtonKind.Icon,
            AutoSize = false,
            Width = Math.Max(Theme.Scale(this, 26), Font.Height + 8),
            Height = Math.Max(Theme.Scale(this, 26), Font.Height + 8),
            Margin = new Padding(2, 1, 2, 1),
        };
        _collapseButton.Click += (_, _) => ToggleBottomPanels();
        _rowTip.SetToolTip(_collapseButton, "收起发送区");
        row.Controls.Add(_collapseButton);
        return row;
    }

    private static ThemeButton SettingButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Kind = ThemeButton.ButtonKind.Default,
    };

    /// <summary>单行，不换行。控件保持自身宽度，避免把「加时间戳」拆成两行。</summary>
    private FlowLayoutPanel MakeLine() => new RowFlowPanel
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        AutoSize = false,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(2, 1, 2, 0),
        BackColor = Theme.Canvas,
    };

    /// <summary>可换行的一行：宽度不够时自动折到下一行，高度随内容增长。</summary>
    private FlowLayoutPanel MakeTopLine() => new()
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        AutoSize = false,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(2, 1, 2, 0),
        BackColor = Theme.Canvas,
    };

    private WrapRowPanel MakeWrapLine() => new WrapRowPanel
    {
        Dock = DockStyle.Top,
        WrapContents = true,
        AutoSize = false,
        Height = LineHeight(),
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(2, 1, 2, 1),
        Width = 1200,   // 构造期先按宽窗口排一次，避免在默认 200px 宽度下反复折行
        BackColor = Theme.Canvas,
    };

    /// <summary>不换行行内的垂直居中（固定行高区域用）。</summary>
    private sealed class RowFlowPanel : FlowLayoutPanel
    {
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
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

    /// <summary>
    /// 可换行行：宽度固定为父容器宽度（AutoSize=false），按首选高度自动增高，
    /// 再按“视觉行”垂直居中。AutoSize=true 会让宽度保持内容宽度而不换行，导致右侧被裁。
    /// </summary>
    private sealed class WrapRowPanel : FlowLayoutPanel
    {
        private bool _adjusting;
        private int _rightStart = int.MaxValue;

        public void MarkRightGroup() => _rightStart = Controls.Count;

        /// <summary>中间填充控件（如文件路径框）：同一行时撑满左右两组之间的空档。</summary>
        public Control? StretchControl { get; set; }

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
                AlignRightGroup();
                StretchToFill();
            }
            finally
            {
                _adjusting = false;
            }
        }

        /// <summary>把标记之后的控件整体推到行右边缘；窄窗折到下一行时也右对齐。</summary>
        private void AlignRightGroup()
        {
            if (_rightStart >= Controls.Count) return;
            int width = ClientSize.Width;
            if (width <= 0) return;
            var rightSet = new HashSet<Control>();
            for (int i = _rightStart; i < Controls.Count; i++) rightSet.Add(Controls[i]);

            var rows = new List<List<Control>>();
            List<Control>? current = null;
            int prevLeft = int.MinValue;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                if (current is null || c.Left <= prevLeft)
                {
                    current = new List<Control>();
                    rows.Add(current);
                }
                current.Add(c);
                prevLeft = c.Left;
            }

            int rightEdge = width - Padding.Right;
            foreach (var row in rows)
            {
                var rights = row.Where(c => rightSet.Contains(c)).ToList();
                if (rights.Count == 0) continue;
                int maxRight = rights.Max(c => c.Right + c.Margin.Right);
                int shift = rightEdge - maxRight;
                if (shift <= 0) continue;
                foreach (Control c in rights) c.Left += shift;
            }
        }

        private void StretchToFill()
        {
            if (StretchControl is null || !StretchControl.Visible) return;
            int width = ClientSize.Width;
            if (width <= 0) return;
            int index = Controls.GetChildIndex(StretchControl);
            if (index <= 0 || index >= Controls.Count - 1) return;
            int leftWidth = 0, rightWidth = 0;
            for (int i = 0; i < Controls.Count; i++)
            {
                Control c = Controls[i];
                if (!c.Visible || ReferenceEquals(c, StretchControl)) continue;
                if (i < index) leftWidth += c.Width + c.Margin.Horizontal;
                else rightWidth += c.Width + c.Margin.Horizontal;
            }
            int available = width - Padding.Horizontal - leftWidth - rightWidth - StretchControl.Margin.Horizontal;
            int target = Math.Max(120, available - 128);
            if (Math.Abs(StretchControl.Width - target) > 2)
                StretchControl.Width = target;
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

    private static Button ActionButton(string text, Action? onClick = null)
    {
        var button = new ThemeButton
        {
            Text = text,
            AutoSize = true,
            Kind = text is "停止" ? ThemeButton.ButtonKind.Danger : ThemeButton.ButtonKind.Default,
        };
        if (onClick != null) button.Click += (_, _) => onClick();
        return button;
    }

    private static FlowLayoutPanel MakeGroup(params Control[] controls)
    {
        var group = new RowFlowPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Canvas,
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

    private static void Put(FlowLayoutPanel line, Control control)
    {
        control.Margin = new Padding(3, 2, 2, 2);
        if (control is Label or CheckBox)
            control.AutoSize = true;
        if (control is Label label)
        {
            label.ForeColor = Theme.Muted;
            label.BackColor = Color.Transparent;
        }
        if (control is Button button)
        {
            button.AutoSize = true;
            button.Margin = new Padding(2, 1, 2, 1);
        }
        line.Controls.Add(control);
    }

    private int TextUnit() => Math.Max(7, TextRenderer.MeasureText("0", Font).Width);

    /// <summary>多条字符串面板：顶部循环发送开关 + 3 页，每页 99 行（HEX/数据/点击发送/顺序/延时）。</summary>
    private Control BuildMultiPanel()
    {
        _multiPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Width = Math.Max(400, _multiPanelWidth) };

        // 顶部条：循环发送、调宽按钮、导入 ini。三页共用。
        int smallButton = Math.Max(26, Font.Height + 10);
        int smallButtonH = Math.Max(22, Font.Height + 6);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2), WrapContents = false, BackColor = Theme.Surface2 };
        _loopSendBox = new ThemeCheckBox { Text = "循环发送", AutoSize = true, Padding = new Padding(2, 5, 6, 0) };
        top.Controls.Add(_loopSendBox);
        var minus = new ThemeButton { Text = "−", Width = smallButton, Height = smallButtonH, Margin = new Padding(2, 1, 0, 0), Kind = ThemeButton.ButtonKind.Icon };
        minus.Click += (_, _) => AdjustMultiButtonWidth(-10);
        var plus = new ThemeButton { Text = "＋", Width = smallButton, Height = smallButtonH, Margin = new Padding(2, 1, 0, 0), Kind = ThemeButton.ButtonKind.Icon };
        plus.Click += (_, _) => AdjustMultiButtonWidth(+10);
        top.Controls.Add(new Label { Text = "点击发送按钮宽度:", AutoSize = true, Padding = new Padding(6, 7, 0, 0), ForeColor = Theme.Muted, BackColor = Color.Transparent });
        top.Controls.Add(minus);
        top.Controls.Add(plus);
        _importIniButton = new ThemeButton { Text = "导入ini", AutoSize = true, Margin = new Padding(8, 1, 0, 0) };
        _importIniButton.Click += (_, _) => ImportIni();
        top.Controls.Add(_importIniButton);

        int contentH;
        using (var probe = new ThemeTextBox { Font = Font })
            contentH = probe.PreferredHeight;
        int headerH = TextRenderer.MeasureText("字符串(双击注释)", Font).Height + 8;
        int rowHeight = Math.Max(contentH, headerH) + 6;
        int hexCol = Math.Max(36, TextRenderer.MeasureText("HEX", Font).Width + 16);
        int orderCol = Math.Max(48, TextRenderer.MeasureText("顺序", Font).Width + 16);
        int delayCol = Math.Max(64, Math.Max(
            TextRenderer.MeasureText("延时ms", Font).Width,
            TextRenderer.MeasureText("1000", Font).Width) + 18);
        int sendCol = Math.Max(_multiBtnWidth, TextRenderer.MeasureText("点击发送", Font).Width + 16);

        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        var pageBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = LineHeight(),
            WrapContents = false,
            Padding = new Padding(0, 1, 0, 0),
            BackColor = Theme.Surface2,
        };
        for (int page = 0; page < IniDocument.MultiPageCount; page++)
        {
            var grid = BuildMultiGrid(page, rowHeight, hexCol, orderCol, delayCol, sendCol);
            _multiGrids[page] = grid;
            var scroll = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Canvas,
                Visible = page == 0,
            };
            scroll.Controls.Add(grid);
            pageHost.Controls.Add(scroll);
            _multiPages[page] = scroll;

            var tab = new ThemeButton
            {
                Text = $"第{page + 1}页",
                AutoSize = false,
                Width = Math.Max(72, TextRenderer.MeasureText($"第{page + 1}页", Font).Width + Theme.Scale(this, 24)),
                Height = Font.Height + 10,
                Margin = new Padding(0, 0, 3, 0),
                Kind = page == 0 ? ThemeButton.ButtonKind.Outline : ThemeButton.ButtonKind.Default,
            };
            int selected = page;
            tab.Click += (_, _) => SelectMultiPage(selected);
            pageBar.Controls.Add(tab);
            _multiTabs[page] = tab;
        }

        _multiPanel.Controls.Add(pageHost);
        _multiPanel.Controls.Add(pageBar);
        _multiPanel.Controls.Add(top);
        return _multiPanel;
    }

    /// <summary>一页 99 行。下标是页内行号加上 page×99，三页连成一张发送表。</summary>
    private MultiRowsPanel BuildMultiGrid(int page, int rowHeight, int hexCol, int orderCol, int delayCol, int sendCol)
    {
        var grid = new MultiRowsPanel
        {
            Dock = DockStyle.Top,
            Height = rowHeight * 100,
            RowHeight = rowHeight,
            HexColumnWidth = hexCol,
            SendColumnWidth = sendCol,
            OrderColumnWidth = orderCol,
            DelayColumnWidth = delayCol,
        };
        grid.SuspendLayout();
        void AddHeader(string text, int col)
        {
            var label = HeaderLabel(text);
            label.Tag = new Point(0, col);
            grid.Controls.Add(label);
        }
        AddHeader("HEX", 0);
        AddHeader("字符串(双击注释)", 1);
        AddHeader("点击发送", 2);
        AddHeader("顺序", 3);
        AddHeader("延时ms", 4);

        int origin = page * IniDocument.MultiRowsPerPage;
        for (int k = 0; k < IniDocument.MultiRowsPerPage; k++)
        {
            int row = origin + k;                          // 闭包捕获全局行号
            int shown = k + 1;                             // 页内行号，每页都从 1 起
            var cb = new ThemeCheckBox
            {
                Checked = false,
                AutoSize = false,
                Width = Math.Max(26, Font.Height + 10),
                Height = Math.Max(20, Font.Height + 4),
                Tag = new Point(k + 1, 0),
            };
            cb.MouseUp += (_, _) => AutoSave();            // 规格：行 HEX 复选框 MouseUp 自动保存
            _rowHex[row] = cb;
            grid.Controls.Add(cb);

            var data = new ThemeTextBox { Tag = new Point(k + 1, 1) };
            data.KeyPress += (_, e) =>
            {
                if (e.KeyChar == (char)1) { data.SelectAll(); e.Handled = true; }   // Ctrl+A 全选
                else ScheduleAutoSave();                                            // 其余键防抖保存，避免每键重写整份 ini
            };
            data.DoubleClick += (_, _) => EditRowComment(row);
            _rowData[row] = data;
            grid.Controls.Add(data);

            var send = new ThemeButton
            {
                Text = shown.ToString(),
                Tag = new Point(k + 1, 2),
                Padding = new Padding(2, 0, 2, 0),
            };
            send.Click += (_, _) => SendRow(row);
            _rowSend[row] = send;
            grid.Controls.Add(send);

            var order = new ThemeTextBox { Text = "0", Tag = new Point(k + 1, 3) };
            _rowTip.SetToolTip(order, "自动发送时按顺序号从小到大,0不发送");
            order.KeyUp += (_, _) =>
            {
                var (t, c) = InputNormalizer.NormalizeInt(order.Text, order.SelectionStart, 0, 0, 99);
                if (t != order.Text)
                {
                    order.Text = t;                       // 仅变化时写回（避免光标被重置导致多位数输入颠倒）
                    order.SelectionStart = c;
                }
                ScheduleAutoSave();
            };
            _rowOrder[row] = order;
            grid.Controls.Add(order);

            var time = new ThemeTextBox { Text = "1000", Tag = new Point(k + 1, 4) };
            time.KeyUp += (_, _) =>
            {
                var (t, c) = InputNormalizer.NormalizeInt(time.Text, time.SelectionStart, 1000, 1, 99999);
                if (t != time.Text)
                {
                    time.Text = t;                        // 仅变化时写回（同样为保住光标）
                    time.SelectionStart = c;
                }
                ScheduleAutoSave();
            };
            _rowTime[row] = time;
            grid.Controls.Add(time);

            _rowComments[row] = shown.ToString();          // 注释默认行号（按钮面板与提示同源）
            _rowTip.SetToolTip(data, _rowComments[row]);
        }

        grid.ResumeLayout(false);
        return grid;
    }

    private static Label HeaderLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(2, 0, 2, 0),
        ForeColor = Theme.Muted,
        BackColor = Theme.Surface2,
    };

    /// <summary>滚动区双缓冲，减少多行面板展开/滚动时的闪烁。</summary>
    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() => DoubleBuffered = true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TryDarkNative(Handle);
        }
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

        private int _layouting;

        public MultiRowsPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var back = new SolidBrush(Theme.Surface))
                e.Graphics.FillRectangle(back, ClientRectangle);
            using (var header = new SolidBrush(Theme.Surface2))
                e.Graphics.FillRectangle(header, 0, 0, Width, Math.Min(RowHeight, Height));
            using var line = new Pen(Theme.WithAlpha(28, Theme.Border));
            for (int row = 1; row <= 99; row++)
            {
                int y = row * RowHeight - 1;
                if (y >= 0 && y < Height)
                    e.Graphics.DrawLine(line, 0, y, Width, y);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (_layouting > 0)
            {
                base.OnLayout(levent);
                return;
            }
            _layouting++;
            try
            {
                // 文本框按首选高度。点击发送按钮还有立体边框，跟文本框一样高时字的上下会被边框切掉。
                int textH = Font.Height + 6;
                int headerH = Font.Height + 8;
                foreach (Control c in Controls)
                {
                    if (c is TextBox tb)
                        textH = Math.Max(textH, tb.PreferredHeight);
                    else if (c is Label)
                        headerH = Math.Max(headerH, TextRenderer.MeasureText(c.Text, Font).Height + 8);
                }
                int buttonH = Math.Max(textH, Font.Height + 10);
                int rowHeight = Math.Max(Math.Max(textH, buttonH), headerH) + 6;
                RowHeight = rowHeight;
                int wanted = rowHeight * 100;
                if (Height != wanted) Height = wanted;

                int hexMin = TextRenderer.MeasureText("HEX", Font).Width + 16;
                int orderMin = TextRenderer.MeasureText("顺序", Font).Width + 16;
                int delayMin = Math.Max(
                    TextRenderer.MeasureText("延时ms", Font).Width,
                    TextRenderer.MeasureText("1000", Font).Width) + 18;
                if (HexColumnWidth < hexMin) HexColumnWidth = hexMin;
                if (OrderColumnWidth < orderMin) OrderColumnWidth = orderMin;
                if (DelayColumnWidth < delayMin) DelayColumnWidth = delayMin;

                base.OnLayout(levent);
                int width = ClientSize.Width;
                int dataWidth = Math.Max(24, width - HexColumnWidth - SendColumnWidth - OrderColumnWidth - DelayColumnWidth);
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
                    int y = row * rowHeight;
                    if (c is Label)
                    {
                        // 表头限制在本列里，不再按文字宽度伸到旁边的栏目上。
                        c.SetBounds(x + 1, y, Math.Max(1, colWidth - 2), rowHeight);
                        continue;
                    }
                    if (col == 0 && c is CheckBox)
                    {
                        int boxWidth = Math.Min(c.Width, Math.Max(1, colWidth - 2));
                        int boxH = Math.Min(c.Height, rowHeight - 4);
                        c.SetBounds(x + (colWidth - boxWidth) / 2,
                            y + Math.Max(0, (rowHeight - boxH) / 2), boxWidth, boxH);
                        continue;
                    }
                    int w = Math.Max(1, colWidth - 2);
                    int h = c switch
                    {
                        TextBox box => box.PreferredHeight,
                        Button => buttonH,
                        _ => c.Height,
                    };
                    h = Math.Min(h, rowHeight - 4);
                    c.SetBounds(x + 1, y + Math.Max(0, (rowHeight - h) / 2), w, h);
                }
            }
            finally
            {
                _layouting--;
            }
        }
    }

    private Control BuildSendArea()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        _sendBox = new ThemeTextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = Theme.MonoFont(10F),
            Text = "abcdefg",            // 规格：发送框默认 ASC 内容
            Tag = "send",
        };
        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        textHost.Controls.Add(_sendBox);
        _sendButton = new ThemeButton
        {
            Text = "发  送",
            Dock = DockStyle.Right,
            Width = Math.Max(Theme.Scale(this, 72), TextRenderer.MeasureText("发送", Font).Width + Theme.Scale(this, 28)),
            Kind = ThemeButton.ButtonKind.Primary,
            Font = new Font(Font, FontStyle.Bold),
        };
        host.Controls.Add(textHost);
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
        _rxRateLabel = new ToolStripStatusLabel("RX 0 B/s") { AutoSize = true, ForeColor = Theme.Accent };
        _txRateLabel = new ToolStripStatusLabel("TX 0 B/s") { AutoSize = true, ForeColor = Theme.StatusSend };
        _bufferLabel = new ToolStripStatusLabel("缓冲 0%") { AutoSize = true, ForeColor = Theme.Muted };
        _connStatus.ForeColor = Theme.Text;
        _sentStatus.ForeColor = Theme.StatusSend;
        _recvStatus.ForeColor = Theme.Accent;
        _modemStatus.ForeColor = Theme.Muted;
        strip.Items.AddRange(new ToolStripItem[]
        {
            _connStatus, _sentStatus, _recvStatus, _modemStatus,
            _rxRateLabel, _txRateLabel, _bufferLabel,
        });
        Theme.StyleStatus(strip);
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
        _portField.PickerRequested += (_, _) => TogglePortPopup();
        _portField.ManualTextCommitted += CommitManualPort;
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
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            AutoSave();
        };
        WireMotion();
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
            RenderLogFromText(replace);             // 截断后的整屏重画（保留分色）
        }
        else if (text.Length > 0)
        {
            AppendRecv(text);
        }
        bool idle;
        lock (_displayLock)
        {
            idle = _pendingDisplay.Length == 0 && _pendingReplace is null;
            if (idle)
            {
                _drainScheduled = false;
                _displayTimer.Stop();
            }
        }
        // 高频接收时不要每 30ms 刷新整条状态栏；100ms 的状态计时器负责过程显示，停泵时补一次终值。
        if (idle) UpdateStatus();
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

        // 一批最多几十 KB；关掉重绘后只做一次最终 Invalidate，避免逐段着色时反复绘制整个文档。
        SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            AppendFormatted(text);
        }
        finally
        {
            SendMessage(_recvBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _recvBox.Invalidate();
        }

        if (_autoFollow && atBottom)
        {
            _recvBox.SelectionStart = _recvBox.TextLength;
            _recvBox.SelectionLength = 0;
            _recvBox.ScrollToCaret();
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
        if (!_recvBox.IsHandleCreated) return true;
        int len = _recvBox.TextLength;
        if (len == 0) return true;
        if (_recvBox.ClientSize.Height <= 0) return true;
        // 滚动条 nMax/nPage 的“底部”在各版本 RichEdit 里差一行，直接看最后一个字符是否在可见区。
        Point last = _recvBox.GetPositionFromCharIndex(len - 1);
        return last.Y >= 0 && last.Y < _recvBox.ClientSize.Height;
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
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int EM_SETCUEBANNER = 0x1501;

    private static void SetCueBanner(TextBox box, string text)
    {
        try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); } catch { }
    }

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
        string piece;
        if (_netMode == NetMode.TcpClient) piece = "TCPCLIENT";
        else if (_netMode == NetMode.TcpServer) piece = "TCPSERVER";
        else if (_netMode == NetMode.Udp) piece = "UDP";
        else
        {
            piece = _port.IsOpen ? _port.OpenedPortName : ComPortService.NormalizePortName(_portCombo.Text);
            if (piece.Length == 0) piece = "COM";
        }
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
        RenderLogBuffer();
    }

    private void ClearReceive()
    {
        ClearSearchHighlight();
        _recvBox.Clear();
        _receive.Clear();                     // 三个累积串与半字节状态一并清空
        Interlocked.Exchange(ref _sentCount, 0);   // 规格：清除窗口把两个计数清 0
        Interlocked.Exchange(ref _recvCount, 0);
        UpdateStatus();
        ShowToast("已清除接收区");
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
            string windowText = _recvBox.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            File.WriteAllBytes(txtPath, SendComposer.EncodeGbk(windowText));   // 窗口内容（原程序用 Lines.SaveToFile）
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

        // 十六进制无空格；文本按原程序的 ANSI（GBK）显示，丢弃 0x00
        var preview = SendComposer.FilePreview(data);
        _receive.SetPreviewBuffers(preview.Hex, preview.Text);

        string header = "文件大小:" + fullSize
                        + "字节,下面是预览的前" + data.Length + "字节内容:\r\n\r\n";
        AppendRecv(header + (_hexDisplayBox.Checked ? preview.Hex : preview.Text));
    }

    private void ToggleOpen()
    {
        if (_port.IsOpen)
        {
            _port.Close();
            _portOpenIntent = false;
        }
        else
        {
            OpenPort();
        }
        UpdateStatus();
    }

    private void OpenPort()
    {
        // 打开失败也保留“想保持打开”的意图，下一次换口/选择端口继续尝试。
        _portOpenIntent = true;
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

    /// <summary>串口已打开时换到另一个 COM 端口才需要关旧口再开新口。</summary>
    internal static bool ShouldReopenSerial(bool wantOpen, NetMode mode, string openedPort, string chosenText)
    {
        if (!wantOpen || mode != NetMode.Serial) return false;
        string opened = ComPortService.NormalizePortName(openedPort);
        string chosen = ComPortService.NormalizePortName(chosenText);
        return chosen.Length > 0 && !string.Equals(opened, chosen, StringComparison.OrdinalIgnoreCase);
    }

    private bool _portReopenScheduled;

    /// <summary>
    /// SelectionChangeCommitted / DropDownClosed 可能先于 ComboBox 更新 Text 触发；
    /// 延后到消息泵处理完选择，再按新文本换口，避免总是打开上一个端口。
    /// </summary>
    private void SchedulePortReopen()
    {
        if (_portReopenScheduled || IsDisposed || !IsHandleCreated) return;
        _portReopenScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _portReopenScheduled = false;
            if (!IsDisposed) ReopenPortIfSwitched();
        }));
    }

    private void ReopenPortIfSwitched()
    {
        bool wantOpen = _port.IsOpen || _portOpenIntent;
        if (!ShouldReopenSerial(wantOpen, _netMode, _port.OpenedPortName, _portCombo.Text)) return;
        if (_port.IsOpen) _port.Close();
        OpenPort();
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
        _statusStrip.BackColor = Theme.StatusBackground(_receive.Counter);

        // 复选框高亮（规格：勾选时黄，否则系统色；加回车换行在 HEX发送 勾选时不亮）
        Highlight(_hexDisplayBox, _hexDisplayBox.Checked);
        Highlight(_hexSendBox, _hexSendBox.Checked);
        Highlight(_timestampBox, _timestampBox.Checked);
        Highlight(_newLineBox, _newLineBox.Checked && !_hexSendBox.Checked);
        Highlight(_timerSendBox, _timerSendBox.Checked);
        Highlight(_recvToFileBox, _recvToFileBox.Checked);

        // 校验组：模式 None(0) 时白底、否则黄底（标签跟随）
        bool verifying = _verifyBox.SelectedIndex > 0;
        Color verifyBack = verifying ? Theme.VerifyField : Theme.Field;
        _verifyNumEdit.BackColor = verifyBack;
        _tailCombo.BackColor = verifyBack;
        _verifyBox.BackColor = verifyBack;
        _crcBox.BackColor = verifyBack;

        _timeoutLabel.Enabled = _timestampBox.Checked;           // 规格：超时标签随“加时间戳”
        _sentStatus.Text = $"S:{Interlocked.Read(ref _sentCount)}";
        _recvStatus.Text = $"R:{Interlocked.Read(ref _recvCount)}";
    }

    private static void Highlight(Control c, bool on)
    {
        if (c is ThemeCheckBox check)
        {
            check.Highlighted = on;
            check.Invalidate();
        }
        else
        {
            c.BackColor = on ? Theme.WithAlpha(30, Theme.Amber) : Theme.Canvas;
        }
    }

    // ———————————————————————— M5：多条字符串 ————————————————————————

    /// <summary>行内改动触发的自动保存（原程序这些交互都会点一次“保存参数”，M8 接入后转调）。</summary>
    private void AutoSave()
    {
        if (_loading) return;
        _autoSaveTimer.Stop();
        SaveConfig();
    }

    /// <summary>连续按键合并成一次保存。关窗时 OnMainFormClosing 会再强制写当前控件内容。</summary>
    private void ScheduleAutoSave()
    {
        if (_loading) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void AdjustMultiButtonWidth(int delta)
    {
        _multiBtnWidth = Fx.Clamp(_multiBtnWidth + delta, 20, 400);
        ApplySendColumnWidth();
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
        int shown = row % IniDocument.MultiRowsPerPage + 1;
        if (!InputComment(shown.ToString(), _rowComments[row], out string result)) return;
        _rowComments[row] = result;
        _rowSend[row].Text = result;
        _rowTip.SetToolTip(_rowData[row], result);
        AutoSave();
    }

    private bool InputComment(string title, string initial, out string result)
    {
        result = initial;
        int S(int value) => Theme.Scale(this, value);
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(S(300), S(100)),
            AutoScaleMode = AutoScaleMode.None,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
            BackColor = Theme.Canvas,
            ForeColor = Theme.Text,
        };
        var label = new Label { Text = "注释:", AutoSize = true, Location = new Point(S(10), S(12)), ForeColor = Theme.Muted, BackColor = Color.Transparent };
        var box = new ThemeTextBox { Text = initial, Location = new Point(S(10), S(34)), Width = S(276) };
        var ok = new ThemeButton { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(S(136), S(64)), Width = S(70), Kind = ThemeButton.ButtonKind.Primary };
        var cancel = new ThemeButton { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(S(216), S(64)), Width = S(70) };
        dlg.Controls.AddRange(new Control[] { label, box, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        Theme.ApplyWindow(dlg);
        Theme.StyleRecursive(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        result = box.Text;
        return true;
    }

    /// <summary>
    /// 循环发送勾选处理（规格 CheckBox6Click）：先关单条定时器；三页共 297 行按顺序号做稳定升序排序；
    /// 打开多条定时器（首拍等待用定时器原有间隔 1000）。
    /// </summary>
    private void OnLoopSendChanged()
    {
        if (_loopSendBox.Checked)
        {
            _sendStringTimer.Stop();                       // 规格：先把 SendStringTimer 关掉
            var pairs = new List<(int Order, int Row)>(IniDocument.MultiRowCount);
            for (int k = 0; k < IniDocument.MultiRowCount; k++)
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
    /// 连续跳过整张表自动取消“循环发送”。
    /// </summary>
    private void LoopSendTick()
    {
        if (!_loopSendBox.Checked) { _multiTimer.Stop(); return; }
        if (!ChannelCanSend()) return;                      // 串口未开或网络未就绪：本拍不动

        var input = new List<(int Order, string Data, int Row)>(_loopOrder.Count);
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

                for (int k = 0; k < _rowSend.Length; k++)
                {
                    if (_rowSend[k] is not ThemeButton button) continue;
                    button.Kind = ThemeButton.ButtonKind.Default;
                    button.Invalidate();
                }
                if (_rowSend[step.Row] is ThemeButton active)
                {
                    active.Kind = ThemeButton.ButtonKind.Outline;   // 当前发送行高亮
                    active.Invalidate();
                }
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
        SyncPortField();
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
            _portOpenIntent = false;
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
        bool serial = _netMode == NetMode.Serial;
        if (_serialSection is not null) _serialSection.Visible = serial;
        if (_netSection is not null) _netSection.Visible = !serial;
        if (_sideModeLabel is not null) _sideModeLabel.Text = serial ? "串口" : "网络";
        _sidePanel?.PerformLayout();
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
        if (_bodyLayout is not null)
        {
            _bodyLayout.ColumnStyles[0].Width = _sideCollapsed ? 0 : _sideWidth;
            _bodyLayout.ColumnStyles[1].Width = _sideCollapsed ? 0 : SideSplitterThickness();
        }
        if (_sideSplitter is not null) _sideSplitter.Visible = !_sideCollapsed;
        _sidePanel?.PerformLayout();
        UpdateComposerLayout();
        UpdateMinimumHeight();
        ScheduleResponsiveRelayout();
        SetCueBanner(_sendBox, "在此输入要发送的数据");
        _lastSentSample = Interlocked.Read(ref _sentCount);
        _lastRecvSample = Interlocked.Read(ref _recvCount);
        _rateTimer.Start();
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
        Dictionary<string, string> descriptions = ComPortService.PortDescriptions();
        var choices = new List<string>(ports.Count);
        foreach (string port in ports)
            choices.Add(ComPortService.FormatPortChoice(
                port, descriptions.TryGetValue(port, out string? desc) ? desc : null));

        _portCombo.Items.Clear();
        foreach (string choice in choices) _portCombo.Items.Add(choice);
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
            string caption = choices[i];
            var item = new ToolStripMenuItem(caption) { Tag = i };
            item.Click += (_, _) =>
            {
                foreach (var it in _protocolItems) it.Checked = false;
                foreach (var it in _comMenuItems) it.Checked = false;
                item.Checked = true;
                _portCombo.Text = item.Text;
                _portCombo.SelectedIndex = (int)item.Tag!;
                DetectMode();
                SchedulePortReopen();
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
                    _portCombo.Text = _portCombo.Items[match]?.ToString() ?? normalized;
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
        SyncPortField();
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
        for (int i = 0; i < 12 && _prewarmQueue.Count > 0; i++)
        {
            Control c = _prewarmQueue.Dequeue();
            try { _ = c.Handle; } catch { }
        }
        if (_prewarmQueue.Count == 0) _prewarmTimer.Stop();
    }

    private void SelectMultiPage(int page)
    {
        for (int i = 0; i < IniDocument.MultiPageCount; i++)
        {
            if (_multiPages[i] is not null) _multiPages[i].Visible = i == page;
            if (_multiTabs[i] is not null)
            {
                _multiTabs[i].Kind = i == page ? ThemeButton.ButtonKind.Outline : ThemeButton.ButtonKind.Default;
                _multiTabs[i].Invalidate();
            }
        }
        _multiPages[page]?.BringToFront();
    }

    private void ToggleMultiPanel()
    {
        CloseFlyout();
        _prewarmTimer.Stop();
        _prewarmQueue.Clear();
        // 规格：切换多条面板；标题在 显示态=隐藏 / 隐藏态=扩展 之间切换。
        bool show = _split.Panel2Collapsed;
        _hideToggleButton.Text = show ? "隐藏" : "扩展";
        bool freezeLog = _recvBox is { IsHandleCreated: true };
        bool followBottom = freezeLog && _autoFollow && RecvAtBottom();
        Point scroll = freezeLog ? GetRecvScroll() : Point.Empty;
        if (freezeLog) SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        // 一次性完成展开/收起；拖动分割线不由这里触发。
        _bodyLayout?.SuspendLayout();
        _split.SuspendLayout();
        _adjustingSplit = true;
        try
        {
            _split.Panel2Collapsed = !show;
        }
        finally
        {
            _adjustingSplit = false;
        }
        if (show) ApplyMultiWidth();
        _adjustingSplit = true;
        try
        {
            _bodyLayout?.ResumeLayout(true);
            _split.ResumeLayout(true);
        }
        finally
        {
            _adjustingSplit = false;
        }
        _bodyLayout?.PerformLayout();
        UpdateLogHeaderResponsive();
        if (freezeLog)
        {
            SendMessage(_recvBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _recvBox.Invalidate();
            if (followBottom)
            {
                _recvBox.SelectionStart = _recvBox.TextLength;
                _recvBox.SelectionLength = 0;
                _recvBox.ScrollToCaret();
            }
            else
            {
                SetRecvScroll(scroll);
            }
        }
    }

    private void ToggleBottomPanels()
    {
        if (_composerPanel is null) return;
        CloseFlyout();
        // 只隐藏发送文本框，保留设置行，━/■ 按钮始终可点。
        bool show = !_composerShown;
        _composerShown = show;
        _collapseButton.Text = show ? "━" : "■";
        _rowTip.SetToolTip(_collapseButton, show ? "收起发送区" : "展开发送区");
        UpdateComposerGridRows();
        UpdateComposerLayout();
        UpdateMinimumHeight();
    }

    /// <summary>切换深/浅主题；不自动保存，调用方决定（菜单点击会 AutoSave，启动/导入不保存）。</summary>
    private void ApplyTheme(bool light)
    {
        if (_lightThemeItem is not null) _lightThemeItem.Checked = light;
        Theme.ApplyTheme(this, light);

        _recvBox.BackColor = Theme.Log;
        _recvBox.ForeColor = Theme.Text;
        _sendBox.BackColor = Theme.Field;
        _sendBox.ForeColor = Theme.Text;
        _filePathBox.BackColor = Theme.Field;
        _filePathBox.ForeColor = Theme.Muted;
        _crcBox.BackColor = Theme.Field;
        _crcBox.ForeColor = Theme.Accent;
        if (_tailCombo is not null)
        {
            _tailCombo.ForeColor = Theme.Text;
            _tailCombo.Invalidate();
        }
        if (_verifyBox is not null)
        {
            _verifyBox.ForeColor = Theme.Text;
            _verifyBox.Invalidate();
        }

        _connStatus.ForeColor = Theme.Text;
        _sentStatus.ForeColor = Theme.StatusSend;
        _recvStatus.ForeColor = Theme.Accent;
        _modemStatus.ForeColor = Theme.Muted;
        _rxRateLabel.ForeColor = Theme.Accent;
        _txRateLabel.ForeColor = Theme.StatusSend;
        _bufferLabel.ForeColor = Theme.Muted;
        if (_recvBox.ContextMenuStrip is ContextMenuStrip logMenu) Theme.StyleContextMenu(logMenu);
        RenderLogAll();
        StatusTick();
        UpdateStatus();
    }

    /// <summary>下拉里可能带“COM38 · USB 串行设备”描述，逻辑和 ini 只用端口名。</summary>
    private string SelectedSerialPortName()
    {
        string normalized = ComPortService.NormalizePortName(_portCombo.Text);
        return normalized.Length > 0 ? normalized : _portCombo.Text;
    }

    private void SyncPortField()
    {
        if (_portField is null || _portCombo is null) return;
        _portField.SetDisplay(_portCombo.Text);
    }

    private void UpdateStatus()
    {
        if (_netMode == NetMode.Serial)
        {
            bool open = _port.IsOpen;
            string state = open ? " 已打开 " : " 已关闭 ";
            string portName = open ? _port.OpenedPortName : SelectedSerialPortName();
            int baud = ComPortService.BaudValueOf(_baudCombo.SelectedIndex, _port.CustomBaud);
            string baudText = _baudCombo.SelectedIndex <= 0 ? baud.ToString() : _baudCombo.Text;
            _connStatus.Text = $"{portName}{state}{baudText}bps,{_port.LineDescription()}";
            if (_logTitleLabel is not null)
                _logTitleLabel.Text = portName.Length > 0 ? portName : "未选择端口";
            if (_logSubLabel is not null)
                _logSubLabel.Text = $"{baudText}bps · {_port.LineDescription()}";
            _sideStatusCard?.SetStatus(open, open ? "已打开" : "未打开",
                $"{baudText} bps · {_port.LineDescription()}");
            if (_sideModeLabel is not null) _sideModeLabel.Text = "串口";
        }
        else
        {
            // 规格：网络模式面板 3 是整个全局串 0x4ED538（下拉文本 + 本机 IP 文本）
            _connStatus.Text = $"{_portCombo.Text} {_localIpCombo.Text}  {(_net.Ready ? "已连接" : "未连接")}";
            if (_logTitleLabel is not null) _logTitleLabel.Text = _portCombo.Text;
            if (_logSubLabel is not null)
                _logSubLabel.Text = $"{_remoteIpBox.Text}:{_remotePortBox.Text} · {(_net.Ready ? "已连接" : "未连接")}";
            _sideStatusCard?.SetStatus(_net.Ready, _net.Ready ? "已连接" : "未连接",
                $"{_portCombo.Text} · {_remoteIpBox.Text}:{_remotePortBox.Text}");
            if (_sideModeLabel is not null) _sideModeLabel.Text = "网络";
        }
        _sentStatus.Text = $"S:{Interlocked.Read(ref _sentCount)}";
        _recvStatus.Text = $"R:{Interlocked.Read(ref _recvCount)}";
        _openButton.Text = _port.IsOpen ? "关闭串口" : "打开串口";
        RefreshLogHeaderLayout();
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

    /// <summary>
    /// 显示菜单「字体/颜色/大小/...」（原 SpeedButton2Click）：把接收框当前字体和文字颜色放进字体对话框，
    /// 确定后应用到接收框。名称、字号、颜色写入 N1088–N1090，粗体/斜体/下划线/删除线写入 N1091。
    /// </summary>
    private void PickRecvFont()
    {
        // 对话框会把传入的 Font 交给 GDI+ ToLogFont。传入控件正在用的那份，
        // 一旦这份已经被释放，打开对话框就抛“参数无效”。这里先交给一份独立副本。
        Font current = LivingRecvFont();
        using var seed = new Font(current.Name, current.SizeInPoints, current.Style, GraphicsUnit.Point);
        using var dlg = new FontDialog();
        dlg.ShowEffects = true;
        dlg.ShowColor = true;
        dlg.Font = seed;
        dlg.Color = Color.FromArgb(255, _recvBox.ForeColor.R, _recvBox.ForeColor.G, _recvBox.ForeColor.B);
        try
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
        }
        catch (ArgumentException)
        {
            MessageBox.Show(this, "无法打开字体对话框。", "字体", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ApplyRecvFont(dlg.Font, dlg.Color);
        AutoSave();
    }

    /// <summary>
    /// 接收框字体若已被释放（Height 会抛“参数无效”），按还读得到的名字和字号重做一份。
    /// </summary>
    private Font LivingRecvFont()
    {
        Font current = _recvBox.Font;
        try
        {
            _ = current.Height;
            return current;
        }
        catch (ArgumentException)
        {
            float points = current.SizeInPoints;
            if (points < 1f || float.IsNaN(points) || float.IsInfinity(points)) points = 9f;
            var healed = new Font(current.Name, points, current.Style, GraphicsUnit.Point);
            _recvBox.Font = healed;
            return healed;
        }
    }

    /// <summary>
    /// 把字体和文字颜色装到接收框。调用方的 Font 不会被收走。
    /// 名称、字号、字形都没变时，TextBox 会拒绝换对象；这时不能释放原来那份，否则下次打开字体对话框会报“参数无效”。
    /// </summary>
    internal void ApplyRecvFont(Font source, Color color)
    {
        float points;
        try
        {
            points = source.SizeInPoints;
        }
        catch (ArgumentException)
        {
            _recvBox.ForeColor = color;
            RenderLogAll();
            return;
        }
        if (points < 1f || float.IsNaN(points) || float.IsInfinity(points)) points = 9f;

        Font next;
        try
        {
            next = new Font(source.Name, points, source.Style, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            _recvBox.ForeColor = color;
            RenderLogAll();
            return;
        }

        Font previous = _recvBox.Font;
        _recvBox.Font = next;
        _recvBox.ForeColor = color;
        if (!ReferenceEquals(_recvBox.Font, next))
        {
            next.Dispose();
            return;
        }
        if (!ReferenceEquals(previous, _recvBox.Font) && !ReferenceEquals(previous, Font))
            previous.Dispose();
        RenderLogAll();
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
        _rateTimer.Stop();
        _abortFlag = true;
        _loopSendBox.Checked = false;
        _timerSendBox.Checked = false;
        _sendStringTimer.Stop();
        _multiTimer.Stop();
        _autoSaveTimer.Stop();
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
                _ => SelectedSerialPortName() is { Length: > 0 } name ? name : "COM1",
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
            FontStyle = (int)_recvBox.Font.Style,
            FontColor = _recvBox.ForeColor.ToArgb(),
            ThemeLight = Theme.IsLight,
            SideCollapsed = _sideUserCollapsed,
            Language = "C",
        };
        if (_hexSendBox.Checked) snap.HexText = _sendBox.Text;
        else snap.AscText = _sendBox.Text;
        for (int i = 0; i < IniDocument.MultiRowCount; i++)
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
            if (Theme.IsLight != snap.ThemeLight)
                ApplyTheme(snap.ThemeLight);

            for (int i = 0; i < IniDocument.MultiRowCount; i++)
            {
                _rowOrder[i].Text = snap.Order[i];
                _rowComments[i] = snap.Comment[i];
                int shown = i % IniDocument.MultiRowsPerPage + 1;
                _rowSend[i].Text = snap.Comment[i].Length > 0 ? snap.Comment[i] : shown.ToString();
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
            // 旧 ini 的默认白底/黑字/雅黑 9 号迁移到深色主题的默认值；用户显式改过的颜色不动。
            int memoBack = snap.MemoBackColor == SystemColors.Window.ToArgb()
                ? Theme.Log.ToArgb()
                : snap.MemoBackColor;
            _recvBox.BackColor = Color.FromArgb(memoBack);
            Color fontColor;
            try
            {
                fontColor = snap.FontColor == SystemColors.WindowText.ToArgb()
                    ? Theme.Text
                    : Color.FromArgb(snap.FontColor);
            }
            catch
            {
                fontColor = Theme.Text;
            }
            _recvBox.ForeColor = fontColor;
            string fontName = snap.FontName;
            string fontSize = snap.FontSize;
            if (fontName == "Microsoft YaHei UI" && fontSize == "9")
            {
                // 旧版本的默认字体；深色主题下默认换成等宽字体
                fontName = "Consolas";
                fontSize = "10";
            }
            if (fontName.Length > 0 &&
                float.TryParse(fontSize, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float size) &&
                size > 0)
            {
                const int styleMask = (int)(FontStyle.Bold | FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout);
                var style = (FontStyle)(snap.FontStyle & styleMask);
                try
                {
                    using var loaded = new Font(fontName, size, style, GraphicsUnit.Point);
                    ApplyRecvFont(loaded, fontColor);
                }
                catch
                {
                    // 字体名在本机不存在时保持当前字体，键仍然按当前字体写回
                }
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

            // 515 左右是旧的固定宽度，展开时误记的超宽值也不当成用户的选择。
            int savedWidth = snap.MultiPanelWidth;
            bool legacyWidth = savedWidth is >= 480 and <= 580 || savedWidth <= 0;
            _multiWidthCustom = !legacyWidth;
            _multiPanelWidth = _multiWidthCustom ? savedWidth : 0;
            _multiBtnWidth = snap.MultiButtonWidth > 0 ? snap.MultiButtonWidth : 120;
            ApplyLoadedPort(snap.PortValue);
            _sideUserCollapsed = snap.SideCollapsed;
            _sideManualOverride = false;
            SetSidebarCollapsed(snap.SideCollapsed, persist: false, manual: false, automatic: false);
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
                string normalized = ComPortService.NormalizePortName(name);
                _portCombo.Text = name;
                int comboMatch = -1;
                for (int i = 0; i < _portCombo.Items.Count; i++)
                {
                    if (ComPortService.NormalizePortName(_portCombo.Items[i]?.ToString() ?? "") == normalized)
                    {
                        comboMatch = i;
                        break;
                    }
                }
                if (comboMatch >= 0)
                {
                    _portCombo.Text = _portCombo.Items[comboMatch]?.ToString() ?? name;
                    _portCombo.SelectedIndex = comboMatch;
                }
                int match = -1;
                for (int i = 0; i < _comMenuItems.Count; i++)
                {
                    if (ComPortService.NormalizePortName(_comMenuItems[i].Text ?? "") == normalized)
                    {
                        match = i;
                        break;
                    }
                }
                if (match >= 0) _comMenuItems[match].Checked = true;
                break;
        }
        DetectMode();
        SyncPortField();
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

    private void ApplySendColumnWidth()
    {
        int width = Fx.Clamp(_multiBtnWidth, 20, 400);
        foreach (MultiRowsPanel grid in _multiGrids)
        {
            if (grid is null) continue;
            grid.SendColumnWidth = width;
            grid.PerformLayout();
        }
    }

    private void ApplyMultiWidth()
    {
        if (_adjustingSplit) return;
        _adjustingSplit = true;
        try
        {
            ApplySendColumnWidth();
            int total = _split.ClientSize.Width;
            if (total < 300) return;
            try
            {
                if (_split.Panel2MinSize > 80) _split.Panel2MinSize = 80;
            }
            catch
            {
                // 窗口还太窄时设不了最小宽度，下一轮再试
            }
            // 没拖过：约 340 逻辑像素（宽窗口下比四分之一窄）。拖过：用记下的像素。
            int want2 = _multiWidthCustom
                ? _multiPanelWidth
                : Math.Max(Math.Max(320, Theme.Scale(this, 340)), total / 4);
            if (want2 > total / 2)
            {
                _multiWidthCustom = false;
                want2 = Math.Max(160, total / 4);
            }
            if (!_multiWidthCustom) _multiPanelWidth = want2;
            want2 = Fx.Clamp(want2, 80, Math.Max(80, total - 160));
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
