using System.Drawing.Drawing2D;
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
    private ContextMenuStrip _saveMenu = null!;
    private string _saveDataDirectory = "";
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
    private Panel _logHost = null!;
    private TextBox _sendBox = null!;
    private Button _sendButton = null!;
    private CheckBox _hexDisplayBox = null!;
    private CheckBox _hexSendBox = null!;
    private CheckBox _newLineBox = null!;
    private CheckBox _autoScrollBox = null!;
    private CheckBox _timestampBox = null!;
    private CheckBox _recvToFileBox = null!;
    private TextBox _timeoutEdit = null!;
    private TextBox _filePathBox = null!;
    private string _recvFileName = "";
    // 接收存文件保持常开：每包 open/close 在高速接收时是最大的一笔固定开销。
    private readonly object _recvFileGate = new();
    private FileStream? _recvFileStream;
    private string _recvFilePath = "";
    private ThemeCheckBox _recvHtmlBox = null!;
    private ThemeTextBox _recvHtmlDirBox = null!;
    private ThemeComboBox _recvHtmlSplitCombo = null!;
    private ThemeComboBox _recvHtmlPaletteCombo = null!;
    private ThemeCheckBox _recvHtmlTxBox = null!;
    private Label _recvHtmlStatus = null!;
    private Button _recvHtmlPickButton = null!;
    private HtmlLogRecorder? _htmlRecorder;
    private bool _suppressHtmlCheck;
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
    private readonly LogHighlighter _logHighlighter = new();
    private readonly List<LogHighlightRule> _highlightRules = new();
    private CommandPalette _commandPalette = null!;
    private CommandPaletteDismissFilter? _commandPaletteFilter;
    private readonly List<int> _recentCommands = new();
    private Control? _commandPaletteReturnFocus;
    private readonly MultiRowsPanel[] _multiGrids = new MultiRowsPanel[IniDocument.MultiPageCount];
    private readonly Panel[] _multiPages = new Panel[IniDocument.MultiPageCount];
    private readonly ThemeButton[] _multiTabs = new ThemeButton[IniDocument.MultiPageCount];
    private readonly ThemeButton?[] _multiCloseTabs = new ThemeButton?[IniDocument.MultiPageCount];
    private Panel _multiPageHost = null!;
    // 多条表格的行高/列宽：首次展开某一页时才按这些尺寸建那 99 行。
    private int _multiRowHeight = 27;
    private int _multiHexCol = 40;
    private int _multiOrderCol = 56;
    private int _multiDelayCol = 72;
    private int _multiSendCol = 120;
    private ThemeButton _multiAddTab = null!;
    private ThemeChip _multiPageChip = null!;
    private Label _multiTitleLabel = null!;
    private Label _multiSubLabel = null!;
    private Label _multiWidthValue = null!;
    private Label _multiFooterPage = null!;
    private Label _multiFooterHint = null!;
    private Label _multiFooterCount = null!;
    private int _multiPageCount = 1;                    // N1094：可见页数，第 1 页始终存在
    private int _multiPage;                             // 当前选中的页下标
    private IniSnapshot? _multiRowSource;               // 最近一次加载的整张多条表，未建页时从这里补
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
    private LogPortPicker _logPortPicker = null!;
    private ThemeButton _logOpenButton = null!;
    private Label _logSubLabel = null!;
    private Control? _portPopupAnchor;
    private bool _portOpenIntent;
    private Panel _composerPanel = null!;
    private TableLayoutPanel _composerGrid = null!;
    private TableLayoutPanel _centerLayout = null!;
    private string _ascSendText = "abcdefg";
    private string _hexSendText = "12 FF 00 30";

    public MainForm()
    {
        Text = $"串口/网络数据调试器(定制版) v{AppVersion}";
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
        SizeChanged += (_, _) =>
        {
            HideToast();
            CloseFlyout();
            ClosePortPopup(false);
            CloseCommandPalette();
            ScheduleResponsiveRelayout();
            ScheduleScrollRecvToLatest();
        };
        DpiChanged += (_, _) =>
        {
            LockPauseButtonWidth();
            ScheduleScrollRecvToLatest();
        };
        Activated += (_, _) => ScheduleScrollRecvToLatest();
        Move += (_, _) =>
        {
            HideToast();
            CloseFlyout();
            ClosePortPopup(false);
            CloseCommandPalette();
            ScheduleScrollRecvToLatest();
        };
        menu.MenuActivate += (_, _) => { CloseFlyout(); ClosePortPopup(false); CloseCommandPalette(); };

        Controls.Add(root);
        Controls.Add(menu);
        MainMenuStrip = menu;
        BuildSettingsFlyouts();
        BuildPortPopup();
        BuildCommandPalette();
        BuildToast();
        ResumeLayout();
    }

    private int LineHeight() => Math.Max(28, Font.Height + 14);

    internal static string AppVersion
    {
        get
        {
            Version? version = typeof(MainForm).Assembly.GetName().Version;
            return version is null ? "1.6.0" : $"{version.Major}.{version.Minor}.{version.Build}";
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
        display.DropDownItems.Add(new ToolStripMenuItem("日志高亮规则...", null, (_, _) => ShowHighlightRules()));
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
        send.DropDownItems.Add(new ToolStripMenuItem("命令面板", null, (_, _) => ToggleCommandPalette())
        {
            ShortcutKeyDisplayString = "Ctrl+K",
        });

        var multi = new ToolStripMenuItem("多条字符串", null, (_, _) => ToggleMultiPanel());
        var tools = new ToolStripMenuItem("工具");
        _gnssItem = new ToolStripMenuItem("GNSS", null, (_, _) => ToggleGnssWindow());
        tools.DropDownItems.Add(_gnssItem);
        _mainTraceItem = new ToolStripMenuItem("Trace监控", null, (_, _) => ToggleMainTraceWindow())
        {
            ToolTipText = "被动解析主日志：设备快照、上行/下发报文与业务事件",
        };
        tools.DropDownItems.Add(_mainTraceItem);
        var help = new ToolStripMenuItem("帮助");
        help.DropDownItems.Add(new ToolStripMenuItem("关于", null, (_, _) =>
            MessageBox.Show(this,
                $"串口/网络数据调试器(定制版)\n版本 v{AppVersion}\nC# / .NET Framework 4.8，依据 sscom5.13.1-rewrite-spec.md 的行为规则实现。",
                $"关于 sscomRe v{AppVersion}", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new ToolStripItem[] { comm, portCfg, display, send, multi, tools, help });
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
        var commandPaletteButton = ActionButton("命令面板", ToggleCommandPalette);
        _rowTip.SetToolTip(commandPaletteButton, "搜索并发送已保存命令（Ctrl+K）");
        Put(panel, commandPaletteButton);
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
        _autoScrollBox = Cb("自动滚动", true);
        _rowTip.SetToolTip(_autoScrollBox, "勾选时日志自动滚到最新。取消后仍继续接收，只是不再跳到最后一行。");
        _autoScrollBox.CheckedChanged += (_, _) => ScheduleScrollRecvToLatest();
        Put(row, _autoScrollBox);
        var rowMargin = new Padding(3, 2, 2, 2);
        _pauseButton = new ThemeButton { Text = "暂停显示", AutoSize = true, Compact = true };
        _pauseButton.Click += (_, _) => ToggleDisplayPause();
        _pauseButton.HandleCreated += (_, _) => LockPauseButtonWidth();
        Put(row, _pauseButton);
        _pauseButton.Margin = rowMargin;
        var clearWindow = new ThemeButton { Text = "清除窗口", AutoSize = true, Compact = true };
        clearWindow.Click += (_, _) => ClearReceive();
        _rowTip.SetToolTip(clearWindow, "清除窗口、计数，以及还没显示的内容");
        Put(row, clearWindow);
        clearWindow.Margin = rowMargin;
        var saveData = new ThemeButton
        {
            Text = "保存数据 ▾",
            AutoSize = true,
            Compact = true,
            Kind = ThemeButton.ButtonKind.Soft,
        };
        saveData.Click += (_, _) => ShowSaveMenu(saveData);
        _rowTip.SetToolTip(saveData, "保存数据（TXT + DAT），或另存为带颜色日志（RTF / HTML）");
        Put(row, saveData);
        saveData.Margin = rowMargin;
        SyncDisplayPauseButton();

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
        public RowFlowPanel()
        {
            DoubleBuffered = true;              // 行内控件改文字/状态时不再闪一下
        }

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

    /// <summary>多条字符串面板：标题/工具条/页签/表格卡/页脚，每页 99 行。</summary>
    private Control BuildMultiPanel()
    {
        int headerH = Math.Max(52, Theme.Scale(this, 56));
        int toolsH = Math.Max(34, Theme.Scale(this, 36));
        int pageBarH = Math.Max(34, Theme.Scale(this, 38));
        int footerH = Math.Max(24, Theme.Scale(this, 26));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        _multiPanel = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, headerH));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, toolsH));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, pageBarH));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, footerH));
        root.SuspendLayout();

        // —— 标题栏：logo、标题、页数胶囊、导入 ——
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(Theme.Scale(this, 12), Theme.Scale(this, 5), Theme.Scale(this, 10), Theme.Scale(this, 5)),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft, 1);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        int logoSize = Math.Max(26, Theme.Scale(this, 30));
        var logo = new AppLogo
        {
            Width = logoSize,
            Height = logoSize,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, Theme.Scale(this, 10), 0),
        };
        brand.Controls.Add(logo, 0, 0);

        var titleBlock = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0) };
        _multiTitleLabel = new Label
        {
            Text = "多条字符串",
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Theme.Text,
            Font = new Font(Font.FontFamily, Font.Size + 1.8F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };
        _multiSubLabel = new Label
        {
            Text = "命令序列 · 每页 99 条",
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Theme.Muted,
            Font = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 0.5F)),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };
        titleBlock.Controls.Add(_multiTitleLabel);
        titleBlock.Controls.Add(_multiSubLabel);
        int titleH = Math.Max(18, Font.Height + 4);
        int subH = Math.Max(14, Font.Height);
        titleBlock.Resize += (_, _) =>
        {
            int top = Math.Max(0, (titleBlock.Height - titleH - subH) / 2);
            _multiTitleLabel.SetBounds(0, top, titleBlock.Width, titleH);
            _multiSubLabel.SetBounds(0, top + titleH, titleBlock.Width, subH);
        };
        brand.Controls.Add(titleBlock, 1, 0);
        header.Controls.Add(brand, 0, 0);

        _multiPageChip = new ThemeChip
        {
            Width = Theme.Scale(this, 96),
            Height = Math.Max(22, Font.Height + 8),
            Font = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 0.5F)),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(Theme.Scale(this, 6), 0, Theme.Scale(this, 8), 0),
        };
        header.Controls.Add(_multiPageChip, 1, 0);

        _importIniButton = new ThemeButton
        {
            Text = "导入 ini",
            AutoSize = true,
            Kind = ThemeButton.ButtonKind.Default,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        _importIniButton.Click += (_, _) => ImportIni();
        header.Controls.Add(_importIniButton, 2, 0);
        root.Controls.Add(header, 0, 0);

        // —— 工具条：循环开关、发送按钮宽度、顺序提示 ——
        var tools = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface2,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(Theme.Scale(this, 12), 0, Theme.Scale(this, 12), 0),
        };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface2,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        _loopSendBox = new ThemeSwitch { Text = "循环发送", AutoSize = true, Margin = new Padding(0, 7, 8, 0) };
        toolLeft.Controls.Add(_loopSendBox);
        toolLeft.Controls.Add(new Panel
        {
            Width = 1,
            Height = 16,
            BackColor = Theme.BorderSoft,
            Margin = new Padding(0, 10, 10, 0),
        });
        toolLeft.Controls.Add(new Label
        {
            Text = "发送按钮宽度",
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 2, 0),
        });
        int stepSize = Math.Max(22, Theme.Scale(this, 24));
        var minus = new ThemeButton
        {
            Text = "−",
            Width = stepSize,
            Height = stepSize,
            Kind = ThemeButton.ButtonKind.Icon,
            Margin = new Padding(0, 6, 0, 0),
        };
        minus.Click += (_, _) => AdjustMultiButtonWidth(-10);
        var plus = new ThemeButton
        {
            Text = "＋",
            Width = stepSize,
            Height = stepSize,
            Kind = ThemeButton.ButtonKind.Icon,
            Margin = new Padding(0, 6, 0, 0),
        };
        plus.Click += (_, _) => AdjustMultiButtonWidth(+10);
        _multiWidthValue = new Label
        {
            Text = _multiBtnWidth.ToString(),
            AutoSize = false,
            Width = Theme.Scale(this, 36),
            Height = stepSize,
            Font = Theme.MonoFont(Math.Max(8F, Font.Size)),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 6, 2, 0),
        };
        toolLeft.Controls.Add(minus);
        toolLeft.Controls.Add(_multiWidthValue);
        toolLeft.Controls.Add(plus);
        tools.Controls.Add(toolLeft, 0, 0);
        root.Controls.Add(tools, 0, 1);

        // —— 页签：胶囊分段；第 1 页无删除入口，页 2/3 可增删 ——
        var pageBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(Theme.Scale(this, 12), Theme.Scale(this, 3), Theme.Scale(this, 12), 0),
        };
        root.Controls.Add(pageBar, 0, 2);

        int contentH;
        using (var probe = new ThemeTextBox { Font = Font })
            contentH = probe.PreferredHeight;
        int headerH2 = TextRenderer.MeasureText("字符串(双击注释)", Font).Height + 8;
        _multiRowHeight = Math.Max(contentH, headerH2) + 8;
        _multiHexCol = Math.Max(36, TextRenderer.MeasureText("HEX", Font).Width + 16);
        _multiOrderCol = Math.Max(48, TextRenderer.MeasureText("顺序", Font).Width + 16);
        _multiDelayCol = Math.Max(64, Math.Max(
            TextRenderer.MeasureText("延时ms", Font).Width,
            TextRenderer.MeasureText("1000", Font).Width) + 18);
        _multiSendCol = Math.Max(_multiBtnWidth, TextRenderer.MeasureText("点击发送", Font).Width + 16);

        var cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.Scale(this, 10), Theme.Scale(this, 4), Theme.Scale(this, 10), Theme.Scale(this, 4)),
            Margin = new Padding(0),
        };
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
        _multiPageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0) };
        card.Controls.Add(_multiPageHost);
        cardHost.Controls.Add(card);
        root.Controls.Add(cardHost, 0, 3);

        int tabH = Math.Max(26, Font.Height + 10);
        for (int page = 0; page < IniDocument.MultiPageCount; page++)
        {
            var tab = new ThemeButton
            {
                Text = $"第{page + 1}页",
                AutoSize = false,
                Width = Math.Max(64, TextRenderer.MeasureText($"第{page + 1}页", Font).Width + Theme.Scale(this, 22)),
                Height = tabH,
                Margin = new Padding(0, 2, 2, 0),
                Kind = page == 0 ? ThemeButton.ButtonKind.SegmentSelected : ThemeButton.ButtonKind.Segment,
            };
            int selected = page;
            tab.Click += (_, _) => SelectMultiPage(selected);
            pageBar.Controls.Add(tab);
            _multiTabs[page] = tab;

            if (page > 0)
            {
                var close = new ThemeButton
                {
                    Text = "×",
                    Kind = ThemeButton.ButtonKind.Icon,
                    AutoSize = false,
                    Width = tabH,
                    Height = tabH,
                    Margin = new Padding(0, 2, 4, 0),
                };
                close.Click += (_, _) => RequestRemoveMultiPage(selected);
                _rowTip.SetToolTip(close, "删除本页");
                pageBar.Controls.Add(close);
                _multiCloseTabs[page] = close;
            }
        }
        // 第 1 页立即建；第 2/3 页第一次切到（或点＋）时再建那 99 行，启动省掉约 2/3 控件。
        EnsureMultiPageBuilt(0);

        _multiAddTab = new ThemeButton
        {
            Text = "＋",
            Kind = ThemeButton.ButtonKind.Icon,
            AutoSize = false,
            Width = tabH,
            Height = tabH,
            Margin = new Padding(3, 2, 0, 0),
        };
        _multiAddTab.Click += (_, _) => AddMultiPage();
        _rowTip.SetToolTip(_multiAddTab, "增加一页（最多 3 页）");
        pageBar.Controls.Add(_multiAddTab);

        // —— 页脚 ——
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(Theme.Scale(this, 14), 0, Theme.Scale(this, 14), 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft, 1);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _multiFooterPage = new Label
        {
            Text = "第 1 页",
            AutoSize = true,
            ForeColor = Theme.Accent,
            Font = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 0.5F), FontStyle.Bold),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, Theme.Scale(this, 8), 0),
        };
        _multiFooterHint = new Label
        {
            Text = "顺序 0 不发送 · 双击格子编辑注释",
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Theme.Dim,
            Font = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 0.5F)),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        _multiFooterCount = new Label
        {
            Text = "99 条 / 页",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 0.5F)),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0),
        };
        footer.Controls.Add(_multiFooterPage, 0, 0);
        footer.Controls.Add(_multiFooterHint, 1, 0);
        footer.Controls.Add(_multiFooterCount, 2, 0);
        root.Controls.Add(footer, 0, 4);

        root.ResumeLayout(true);
        SelectMultiPage(0);
        return root;
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
        void AddHeader(string text, int col, string? tip = null)
        {
            var label = HeaderLabel(text);
            label.Tag = new Point(0, col);
            label.MouseEnter += (_, _) => grid.SetHoverRow(0);
            label.MouseLeave += (_, _) => grid.ClearHoverIfOutside();
            if (tip is not null) _rowTip.SetToolTip(label, tip);
            grid.Controls.Add(label);
        }
        AddHeader("HEX", 0);
        AddHeader("字符串", 1, "双击字符串格编辑注释");
        AddHeader("点击发送", 2);
        AddHeader("顺序", 3);
        AddHeader("延时ms", 4);

        void Track(Control control, int displayRow)
        {
            control.MouseEnter += (_, _) => grid.SetHoverRow(displayRow);
            control.MouseLeave += (_, _) => grid.ClearHoverIfOutside();
            if (control is TextBox box)
            {
                box.Enter += (_, _) => grid.SetFocusCell(box);
                box.Leave += (_, _) => grid.SetFocusCell(null);
            }
        }

        Font mono = Theme.MonoFont(Math.Max(8F, Font.Size));
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
            Track(cb, shown);

            var data = new ThemeTextBox
            {
                Tag = new Point(k + 1, 1),
                BorderStyle = BorderStyle.None,
                Font = mono,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
            };
            data.KeyPress += (_, e) =>
            {
                if (e.KeyChar == (char)1) { data.SelectAll(); e.Handled = true; }   // Ctrl+A 全选
                else ScheduleAutoSave();                                            // 其余键防抖保存，避免每键重写整份 ini
            };
            data.DoubleClick += (_, _) => EditRowComment(row);
            _rowData[row] = data;
            grid.Controls.Add(data);
            Track(data, shown);

            var send = new ThemeButton
            {
                Text = shown.ToString(),
                Tag = new Point(k + 1, 2),
                Kind = ThemeButton.ButtonKind.Soft,
                Padding = new Padding(2, 0, 2, 0),
            };
            send.Click += (_, _) => SendRow(row);
            _rowSend[row] = send;
            grid.Controls.Add(send);
            Track(send, shown);

            var order = new ThemeTextBox
            {
                Text = "0",
                Tag = new Point(k + 1, 3),
                BorderStyle = BorderStyle.None,
                Font = mono,
                TextAlign = HorizontalAlignment.Right,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
            };
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
            Track(order, shown);

            var time = new ThemeTextBox
            {
                Text = "1000",
                Tag = new Point(k + 1, 4),
                BorderStyle = BorderStyle.None,
                Font = mono,
                TextAlign = HorizontalAlignment.Right,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
            };
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
            Track(time, shown);

            _rowComments[row] = shown.ToString();          // 注释默认行号（按钮面板与提示同源）
            _rowTip.SetToolTip(data, _rowComments[row]);
        }

        grid.MouseMove += (_, e) => grid.SetHoverRowFromPoint(e.Location);
        grid.MouseLeave += (_, _) => grid.ClearHoverIfOutside();
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
        BackColor = Color.Transparent,
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

    /// <summary>多条面板的圆角表格卡。</summary>
    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var back = new SolidBrush(Theme.Canvas);
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using GraphicsPath path = Theme.RoundedRect(bounds, Theme.Scale(this, 12));
            using (var fill = new SolidBrush(Theme.Surface))
                g.FillPath(fill, path);
            using var pen = new Pen(Theme.BorderSoft, 1);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>标题栏里的 exe 图标。</summary>
    private sealed class AppLogo : Control
    {
        public AppLogo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.FillBackground(g, this);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Rectangle target = new(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            g.DrawImage(Theme.AppIconBitmap, target);
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
        private int _hoverRow = -1;
        private Control? _focusCell;

        public MultiRowsPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
        }

        public void SetHoverRow(int row)
        {
            if (_hoverRow == row) return;
            int old = _hoverRow;
            _hoverRow = row;
            InvalidateRow(old);
            InvalidateRow(row);
        }

        public void SetHoverRowFromPoint(Point point)
        {
            int row = RowHeight <= 0 ? -1 : point.Y / RowHeight;
            SetHoverRow(row is >= 1 and <= 99 ? row : -1);
        }

        public void ClearHoverIfOutside()
        {
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) SetHoverRow(-1);
        }

        public void SetFocusCell(Control? cell)
        {
            if (ReferenceEquals(_focusCell, cell)) return;
            Control? old = _focusCell;
            _focusCell = cell;
            InvalidateCell(old);
            InvalidateCell(cell);
        }

        private void InvalidateRow(int row)
        {
            if (row is < 1 or > 99 || RowHeight <= 0) return;
            Invalidate(new Rectangle(0, row * RowHeight, Width, RowHeight));
        }

        private void InvalidateCell(Control? cell)
        {
            if (cell is null || cell.IsDisposed) return;
            Invalidate(Rectangle.Inflate(cell.Bounds, 5, 5));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            int rh = Math.Max(1, RowHeight);
            int headerH = Math.Min(rh, Height);
            using (var back = new SolidBrush(Theme.Surface))
                g.FillRectangle(back, ClientRectangle);
            using (var header = new SolidBrush(Theme.Surface2))
                g.FillRectangle(header, 0, 0, Width, headerH);

            Color stripe = Theme.Mix(Theme.Surface, Theme.Surface2, Theme.IsLight ? 0.36F : 0.28F);
            int first = Math.Max(1, clip.Top / rh);
            int last = Math.Min(99, clip.Bottom / rh + 1);
            int barWidth = Math.Max(1, Theme.Scale(this, 2));
            using (var stripeBrush = new SolidBrush(stripe))
            using (var hoverBrush = new SolidBrush(Theme.WithAlpha(Theme.IsLight ? 14 : 20, Theme.Accent)))
            using (var hoverBar = new SolidBrush(Theme.WithAlpha(180, Theme.Accent)))
            {
                for (int row = first; row <= last; row++)
                {
                    int y = row * rh;
                    if (row % 2 == 0)
                        g.FillRectangle(stripeBrush, 0, y, Width, rh);
                    if (row == _hoverRow)
                    {
                        g.FillRectangle(hoverBrush, 0, y, Width, rh);
                        g.FillRectangle(hoverBar, 0, y + 5, barWidth, Math.Max(1, rh - 10));
                    }
                }
            }

            using (var line = new Pen(Theme.WithAlpha(26, Theme.Border)))
            {
                for (int row = first; row <= last; row++)
                {
                    int y = row * rh - 1;
                    if (y >= clip.Top && y <= clip.Bottom)
                        g.DrawLine(line, 0, y, Width, y);
                }
            }
            if (headerH > 0)
            {
                using var headerLine = new Pen(Theme.BorderSoft);
                g.DrawLine(headerLine, 0, headerH - 1, Width, headerH - 1);
                using var accent = new SolidBrush(Theme.WithAlpha(150, Theme.Accent));
                g.FillRectangle(accent, HexColumnWidth + 4, Math.Max(0, headerH - 3),
                    Math.Max(8, Theme.Scale(this, 16)), Math.Max(1, Theme.Scale(this, 2)));
            }

            using var fieldBrush = new SolidBrush(Theme.Field);
            using var borderPen = new Pen(Theme.BorderSoft, 1);
            using var focusPen = new Pen(Theme.AccentBright, Math.Max(2, Theme.Scale(this, 2)));
            foreach (Control c in Controls)
            {
                if (c is not TextBox || c.Tag is not Point cell || cell.Y is not (1 or 3 or 4)) continue;
                if (cell.X < first || cell.X > last || !c.Visible) continue;
                Rectangle field = Rectangle.Inflate(c.Bounds, 2, 2);
                if (!clip.IntersectsWith(field)) continue;
                using GraphicsPath path = Theme.RoundedRect(field, Theme.Scale(this, 7));
                g.FillPath(fieldBrush, path);
                g.DrawPath(ReferenceEquals(c, _focusCell) ? focusPen : borderPen, path);
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
                int rowHeight = Math.Max(Math.Max(textH, buttonH), headerH) + 8;
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
                        c.SetBounds(x + 3, y, Math.Max(1, colWidth - 6), rowHeight);
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
                    bool textField = c is TextBox;
                    int w = Math.Max(1, colWidth - (textField ? 6 : 4));
                    int h = c switch
                    {
                        TextBox box => box.PreferredHeight,
                        Button => buttonH,
                        _ => c.Height,
                    };
                    h = Math.Min(h, rowHeight - 4);
                    c.SetBounds(x + (textField ? 3 : 2), y + Math.Max(0, (rowHeight - h) / 2), w, h);
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
        _portField.PickerRequested += (_, _) => TogglePortPopup(_portField);
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
        if (_snapGnss) _nmea.Push(data);
        if (_mainTraceEnabled) _mainTrace.Push(data);
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
        // 整屏重画进行中：先不动接收框，数据留在队列里，画完再追平。
        if (_renderBusy) return;
        TakePending(out string text, out string? replace);
        if (_displayPaused)
        {
            if (text.Length > 0 || replace is not null)
                AbsorbDisplayHold(text, replace, recordHtml: true);
            StopDisplayTimerIfIdle();
            return;
        }
        if (replace is not null)
        {
            RenderLogFromText(replace);             // 截断后的整屏重画（保留分色）
            if (text.Length > 0)
                EnqueueDisplay(text, null);         // 截断之后才到的段，等重画完再补上
        }
        else if (text.Length > 0)
        {
            AppendRecv(text, recordHtml: true);
        }
        StopDisplayTimerIfIdle();
    }

    private void TakePending(out string text, out string? replace)
    {
        lock (_displayLock)
        {
            text = _pendingDisplay.ToString();
            _pendingDisplay.Clear();
            replace = _pendingReplace;
            _pendingReplace = null;
        }
    }

    private void StopDisplayTimerIfIdle()
    {
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
    /// 追加接收内容：勾选“自动滚动”时始终显示最新内容；取消勾选才保留当前光标/滚动位置。
    /// </summary>
    private int _htmlSkip;

    private void NoteHtml(string text)
    {
        if (text.Length == 0 || _htmlRecorder is null) return;
        if (_htmlSkip > 0)
        {
            int skip = Math.Min(_htmlSkip, text.Length);
            _htmlSkip -= skip;
            if (skip < text.Length)
                _htmlRecorder.Append(text.Substring(skip));
            return;
        }
        _htmlRecorder.Append(text);
    }

    private void AppendRecv(string text, bool recordHtml = true)
    {
        if (text.Length == 0) return;
        // 整屏重画还没画完时先排队。这段要等真正画上时再记，避免排进去和画出来各记一次。
        if (_renderBusy)
        {
            EnqueueDisplay(text, null);
            return;
        }
        if (recordHtml) NoteHtml(text);
        if (_displayPaused)
        {
            AbsorbDisplayHold(text, null);
            return;
        }
        if (!_recvBox.IsHandleCreated)
        {
            _recvBox.AppendText(text);
            return;
        }

        bool follow = _autoScrollBox.Checked;
        int selStart = 0;
        int selLen = 0;
        Point scroll = Point.Empty;
        if (!follow)
        {
            selStart = _recvBox.SelectionStart;
            selLen = _recvBox.SelectionLength;
            scroll = GetRecvScroll();
        }

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

        if (follow)
        {
            EnsureRecvAtLatest();
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
        lock (_recvFileGate)
        {
            try
            {
                if (_recvFileStream is null ||
                    !string.Equals(_recvFilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _recvFileStream?.Dispose();
                    _recvFileStream = null;
                    _recvFilePath = path;
                    _recvFileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                }
                _recvFileStream.Write(data, 0, data.Length);
            }
            catch
            {
                // 打开/写入失败时按原行为静默丢弃，并释放句柄等下一次重试
                try { _recvFileStream?.Dispose(); } catch { }
                _recvFileStream = null;
            }
        }
    }

    /// <summary>关闭接收文件句柄（取消勾选或退出时）。</summary>
    private void CloseRecvFile()
    {
        lock (_recvFileGate)
        {
            try { _recvFileStream?.Dispose(); } catch { }
            _recvFileStream = null;
            _recvFilePath = "";
        }
    }

    private void OnRecvToFileChanged()
    {
        if (!_recvToFileBox.Checked)
        {
            CloseRecvFile();
            return;
        }
        string piece = CurrentTransferName();
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
        if (_timestampBox.Checked && LogHasTimestampMarkers())
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

    /// <summary>只在控件内原地查找标记，避免为判断一次把整段 1M 字符复制成字符串。</summary>
    private bool LogHasTimestampMarkers()
    {
        CompletePendingRenderLog();
        if (!_recvBox.IsHandleCreated)
            return _recvBox.Text.Contains('←') || _recvBox.Text.Contains('→');
        if (_recvBox.TextLength == 0) return false;
        return _recvBox.Find("←", 0, RichTextBoxFinds.None) >= 0
            || _recvBox.Find("→", 0, RichTextBoxFinds.None) >= 0;
    }

    private void ClearReceive()
    {
        CancelRenderLog();                    // 终止未完成的整屏重画并恢复重绘
        DiscardUnpainted();                   // 停止显示期间积压的内容一并丢掉
        ClearSearchHighlight();
        _recvBox.Clear();
        _receive.Clear();                     // 三个累积串与半字节状态一并清空
        Interlocked.Exchange(ref _sentCount, 0);   // 规格：清除窗口把两个计数清 0
        Interlocked.Exchange(ref _recvCount, 0);
        UpdateStatus();
        ShowToast("已清除接收区");
    }

    /// <summary>保存数据：选定目录后直接写 SaveWindows 时间戳 .TXT 和 SAVE 时间戳 .DAT。</summary>
    private void SaveData()
    {
        CompletePendingRenderLog();                        // 保存的是完整窗口内容，不能是半屏
        string initial = _saveDataDirectory.Length > 0 && Directory.Exists(_saveDataDirectory)
            ? _saveDataDirectory
            : Directory.GetCurrentDirectory();
        string? directory = SaveDataDialog.Choose(this, initial);
        if (directory is null) return;
        _saveDataDirectory = directory;
        string dt = SendComposer.FileTimestamp();
        string datPath = Path.Combine(directory, "SAVE" + dt + ".DAT");
        string txtPath = Path.Combine(directory, "SaveWindows" + dt + ".TXT");

        try
        {
            File.WriteAllBytes(txtPath, SendComposer.EncodeGbk(LogicalWindowText()));   // 含停止显示期间还没画上的内容
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
            MessageBox.Show(this, "目录不可写!请另选一个目录试试.", "保存数据",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(this,
            "收到的原始数据已保存,文件名:\r\n" + datPath
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
            _mainTrace.Reset();
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
        _mainTrace.Reset();
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
            _mainTrace.Reset();
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
        _logOpenButton.Enabled = false;         // 发送文件期间不允许从日志头关串口

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
        _logOpenButton.Enabled = true;
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
            {
                string text = $"CTS={(m.Cts ? 1 : 0)} DSR={(m.Dsr ? 1 : 0)} RLSD={(m.Rlsd ? 1 : 0)}";
                if (_modemStatus.Text != text) _modemStatus.Text = text;
            }
        }
        else if (_modemStatus.Text != "CTS=- DSR=- RLSD=-")
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
        Highlight(_recvHtmlBox, _recvHtmlBox.Checked);
        UpdateHtmlCaptureStatus();

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
            if (check.Highlighted == on) return;   // 100ms 轮询，状态没变就别重绘
            check.Highlighted = on;
            check.Invalidate();
        }
        else
        {
            Color back = on ? Theme.WithAlpha(30, Theme.Amber) : Theme.Canvas;
            if (c.BackColor != back) c.BackColor = back;
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
        UpdateMultiWidthLabel();
        AutoSave();
    }

    /// <summary>行内“点击发送”按钮：数据空则什么都不做；否则用该行的 HEX 开关发送。</summary>
    private void SendRow(int row)
    {
        string data = _rowData[row].Text;
        if (data.Length == 0) return;
        SendPayload(data, _rowHex[row].Checked);
        RememberRecentCommand(row);
    }

    /// <summary>双击数据框输入注释（标题=行号）；确定后写入提示与行按钮标题并自动保存。</summary>
    private void EditRowComment(int row)
    {
        int shown = row % IniDocument.MultiRowsPerPage + 1;
        if (!InputComment(shown.ToString(), _rowComments[row], out string result)) return;
        _rowComments[row] = result;
        _rowSend[row].Text = result;
        _rowTip.SetToolTip(_rowData[row], result);
        StyleSendButton(row);
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
                pairs.Add((_rowOrder[k] is null ? 0 : ParseIntOr0(_rowOrder[k].Text), k));   // 未建页按 0 不发送
            _loopOrder.Clear();
            _loopOrder.AddRange(pairs.OrderBy(t => t.Order));   // 冒泡“大于才交换”= 稳定升序
            _planner.StartFlag = true;                     // 0x4ED64E = 1
            _multiTimer.Start();
        }
        else
        {
            _multiTimer.Stop();
            ClearMultiSendHighlight();
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
            input.Add((order, _rowData[row]?.Text ?? "", row));    // 顺序号用快照，数据实时读

        MultiSendPlanner.Step step = _planner.Tick(input);
        switch (step.Kind)
        {
            case MultiSendPlanner.Kind.Stop:
                _loopSendBox.Checked = false;               // 触发停表
                return;
            case MultiSendPlanner.Kind.None:
                return;
            case MultiSendPlanner.Kind.Send:
                TextBox? stepData = _rowData[step.Row];
                if (stepData is null) return;               // 该行所在页还没建，跳过
                int iv = int.TryParse(_rowTime[step.Row]?.Text.Trim(), out var p) ? p : 1000;
                _multiTimer.Interval = Fx.Clamp(iv, 1, 99999);   // 先改间隔（WinForms 间隔必须 ≥1）

                SendPayload(stepData.Text, _rowHex[step.Row].Checked);   // 再发送该行

                for (int k = 0; k < _rowSend.Length; k++)
                {
                    if (_rowSend[k] is not ThemeButton button) continue;
                    button.Kind = ThemeButton.ButtonKind.Soft;
                    button.Invalidate();
                }
                if (_rowSend[step.Row] is ThemeButton active)
                {
                    active.Kind = ThemeButton.ButtonKind.SegmentSelected;   // 当前发送行高亮
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
        _mainTrace.Reset();
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
        _mainTrace.Reset();
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
            if (!_port.IsOpen) return;
            _port.Write(data);
        }
        else if (_net.Ready)
        {
            _net.Send(data, _netMode, _remoteIpBox.Text.Trim(), ParsePort(_remotePortBox.Text, 8080));
        }
        else
        {
            return;
        }
        // 本机发送单独计数，不能算作设备下发或设备上报
        if (_mainTraceEnabled) _mainTrace.NoteTx(data);
        // 发送数据直接进入持续记录；显示回显是否开启不影响这里
        if (_htmlRecorder is not null)
        {
            string content = _snapHexDisplay
                ? SendComposer.HexText(data)
                : SendComposer.EscapeForDisplay(data);
            _htmlRecorder.AppendTx(content);
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

    /// <summary>某一页的多条表格第一次用到时才建：99 行 × 6 个控件，三页提前建代价太大。</summary>
    private void EnsureMultiPageBuilt(int page)
    {
        if (page < 0 || page >= IniDocument.MultiPageCount) return;
        if (_multiPageHost is null || _multiPages[page] is not null) return;
        MultiRowsPanel grid = BuildMultiGrid(page, _multiRowHeight, _multiHexCol, _multiOrderCol, _multiDelayCol, _multiSendCol);
        _multiGrids[page] = grid;
        var scroll = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Surface,
            Visible = false,
        };
        scroll.Controls.Add(grid);
        _multiPageHost.Controls.Add(scroll);
        _multiPages[page] = scroll;
        if (_multiRowSource is not null)
        {
            int origin = page * IniDocument.MultiRowsPerPage;
            for (int i = origin; i < origin + IniDocument.MultiRowsPerPage; i++)
                ApplyMultiRow(i, _multiRowSource);
        }
    }

    private void SelectMultiPage(int page)
    {
        if (_multiPageCount <= 0) return;
        _multiPage = Fx.Clamp(page, 0, _multiPageCount - 1);
        EnsureMultiPageBuilt(_multiPage);       // 首次切到某页时才展开它的 99 行
        Panel? host = _multiPageHost;
        bool freeze = host is { IsHandleCreated: true };
        if (freeze) SendMessage(host!.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        host?.SuspendLayout();
        try
        {
            for (int i = 0; i < IniDocument.MultiPageCount; i++)
            {
                bool exists = i < _multiPageCount;
                if (_multiTabs[i] is not null)
                {
                    _multiTabs[i].Visible = exists;
                    _multiTabs[i].Kind = i == _multiPage
                        ? ThemeButton.ButtonKind.SegmentSelected
                        : ThemeButton.ButtonKind.Segment;
                    _multiTabs[i].Invalidate();
                }
                if (_multiCloseTabs[i] is not null) _multiCloseTabs[i]!.Visible = exists;
                Panel? pagePanel = _multiPages[i];
                bool want = exists && i == _multiPage;
                if (pagePanel is not null && pagePanel.Visible != want) pagePanel.Visible = want;
            }
        }
        finally
        {
            host?.ResumeLayout(true);
            if (freeze)
            {
                SendMessage(host!.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                host.Invalidate(true);
            }
        }
        if (_multiAddTab is not null) _multiAddTab.Enabled = _multiPageCount < IniDocument.MultiPageCount;
        _multiPages[_multiPage]?.BringToFront();
        UpdateMultiPageChrome();
    }

    private void UpdateMultiPageChrome()
    {
        if (_multiPageChip is null) return;
        string label = $"{_multiPage + 1} / {_multiPageCount}";
        _multiPageChip.Text = label;
        _multiPageChip.Width = Math.Max(Theme.Scale(this, 48),
            TextRenderer.MeasureText(label, _multiPageChip.Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Theme.Scale(this, 22));
        _multiPageChip.Invalidate();
        if (_multiFooterPage is not null) _multiFooterPage.Text = $"第 {_multiPage + 1} 页";
    }

    private void UpdateMultiWidthLabel()
    {
        if (_multiWidthValue is not null) _multiWidthValue.Text = _multiBtnWidth.ToString();
    }

    private void ApplyMultiTheme()
    {
        if (_multiTitleLabel is not null) _multiTitleLabel.ForeColor = Theme.Text;
        if (_multiSubLabel is not null) _multiSubLabel.ForeColor = Theme.Muted;
        if (_multiWidthValue is not null) _multiWidthValue.ForeColor = Theme.Text;
        if (_multiFooterPage is not null) _multiFooterPage.ForeColor = Theme.Accent;
        if (_multiFooterHint is not null) _multiFooterHint.ForeColor = Theme.Dim;
        if (_multiFooterCount is not null) _multiFooterCount.ForeColor = Theme.Muted;
        _multiPageChip?.Invalidate();
        foreach (var grid in _multiGrids) grid?.Invalidate();
    }

    private void AddMultiPage()
    {
        if (_multiPageCount >= IniDocument.MultiPageCount) return;
        _multiPageCount++;
        SelectMultiPage(_multiPageCount - 1);
        AutoSave();
    }

    private void RequestRemoveMultiPage(int page)
    {
        if (page <= 0 || page >= _multiPageCount) return;
        string shift = page < _multiPageCount - 1 ? "\n后面的页会依次前移。" : "";
        DialogResult answer = MessageBox.Show(this,
            $"删除第{page + 1}页？\n\n该页 99 行的内容将被清除。{shift}\n\n此操作不可撤销。",
            "删除本页", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) RemoveMultiPage(page);
    }

    private void RemoveMultiPage(int page)
    {
        if (page <= 0 || page >= _multiPageCount) return;
        if (_loopSendBox.Checked) _loopSendBox.Checked = false;
        _loopOrder.Clear();
        ClearMultiSendHighlight();

        int last = _multiPageCount - 1;
        for (int p = page; p < last; p++) CopyMultiPage(p + 1, p);
        ClearMultiPage(last);
        _multiPageCount--;
        int selected = _multiPage > page ? _multiPage - 1 : _multiPage;
        SelectMultiPage(Math.Min(selected, _multiPageCount - 1));
        AutoSave();
    }

    private void CopyMultiPage(int fromPage, int toPage)
    {
        EnsureMultiPageBuilt(fromPage);
        EnsureMultiPageBuilt(toPage);
        int from = fromPage * IniDocument.MultiRowsPerPage;
        int to = toPage * IniDocument.MultiRowsPerPage;
        for (int k = 0; k < IniDocument.MultiRowsPerPage; k++)
        {
            int src = from + k, dst = to + k;
            _rowOrder[dst].Text = _rowOrder[src].Text;
            _rowTime[dst].Text = _rowTime[src].Text;
            _rowHex[dst].Checked = _rowHex[src].Checked;
            _rowData[dst].Text = _rowData[src].Text;
            _rowComments[dst] = _rowComments[src];
            _rowSend[dst].Text = _rowSend[src].Text;
            _rowTip.SetToolTip(_rowData[dst], _rowComments[dst]);
            StyleSendButton(dst);
        }
    }

    private void ClearMultiPage(int page)
    {
        EnsureMultiPageBuilt(page);
        int origin = page * IniDocument.MultiRowsPerPage;
        for (int k = 0; k < IniDocument.MultiRowsPerPage; k++)
        {
            int row = origin + k;
            string shown = (k + 1).ToString();
            _rowOrder[row].Text = "0";
            _rowTime[row].Text = "1000";
            _rowHex[row].Checked = false;
            _rowData[row].Text = "";
            _rowComments[row] = shown;
            _rowSend[row].Text = shown;
            _rowTip.SetToolTip(_rowData[row], shown);
            StyleSendButton(row);
        }
    }

    private void StyleSendButton(int row)
    {
        if (_rowSend[row] is not ThemeButton button) return;
        int shown = row % IniDocument.MultiRowsPerPage + 1;
        string comment = _rowComments[row] ?? shown.ToString();
        button.AccentText = comment.Length > 0 && comment != shown.ToString();
        if (button.Kind is ThemeButton.ButtonKind.Default or ThemeButton.ButtonKind.Outline)
            button.Kind = ThemeButton.ButtonKind.Soft;
        button.Invalidate();
    }

    /// <summary>把快照里的一行套到控件上；该行所在页还没建时直接跳过。</summary>
    private void ApplyMultiRow(int row, IniSnapshot snap)
    {
        if (_rowOrder[row] is null) return;
        _rowOrder[row].Text = snap.Order[row];
        _rowComments[row] = snap.Comment[row];
        int shown = row % IniDocument.MultiRowsPerPage + 1;
        _rowSend[row].Text = snap.Comment[row].Length > 0 ? snap.Comment[row] : shown.ToString();
        _rowTip.SetToolTip(_rowData[row], snap.Comment[row]);
        _rowTime[row].Text = snap.Delay[row];
        _rowHex[row].Checked = snap.Hex[row];
        _rowData[row].Text = snap.Data[row];
        StyleSendButton(row);
    }

    private void ClearMultiSendHighlight()
    {
        foreach (var button in _rowSend)
        {
            if (button is not ThemeButton theme || theme.Kind == ThemeButton.ButtonKind.Soft) continue;
            theme.Kind = ThemeButton.ButtonKind.Soft;
            theme.Invalidate();
        }
    }

    internal int MultiPageCountForTest => _multiPageCount;
    internal int MultiPageSelectedForTest => _multiPage;
    internal bool MultiPageVisibleForTest(int page) =>
        page >= 0 && page < IniDocument.MultiPageCount && _multiPages[page] is { Visible: true };
    internal bool MultiPageCloseVisibleForTest(int page) =>
        page >= 0 && page < IniDocument.MultiPageCount && _multiCloseTabs[page] is { Visible: true };
    internal void SetMultiPageCountForTest(int count)
    {
        _multiPageCount = Fx.Clamp(count, 1, IniDocument.MultiPageCount);
        SelectMultiPage(0);
    }
    internal void AddMultiPageForTest() => AddMultiPage();
    internal void RemoveMultiPageForTest(int page) => RemoveMultiPage(page);
    internal void SetMultiPageDataForTest(int page, int row, string text) =>
        _rowData[page * IniDocument.MultiRowsPerPage + row].Text = text;
    internal string MultiPageDataForTest(int page, int row) =>
        _rowData[page * IniDocument.MultiRowsPerPage + row].Text;

    private void ToggleMultiPanel()
    {
        CloseFlyout();
        // 页 2/3 默认隐藏，首次显示前仍需建句柄；不要因展开面板而中断预热。
        // 规格：切换多条面板；标题在 显示态=隐藏 / 隐藏态=扩展 之间切换。
        bool show = _split.Panel2Collapsed;
        _hideToggleButton.Text = show ? "隐藏" : "扩展";
        bool freezeLog = _recvBox is { IsHandleCreated: true };
        bool followBottom = freezeLog && _autoScrollBox.Checked;
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
        CloseCommandPalette();
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
        _gnssWindow?.RefreshTheme();
        _mainTraceWindow?.RefreshTheme();
        if (_recvBox.ContextMenuStrip is ContextMenuStrip logMenu) Theme.StyleContextMenu(logMenu);
        if (_saveMenu is not null) Theme.StyleContextMenu(_saveMenu);
        // 配色已经换好，先让窗口重画；日志就是 1M 字符，重画改为按块异步，点击立即有反馈。
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                RenderLogAll();
                StatusTick();
                UpdateStatus();
                ApplyMultiTheme();
            }));
        }
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
            _logPortPicker?.SetDisplay(portName.Length > 0 ? portName : "未选择端口");
            if (_logOpenButton is not null)
            {
                string label = open ? "关闭" : "打开";
                if (_logOpenButton.Text != label) _logOpenButton.Text = label;
                ThemeButton.ButtonKind kind = open ? ThemeButton.ButtonKind.Outline : ThemeButton.ButtonKind.Primary;
                if (_logOpenButton.Kind != kind)
                {
                    _logOpenButton.Kind = kind;
                    _logOpenButton.Invalidate();
                }
                if (_logOpenButton.Enabled != _openButton.Enabled) _logOpenButton.Enabled = _openButton.Enabled;
                if (!_logOpenButton.Visible) _logOpenButton.Visible = true;
                _rowTip.SetToolTip(_logOpenButton, open ? "关闭串口" : "打开串口");
            }
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
            _logPortPicker?.SetDisplay(_portCombo.Text);
            if (_logOpenButton is { Visible: true }) _logOpenButton.Visible = false;   // 网络模式没有串口开关
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

    private void ShowHighlightRules()
    {
        string recentLog = _recvBox.Text;
        if (recentLog.Length > 12000) recentLog = recentLog.Substring(recentLog.Length - 12000);
        List<LogHighlightRule>? next = HighlightRulesDialog.Show(
            this, _highlightRules, Theme.IsLight, recentLog);
        if (next is null) return;
        _highlightRules.Clear();
        _highlightRules.AddRange(next);
        _logHighlighter.SetRules(_highlightRules);
        RenderLogBuffer();
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
        try { _htmlRecorder?.Dispose(); } catch { /* 关闭时写 footer 失败不影响退出 */ }
        _htmlRecorder = null;
        try { _mainTraceWindow?.Close(); } catch { /* 面板计时器停止失败不阻塞退出 */ }
        CloseRecvFile();                       // 先把接收文件 flush/关闭再存参数
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
            AutoScroll = _autoScrollBox.Checked,
            HighlightRulesJson = LogHighlightRuleStore.ToJson(_highlightRules),
            RecvHtmlDir = _recvHtmlDirBox.Text,
            RecvHtmlSplitMb = HtmlSplitMb(),
            RecvHtmlLight = _recvHtmlPaletteCombo.SelectedIndex == 0,
            RecvHtmlIncludeTx = _recvHtmlTxBox.Checked,
            RecvHtmlSettingsVersion = HtmlSettingsVersion,
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
            MultiPageCount = _multiPageCount,
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
            if (_rowOrder[i] is not null)
            {
                snap.Order[i] = _rowOrder[i].Text;
                snap.Comment[i] = _rowComments[i];
                snap.Delay[i] = _rowTime[i].Text;
                snap.Hex[i] = _rowHex[i].Checked;
                snap.Data[i] = _rowData[i].Text;
                continue;
            }
            // 这一页还没建：沿用加载时的数据，隐藏页不会因为按需建页而丢内容。
            int shown = i % IniDocument.MultiRowsPerPage + 1;
            snap.Order[i] = _multiRowSource?.Order[i] ?? "0";
            snap.Comment[i] = _multiRowSource?.Comment[i] ?? shown.ToString();
            snap.Delay[i] = _multiRowSource?.Delay[i] ?? "1000";
            snap.Hex[i] = _multiRowSource?.Hex[i] ?? false;
            snap.Data[i] = _multiRowSource?.Data[i] ?? "";
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

            _multiRowSource = snap;
            for (int i = 0; i < IniDocument.MultiRowCount; i++)
                ApplyMultiRow(i, snap);             // 未建的页没有控件，等切到那一页再套数据
            _multiPageCount = Fx.Clamp(snap.MultiPageCount, 1, IniDocument.MultiPageCount);
            SelectMultiPage(0);
            List<LogHighlightRule> rules;
            if (string.IsNullOrWhiteSpace(snap.HighlightRulesJson))
            {
                rules = LogHighlightRuleStore.CreateDefaults(Theme.IsLight);
            }
            else
            {
                List<LogHighlightRule> stored = LogHighlightRuleStore.FromJson(
                    snap.HighlightRulesJson, out int rulesVersion);
                rules = LogHighlightRuleStore.Upgrade(stored, Theme.IsLight, rulesVersion);
            }
            _highlightRules.Clear();
            _highlightRules.AddRange(rules);
            _logHighlighter.SetRules(_highlightRules);

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
            _autoScrollBox.Checked = snap.AutoScroll;
            if (_htmlRecorder is not null)
            {
                _suppressHtmlCheck = true;
                _recvHtmlBox.Checked = false;
                _suppressHtmlCheck = false;
                StopHtmlCapture(showToast: false);
            }
            _recvHtmlDirBox.Text = snap.RecvHtmlDir;
            _recvHtmlSplitCombo.SelectedIndex = NormalizeRecvHtmlSplitMb(
                snap.RecvHtmlSplitMb, snap.RecvHtmlSettingsVersion) switch
            {
                0 => 0,
                10 => 1,
                25 => 2,
                100 => 4,
                _ => 3,
            };
            _recvHtmlPaletteCombo.SelectedIndex = snap.RecvHtmlLight ? 0 : 1;
            _recvHtmlTxBox.Checked = snap.RecvHtmlIncludeTx;
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
        UpdateMultiWidthLabel();
        foreach (MultiRowsPanel grid in _multiGrids)
        {
            if (grid is null) continue;
            if (grid.SendColumnWidth == width) continue;   // 窗口拉伸时列宽没变，不必重排 99 行
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
