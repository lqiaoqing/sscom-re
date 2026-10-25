using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// Phase 2 工作区：左连接栏、中间日志+发送、右多条抽屉。
/// 现有行为和字段保持不变，这里只负责重新摆放控件。
/// </summary>
public sealed partial class MainForm
{
    private int _composerHeight;

    // ———————————————————————— 左连接栏 ————————————————————————

    private Control BuildSidebar()
    {
        _sidePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface2,
            AutoScroll = true,
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface2,
            Padding = new Padding(12, 10, 12, 8),
            Margin = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        void Add(Control control, int gap = 6)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, gap);
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
            row++;
        }

        int headerButton = Math.Max(30, Font.Height + 8);
        var head = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, headerButton + 4));
        head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var headText = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Top,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        headText.Controls.Add(new Label
        {
            Text = "连接",
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold),
            Margin = new Padding(0, 0, 8, 0),
        });
        _sideModeLabel = new Label
        {
            Text = "串口",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 2, 0, 0),
        };
        headText.Controls.Add(_sideModeLabel);
        var collapse = new ThemeButton
        {
            Text = "«",
            Width = headerButton,
            Height = headerButton,
            Kind = ThemeButton.ButtonKind.Icon,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        collapse.Click += (_, _) => SetSidebarCollapsed(true, persist: true, manual: true, automatic: false);
        _rowTip.SetToolTip(collapse, "收起连接栏");
        head.Controls.Add(headText, 0, 0);
        head.Controls.Add(collapse, 1, 0);
        Add(head, 7);

        Add(SideLabel("端口号"), 3);
        var portRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
        };
        int refreshSize = Math.Max(30, Font.Height + 10);
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, refreshSize + 4));
        _portCombo = new ThemeComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDown,
            Margin = new Padding(0),
        };
        var refresh = new ThemeButton
        {
            Text = "↻",
            Dock = DockStyle.Top,
            Width = refreshSize,
            Height = refreshSize,
            Kind = ThemeButton.ButtonKind.Icon,
            Margin = new Padding(4, 0, 0, 0),
        };
        refresh.Click += (_, _) => RefreshPortList();
        _rowTip.SetToolTip(refresh, "刷新串口列表");
        portRow.Controls.Add(_portCombo, 0, 0);
        portRow.Controls.Add(refresh, 1, 0);
        Add(portRow, 8);

        _serialSection = BuildSerialSection();
        _netSection = BuildNetSection();
        Add(_serialSection, 0);
        Add(_netSection, 0);

        Add(new Label
        {
            Text = "SSCOM 重写版 · C#",
            AutoSize = true,
            ForeColor = Theme.Dim,
            Margin = new Padding(0, 12, 0, 0),
        }, 0);

        _sideContent = layout;
        _railPanel = BuildRail();
        _sidePanel.Controls.Add(layout);
        _sidePanel.Controls.Add(_railPanel);
        _railPanel.Visible = _sideCollapsed;
        layout.Visible = !_sideCollapsed;
        _sidePanel.Resize += (_, _) => FitSidebar(layout);
        _sidePanel.Layout += (_, _) => FitSidebar(layout);
        return _sidePanel;
    }

    private void FitSidebar(Control layout)
    {
        if (_sidePanel is null || _sideContent is null || !_sideContent.Visible) return;
        int scroll = _sidePanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        int width = Math.Max(160, _sidePanel.ClientSize.Width - scroll);
        if (layout.Width != width) layout.Width = width;
    }

    private int RailWidth() => Math.Max(48, Theme.Scale(this, 52));

    private int RailButtonHeight() => Math.Max(34, Font.Height + 10);

    private Panel BuildRail()
    {
        var rail = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface2 };
        var table = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface2,
            Padding = new Padding(4, 8, 4, 6),
            Margin = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Add(Control control, int gap = 6)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, gap);
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control, 0, row);
            row++;
        }

        _railExpand = new ThemeButton
        {
            Text = "»",
            Height = RailButtonHeight(),
            Kind = ThemeButton.ButtonKind.Icon,
        };
        _railExpand.Click += (_, _) => SetSidebarCollapsed(false, persist: true, manual: true, automatic: false);
        _rowTip.SetToolTip(_railExpand, "展开连接栏");
        Add(_railExpand, 8);

        _railDot = new Label
        {
            Text = "●",
            AutoSize = false,
            Height = Math.Max(16, Font.Height - 6),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Green,
            BackColor = Color.Transparent,
        };
        Add(_railDot, 2);
        _railModeLabel = new Label
        {
            Text = "串口",
            AutoSize = false,
            Height = Font.Height + 2,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
        };
        Add(_railModeLabel, 2);
        _railPortLabel = new Label
        {
            Text = "COM1",
            AutoSize = false,
            Height = Font.Height + 2,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Text,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };
        Add(_railPortLabel, 10);
        _railOpen = new ThemeButton { Text = "开", Height = RailButtonHeight() };
        _railOpen.Click += (_, _) => ToggleOpen();
        Add(_railOpen, 0);

        rail.Controls.Add(table);
        return rail;
    }

    private void UpdateSidebarRail()
    {
        if (_railOpen is null) return;
        bool serial = _netMode == NetMode.Serial;
        bool connected = serial ? _port.IsOpen : _net.Ready;
        _railDot.ForeColor = connected ? Theme.Green : Theme.Dim;
        _railModeLabel.Text = serial ? "串口" : "网络";
        string port = serial
            ? (_port.IsOpen ? _port.OpenedPortName : ComPortService.NormalizePortName(_portCombo.Text))
            : _portCombo.Text;
        _railPortLabel.Text = port.Length > 0 ? port : "—";
        _railModeLabel.ForeColor = Theme.Muted;
        _railPortLabel.ForeColor = Theme.Text;
        _railOpen.Visible = serial;
        _railOpen.Text = _port.IsOpen ? "关" : "开";
        _rowTip.SetToolTip(_railOpen, _port.IsOpen ? "关闭串口" : "打开串口");
    }

    private void SetSidebarCollapsed(bool collapsed, bool persist, bool manual, bool automatic)
    {
        if (_sidePanel is null) return;
        _sideCollapsed = collapsed;
        if (manual)
        {
            _sideManualOverride = true;
            _sideAutoCollapsed = false;
            _sideUserCollapsed = collapsed;
        }
        else if (automatic)
        {
            _sideAutoCollapsed = collapsed;
        }
        else
        {
            _sideAutoCollapsed = false;
        }
        if (_sideContent is not null) _sideContent.Visible = !collapsed;
        if (_railPanel is not null) _railPanel.Visible = collapsed;
        _sidePanel.AutoScroll = !collapsed;
        if (_bodyLayout is not null)
            _bodyLayout.ColumnStyles[0].Width = collapsed ? RailWidth() : SidebarWidth();
        _sidePanel.PerformLayout();
        _bodyLayout?.PerformLayout();
        UpdateSidebarRail();
        if (persist) AutoSave();
    }

    private TableLayoutPanel BuildSerialSection()
    {
        var section = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Add(Control control, int gap = 6)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, gap);
            section.RowCount = row + 1;
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.Controls.Add(control, 0, row);
            row++;
        }

        Add(SideSectionTitle("线路参数"), 4);
        Add(SideLabel("波特率"), 3);
        int unit = TextUnit();
        _baudCombo = new ThemeComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var b in ComPortService.BaudItems) _baudCombo.Items.Add(b);
        _baudCombo.SelectedIndex = 13;
        Add(_baudCombo, 6);

        _customBaudBox = new ThemeTextBox { Text = _port.CustomBaud.ToString(), Visible = false };
        _customBaudBox.VisibleChanged += (_, _) =>
        {
            _serialSection?.PerformLayout();
            ScheduleResponsiveRelayout();
        };
        Add(_customBaudBox, 5);

        _rtsBox = new ThemeCheckBox { Text = "RTS", AutoSize = true };
        _dtrBox = new ThemeCheckBox { Text = "DTR", AutoSize = true };
        var flags = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Dock = DockStyle.Top,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
        };
        flags.Controls.Add(_rtsBox);
        flags.Controls.Add(_dtrBox);
        Add(flags, 5);

        _lineInfoLabel = new Label
        {
            Text = "未打开",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 6),
        };
        Add(_lineInfoLabel, 4);

        int buttonH = Math.Max(30, Font.Height + 12);
        _openButton = new ThemeButton
        {
            Text = "打开串口",
            Dock = DockStyle.Top,
            Height = buttonH,
            Kind = ThemeButton.ButtonKind.Primary,
            Margin = new Padding(0, 0, 0, 4),
        };
        Add(_openButton, 4);

        _moreSettingsButton = new ThemeButton
        {
            Text = "更多串口设置",
            Dock = DockStyle.Top,
            Height = buttonH,
            Margin = new Padding(0),
        };
        _moreSettingsButton.Click += (_, _) => ShowPortSettings();
        Add(_moreSettingsButton, 0);

        return section;
    }

    private TableLayoutPanel BuildNetSection()
    {
        var section = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Visible = false,
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Add(Control control, int gap = 5)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, gap);
            section.RowCount = row + 1;
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.Controls.Add(control, 0, row);
            row++;
        }

        Add(SideSectionTitle("网络参数"), 4);
        int unit = TextUnit();
        _remoteIpBox = new ThemeTextBox { Text = "" };
        Add(SideLabel("远程 IP"), 3);
        Add(_remoteIpBox, 5);
        _remotePortBox = new ThemeTextBox { Text = "8080" };
        Add(SideLabel("远程端口"), 3);
        Add(_remotePortBox, 7);
        _localIpCombo = new ThemeComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown };
        Add(SideLabel("本地 IP"), 3);
        Add(_localIpCombo, 5);
        _localPortBox = new ThemeTextBox { Text = "8080" };
        Add(SideLabel("本地端口"), 3);
        Add(_localPortBox, 7);

        int buttonH = Math.Max(30, Font.Height + 12);
        _connectButton = new ThemeButton
        {
            Text = "连接",
            AutoSize = false,
            Width = unit * 6,
            Height = buttonH,
            Kind = ThemeButton.ButtonKind.Primary,
            Margin = new Padding(0, 0, 6, 0),
        };
        _disconnectButton = new ThemeButton
        {
            Text = "断开",
            AutoSize = false,
            Width = unit * 6,
            Height = buttonH,
            Margin = new Padding(0),
        };
        _connectButton.Click += (_, _) => ConnectNetwork();
        _disconnectButton.Click += (_, _) => DisconnectNetwork();
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Dock = DockStyle.Top,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
        };
        actions.Controls.Add(_connectButton);
        actions.Controls.Add(_disconnectButton);
        Add(actions, 0);

        return section;
    }

    private static Label SideLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    private Label SideSectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Dim,
        BackColor = Color.Transparent,
        Font = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 0.5F)),
        Margin = new Padding(0),
    };

    // ———————————————————————— 中间工作区 ————————————————————————

    private Control BuildCenterPhase2()
    {
        _recvBox = new ThemeRichTextBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.MonoFont(10F),
            Tag = "log",
        };
        _recvBox.ContextMenuStrip = BuildLogContextMenu();

        var logBar = BuildLogBar();
        var composer = BuildComposer();

        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Log,
        };
        _centerLayout = center;
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, LogBarHeight()));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, ComposerHeight()));

        var recvHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        recvHost.Controls.Add(_recvBox);
        center.Controls.Add(logBar, 0, 0);
        center.Controls.Add(recvHost, 0, 1);
        center.Controls.Add(composer, 0, 2);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Theme.Border,
        };
        _split.Panel1.BackColor = Theme.Log;
        _split.Panel2.BackColor = Theme.Surface;
        _split.Panel1.Controls.Add(center);
        _split.Panel2.Controls.Add(BuildMultiPanel());
        _split.Panel2Collapsed = true;
        _split.Resize += (_, _) =>
        {
            if (!_split.Panel2Collapsed) ApplyMultiWidth();
        };
        _split.SplitterMoving += (_, _) =>
        {
            if (!_adjustingSplit) _userMovingSplitter = true;
        };
        _split.SplitterMoved += (_, _) =>
        {
            if (!_userMovingSplitter || _adjustingSplit || _split.Panel2Collapsed) return;
            _userMovingSplitter = false;
            _multiWidthCustom = true;
            _multiPanelWidth = _split.Panel2.Width;
        };
        return _split;
    }

    private Control BuildLogBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(10, 0, 10, 0),
            BackColor = Theme.Surface2,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var labels = new RowFlowPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface2,
        };
        _logTitleLabel = new Label
        {
            Text = "未连接",
            AutoSize = false,
            AutoEllipsis = true,
            Width = 76,
            Height = Font.Height + 8,
            ForeColor = Theme.Text,
            Font = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 9, 0),
        };
        _logSubLabel = new Label
        {
            Text = "",
            AutoSize = false,
            AutoEllipsis = true,
            Width = 240,
            Height = Font.Height + 8,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        labels.Controls.Add(_logTitleLabel);
        labels.Controls.Add(_logSubLabel);
        bar.Controls.Add(labels, 0, 0);

        var actions = new RowFlowPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface2,
        };
        _searchBox = new ThemeTextBox
        {
            Width = Math.Max(150, Theme.Scale(this, 150)),
            PlaceholderText = "搜索日志  Enter",
            Margin = new Padding(0, 0, 4, 0),
        };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SearchNext(!e.Shift);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _searchBox.Clear();
                ClearSearchHighlight();
                _recvBox.Focus();
            }
        };
        _pauseButton = new ThemeButton
        {
            Text = "暂停",
            AutoSize = true,
            Margin = new Padding(0, 0, 3, 0),
        };
        _pauseButton.Click += (_, _) => ToggleFollow();
        var clear = ActionButton("清除", ClearReceive);
        _rowTip.SetToolTip(clear, "清除窗口");
        var save = ActionButton("保存", SaveData);
        _rowTip.SetToolTip(save, "保存数据");
        actions.Controls.Add(_searchBox);
        actions.Controls.Add(_pauseButton);
        actions.Controls.Add(clear);
        actions.Controls.Add(save);
        bar.Controls.Add(actions, 1, 0);

        RefreshLogHeaderLayout();
        return bar;
    }

    private void RefreshLogHeaderLayout()
    {
        if (_logTitleLabel is null || _logSubLabel is null) return;
        _logTitleLabel.ForeColor = Theme.Text;
        _logSubLabel.ForeColor = Theme.Muted;
        int h = Math.Max(Font.Height + 6, _logTitleLabel.Font.Height + 6);
        int Measure(Label label) => TextRenderer.MeasureText(label.Text, label.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 20;
        _logTitleLabel.Height = h;
        _logTitleLabel.Width = Math.Max(40, Measure(_logTitleLabel));
        _logSubLabel.Height = h;
        _logSubLabel.Width = Math.Max(40, Measure(_logSubLabel));
        _logTitleLabel.Parent?.PerformLayout();
    }

    private Control BuildComposer()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Border,
            Padding = new Padding(0, 1, 0, 0),
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface2,
        };
        _composerGrid = grid;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LineHeight() + 4));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LineHeight() + 4));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SendAreaHeight()));

        _settingsRow1 = BuildOptionsRow();
        _settingsRow2 = BuildVerifyRow();
        _sendArea = BuildSendArea();
        _settingsRow1.Dock = DockStyle.Top;
        _settingsRow2.Dock = DockStyle.Top;
        _sendArea.Dock = DockStyle.Fill;
        grid.Controls.Add(_settingsRow1, 0, 0);
        grid.Controls.Add(_settingsRow2, 0, 1);
        grid.Controls.Add(_sendArea, 0, 2);

        _settingsRow1.SizeChanged += (_, _) =>
        {
            UpdateComposerGridRows();
            ScheduleResponsiveRelayout();
        };
        _settingsRow2.SizeChanged += (_, _) =>
        {
            UpdateComposerGridRows();
            ScheduleResponsiveRelayout();
        };
        panel.Controls.Add(grid);
        _composerPanel = panel;
        UpdateComposerGridRows();
        return panel;
    }

    private int LogBarHeight() => Math.Max(44, Theme.Scale(this, 44));

    private int SendAreaHeight() => LineHeight() * 2 + 4;

    private int RowHeightOf(WrapRowPanel? row) =>
        row is null ? LineHeight() : Math.Max(LineHeight(), row.Height) + 4;

    private int ComposerHeight()
    {
        if (_composerPanel is { Visible: false }) return 0;
        return _composerHeight;
    }

    private void UpdateComposerGridRows()
    {
        if (_composerGrid is null || _settingsRow1 is null || _settingsRow2 is null) return;
        int row0 = RowHeightOf(_settingsRow1);
        int row1 = RowHeightOf(_settingsRow2);
        int row2 = SendAreaHeight();
        _composerHeight = row0 + row1 + row2 + 1;
        SetComposerRow(0, row0);
        SetComposerRow(1, row1);
        SetComposerRow(2, row2);
        if (_centerLayout is not null && _centerLayout.RowStyles.Count > 2)
        {
            float height = _composerPanel is { Visible: true } ? _composerHeight : 0;
            RowStyle style = _centerLayout.RowStyles[2];
            if (Math.Abs(style.Height - height) > 0.5f) style.Height = height;
        }
    }

    private void SetComposerRow(int row, int height)
    {
        RowStyle style = _composerGrid.RowStyles[row];
        if (Math.Abs(style.Height - height) > 0.5f) style.Height = height;
    }

    private void UpdateComposerLayout()
    {
        if (_centerLayout is not null && _centerLayout.RowStyles.Count > 0)
        {
            float logBar = LogBarHeight();
            RowStyle style = _centerLayout.RowStyles[0];
            if (Math.Abs(style.Height - logBar) > 0.5f) style.Height = logBar;
        }
        if (_sendButton is not null)
        {
            int width = Math.Max(Theme.Scale(this, 72),
                TextRenderer.MeasureText("发送", _sendButton.Font).Width + Theme.Scale(this, 28));
            if (_sendButton.Width != width) _sendButton.Width = width;
        }
        UpdateComposerGridRows();
        UpdateLogHeaderResponsive();
        _centerLayout?.PerformLayout();
        _composerGrid?.PerformLayout();
    }

    /// <summary>窄窗时先收起次要信息，给日志和发送按钮留空间。</summary>
    private void UpdateLogHeaderResponsive()
    {
        if (_centerLayout is null) return;
        int width = _centerLayout.ClientSize.Width;
        bool showSearch = width >= 620;
        bool showSub = width >= 420;
        if (_searchBox is not null)
        {
            int wanted = Math.Max(150, Theme.Scale(this, 150));
            if (_searchBox.Width != wanted) _searchBox.Width = wanted;
            if (_searchBox.Visible != showSearch) _searchBox.Visible = showSearch;
        }
        if (_logSubLabel is not null && _logSubLabel.Visible != showSub) _logSubLabel.Visible = showSub;
    }
}
