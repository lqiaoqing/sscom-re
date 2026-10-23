using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// 深色主题的配色、字体、DPI 尺寸与通用绘制工具。
/// 布局仍按原程序的物理像素约定；自绘控件只使用这里的颜色和按字体推导的尺寸。
/// </summary>
internal static class Theme
{
    public static readonly Color Canvas = Hex("#0B1119");
    public static readonly Color Surface = Hex("#101620");
    public static readonly Color Surface2 = Hex("#0D131C");
    public static readonly Color Raised = Hex("#151D28");
    public static readonly Color Hover = Hex("#1B2532");
    public static readonly Color Pressed = Hex("#202C3B");
    public static readonly Color Border = Hex("#2C3A4E");
    public static readonly Color BorderSoft = Hex("#202B3B");
    public static readonly Color Field = Hex("#0B121B");
    public static readonly Color Log = Hex("#0A1018");
    public static readonly Color Text = Hex("#DBE4EE");
    public static readonly Color Muted = Hex("#A7B5C6");
    public static readonly Color Dim = Hex("#73859B");
    public static readonly Color Accent = Hex("#2FC9B9");
    public static readonly Color AccentBright = Hex("#45E0CD");
    public static readonly Color AccentInk = Hex("#062B28");
    public static readonly Color Amber = Hex("#F0B45A");
    public static readonly Color AmberInk = Hex("#2B1B05");
    public static readonly Color Danger = Hex("#FF7272");
    public static readonly Color Green = Hex("#4ADE80");

    public static Color Hex(string value) => ColorTranslator.FromHtml(value);

    public static Font MonoFont(float size = 10F)
    {
        string name = FontExists("Cascadia Mono") ? "Cascadia Mono"
            : FontExists("Consolas") ? "Consolas"
            : FontFamily.GenericMonospace.Name;
        return new Font(name, size, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static readonly System.Reflection.PropertyInfo? DoubleBufferedProperty =
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static void SetDoubleBuffered(Control control)
    {
        try { DoubleBufferedProperty?.SetValue(control, true); } catch { }
    }

    private static bool FontExists(string name)
    {
        using var collection = new InstalledFontCollection();
        foreach (FontFamily family in collection.Families)
            if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static int Dpi(Control? control) => Math.Max(96, control?.DeviceDpi ?? 96);

    public static int Scale(Control? control, int value) =>
        (int)Math.Round(value * Dpi(control) / 96.0, MidpointRounding.AwayFromZero);

    public static int RowHeight(Font font) => Math.Max(28, font.Height + 14);

    public static Color WithAlpha(int alpha, Color color) => Color.FromArgb(alpha, color);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var arc = new Rectangle(bounds.X, bounds.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>控件自绘时用来铺底：沿父链找第一个不透明的背景色。</summary>
    public static Color BackgroundOf(Control control)
    {
        for (Control? c = control.Parent; c is not null; c = c.Parent)
            if (c.BackColor.A == 255)
                return c.BackColor;
        return Canvas;
    }

    public static void FillBackground(Graphics g, Control control)
    {
        using var brush = new SolidBrush(BackgroundOf(control));
        g.FillRectangle(brush, control.ClientRectangle);
    }

    // ———————————————————————— 窗口 / 菜单 / 状态栏 ————————————————————————

    public static void ApplyWindow(Form form)
    {
        form.BackColor = Canvas;
        form.ForeColor = Text;
        SetDoubleBuffered(form);
        if (form.IsHandleCreated) ApplyChrome(form.Handle);
        else form.HandleCreated += (_, _) => ApplyChrome(form.Handle);
        try { form.Icon = AppIcon.Value; } catch { /* 取图标失败不影响运行 */ }
    }

    private static readonly Lazy<Icon> AppIcon = new(() =>
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8);
            using var brush = new LinearGradientBrush(new Rectangle(1, 1, 30, 30), AccentBright, Hex("#1D9F95"), 45F);
            g.FillPath(brush, path);
            using var pen = new Pen(AccentInk, 2.2F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(6, 17), new PointF(10, 17), new PointF(13, 9),
                new PointF(18, 24), new PointF(21, 17), new PointF(26, 17),
            });
        }
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    });

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private static void ApplyChrome(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            // Win10 1809–1909 用 19，之后的版本用 20
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch
        {
            // Win10 或旧版本 DWM 不支持这些属性，保持原生外观即可
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    /// <summary>尽量把原生滚动条/下拉箭头切到深色；不支持时静默忽略。</summary>
    public static void TryDarkNative(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
    }

    public static void StyleMenu(MenuStrip menu)
    {
        menu.BackColor = Surface2;
        menu.ForeColor = Muted;
        menu.RenderMode = ToolStripRenderMode.Professional;
        ToolStripManager.Renderer = new ThemeMenuRenderer();
        menu.Renderer = ToolStripManager.Renderer;
        StyleMenuItems(menu.Items, menu.Renderer);
    }

    private static readonly Dictionary<ToolStripMenuItem, string> MenuBaseText = new();

    private static void StyleMenuItems(ToolStripItemCollection items, ToolStripRenderer renderer)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = Text;
            if (item is ToolStripMenuItem menuItem)
            {
                if (!MenuBaseText.ContainsKey(menuItem)) MenuBaseText[menuItem] = menuItem.Text;
                menuItem.CheckedChanged -= MenuItemCheckedChanged;
                menuItem.CheckedChanged += MenuItemCheckedChanged;
                ApplyMenuCheckText(menuItem);

                menuItem.DropDown.BackColor = Surface;
                menuItem.DropDown.ForeColor = Text;
                menuItem.DropDown.Renderer = renderer;
                if (menuItem.DropDown is ToolStripDropDownMenu dropDown)
                {
                    dropDown.ShowImageMargin = false;
                    dropDown.ShowCheckMargin = false;
                }
                StyleMenuItems(menuItem.DropDownItems, renderer);
            }
        }
    }

    private static void MenuItemCheckedChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item) ApplyMenuCheckText(item);
    }

    /// <summary>原生菜单的勾选列在深色主题下是一块白底，改用文本前缀表示勾选。</summary>
    private static void ApplyMenuCheckText(ToolStripMenuItem item)
    {
        if (!MenuBaseText.TryGetValue(item, out string? baseText))
        {
            baseText = item.Text;
            MenuBaseText[item] = baseText;
        }
        string wanted = item.Checked ? "✓  " + baseText : baseText;
        if (item.Text != wanted) item.Text = wanted;
    }

    public static void StyleStatus(StatusStrip strip)
    {
        strip.BackColor = Surface2;
        strip.ForeColor = Muted;
        strip.SizingGrip = false;
        strip.RenderMode = ToolStripRenderMode.Professional;
        strip.Renderer = new ThemeStatusRenderer();
    }

    /// <summary>给尚未替换成自绘类的控件兜底，保证对话框和遗漏控件也是深色。</summary>
    public static void StyleRecursive(Control root)
    {
        StyleOne(root);
        foreach (Control child in root.Controls) StyleRecursive(child);
    }

    private static void StyleOne(Control control)
    {
        switch (control)
        {
            case ThemeButton or ThemeCheckBox or ThemeComboBox or ThemeTextBox:
                return;
            case Label label:
                label.ForeColor = Muted;
                label.BackColor = Color.Transparent;
                break;
            case TextBox textBox:
                textBox.BackColor = Field;
                textBox.ForeColor = Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.BackColor = Field;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Raised;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Pressed;
                break;
            case CheckBox checkBox:
                checkBox.ForeColor = Muted;
                checkBox.BackColor = Color.Transparent;
                break;
            case SplitContainer split:
                split.BackColor = Border;
                split.Panel1.BackColor = Log;
                split.Panel2.BackColor = Surface;
                break;
            case Panel or FlowLayoutPanel or TableLayoutPanel:
                if (control.BackColor == SystemColors.Control || control.BackColor == SystemColors.Window)
                    control.BackColor = Surface;
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

/// <summary>深色菜单/下拉/提示条的配色表。</summary>
internal sealed class ThemeColorTable : ProfessionalColorTable
{
    public ThemeColorTable() => UseSystemColors = false;

    public override Color MenuStripGradientBegin => Theme.Surface2;
    public override Color MenuStripGradientEnd => Theme.Surface2;
    public override Color MenuItemSelected => Theme.Raised;
    public override Color MenuItemSelectedGradientBegin => Theme.Raised;
    public override Color MenuItemSelectedGradientEnd => Theme.Raised;
    public override Color MenuItemPressedGradientBegin => Theme.Raised;
    public override Color MenuItemPressedGradientMiddle => Theme.Raised;
    public override Color MenuItemPressedGradientEnd => Theme.Raised;
    public override Color MenuItemBorder => Theme.Border;
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color ToolStripBorder => Theme.Border;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.BorderSoft;
    public override Color ButtonSelectedHighlight => Theme.Raised;
    public override Color ButtonSelectedGradientBegin => Theme.Raised;
    public override Color ButtonSelectedGradientEnd => Theme.Raised;
    public override Color ButtonPressedGradientBegin => Theme.Pressed;
    public override Color ButtonPressedGradientEnd => Theme.Pressed;
    public override Color CheckBackground => Theme.Accent;
    public override Color CheckSelectedBackground => Theme.Accent;
    public override Color CheckPressedBackground => Theme.Accent;
    public override Color MenuBorder => Theme.Border;
    public override Color StatusStripGradientBegin => Theme.Surface2;
    public override Color StatusStripGradientEnd => Theme.Surface2;
}

internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemeMenuRenderer() : base(new ThemeColorTable()) { RoundedEdges = false; }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var back = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(back, e.AffectedBounds);
        using var pen = new Pen(Theme.BorderSoft);
        e.Graphics.DrawLine(pen, e.AffectedBounds.Right - 1, e.AffectedBounds.Top,
            e.AffectedBounds.Right - 1, e.AffectedBounds.Bottom);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }
}

internal sealed class ThemeStatusRenderer : ToolStripProfessionalRenderer
{
    public ThemeStatusRenderer() : base(new ThemeColorTable()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        Color back = e.ToolStrip.BackColor.A == 255 ? e.ToolStrip.BackColor : Theme.Surface2;
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.BorderSoft);
        e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
    }
}
