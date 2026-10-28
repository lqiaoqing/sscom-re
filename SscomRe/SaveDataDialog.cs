using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>保存数据前选择目录。确定后由调用方直接写文件。</summary>
internal static class SaveDataDialog
{
    public static string? Choose(IWin32Window owner, string initialDirectory)
    {
        int S(int value) => Theme.Scale(owner as Control, value);
        using var form = new Form
        {
            Text = "保存数据",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(S(460), S(148)),
            AutoScaleMode = AutoScaleMode.None,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Theme.Canvas,
            ForeColor = Theme.Text,
        };

        form.Controls.Add(new Label
        {
            Text = "将保存两份文件：原始接收数据（.DAT）和窗口内容（.TXT）。",
            AutoSize = false,
            Location = new Point(S(16), S(14)),
            Size = new Size(S(428), S(22)),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
        });
        form.Controls.Add(new Label
        {
            Text = "保存到",
            AutoSize = true,
            Location = new Point(S(16), S(44)),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
        });
        var pathBox = new ThemeTextBox
        {
            Text = initialDirectory,
            Location = new Point(S(16), S(66)),
            Width = S(348),
        };
        var browse = new ThemeButton
        {
            Text = "浏览",
            AutoSize = false,
            Location = new Point(S(372), S(66)),
            Size = new Size(S(72), S(28)),
        };
        var error = new Label
        {
            Text = "",
            AutoSize = false,
            Location = new Point(S(16), S(98)),
            Size = new Size(S(220), S(36)),
            ForeColor = Theme.Danger,
            BackColor = Color.Transparent,
        };
        var cancel = new ThemeButton
        {
            Text = "取消",
            AutoSize = false,
            Size = new Size(S(84), S(30)),
            Location = new Point(S(252), S(104)),
        };
        var ok = new ThemeButton
        {
            Text = "保存",
            AutoSize = false,
            Kind = ThemeButton.ButtonKind.Primary,
            Size = new Size(S(96), S(30)),
            Location = new Point(S(344), S(104)),
        };
        form.Controls.Add(pathBox);
        form.Controls.Add(browse);
        form.Controls.Add(error);
        form.Controls.Add(cancel);
        form.Controls.Add(ok);
        ok.DialogResult = DialogResult.None;
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        bool TryAccept()
        {
            string dir = pathBox.Text.Trim().Trim('"');
            if (dir.Length == 0)
            {
                error.Text = "请填写保存目录。";
                return false;
            }
            try
            {
                if (!Directory.Exists(dir))
                {
                    error.Text = "目录不存在。";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                return false;
            }
            pathBox.Text = dir;
            return true;
        }

        browse.Click += (_, _) =>
        {
            using var browser = new FolderBrowserDialog
            {
                Description = "选择保存目录",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(pathBox.Text.Trim()) ? pathBox.Text.Trim() : initialDirectory,
            };
            if (browser.ShowDialog(form) == DialogResult.OK)
            {
                pathBox.Text = browser.SelectedPath;
                error.Text = "";
            }
        };
        ok.Click += (_, _) =>
        {
            if (!TryAccept()) return;
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        Theme.ApplyWindow(form);
        Theme.StyleRecursive(form);
        pathBox.Select(pathBox.Text.Length, 0);
        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        string chosen = pathBox.Text.Trim();
        return chosen.Length == 0 ? null : chosen;
    }
}
