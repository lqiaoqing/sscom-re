using System.Drawing;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// 连接栏的端口选择字段：显示端口名 + 弱化的设备描述，点击打开 PortPopup。
/// 双击不了也没关系：F2 进入手动输入，回车提交。
/// </summary>
internal sealed class PortField : Control
{
    private string _display = "";
    private bool _hover;
    private bool _editing;
    private ThemeTextBox? _edit;

    public PortField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Height = 46;
    }

    public event EventHandler? PickerRequested;
    public event Action<string>? ManualTextCommitted;

    public bool Opened { get; set; }
    public string DisplayText => _display;

    public void SetDisplay(string text)
    {
        string next = text ?? "";
        if (_display == next) return;
        _display = next;
        if (_editing) _edit?.Hide();
        _editing = false;
        Invalidate();
    }

    public void BeginManualEdit()
    {
        if (_editing) return;
        _edit ??= BuildEditor();
        (string name, _) = SplitDisplay(_display);
        _edit.Text = name.Length > 0 ? name : _display;
        int editHeight = Math.Max(1, _edit.PreferredHeight);
        _edit.Bounds = new Rectangle(Theme.Scale(this, 12),
            Math.Max(1, (Height - editHeight) / 2),
            Math.Max(40, Width - Theme.Scale(this, 24)), editHeight);
        _edit.Visible = true;
        _editing = true;
        _edit.BringToFront();
        _edit.Focus();
        _edit.SelectAll();
        Invalidate();
    }

    private ThemeTextBox BuildEditor()
    {
        var box = new ThemeTextBox
        {
            BorderStyle = BorderStyle.None,
            Visible = false,
            Font = Font,
        };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                CommitEditor();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelEditor();
            }
        };
        box.LostFocus += (_, _) => CancelEditor();
        Controls.Add(box);
        return box;
    }

    private void CommitEditor()
    {
        if (!_editing) return;
        string text = (_edit?.Text ?? "").Trim();
        _editing = false;
        _edit?.Hide();
        Invalidate();
        if (text.Length > 0) ManualTextCommitted?.Invoke(text);
        Focus();
    }

    private void CancelEditor()
    {
        if (!_editing) return;
        _editing = false;
        _edit?.Hide();
        Invalidate();
        Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Scale(this, 9);
        Color back = _hover || Opened ? Theme.Hover : Theme.Field;
        if (_editing) back = Theme.Field;
        Color border = Opened || Focused ? Theme.AccentBright : Theme.Border;
        using (var path = Theme.RoundedRect(bounds, radius))
        {
            using (var brush = new SolidBrush(back)) g.FillPath(brush, path);
            using var pen = new Pen(border, Math.Max(1F, Theme.Scale(this, 1)));
            g.DrawPath(pen, path);
        }

        int pad = Theme.Scale(this, 12);
        int arrow = Theme.Scale(this, 14);
        int arrowX = Width - pad - arrow;
        using (var pen = new Pen(Theme.Muted, Math.Max(1.4F, Theme.Scale(this, 1)))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        })
        {
            int cx = arrowX + arrow / 2;
            int cy = Height / 2;
            int half = Math.Max(3, arrow / 2 - 1);
            g.DrawLines(pen, new[]
            {
                new PointF(cx - half, cy - half / 2F),
                new PointF(cx, cy + half / 2F),
                new PointF(cx + half, cy - half / 2F),
            });
        }

        if (_editing) return;

        (string name, string desc) = SplitDisplay(_display);
        int textLeft = pad;
        int textRight = arrowX - Theme.Scale(this, 8);
        int available = Math.Max(1, textRight - textLeft);
        string shown = name.Length > 0 ? name : "选择端口";
        using var nameFont = new Font(Font, FontStyle.Bold);
        using var descFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 1F));
        Color nameColor = name.Length > 0 ? Theme.Text : Theme.Dim;
        Size nameSize = TextRenderer.MeasureText(shown, nameFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var nameRect = new Rectangle(textLeft, 0, Math.Min(available, nameSize.Width + 2), Height);
        TextRenderer.DrawText(g, shown, nameFont, nameRect, nameColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (desc.Length > 0)
        {
            int descX = textLeft + Math.Min(available, nameSize.Width) + Theme.Scale(this, 8);
            int descWidth = textRight - descX;
            if (descWidth > Theme.Scale(this, 24))
            {
                var descRect = new Rectangle(descX, 0, descWidth, Height);
                TextRenderer.DrawText(g, desc, descFont, descRect, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    private static (string Name, string Description) SplitDisplay(string text)
    {
        int at = text.IndexOf(" · ", StringComparison.Ordinal);
        return at >= 0
            ? (text.Substring(0, at).Trim(), text.Substring(at + 3).Trim())
            : (text.Trim(), "");
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_editing)
        {
            Focus();
            PickerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e)
    {
        if (!_editing) Invalidate();
        base.OnLostFocus(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Enter or Keys.Space or Keys.Down or Keys.F2 => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2)
        {
            e.SuppressKeyPress = true;
            BeginManualEdit();
            return;
        }
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down)
        {
            e.SuppressKeyPress = true;
            PickerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }
}
