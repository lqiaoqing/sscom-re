using System.Drawing;
using System.Text;

namespace SscomRe;

/// <summary>日志高亮规则管理。返回 null 表示取消；返回列表表示应用新规则。</summary>
internal static class HighlightRulesDialog
{
    public static List<LogHighlightRule>? Show(IWin32Window owner, IReadOnlyList<LogHighlightRule> source, bool light,
        string? recentLog = null)
    {
        using var form = new Form
        {
            Text = "日志高亮规则",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.None,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Theme.Canvas,
            ForeColor = Theme.Text,
        };
        int S(int value) => Theme.Scale(owner as Control, value);
        form.ClientSize = new Size(S(800), S(590));

        var draft = source.Select(rule => rule.Clone()).ToList();
        int selected = draft.Count > 0 ? 0 : -1;
        bool updating = false;

        var title = new Label
        {
            Text = "日志高亮规则",
            AutoSize = true,
            Font = new Font(form.Font.FontFamily, form.Font.Size + 2.5F, FontStyle.Bold),
            ForeColor = Theme.Text,
            Location = new Point(S(18), S(12)),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "按关键字或正则给接收/发送日志着色；列表从上到下优先，搜索高亮始终优先。",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Location = new Point(S(18), S(37)),
            BackColor = Color.Transparent,
        };
        form.Controls.Add(title);
        form.Controls.Add(subtitle);

        var listTitle = new Label
        {
            Text = "规则",
            AutoSize = true,
            ForeColor = Theme.Text,
            Location = new Point(S(18), S(73)),
            BackColor = Color.Transparent,
        };
        var addButton = new ThemeButton
        {
            Text = "＋ 新增",
            AutoSize = true,
            Kind = ThemeButton.ButtonKind.Default,
            Location = new Point(S(214), S(68)),
        };
        form.Controls.Add(listTitle);
        form.Controls.Add(addButton);

        var listHost = new Panel
        {
            Location = new Point(S(18), S(98)),
            Size = new Size(S(264), S(330)),
            BackColor = Theme.BorderSoft,
            Padding = new Padding(1),
            AutoScroll = true,
        };
        var ruleList = new HighlightRuleList(draft, S)
        {
            Dock = DockStyle.Top,
        };
        listHost.Controls.Add(ruleList);
        form.Controls.Add(listHost);

        var remove = new ThemeButton
        {
            Text = "删除",
            AutoSize = true,
            Location = new Point(S(18), S(440)),
        };
        var up = new ThemeButton
        {
            Text = "上移",
            AutoSize = true,
            Location = new Point(S(92), S(440)),
        };
        var down = new ThemeButton
        {
            Text = "下移",
            AutoSize = true,
            Location = new Point(S(166), S(440)),
        };
        form.Controls.Add(remove);
        form.Controls.Add(up);
        form.Controls.Add(down);

        int ex = S(302);
        int ew = S(476);
        Label EditorLabel(string text, int y) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Location = new Point(ex, S(y)),
            BackColor = Color.Transparent,
        };
        var nameLabel = EditorLabel("名称", 74);
        var nameBox = new ThemeTextBox
        {
            Location = new Point(ex, S(94)),
            Width = ew,
        };
        var typeLabel = EditorLabel("匹配方式", 132);
        var typeBox = new ThemeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(ex, S(152)),
            Width = S(128),
        };
        typeBox.Items.AddRange(new object[] { "关键字", "正则" });
        var caseBox = new ThemeCheckBox
        {
            Text = "区分大小写",
            AutoSize = true,
            Location = new Point(ex + S(148), S(154)),
        };
        var patternLabel = EditorLabel("内容", 188);
        var patternBox = new ThemeTextBox
        {
            Location = new Point(ex, S(208)),
            Width = ew,
        };
        var scopeLabel = EditorLabel("作用范围", 246);
        var rxBox = new ThemeCheckBox
        {
            Text = "接收",
            AutoSize = true,
            Location = new Point(ex, S(267)),
        };
        var txBox = new ThemeCheckBox
        {
            Text = "发送",
            AutoSize = true,
            Location = new Point(ex + S(66), S(267)),
        };
        var wholeBox = new ThemeCheckBox
        {
            Text = "整行着色",
            AutoSize = true,
            Location = new Point(ex + S(156), S(267)),
        };
        var colorLabel = EditorLabel("颜色", 304);
        var foreSwatch = new ColorSwatch("文字", S(0)) { Location = new Point(ex, S(324)), Width = S(196), Height = S(30) };
        var backSwatch = new ColorSwatch("背景", S(0)) { Location = new Point(ex + S(212), S(324)), Width = S(196), Height = S(30) };
        var noBack = new ThemeButton
        {
            Text = "无背景",
            AutoSize = true,
            Location = new Point(ex + S(420), S(324)),
        };
        var previewLabel = EditorLabel("预览", 366);
        var preview = new RichTextBox
        {
            Location = new Point(ex, S(386)),
            Size = new Size(ew, S(84)),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Log,
            ForeColor = Theme.Text,
            Font = Theme.MonoFont(9F),
            ScrollBars = RichTextBoxScrollBars.None,
            DetectUrls = false,
            TabStop = false,
        };
        form.Controls.AddRange(new Control[]
        {
            nameLabel, nameBox, typeLabel, typeBox, caseBox, patternLabel, patternBox,
            scopeLabel, rxBox, txBox, wholeBox, colorLabel, foreSwatch, backSwatch, noBack,
            previewLabel, preview,
        });

        var import = new ThemeButton
        {
            Text = "导入",
            AutoSize = true,
            Location = new Point(S(18), S(542)),
        };
        var export = new ThemeButton
        {
            Text = "导出",
            AutoSize = true,
            Location = new Point(S(86), S(542)),
        };
        var cancel = new ThemeButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Location = new Point(S(602), S(542)),
        };
        var save = new ThemeButton
        {
            Text = "保存规则",
            Kind = ThemeButton.ButtonKind.Primary,
            AutoSize = true,
            Location = new Point(S(692), S(542)),
        };
        form.Controls.AddRange(new Control[] { import, export, cancel, save });
        form.CancelButton = cancel;

        void UpdatePreview()
        {
            LogHighlightRule? selectedRule = selected >= 0 && selected < draft.Count ? draft[selected] : null;
            var palette = new LogHighlightPalette(
                Theme.Text, Theme.Log, Theme.Dim, Theme.Accent, Theme.StatusSend,
                Theme.Accent, Theme.TxWash);
            List<StyledLogLine> lines = PreviewStyledLines(selectedRule, draft, recentLog, palette);
            preview.Clear();
            if (lines.Count == 0)
            {
                preview.SelectionColor = Theme.Dim;
                preview.SelectionBackColor = Theme.Log;
                preview.AppendText(selectedRule is null
                    ? "勾选或新建规则后可预览"
                    : "最近日志和示例中没有命中此规则");
                return;
            }
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    preview.SelectionColor = Theme.Text;
                    preview.SelectionBackColor = Theme.Log;
                    preview.AppendText("\n");
                }
                foreach (StyledLogRun run in lines[i].Runs)
                {
                    int start = preview.TextLength;
                    preview.AppendText(run.Text);
                    preview.Select(start, run.Text.Length);
                    preview.SelectionColor = run.Fore;
                    preview.SelectionBackColor = run.Back;
                }
            }
            preview.Select(0, 0);
        }

        void LoadEditor()
        {
            updating = true;
            try
            {
                ruleList.SelectedIndex = selected;
                bool has = selected >= 0 && selected < draft.Count;
                previewLabel.Text = has && !draft[selected].Enabled
                    ? "预览（规则已关闭，按开启效果显示）"
                    : "预览";
                nameBox.Enabled = has;
                typeBox.Enabled = has;
                patternBox.Enabled = has;
                caseBox.Enabled = has;
                rxBox.Enabled = has;
                txBox.Enabled = has;
                wholeBox.Enabled = has;
                foreSwatch.Enabled = has;
                backSwatch.Enabled = has;
                noBack.Enabled = has;
                if (!has)
                {
                    nameBox.Text = "";
                    patternBox.Text = "";
                    typeBox.SelectedIndex = 0;
                    caseBox.Checked = false;
                    rxBox.Checked = false;
                    txBox.Checked = false;
                    wholeBox.Checked = false;
                    foreSwatch.Value = Color.Transparent;
                    backSwatch.Value = Color.Transparent;
                }
                else
                {
                    LogHighlightRule rule = draft[selected];
                    nameBox.Text = rule.Name;
                    patternBox.Text = rule.Pattern;
                    typeBox.SelectedIndex = rule.IsRegex ? 1 : 0;
                    caseBox.Checked = rule.CaseSensitive;
                    rxBox.Checked = rule.Rx;
                    txBox.Checked = rule.Tx;
                    wholeBox.Checked = rule.WholeLine;
                    foreSwatch.Value = Color.FromArgb(rule.ForeColor);
                    backSwatch.Value = Color.FromArgb(rule.BackColor);
                }
                ruleList.Invalidate();
                UpdatePreview();
            }
            finally
            {
                updating = false;
            }
        }

        void CommitEditor()
        {
            if (updating || selected < 0 || selected >= draft.Count) return;
            LogHighlightRule rule = draft[selected];
            rule.Name = nameBox.Text;
            rule.Pattern = patternBox.Text;
            rule.IsRegex = typeBox.SelectedIndex == 1;
            rule.CaseSensitive = caseBox.Checked;
            rule.Rx = rxBox.Checked;
            rule.Tx = txBox.Checked;
            rule.WholeLine = wholeBox.Checked;
            rule.ForeColor = foreSwatch.Value.ToArgb();
            rule.BackColor = backSwatch.Value.ToArgb();
            ruleList.Invalidate();
        }

        void OnEditorChanged()
        {
            if (updating) return;
            CommitEditor();
            UpdatePreview();
        }

        typeBox.SelectedIndexChanged += (_, _) => OnEditorChanged();
        nameBox.TextChanged += (_, _) => OnEditorChanged();
        patternBox.TextChanged += (_, _) => OnEditorChanged();
        caseBox.CheckedChanged += (_, _) => OnEditorChanged();
        rxBox.CheckedChanged += (_, _) => OnEditorChanged();
        txBox.CheckedChanged += (_, _) => OnEditorChanged();
        wholeBox.CheckedChanged += (_, _) => OnEditorChanged();
        foreSwatch.ColorChanged += (_, _) => OnEditorChanged();
        backSwatch.ColorChanged += (_, _) => OnEditorChanged();
        noBack.Click += (_, _) =>
        {
            if (updating) return;
            backSwatch.Value = Color.Transparent;
            OnEditorChanged();
        };

        ruleList.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            CommitEditor();
            selected = ruleList.SelectedIndex;
            LoadEditor();
        };
        ruleList.RuleToggled += (_, _) =>
        {
            ruleList.Invalidate();
            if (selected >= 0 && selected < draft.Count)
                previewLabel.Text = draft[selected].Enabled
                    ? "预览"
                    : "预览（规则已关闭，按开启效果显示）";
            UpdatePreview();
        };

        addButton.Click += (_, _) =>
        {
            CommitEditor();
            var rule = new LogHighlightRule
            {
                Name = "新规则",
                Pattern = "关键字",
                IsRegex = false,
                Enabled = true,
                Rx = true,
                Tx = true,
                ForeColor = Theme.Text.ToArgb(),
                BackColor = 0,
            };
            draft.Add(rule);
            selected = draft.Count - 1;
            ruleList.UpdateHeight();
            LoadEditor();
            nameBox.Focus();
            nameBox.SelectAll();
        };

        remove.Click += (_, _) =>
        {
            if (selected < 0 || selected >= draft.Count) return;
            draft.RemoveAt(selected);
            if (draft.Count == 0) selected = -1;
            else selected = Math.Min(selected, draft.Count - 1);
            ruleList.UpdateHeight();
            LoadEditor();
        };

        up.Click += (_, _) =>
        {
            if (selected <= 0) return;
            CommitEditor();
            int at = selected;
            (draft[at - 1], draft[at]) = (draft[at], draft[at - 1]);
            selected = at - 1;
            ruleList.Invalidate();
            ruleList.SelectedIndex = selected;
            LoadEditor();
        };

        down.Click += (_, _) =>
        {
            if (selected < 0 || selected >= draft.Count - 1) return;
            CommitEditor();
            int at = selected;
            (draft[at + 1], draft[at]) = (draft[at], draft[at + 1]);
            selected = at + 1;
            ruleList.Invalidate();
            ruleList.SelectedIndex = selected;
            LoadEditor();
        };

        import.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "高亮规则 (*.json)|*.json|所有文件|*.*",
                Title = "导入高亮规则",
            };
            if (dlg.ShowDialog(form) != DialogResult.OK) return;
            try
            {
                string json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                List<LogHighlightRule> loaded = LogHighlightRuleStore.FromJson(json);
                if (loaded.Count == 0)
                {
                    MessageBox.Show(form, "文件里没有可用规则。", "导入高亮规则",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                draft.Clear();
                draft.AddRange(loaded);
                selected = 0;
                ruleList.UpdateHeight();
                LoadEditor();
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, "导入失败：" + ex.Message, "导入高亮规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        export.Click += (_, _) =>
        {
            CommitEditor();
            using var dlg = new SaveFileDialog
            {
                Filter = "高亮规则 (*.json)|*.json",
                FileName = "sscom-highlight-rules.json",
                Title = "导出高亮规则",
            };
            if (dlg.ShowDialog(form) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName, LogHighlightRuleStore.ToJson(draft), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, "导出失败：" + ex.Message, "导出高亮规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        save.Click += (_, _) =>
        {
            CommitEditor();
            var checker = new LogHighlighter();
            checker.SetRules(draft);
            if (checker.Errors.Count > 0)
            {
                MessageBox.Show(form, "规则有错误：\n" + string.Join("\n", checker.Errors),
                    "保存高亮规则", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        ruleList.UpdateHeight();
        LoadEditor();

        Theme.ApplyWindow(form);
        Theme.StyleRecursive(form);
        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        return draft;
    }

    private static readonly string[] GenericSamples =
    {
        "[10:23:41.512]收←◆ ERROR timeout, retry 2/3",
        "[10:23:42.108]收←◆ WARN low voltage",
        "[10:23:43.204]收←◆ +CSQ: 20,99 OK",
        "[10:23:44.300]收←◆ CONNECT 115200, 已注册",
        "[10:23:45.396]收←◆ $GNRMC,022704.56,A,22.7046,N,113.8121,E",
        "[10:23:46.492]收←◆ MQTT PINGREQ → PINGRESP",
        "[10:23:47.588]收←◆ AT+QICSGP=1",
        "[10:23:48.684]收←◆ PING / PONG 心跳",
        "[10:23:49.780]收←◆ DEBUG trackerSystem.c last coord saved",
    };

    /// <summary>选中规则即使处于关闭也按开启效果预览；未选中时用已开启的规则。</summary>
    internal static List<StyledLogLine> PreviewStyledLines(
        LogHighlightRule? selectedRule,
        IEnumerable<LogHighlightRule> draft,
        string? recentLog,
        LogHighlightPalette palette)
    {
        List<LogHighlightRule> rules;
        if (selectedRule is null)
        {
            rules = draft.Where(rule => rule.Enabled).Select(rule => rule.Clone()).ToList();
        }
        else
        {
            LogHighlightRule previewRule = selectedRule.Clone();
            previewRule.Enabled = true;
            rules = new List<LogHighlightRule> { previewRule };
        }
        var highlighter = new LogHighlighter();
        highlighter.SetRules(rules);
        var shown = new List<StyledLogLine>();
        foreach (string candidate in PreviewCandidates(selectedRule, recentLog))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            foreach (LogFormatter.Line line in LogFormatter.Parse(candidate, false))
            {
                if (line.Spans.Count == 0) continue;
                StyledLogLine styled = highlighter.Style(line, palette);
                if (!styled.Matched) continue;
                shown.Add(styled);
                break;
            }
            if (shown.Count >= 3) break;
        }
        return shown;
    }

    private static IEnumerable<string> PreviewCandidates(LogHighlightRule? rule, string? recentLog)
    {
        bool hasRuleSample = false;
        if (rule is not null)
        {
            string? preset = PresetSample(rule.PresetKey);
            if (preset is not null)
            {
                hasRuleSample = true;
                yield return preset;
            }
            if (!rule.IsRegex && !string.IsNullOrWhiteSpace(rule.Pattern))
            {
                hasRuleSample = true;
                string keyword = rule.Pattern.Replace('\r', ' ').Replace('\n', ' ');
                yield return $"[10:23:41.512]收←◆ {keyword} 命中示例 {keyword}";
            }
        }
        if (!string.IsNullOrEmpty(recentLog))
        {
            string text = recentLog!;
            if (text.Length > 12000) text = text.Substring(text.Length - 12000);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int from = Math.Max(0, lines.Length - 40);
            for (int i = lines.Length - 1; i >= from; i--)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                if (line.Length > 120) line = line.Substring(0, 120);
                yield return line;
            }
        }
        if (!hasRuleSample)
        {
            foreach (string sample in GenericSamples) yield return sample;
        }
    }

    private static string? PresetSample(string presetKey) => presetKey switch
    {
        "error" => "[10:23:41.512]收←◆ ERROR GPS timeout, retry 2/3",
        "warn" => "[10:23:42.108]收←◆ WARN low voltage, retry 2/3",
        "success" => "[10:23:43.204]收←◆ +CSQ: 20,99 OK",
        "connect" => "[10:23:44.300]收←◆ CONNECT 115200, 已注册",
        "gps" => "[10:23:45.396]收←◆ $GNRMC,022704.56,A,22.7046,N,113.8121,E",
        "mqtt" => "[10:23:46.492]收←◆ MQTT PINGREQ → PINGRESP",
        "at" => "[10:23:47.588]收←◆ AT+QICSGP=1",
        "heartbeat" => "[10:23:48.684]收←◆ PING / PONG 心跳",
        "debug" => "[10:23:49.780]收←◆ DEBUG trackerSystem.c last coord saved",
        _ => null,
    };

    /// <summary>规则列表：开关、颜色点、名称/模式；自身不滚动，外层 Panel 负责滚动。</summary>
    private sealed class HighlightRuleList : Control
    {
        private readonly List<LogHighlightRule> _rules;
        private readonly Func<int, int> _scale;
        private int _selectedIndex = -1;

        public HighlightRuleList(List<LogHighlightRule> rules, Func<int, int> scale)
        {
            _rules = rules;
            _scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
        }

        public event EventHandler? SelectionChanged;
        public event EventHandler? RuleToggled;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                _selectedIndex = value;
                Invalidate();
            }
        }
        private int RowHeight => _scale(54);
        private int SwitchWidth => _scale(30);
        private int SwitchHeight => _scale(16);

        public void UpdateHeight()
        {
            int height = Math.Max(RowHeight, _rules.Count * RowHeight);
            if (Height != height) Height = height;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.FillBackground(g, this);
            for (int i = 0; i < _rules.Count; i++)
            {
                LogHighlightRule rule = _rules[i];
                var row = new Rectangle(0, i * RowHeight, Width, RowHeight);
                if (i == _selectedIndex)
                {
                    var fill = new Rectangle(_scale(2), row.Top + _scale(2),
                        Math.Max(1, row.Width - _scale(4)), row.Height - _scale(4));
                    using var path = Theme.RoundedRect(fill, _scale(7));
                    using var brush = new SolidBrush(Theme.WithAlpha(24, Theme.Accent));
                    g.FillPath(brush, path);
                    using var pen = new Pen(Theme.WithAlpha(70, Theme.Accent), 1);
                    g.DrawPath(pen, path);
                }

                int left = _scale(12);
                using (var grip = new Pen(Theme.Dim, 1))
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int yy = row.Top + _scale(20) + k * _scale(5);
                        g.DrawLine(grip, left, yy, left + _scale(5), yy);
                    }
                }
                left += _scale(14);
                using (var dot = new SolidBrush(Color.FromArgb(255, Color.FromArgb(rule.ForeColor))))
                    g.FillEllipse(dot, left, row.Top + _scale(20), _scale(8), _scale(8));
                left += _scale(16);

                using var nameFont = new Font(Font.FontFamily, Font.Size - 0.5F, FontStyle.Bold);
                using var pathFont = new Font("Consolas", Math.Max(7F, Font.Size - 1.2F));
                TextRenderer.DrawText(g, rule.Name, nameFont,
                    new Rectangle(left, row.Top + _scale(8), Width - left - _scale(48), _scale(18)),
                    rule.Enabled ? Theme.Text : Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, rule.Pattern, pathFont,
                    new Rectangle(left, row.Top + _scale(27), Width - left - _scale(16), _scale(17)),
                    Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                var track = new Rectangle(Width - _scale(14) - SwitchWidth,
                    row.Top + (RowHeight - SwitchHeight) / 2, SwitchWidth, SwitchHeight);
                using (var path = Theme.RoundedRect(track, SwitchHeight / 2))
                {
                    using var fill = new SolidBrush(rule.Enabled ? Theme.Accent : Theme.CheckFill);
                    g.FillPath(fill, path);
                    using var pen = new Pen(rule.Enabled ? Theme.Accent : Theme.CheckBorder, 1);
                    g.DrawPath(pen, path);
                }
                int knob = SwitchHeight - _scale(4);
                int knobX = rule.Enabled ? track.Right - knob - _scale(2) : track.Left + _scale(2);
                using (var knobBrush = new SolidBrush(rule.Enabled ? Theme.AccentInk : Theme.Muted))
                    g.FillEllipse(knobBrush, knobX, track.Top + _scale(2), knob, knob);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int index = e.Y / Math.Max(1, RowHeight);
            if (index < 0 || index >= _rules.Count) return;
            int switchLeft = Width - _scale(14) - SwitchWidth;
            if (e.X >= switchLeft)
            {
                _rules[index].Enabled = !_rules[index].Enabled;
                RuleToggled?.Invoke(this, EventArgs.Empty);
                Invalidate();
                return;
            }
            if (_selectedIndex != index)
            {
                _selectedIndex = index;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    /// <summary>点击选择颜色的色块按钮。</summary>
    private sealed class ColorSwatch : Control
    {
        private readonly string _caption;
        private readonly int _padding;
        private Color _value;

        public ColorSwatch(string caption, int padding)
        {
            _caption = caption;
            _padding = padding;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Height = 30;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public event EventHandler? ColorChanged;
        public Color Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                Invalidate();
                ColorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.FillBackground(g, this);
            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = Theme.RoundedRect(bounds, Theme.Scale(this, 7)))
            {
                using var fill = new SolidBrush(Theme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.Border, 1);
                g.DrawPath(pen, path);
            }
            var swatch = new Rectangle(_padding + 2, 6, 20, Height - 12);
            if (_value.A == 0)
            {
                TextRenderer.DrawText(g, "无", Font,
                    new Rectangle(swatch.Left, swatch.Top, swatch.Width, swatch.Height), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Color shown = _value.A == 255
                    ? _value
                    : Theme.Mix(Theme.Log, Color.FromArgb(255, _value), _value.A / 255F);
                using var brush = new SolidBrush(shown);
                g.FillRectangle(brush, swatch);
                using var pen = new Pen(Theme.Border, 1);
                g.DrawRectangle(pen, swatch);
            }
            TextRenderer.DrawText(g, _caption + "  " + Hex(_value), Font,
                new Rectangle(swatch.Right + 7, 0, Width - swatch.Right - 10, Height),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            using var dlg = new ColorDialog
            {
                Color = _value.A == 0 ? Theme.Text : Color.FromArgb(255, _value),
                FullOpen = true,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) Value = dlg.Color;
        }

        private static string Hex(Color color) =>
            color.A == 0 ? "透明"
            : color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"{Math.Round(color.A * 100.0 / 255)}% #{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
