namespace SscomRe;

/// <summary>
/// 自建的串口线路参数对话框。只写入 <see cref="ComPortService"/> 的字段。
/// 已经打开的串口不重配，下次打开才用这些值。
/// </summary>
internal static class PortSettingsDialog
{
    public static void Show(IWin32Window owner, ComPortService port)
    {
        int S(int value) => Theme.Scale(owner as Control, value);
        using var form = new Form
        {
            Text = "更多串口设置",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(S(360), S(320)),
            AutoScaleMode = AutoScaleMode.None,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Theme.Canvas,
            ForeColor = Theme.Text,
        };

        int y = S(12);
        int rowHeight = S(32);
        ComboBox AddCombo(string label, string[] items, int selected)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(S(12), y + S(4)), ForeColor = Theme.Muted, BackColor = Color.Transparent });
            var box = new ThemeComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(S(120), y),
                Width = S(220),
            };
            box.Items.AddRange(items);
            box.SelectedIndex = Fx.Clamp(selected, 0, items.Length - 1);
            form.Controls.Add(box);
            y += rowHeight;
            return box;
        }

        int dataIndex = port.DataBits is >= 5 and <= 8 ? port.DataBits - 5 : 3;
        var data = AddCombo("数据位", ComPortService.DataBitsItems, dataIndex);
        var stop = AddCombo("停止位", ComPortService.StopBitsItems, port.StopBitsIndex);
        var parity = AddCombo("校验", ComPortService.ParityItems, port.ParityIndex);
        var flow = AddCombo("流控", ComPortService.FlowItems, port.FlowIndex);

        TextBox AddNumber(string label, int value)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(S(12), y + S(4)), ForeColor = Theme.Muted, BackColor = Color.Transparent });
            var box = new ThemeTextBox { Text = value.ToString(), Location = new Point(S(120), y), Width = S(220) };
            form.Controls.Add(box);
            y += rowHeight;
            return box;
        }

        var buffer = AddNumber("输入缓冲", port.InputBufferSize);
        var readTo = AddNumber("读超时", port.ReadTimeoutMs);
        var writeTo = AddNumber("写超时", port.WriteTimeoutMs);

        form.Controls.Add(new Label
        {
            Text = "这些设置在下次打开串口时生效。",
            AutoSize = true,
            Location = new Point(S(12), y + S(4)),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
        });

        var ok = new ThemeButton
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(S(180), S(280)),
            Width = S(75),
            Kind = ThemeButton.ButtonKind.Primary,
        };
        var cancel = new ThemeButton { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(S(264), S(280)), Width = S(75) };
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.ApplyWindow(form);
        Theme.StyleRecursive(form);

        if (form.ShowDialog(owner) != DialogResult.OK) return;

        port.DataBits = 5 + Math.Max(0, data.SelectedIndex);
        port.StopBitsIndex = stop.SelectedIndex;
        port.ParityIndex = parity.SelectedIndex;
        port.FlowIndex = flow.SelectedIndex;
        if (int.TryParse(buffer.Text.Trim(), out int buf) && buf > 0)
            port.InputBufferSize = buf;
        if (int.TryParse(readTo.Text.Trim(), out int rt))
            port.ReadTimeoutMs = rt;
        if (int.TryParse(writeTo.Text.Trim(), out int wt))
            port.WriteTimeoutMs = wt;
    }
}
