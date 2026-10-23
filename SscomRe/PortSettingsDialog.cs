namespace SscomRe;

/// <summary>
/// 自建的串口线路参数对话框。只写入 <see cref="ComPortService"/> 的字段。
/// 已经打开的串口不重配，下次打开才用这些值。
/// </summary>
internal static class PortSettingsDialog
{
    public static void Show(IWin32Window owner, ComPortService port)
    {
        using var form = new Form
        {
            Text = "更多串口设置",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 292),
            Font = SystemFonts.MessageBoxFont,
        };

        int y = 12;
        ComboBox AddCombo(string label, string[] items, int selected)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, y + 4) });
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(120, y),
                Width = 220,
            };
            box.Items.AddRange(items);
            box.SelectedIndex = Fx.Clamp(selected, 0, items.Length - 1);
            form.Controls.Add(box);
            y += 28;
            return box;
        }

        int dataIndex = port.DataBits is >= 5 and <= 8 ? port.DataBits - 5 : 3;
        var data = AddCombo("数据位", ComPortService.DataBitsItems, dataIndex);
        var stop = AddCombo("停止位", ComPortService.StopBitsItems, port.StopBitsIndex);
        var parity = AddCombo("校验", ComPortService.ParityItems, port.ParityIndex);
        var flow = AddCombo("流控", ComPortService.FlowItems, port.FlowIndex);

        TextBox AddNumber(string label, int value)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, y + 4) });
            var box = new TextBox { Text = value.ToString(), Location = new Point(120, y), Width = 220 };
            form.Controls.Add(box);
            y += 28;
            return box;
        }

        var buffer = AddNumber("输入缓冲", port.InputBufferSize);
        var readTo = AddNumber("读超时", port.ReadTimeoutMs);
        var writeTo = AddNumber("写超时", port.WriteTimeoutMs);

        form.Controls.Add(new Label
        {
            Text = "这些设置在下次打开串口时生效。",
            AutoSize = true,
            Location = new Point(12, y + 4),
        });

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(180, 256), Width = 75 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(264, 256), Width = 75 };
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

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
