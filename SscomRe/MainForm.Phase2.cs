using System.Drawing;
using System.Runtime.InteropServices;
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

        int headerButton = Math.Max(Theme.Scale(this, 30), Font.Height + 10);
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
            Anchor = AnchorStyles.Left,
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
        _sideModeLabel = new ThemeChip
        {
            Text = "串口",
            Width = Theme.Scale(this, 52),
            Height = Theme.Scale(this, 24),
            Margin = new Padding(0),
        };
        headText.Controls.Add(_sideModeLabel);
        var refresh = new ThemeButton
        {
            Text = "↻",
            Width = headerButton,
            Height = headerButton,
            Kind = ThemeButton.ButtonKind.Icon,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        refresh.Click += (_, _) => RefreshPortList();
        _rowTip.SetToolTip(refresh, "刷新串口列表");
        head.Controls.Add(headText, 0, 0);
        head.Controls.Add(refresh, 1, 0);
        Add(head, 9);
        Add(new Panel { Height = 1, BackColor = Theme.BorderSoft, Margin = new Padding(0) }, 9);

        Add(SideLabel("端口号"), 6);
        _portCombo = new ThemeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Visible = false,
            TabStop = false,
            Location = new Point(-200, -200),
            Size = new Size(1, 1),
        };
        Controls.Add(_portCombo);
        _portField = new PortField { Height = Theme.Scale(this, 46) };
        _rowTip.SetToolTip(_portField, "点击选择端口；F2 手动输入");
        Add(_portField, 10);

        _serialSection = BuildSerialSection();
        _netSection = BuildNetSection();
        Add(_serialSection, 0);
        Add(_netSection, 0);

        _sideContent = layout;
        _sidePanel.Controls.Add(layout);
        layout.Visible = !_sideCollapsed;
        _sidePanel.Visible = !_sideCollapsed;
        _sidePanel.AutoScroll = !_sideCollapsed;
        _sidePanel.Resize += (_, _) => FitSidebar(layout);
        _sidePanel.Layout += (_, _) => FitSidebar(layout);
        return _sidePanel;
    }

    private void FitSidebar(Control layout)
    {
        if (_sidePanel is null || _sideContent is null || !_sidePanel.Visible) return;
        int scroll = _sidePanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        int width = Math.Max(160, _sidePanel.ClientSize.Width - scroll);
        if (layout.Width != width) layout.Width = width;
    }

    private void SetSidebarCollapsed(bool collapsed, bool persist, bool manual, bool automatic)
    {
        if (_sidePanel is null) return;
        CloseFlyout();
        ClosePortPopup(false);
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
        ApplySidebarState();
        // N1093 防抖落盘，别让同步写整份 ini 卡住点击。
        if (persist) ScheduleAutoSave();
    }

    private void ApplySidebarState()
    {
        if (_sidePanel is null) return;
        bool collapsed = _sideCollapsed;
        bool freezeLog = _recvBox is { IsHandleCreated: true };
        bool followBottom = freezeLog && _autoScrollBox.Checked;
        Point scroll = freezeLog ? GetRecvScroll() : Point.Empty;
        if (freezeLog) SendMessage(_recvBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        bool drawerOpen = _split is { Panel2Collapsed: false };

        _sidePanel.SuspendLayout();
        _bodyLayout?.SuspendLayout();
        _split?.SuspendLayout();
        _adjustingSplit = true;
        try
        {
            _sidePanel.Visible = !collapsed;
            _sidePanel.AutoScroll = !collapsed;
            if (_sideContent is not null) _sideContent.Visible = !collapsed;
            if (_bodyLayout is not null)
            {
                _bodyLayout.ColumnStyles[0].Width = collapsed ? 0 : _sideWidth;
                _bodyLayout.ColumnStyles[1].Width = collapsed ? 0 : SideSplitterThickness();
            }
            if (_sideSplitter is not null) _sideSplitter.Visible = !collapsed;
        }
        finally
        {
            _bodyLayout?.ResumeLayout(true);
            _split?.ResumeLayout(true);
            _sidePanel.ResumeLayout(true);
            _adjustingSplit = false;
            if (drawerOpen) ApplyMultiWidth();
            _bodyLayout?.PerformLayout();
            if (!collapsed) FitSidebar(_sideContent);
            UpdateSidebarToggle();
            if (_centerLayout is not null) UpdateLogHeaderResponsive();
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
    }

    private void UpdateSidebarToggle()
    {
        if (_sidebarToggle is null) return;
        _sidebarToggle.Text = _sideCollapsed ? "»" : "«";
        _rowTip.SetToolTip(_sidebarToggle, _sideCollapsed ? "展开连接栏" : "收起连接栏");
    }

    private void BuildPortPopup()
    {
        _portPopup = new PortPopup { Font = Font };
        _portPopup.ItemChosen += ChoosePortFromPopup;
        _portPopup.Dismissed += () => ClosePortPopup(true);
        Controls.Add(_portPopup);
        _portPopupFilter = new PortPopupDismissFilter(this);
        Application.AddMessageFilter(_portPopupFilter);
        FormClosed += (_, _) =>
        {
            if (_portPopupFilter is null) return;
            Application.RemoveMessageFilter(_portPopupFilter);
            _portPopupFilter = null;
        };
    }

    private void BuildCommandPalette()
    {
        _commandPalette = new CommandPalette { Font = Font, Visible = false };
        _commandPalette.Activate += item => ExecutePaletteItem(item, fillOnly: false);
        _commandPalette.FillRequested += item => ExecutePaletteItem(item, fillOnly: true);
        _commandPalette.Dismissed += CloseCommandPalette;
        Controls.Add(_commandPalette);
        _commandPalette.BringToFront();
        _commandPaletteFilter = new CommandPaletteDismissFilter(this);
        Application.AddMessageFilter(_commandPaletteFilter);
        FormClosed += (_, _) =>
        {
            if (_commandPaletteFilter is null) return;
            Application.RemoveMessageFilter(_commandPaletteFilter);
            _commandPaletteFilter = null;
        };
    }

    private void ToggleCommandPalette()
    {
        if (_commandPalette is null) return;
        if (_commandPalette.Visible)
        {
            CloseCommandPalette();
            return;
        }
        CloseFlyout();
        ClosePortPopup(false);
        HideToast();
        List<CommandPaletteItem> items = BuildCommandItems();
        _commandPaletteReturnFocus = ActiveControl;
        _commandPalette.RefreshTheme();
        _commandPalette.SetItems(items);
        int margin = Theme.Scale(this, 12);
        int width = Math.Min(Theme.Scale(this, 640), Math.Max(320, ClientSize.Width - margin * 2));
        int height = Math.Min(Theme.Scale(this, 520), Math.Max(280, ClientSize.Height - Theme.Scale(this, 80)));
        int x = Math.Max(margin, (ClientSize.Width - width) / 2);
        int y = Math.Max(Theme.Scale(this, 60), (ClientSize.Height - height) / 3);
        _commandPalette.Bounds = new Rectangle(x, y, width, height);
        _commandPalette.Visible = true;
        _commandPalette.BringToFront();
        _commandPalette.FocusSearch();
    }

    private void CloseCommandPalette()
    {
        if (_commandPalette is null || !_commandPalette.Visible) return;
        _commandPalette.Visible = false;
        Control? back = _commandPaletteReturnFocus;
        _commandPaletteReturnFocus = null;
        if (back is { CanFocus: true } && !back.IsDisposed) back.Focus();
    }

    private List<CommandPaletteItem> BuildCommandItems()
    {
        var items = new List<CommandPaletteItem>();
        var seen = new HashSet<int>();

        void AddRow(int row, string section)
        {
            if (row < 0 || row >= IniDocument.MultiRowCount || !seen.Add(row)) return;
            string data = _rowData[row]?.Text ?? _multiRowSource?.Data[row] ?? "";
            if (string.IsNullOrEmpty(data)) return;
            bool hex = _rowHex[row]?.Checked ?? _multiRowSource?.Hex[row] ?? false;
            string comment = _rowComments[row] ?? _multiRowSource?.Comment[row] ?? "";
            int page = row / IniDocument.MultiRowsPerPage;
            int local = row % IniDocument.MultiRowsPerPage + 1;
            bool custom = comment.Length > 0 && comment != local.ToString();
            string title = custom ? comment : data;
            string subtitle = custom ? data : "";
            items.Add(new CommandPaletteItem(section, $"P{page + 1} · {local}", title, subtitle, data, hex, row));
        }

        void AddPage(int page, string section)
        {
            int origin = page * IniDocument.MultiRowsPerPage;
            for (int k = 0; k < IniDocument.MultiRowsPerPage; k++)
                AddRow(origin + k, section);
        }

        AddPage(_multiPage, $"当前页 · 第 {_multiPage + 1} 页");
        foreach (int row in _recentCommands)
            AddRow(row, "最近发送");
        for (int page = 0; page < _multiPageCount; page++)
            if (page != _multiPage)
                AddPage(page, $"第 {page + 1} 页");
        return items;
    }

    private void ExecutePaletteItem(CommandPaletteItem item, bool fillOnly)
    {
        if (fillOnly)
        {
            if (_hexSendBox.Checked != item.Hex) _hexSendBox.Checked = item.Hex;
            _sendBox.Text = item.Data;
            CloseCommandPalette();
            _sendBox.Focus();
            return;
        }
        if (!ChannelCanSend())
        {
            ShowToast("串口未打开或网络未连接");
            return;
        }
        SendPayload(item.Data, item.Hex);
        RememberRecentCommand(item.Row);
        CloseCommandPalette();
        ShowToast("已发送 " + item.Badge);
    }

    private void RememberRecentCommand(int row)
    {
        if (row < 0 || row >= IniDocument.MultiRowCount) return;
        _recentCommands.Remove(row);
        _recentCommands.Insert(0, row);
        while (_recentCommands.Count > 12) _recentCommands.RemoveAt(_recentCommands.Count - 1);
    }

    internal bool CommandPaletteVisibleForTest => _commandPalette?.Visible == true;
    internal int CommandPaletteItemCountForTest => _commandPalette?.ItemCount ?? -1;
    internal int CommandPaletteFilteredCountForTest => _commandPalette?.FilteredItemCount ?? -1;
    internal void SetCommandPaletteQueryForTest(string query) => _commandPalette?.SetQueryForTest(query);
    internal void OpenCommandPaletteForTest() => ToggleCommandPalette();
    internal void CloseCommandPaletteForTest() => CloseCommandPalette();

    private void TogglePortPopup(Control anchor)
    {
        if (_portPopup is null) return;
        if (_portPopup.Visible)
        {
            ClosePortPopup(true);
            return;
        }
        CloseFlyout();
        CloseCommandPalette();
        _portPopupAnchor = anchor;
        _portPopup.Width = Math.Max(Theme.Scale(this, 250), anchor.Width);
        string[] items = _portCombo.Items.Cast<object?>()
            .Select(item => item?.ToString() ?? "").ToArray();
        _portPopup.SetItems(items, _portCombo.SelectedIndex);

        Point below = anchor.PointToScreen(new Point(0, anchor.Height + Theme.Scale(this, 4)));
        Point position = PointToClient(below);
        int margin = Theme.Scale(this, 8);
        int bottomLimit = ClientSize.Height - (_statusStrip?.Height ?? 0) - margin;
        if (position.Y + _portPopup.Height > bottomLimit)
        {
            Point above = anchor.PointToScreen(Point.Empty);
            position.Y = Math.Max(margin,
                PointToClient(above).Y - _portPopup.Height - Theme.Scale(this, 4));
        }
        position.X = Math.Max(margin,
            Math.Min(position.X, ClientSize.Width - _portPopup.Width - margin));
        _portPopup.Location = position;
        _portPopup.Visible = true;
        _portPopup.BringToFront();
        _portField.Opened = ReferenceEquals(anchor, _portField);
        _logPortPicker.Opened = ReferenceEquals(anchor, _logPortPicker);
        _portField.Invalidate();
        _logPortPicker.Invalidate();
        _portPopup.Focus();
    }

    private bool IsPortPopupAnchor(Control control) =>
        ReferenceEquals(control, _portField) || _portField.Contains(control) ||
        ReferenceEquals(control, _logPortPicker) || _logPortPicker.Contains(control);

    private void ClosePortPopup(bool focusAnchor)
    {
        if (_portPopup is null || !_portPopup.Visible) return;
        _portPopup.Visible = false;
        Control? anchor = _portPopupAnchor;
        _portPopupAnchor = null;
        _portField.Opened = false;
        _logPortPicker.Opened = false;
        _portField.Invalidate();
        _logPortPicker.Invalidate();
        if (focusAnchor && anchor is { CanFocus: true }) anchor.Focus();
    }

    private void ChoosePortFromPopup(int index)
    {
        if (index < 0 || index >= _portCombo.Items.Count) return;
        bool same = _portCombo.SelectedIndex == index;
        ClosePortPopup(true);
        string item = _portCombo.Items[index]?.ToString() ?? _portCombo.Text;
        _portCombo.Text = item;
        _portCombo.SelectedIndex = index;
        DetectMode();
        SyncPortField();
        SchedulePortReopen();
        UpdateStatus();                 // 端口关闭时也要立刻刷新日志头/状态栏
    }

    private void CommitManualPort(string text)
    {
        ClosePortPopup(false);
        _portCombo.SelectedIndex = -1;
        _portCombo.Text = text;
        DetectMode();
        SyncPortField();
        SchedulePortReopen();
        UpdateStatus();                 // 手动输入端口同样即时刷新显示
    }

    internal void SetSidebarCollapsedForTest(bool collapsed) =>
        SetSidebarCollapsed(collapsed, persist: false, manual: true, automatic: false);

    internal bool SidebarCollapsedForTest => _sideCollapsed;
    internal bool SidebarVisibleForTest => _sidePanel?.Visible == true;
    internal int SidebarColumnWidthForTest => (int)Math.Round(_bodyLayout?.ColumnStyles[0].Width ?? -1);
    internal int SidebarExpandedWidthForTest => _sideWidth;
    internal bool SidebarToggleVisibleForTest => _sidebarToggle?.Visible == true;
    internal string SidebarToggleTextForTest => _sidebarToggle?.Text ?? "";
    internal bool LogTitleVisibleForTest => _logPortPicker is { Visible: true };
    internal string LogPortPickerTextForTest => _logPortPicker?.DisplayText ?? "";
    internal void OpenLogPortPopupForTest() => TogglePortPopup(_logPortPicker);
    internal void ResetMultiPanelWidthForTest()
    {
        _multiWidthCustom = false;
        _multiPanelWidth = 0;
        ApplyMultiWidth();
    }
    internal string[] PortComboItemsForTest =>
        _portCombo.Items.Cast<object?>().Select(item => item?.ToString() ?? "").ToArray();
    internal int PortFieldWidthForTest => _portField?.Width ?? -1;
    internal int BaudComboWidthForTest => _baudCombo.Width;
    internal string PortFieldTextForTest => _portField?.DisplayText ?? "";
    internal string PortComboTextForTest => _portCombo?.Text ?? "";
    internal void OpenPortPopupForTest() => TogglePortPopup(_portField);
    internal void ClosePortPopupForTest() => ClosePortPopup(false);
    internal bool PortPopupVisibleForTest => _portPopup?.Visible == true;

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

        var sectionHead = new TableLayoutPanel
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
        sectionHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sectionHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sectionHead.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var sectionTitle = SideSectionTitle("线路参数");
        sectionTitle.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        sectionHead.Controls.Add(sectionTitle, 0, 0);
        sectionHead.Controls.Add(new Label
        {
            Text = "下次打开生效",
            AutoSize = true,
            ForeColor = Theme.Dim,
            Font = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 1.5F)),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Margin = new Padding(0),
        }, 1, 0);
        Add(sectionHead, 6);

        Add(SideLabel("波特率"), 6);
        int unit = TextUnit();
        _baudCombo = new ThemeComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var b in ComPortService.BaudItems) _baudCombo.Items.Add(b);
        _baudCombo.SelectedIndex = 13;
        Add(_baudCombo, 12);

        _customBaudBox = new ThemeTextBox { Text = _port.CustomBaud.ToString(), Visible = false };
        _customBaudBox.VisibleChanged += (_, _) =>
        {
            _serialSection?.PerformLayout();
            ScheduleResponsiveRelayout();
        };
        Add(_customBaudBox, 6);

        Add(new Panel { Height = Theme.Scale(this, 4), BackColor = Theme.Surface2, Margin = new Padding(0) }, 4);
        int chipH = Math.Max(Theme.Scale(this, 30), Font.Height + Theme.Scale(this, 6));
        int chipW = Math.Max(Theme.Scale(this, 80),
            TextRenderer.MeasureText("RTS", Font).Width + Theme.Scale(this, 56));
        var flags = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Top,
            BackColor = Theme.Surface2,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        _rtsBox = new ThemeCheckChip
        {
            Text = "RTS",
            Width = chipW,
            Height = chipH,
            Margin = new Padding(0, 0, Theme.Scale(this, 10), 0),
        };
        _dtrBox = new ThemeCheckChip
        {
            Text = "DTR",
            Width = chipW,
            Height = chipH,
            Margin = new Padding(0),
        };
        flags.Controls.Add(_rtsBox);
        flags.Controls.Add(_dtrBox);
        Add(flags, 12);

        _sideStatusCard = new SideStatusCard { Height = Theme.Scale(this, 48) };
        Add(_sideStatusCard, 14);

        int buttonH = Math.Max(Theme.Scale(this, 42), Font.Height + Theme.Scale(this, 18));
        _openButton = new ThemeButton
        {
            Text = "打开串口",
            Dock = DockStyle.Top,
            Height = buttonH,
            Kind = ThemeButton.ButtonKind.Primary,
            Margin = new Padding(0, 0, 0, 8),
        };
        Add(_openButton, 8);

        Add(new Panel { Height = 1, BackColor = Theme.BorderSoft, Margin = new Padding(0) }, 4);

        _moreSettingsButton = new ThemeButton
        {
            Text = "更多串口设置",
            Dock = DockStyle.Top,
            Height = Theme.Scale(this, 38),
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
            if (!_split.Panel2Collapsed && !_adjustingSplit) ApplyMultiWidth();
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
        _logPortPicker = new LogPortPicker
        {
            Width = 76,
            Height = Font.Height + 8,
            Font = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold),
            Margin = new Padding(0, 0, 9, 0),
        };
        _logPortPicker.PickerRequested += (_, _) => TogglePortPopup(_logPortPicker);
        _rowTip.SetToolTip(_logPortPicker, "点击快捷切换串口/网络模式");
        // 日志头里的串口开关：不用回连接栏就能开/关当前串口。
        // 宽度按“打开/关闭”里较宽的那个定死，切换文字时不会推着后面的速率文字左右跳。
        int openLabelWidth = Math.Max(
            TextRenderer.MeasureText("打开", Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width,
            TextRenderer.MeasureText("关闭", Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width);
        _logOpenButton = new ThemeButton
        {
            Text = "打开",
            AutoSize = false,
            Width = openLabelWidth + Theme.Scale(this, 18),
            Height = Font.Height + 12,
            Kind = ThemeButton.ButtonKind.Primary,
            Margin = new Padding(0, 0, 9, 0),
        };
        _logOpenButton.Click += (_, _) => ToggleOpen();
        _rowTip.SetToolTip(_logOpenButton, "打开/关闭串口");
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
        int toggleSize = Math.Max(28, Font.Height + 10);
        _sidebarToggle = new ThemeButton
        {
            Text = "«",
            AutoSize = false,
            Width = toggleSize,
            Height = toggleSize,
            Kind = ThemeButton.ButtonKind.Icon,
            Margin = new Padding(0, 0, 4, 0),
        };
        _sidebarToggle.Click += (_, _) =>
            SetSidebarCollapsed(!_sideCollapsed, persist: true, manual: true, automatic: false);
        labels.Controls.Add(_sidebarToggle);
        labels.Controls.Add(_logPortPicker);
        labels.Controls.Add(_logOpenButton);
        labels.Controls.Add(_logSubLabel);
        UpdateSidebarToggle();
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
        _saveMenu = BuildSaveMenu();
        var save = ActionButton("保存 ▾", null);
        save.Click += (_, _) => _saveMenu.Show(save, new Point(0, save.Height));
        _rowTip.SetToolTip(save, "保存数据（TXT + DAT）或另存为带颜色日志（RTF / HTML）");
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
        if (_logPortPicker is null || _logSubLabel is null) return;
        _logSubLabel.ForeColor = Theme.Muted;
        int h = Math.Max(Font.Height + 6, _logPortPicker.Font.Height + 6);
        int pickerWidth = Math.Max(40, _logPortPicker.PreferredWidth);
        int subWidth = Math.Max(40, TextRenderer.MeasureText(_logSubLabel.Text, _logSubLabel.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 20);
        // 尺寸没变就不要重排：UpdateStatus 很频繁，白白 PerformLayout 会让整行闪。
        if (_logPortPicker.Width == pickerWidth && _logPortPicker.Height == h &&
            _logSubLabel.Width == subWidth && _logSubLabel.Height == h) return;
        _logPortPicker.Height = h;
        _logPortPicker.Width = pickerWidth;
        _logSubLabel.Height = h;
        _logSubLabel.Width = subWidth;
        _logPortPicker.Parent?.PerformLayout();
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
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface2,
        };
        _composerGrid = grid;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LineHeight() + 4));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SendAreaHeight()));

        _settingsRow1 = BuildOptionsRow();
        _sendArea = BuildSendArea();
        _settingsRow1.Dock = DockStyle.Top;
        _sendArea.Dock = DockStyle.Fill;
        grid.Controls.Add(_settingsRow1, 0, 0);
        grid.Controls.Add(_sendArea, 0, 1);

        _settingsRow1.SizeChanged += (_, _) =>
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
        return _composerHeight;
    }

    private void UpdateComposerGridRows()
    {
        if (_composerGrid is null || _settingsRow1 is null) return;
        int row0 = RowHeightOf(_settingsRow1);
        int row1 = _composerShown ? SendAreaHeight() : 0;
        _composerHeight = row0 + row1 + 1;
        if (_sendArea is not null) _sendArea.Visible = _composerShown;
        SetComposerRow(0, row0);
        SetComposerRow(1, row1);
        if (_centerLayout is not null && _centerLayout.RowStyles.Count > 2)
        {
            RowStyle style = _centerLayout.RowStyles[2];
            if (Math.Abs(style.Height - _composerHeight) > 0.5f) style.Height = _composerHeight;
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

    // ———————————————————————— 低频设置浮层 ————————————————————————

    private void BuildSettingsFlyouts()
    {
        _recvSettingsFlyout = BuildReceiveSettingsFlyout();
        _sendSettingsFlyout = BuildSendSettingsFlyout();
        Controls.Add(_recvSettingsFlyout);
        Controls.Add(_sendSettingsFlyout);

        _recvSettingsButton.Click += (_, _) => ToggleFlyout(_recvSettingsFlyout, _recvSettingsButton);
        _sendSettingsButton.Click += (_, _) => ToggleFlyout(_sendSettingsFlyout, _sendSettingsButton);
        _rowTip.SetToolTip(_recvSettingsButton, "接收设置：时间戳、超时与接收文件");
        _rowTip.SetToolTip(_sendSettingsButton, "发送设置：帧尾位置与校验");

        _flyoutFilter = new FlyoutDismissFilter(this);
        Application.AddMessageFilter(_flyoutFilter);
        FormClosed += (_, _) =>
        {
            if (_flyoutFilter is null) return;
            Application.RemoveMessageFilter(_flyoutFilter);
            _flyoutFilter = null;
        };
    }

    private int FlyoutWidth(int logical) => Math.Max(300, Theme.Scale(this, logical));
    private int FlyoutPadding() => Math.Max(10, Theme.Scale(this, 14));

    private Panel BuildReceiveSettingsFlyout()
    {
        Panel shell = NewFlyoutShell(out Panel body);
        TableLayoutPanel content = NewFlyoutContent();
        int inner = FlyoutWidth(480) - 2 - FlyoutPadding() * 2;
        int row = 0;
        void Add(Control control, int gap)
        {
            control.Margin = new Padding(0, 0, 0, gap);
            content.RowCount = row + 1;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
            row++;
        }

        Add(FlyoutTitle("接收设置"), 2);
        Add(FlyoutHint("日志显示、原始落盘与 HTML 记录", inner), 10);
        Add(FlyoutDivider(), 12);

        _timestampBox = new ThemeCheckBox { Text = "加时间戳和分包显示", AutoSize = true, Checked = true };
        Add(_timestampBox, 6);

        int unit = TextUnit();
        _timeoutLabel = FlyoutLabel("超时时间:");
        _timeoutEdit = new ThemeTextBox { Text = "20", Width = unit * 4 };
        Add(FlyoutRow(inner, _timeoutLabel, _timeoutEdit, FlyoutLabel("ms")), 12);

        _recvToFileBox = new ThemeCheckBox { Text = "接收数据到文件", AutoSize = true };
        Add(_recvToFileBox, 6);
        Add(FlyoutHint("原始字节写入当前工作目录，按端口/协议与时间命名。", inner), 10);

        Add(FlyoutDivider(), 12);
        _recvHtmlBox = new ThemeCheckBox { Text = "持续记录 HTML（带颜色）", AutoSize = true };
        _recvHtmlBox.CheckedChanged += (_, _) => OnRecvHtmlChanged();
        _rowTip.SetToolTip(_recvHtmlBox, "按开始记录时的显示模式（HEX/文本）与规则，持续写入可独立打开的 HTML 分卷");
        Add(_recvHtmlBox, 4);

        int fieldH = Math.Max(Theme.Scale(this, 30), Font.Height + Theme.Scale(this, 10));
        var dirRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = fieldH,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
        };
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dirRow.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldH));
        Label dirLabel = FlyoutLabel("目录");
        dirLabel.Margin = new Padding(0, 0, Theme.Scale(this, 6), 0);
        dirLabel.Anchor = AnchorStyles.Left;
        _recvHtmlDirBox = new ThemeTextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, Theme.Scale(this, 6), 0),
            Font = Theme.MonoFont(8F),
            PlaceholderText = "默认：程序当前目录",
        };
        _recvHtmlDirBox.TextChanged += (_, _) =>
        {
            string path = _recvHtmlDirBox.Text.Trim();
            _rowTip.SetToolTip(_recvHtmlDirBox, path.Length == 0 ? "默认：程序当前目录" : path);
        };
        _rowTip.SetToolTip(_recvHtmlDirBox, "默认：程序当前目录");
        _recvHtmlPickButton = ActionButton("选择...");
        _recvHtmlPickButton.Margin = new Padding(0, 0, Theme.Scale(this, 6), 0);
        _recvHtmlPickButton.Anchor = AnchorStyles.Left;
        _recvHtmlPickButton.Click += (_, _) => PickHtmlDir();
        var openHtmlDir = ActionButton("打开");
        openHtmlDir.Margin = new Padding(0);
        openHtmlDir.Anchor = AnchorStyles.Left;
        openHtmlDir.Click += (_, _) => OpenHtmlDir();
        _rowTip.SetToolTip(openHtmlDir, "打开 HTML 日志所在文件夹");
        dirRow.Controls.Add(dirLabel, 0, 0);
        dirRow.Controls.Add(_recvHtmlDirBox, 1, 0);
        dirRow.Controls.Add(_recvHtmlPickButton, 2, 0);
        dirRow.Controls.Add(openHtmlDir, 3, 0);
        Add(dirRow, 6);

        var optionGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
        };
        optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        optionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 下拉的关闭高度由 ItemHeight 决定，比 PreferredHeight 高。AutoSize 行会裁掉底边。
        int comboH = ThemeComboBox.ClosedHeightFor(this);
        optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, comboH));
        Label splitLabel = FlyoutLabel("分卷");
        splitLabel.Margin = new Padding(0, 0, 0, 3);
        Label paletteLabel = FlyoutLabel("调色板");
        paletteLabel.Margin = new Padding(Theme.Scale(this, 8), 0, 0, 3);
        _recvHtmlSplitCombo = new ThemeComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, Theme.Scale(this, 8), 0),
            MinimumSize = new Size(0, comboH),
        };
        _recvHtmlSplitCombo.Items.AddRange(new object[] { "不分卷", "10 MB", "25 MB", "50 MB", "100 MB" });
        _recvHtmlSplitCombo.SelectedIndex = 3;
        _recvHtmlPaletteCombo = new ThemeComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0),
            MinimumSize = new Size(0, comboH),
        };
        _recvHtmlPaletteCombo.Items.AddRange(new object[] { "浅色文档", "跟随界面主题" });
        _recvHtmlPaletteCombo.SelectedIndex = 0;
        optionGrid.Controls.Add(splitLabel, 0, 0);
        optionGrid.Controls.Add(paletteLabel, 1, 0);
        optionGrid.Controls.Add(_recvHtmlSplitCombo, 0, 1);
        optionGrid.Controls.Add(_recvHtmlPaletteCombo, 1, 1);
        Add(optionGrid, 6);

        _recvHtmlTxBox = new ThemeCheckBox { Text = "含发送回显", AutoSize = true };
        _rowTip.SetToolTip(_recvHtmlTxBox, "记录发送按钮、文件和终端的发送数据");
        Add(_recvHtmlTxBox, 6);

        _recvHtmlStatus = FlyoutHint("未开始记录（勾选后开始）", inner);
        Add(_recvHtmlStatus, 4);
        Add(FlyoutHint("按开始记录时的规则与调色板；修改后重新勾选生效。", inner), 0);

        body.Controls.Add(content);
        SizeFlyout(shell, content, 480);
        shell.Tag = content;
        return shell;
    }

    private Panel BuildSendSettingsFlyout()
    {
        Panel shell = NewFlyoutShell(out Panel body);
        TableLayoutPanel content = NewFlyoutContent();
        int inner = FlyoutWidth(380) - 2 - FlyoutPadding() * 2;
        int row = 0;
        void Add(Control control, int gap)
        {
            control.Margin = new Padding(0, 0, 0, gap);
            content.RowCount = row + 1;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
            row++;
        }

        Add(FlyoutTitle("发送设置"), 2);
        Add(FlyoutHint("帧尾位置与校验算法", inner), 10);
        Add(FlyoutDivider(), 12);

        int unit = TextUnit();
        Add(FlyoutLabel("帧尾位置"), 4);
        _verifyNumEdit = new ThemeTextBox { Text = "1", Width = unit * 4 };
        Add(FlyoutRow(inner, FlyoutLabel("第"), _verifyNumEdit, FlyoutLabel("字节至")), 6);

        _tailCombo = new ThemeComboBox
        {
            Dock = DockStyle.Top,
            DropDownWidth = Math.Max(inner, Theme.Scale(this, 320)),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _tailCombo.Items.AddRange(new object[]
        {
            "末尾 (无帧尾字节)",
            "第-1 (最后1个字节是帧尾,校验插在帧尾前面)",
            "第-2 (最后2个字节是帧尾,校验插在帧尾前面)",
            "第-3 (最后3个字节是帧尾,校验插在帧尾前面)",
            "第-4 (最后4个字节是帧尾,校验插在帧尾前面)",
        });
        _tailCombo.SelectedIndex = 0;
        Add(_tailCombo, 12);

        Add(FlyoutLabel("加校验"), 4);
        int fieldH = Math.Max(Theme.Scale(this, 30), Font.Height + Theme.Scale(this, 10));
        var verifyFields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = fieldH,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
        };
        verifyFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        verifyFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        verifyFields.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldH));
        _verifyBox = new ThemeComboBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, Theme.Scale(this, 6), 0),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _verifyBox.Items.AddRange(new object[] { "None", "ModbusCRC16", "CCITT-CRC16", "CRC32", "0-ADD8", "ADD8", "XOR8", "ADD16" });
        _verifyBox.SelectedIndex = 0;
        _crcBox = new ThemeTextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ReadOnly = true,
            TabStop = false,
            ForeColor = Theme.Accent,
            PlaceholderText = "CRC",
        };
        verifyFields.Controls.Add(_verifyBox, 0, 0);
        verifyFields.Controls.Add(_crcBox, 1, 0);
        Add(verifyFields, 8);
        Add(FlyoutHint("None 表示不校验；校验值按所选算法实时计算。", inner), 0);

        body.Controls.Add(content);
        SizeFlyout(shell, content, 380);
        shell.Tag = content;
        return shell;
    }

    private Panel NewFlyoutShell(out Panel body)
    {
        var shell = new Panel
        {
            Visible = false,
            BackColor = Theme.Border,
            Padding = new Padding(1),
            Margin = new Padding(0),
        };
        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(FlyoutPadding()),
            Margin = new Padding(0),
        };
        shell.Controls.Add(body);
        return shell;
    }

    private static TableLayoutPanel NewFlyoutContent()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = false,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return content;
    }

    private Label FlyoutTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        BackColor = Color.Transparent,
        Font = new Font(Font.FontFamily, Font.Size + 1.5F, FontStyle.Bold),
    };

    private static Label FlyoutHint(string text, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        ForeColor = Theme.Dim,
        BackColor = Color.Transparent,
    };

    private static Label FlyoutLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        BackColor = Color.Transparent,
    };

    private static Control FlyoutDivider() => new Panel
    {
        Height = 1,
        BackColor = Theme.BorderSoft,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
    };

    private static FlowLayoutPanel FlyoutRow(int width, params Control[] controls)
    {
        var row = new RowFlowPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            MaximumSize = new Size(width, 0),
            BackColor = Theme.Surface,
        };
        for (int i = 0; i < controls.Length; i++)
        {
            controls[i].Margin = new Padding(0, 0, i == controls.Length - 1 ? 0 : Theme.Scale(controls[i], 6), 0);
            row.Controls.Add(controls[i]);
        }
        return row;
    }

    private void SizeFlyout(Panel shell, TableLayoutPanel content, int logicalWidth)
    {
        int width = FlyoutWidth(logicalWidth);
        int pad = FlyoutPadding();
        int inner = width - 2 - pad * 2;
        shell.Width = width;
        content.Width = inner;
        foreach (Control child in content.Controls)
        {
            if (child is FlowLayoutPanel { WrapContents: true } row)
                row.MaximumSize = new Size(inner, 0);
            else if (child is Label label && label.MaximumSize.Width > 0)
                label.MaximumSize = new Size(inner, 0);
        }
        FitHtmlOptionCombos();
        content.PerformLayout();
        int height = content.GetPreferredSize(new Size(inner, 0)).Height;
        if (height <= 0) height = Theme.Scale(this, 120);
        content.Height = height;
        shell.Height = height + pad * 2 + 2;
        shell.PerformLayout();
    }

    /// <summary>
    /// 句柄创建后原生下拉可能比公式再高几个像素。行高跟上去，避免底边再次被裁切。
    /// </summary>
    private void FitHtmlOptionCombos()
    {
        if (_recvHtmlSplitCombo?.Parent is not TableLayoutPanel grid) return;
        if (grid.RowStyles.Count < 2) return;
        int needed = ThemeComboBox.ClosedHeightFor(this);
        if (_recvHtmlSplitCombo.IsHandleCreated && _recvHtmlPaletteCombo.IsHandleCreated)
        {
            int live = Math.Max(_recvHtmlSplitCombo.Height, _recvHtmlPaletteCombo.Height);
            if (live > needed && live <= needed + 8) needed = live;
        }
        RowStyle style = grid.RowStyles[1];
        if (style.SizeType == SizeType.Absolute && Math.Abs(style.Height - needed) < 0.5f) return;
        grid.RowStyles[1] = new RowStyle(SizeType.Absolute, needed);
        var min = new Size(0, needed);
        _recvHtmlSplitCombo.MinimumSize = min;
        _recvHtmlPaletteCombo.MinimumSize = min;
    }

    private void ToggleFlyout(Panel flyout, Control anchor)
    {
        if (ReferenceEquals(_openFlyout, flyout))
        {
            CloseFlyout();
            return;
        }
        // 浮层因“点外部”关闭后，同一锚点按钮的 Click 还会到达一次；这次点击不再重新打开。
        if ((DateTime.UtcNow - _flyoutClosedAt).TotalMilliseconds < 200 &&
            ReferenceEquals(_flyoutClosedAnchor, anchor)) return;
        ShowFlyout(flyout, anchor);
    }

    private void ShowFlyout(Panel flyout, Control anchor)
    {
        ClosePortPopup(false);
        CloseFlyout();
        if (flyout.Tag is not TableLayoutPanel content) return;
        int logicalWidth = ReferenceEquals(flyout, _recvSettingsFlyout) ? 480 : 380;
        SizeFlyout(flyout, content, logicalWidth);
        Point corner = anchor.PointToScreen(new Point(anchor.Width, 0));
        Point p = PointToClient(corner);
        int x = p.X - flyout.Width;
        int y = p.Y - flyout.Height - Theme.Scale(this, 8);
        int margin = Theme.Scale(this, 8);
        int maxX = Math.Max(margin, ClientSize.Width - flyout.Width - margin);
        int maxY = Math.Max(margin, ClientSize.Height - flyout.Height - margin);
        flyout.Location = new Point(Math.Max(margin, Math.Min(x, maxX)), Math.Max(margin, Math.Min(y, maxY)));
        HideToast();
        flyout.Visible = true;
        flyout.BringToFront();
        _openFlyout = flyout;
        _openFlyoutAnchor = anchor;
        FirstFocusable(flyout)?.Focus();
    }

    private void CloseFlyout()
    {
        if (_openFlyout is null) return;
        Panel flyout = _openFlyout;
        Control? anchor = _openFlyoutAnchor;
        bool hadFocus = flyout.ContainsFocus;
        _openFlyout = null;
        _openFlyoutAnchor = null;
        _flyoutClosedAnchor = anchor;
        flyout.Visible = false;
        _flyoutClosedAt = DateTime.UtcNow;
        if (hadFocus && anchor is { IsDisposed: false, Visible: true, CanFocus: true })
            anchor.Focus();
    }

    private static Control? FirstFocusable(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child.Visible && child.Enabled && child.TabStop && child.CanSelect) return child;
            Control? nested = FirstFocusable(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    internal void OpenReceiveSettingsForTest() => ShowFlyout(_recvSettingsFlyout, _recvSettingsButton);
    internal void OpenSendSettingsForTest() => ShowFlyout(_sendSettingsFlyout, _sendSettingsButton);
    internal void CloseSettingsForTest() => CloseFlyout();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K))
        {
            ToggleCommandPalette();
            return true;
        }
        if (keyData == Keys.Escape && _commandPalette is { Visible: true })
        {
            CloseCommandPalette();
            return true;
        }
        if (keyData == Keys.Escape && _portPopup is { Visible: true })
        {
            ClosePortPopup(true);
            return true;
        }
        if (keyData == Keys.Escape && _openFlyout is not null &&
            !(_tailCombo?.DroppedDown ?? false) && !(_verifyBox?.DroppedDown ?? false))
        {
            CloseFlyout();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        // 切到其它应用时关闭浮层；同进程的下拉/对话框不会触发 WM_ACTIVATEAPP。
        const int WM_ACTIVATEAPP = 0x001C;
        if (m.Msg == WM_ACTIVATEAPP && m.WParam == IntPtr.Zero)
        {
            CloseFlyout();
            ClosePortPopup(false);
            CloseCommandPalette();
        }
        base.WndProc(ref m);
    }

    private sealed class CommandPaletteDismissFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const uint GA_ROOT = 2;

        private readonly MainForm _form;

        public CommandPaletteDismissFilter(MainForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            CommandPalette? palette = _form._commandPalette;
            if (palette is null || !palette.Visible) return false;
            if (m.Msg is not (WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or
                WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN)) return false;

            Control? control = Control.FromHandle(m.HWnd);
            if (control is not null)
            {
                if (ReferenceEquals(control, palette) || palette.Contains(control)) return false;
                if (ReferenceEquals(control.TopLevelControl, _form)) _form.CloseCommandPalette();
                return false;
            }
            if (GetAncestor(m.HWnd, GA_ROOT) == _form.Handle) _form.CloseCommandPalette();
            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }

    private sealed class PortPopupDismissFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const uint GA_ROOT = 2;

        private readonly MainForm _form;

        public PortPopupDismissFilter(MainForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            PortPopup? popup = _form._portPopup;
            if (popup is null || !popup.Visible) return false;
            if (m.Msg is not (WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or
                WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN)) return false;

            Control? control = Control.FromHandle(m.HWnd);
            if (control is not null)
            {
                if (ReferenceEquals(control, popup) || popup.Contains(control)) return false;
                if (_form.IsPortPopupAnchor(control)) return false;
                if (ReferenceEquals(control.TopLevelControl, _form)) _form.ClosePortPopup(false);
                return false;
            }
            if (GetAncestor(m.HWnd, GA_ROOT) == _form.Handle) _form.ClosePortPopup(false);
            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }

    private sealed class FlyoutDismissFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const uint GA_ROOT = 2;

        private readonly MainForm _form;

        public FlyoutDismissFilter(MainForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            Panel? flyout = _form._openFlyout;
            if (flyout is null) return false;
            if (m.Msg is not (WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or
                WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN)) return false;

            Control? control = Control.FromHandle(m.HWnd);
            if (control is not null)
            {
                if (ReferenceEquals(control, flyout) || flyout.Contains(control)) return false;
                if (ReferenceEquals(control.TopLevelControl, _form)) _form.CloseFlyout();
                return false;
            }
            if (GetAncestor(m.HWnd, GA_ROOT) == _form.Handle) _form.CloseFlyout();
            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }
}
