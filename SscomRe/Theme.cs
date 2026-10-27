using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SscomRe;

/// <summary>
/// 主题配色、字体、DPI 尺寸与通用绘制工具。深/浅两套 palette 通过菜单切换。
/// 布局仍按原程序的物理像素约定；自绘控件只使用这里的颜色和按字体推导的尺寸。
/// </summary>
internal static class Theme
{
    private static readonly ThemePalette DarkPalette = new()
    {
        IsDark = true,
        Canvas = Hex("#0B1119"),
        Surface = Hex("#101620"),
        Surface2 = Hex("#0D131C"),
        Raised = Hex("#151D28"),
        Hover = Hex("#1B2532"),
        Pressed = Hex("#202C3B"),
        Border = Hex("#2C3A4E"),
        BorderSoft = Hex("#202B3B"),
        Field = Hex("#0B121B"),
        Log = Hex("#0A1018"),
        Text = Hex("#DBE4EE"),
        Muted = Hex("#A7B5C6"),
        Dim = Hex("#73859B"),
        Accent = Hex("#2FC9B9"),
        AccentBright = Hex("#45E0CD"),
        AccentDeep = Hex("#27B9AA"),
        AccentInk = Hex("#062B28"),
        Amber = Hex("#F0B45A"),
        AmberInk = Hex("#2B1B05"),
        AmberText = Hex("#E7CFA9"),
        StatusSend = Hex("#F0B45A"),
        CheckFill = Hex("#0C141F"),
        CheckBorder = Hex("#4A5C74"),
        CheckActiveBorder = Hex("#FFD08A"),
        Danger = Hex("#FF7272"),
        Green = Hex("#4ADE80"),
        VerifyField = Color.FromArgb(38, 36, 27),
        StatusCritical = Color.FromArgb(82, 38, 38),
        StatusHigh = Color.FromArgb(78, 46, 34),
        StatusMid = Color.FromArgb(66, 50, 34),
        StatusLow = Color.FromArgb(54, 48, 34),
        TxWash = Hex("#17140F"),
        SearchHighlight = Hex("#365E58"),
        WarmFillAlpha = 20,
        WarmBorderAlpha = 44,
    };

    private static readonly ThemePalette LightPalette = new()
    {
        IsDark = false,
        Canvas = Hex("#EEF3F8"),
        Surface = Hex("#FFFFFF"),
        Surface2 = Hex("#E8EEF5"),
        Raised = Hex("#DDE7F1"),
        Hover = Hex("#E9F1F8"),
        Pressed = Hex("#D6E3F0"),
        Border = Hex("#C7D4E2"),
        BorderSoft = Hex("#DCE5EF"),
        Field = Hex("#FFFFFF"),
        Log = Hex("#FBFDFF"),
        Text = Hex("#17232E"),
        Muted = Hex("#4F6275"),
        Dim = Hex("#7A8B9C"),
        Accent = Hex("#0B8A7E"),
        AccentBright = Hex("#12A697"),
        AccentDeep = Hex("#086B62"),
        AccentInk = Hex("#FFFFFF"),
        Amber = Hex("#E0A340"),
        AmberInk = Hex("#2B1B05"),
        AmberText = Hex("#6F4A12"),
        StatusSend = Hex("#8A5A00"),
        CheckFill = Hex("#FFFFFF"),
        CheckBorder = Hex("#9FB0C2"),
        CheckActiveBorder = Hex("#C98A2E"),
        Danger = Hex("#C43D3D"),
        Green = Hex("#1C8F52"),
        VerifyField = Hex("#F6ECD9"),
        StatusCritical = Hex("#F6D7D7"),
        StatusHigh = Hex("#F9E2D5"),
        StatusMid = Hex("#FAEBD9"),
        StatusLow = Hex("#FCF3E4"),
        TxWash = Hex("#FCF6E9"),
        SearchHighlight = Hex("#C9E7E2"),
        WarmFillAlpha = 30,
        WarmBorderAlpha = 70,
    };

    public static ThemePalette Current { get; private set; } = DarkPalette;
    public static bool IsLight => !Current.IsDark;

    public static void SetLight(bool light) => Current = light ? LightPalette : DarkPalette;

    public static Color Canvas => Current.Canvas;
    public static Color Surface => Current.Surface;
    public static Color Surface2 => Current.Surface2;
    public static Color Raised => Current.Raised;
    public static Color Hover => Current.Hover;
    public static Color Pressed => Current.Pressed;
    public static Color Border => Current.Border;
    public static Color BorderSoft => Current.BorderSoft;
    public static Color Field => Current.Field;
    public static Color Log => Current.Log;
    public static Color Text => Current.Text;
    public static Color Muted => Current.Muted;
    public static Color Dim => Current.Dim;
    public static Color Accent => Current.Accent;
    public static Color AccentBright => Current.AccentBright;
    public static Color AccentDeep => Current.AccentDeep;
    public static Color AccentInk => Current.AccentInk;
    public static Color Amber => Current.Amber;
    public static Color AmberInk => Current.AmberInk;
    public static Color AmberText => Current.AmberText;
    public static Color StatusSend => Current.StatusSend;
    public static Color CheckFill => Current.CheckFill;
    public static Color CheckBorder => Current.CheckBorder;
    public static Color CheckActiveBorder => Current.CheckActiveBorder;
    public static Color Danger => Current.Danger;
    public static Color Green => Current.Green;
    public static Color VerifyField => Current.VerifyField;
    public static int WarmFillAlpha => Current.WarmFillAlpha;
    public static int WarmBorderAlpha => Current.WarmBorderAlpha;

    public static Color TxWash => Current.TxWash;
    public static Color SearchHighlight => Current.SearchHighlight;

    public static Color StatusBackground(long count) => count switch
    {
        >= 2_000_000 => Current.StatusCritical,
        >= 1_000_000 => Current.StatusHigh,
        >= 500_000 => Current.StatusMid,
        >= 200_000 => Current.StatusLow,
        _ => Current.Surface2,
    };

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

    public static Color Mix(Color from, Color to, float amount)
    {
        float t = Math.Max(0F, Math.Min(1F, amount));
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * t),
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }

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
        try
        {
            Icon? extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted is not null) return extracted;
        }
        catch
        {
            // 没嵌入图标时退回下面手绘的占位图标
        }
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

    private static readonly Lazy<Bitmap> AppIconBitmapLazy = new(() => AppIcon.Value.ToBitmap());
    public static Bitmap AppIconBitmap => AppIconBitmapLazy.Value;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private static void ApplyChrome(IntPtr hwnd)
    {
        try
        {
            int on = Current.IsDark ? 1 : 0;
            // Win10 1809–1909 用 19，之后的版本用 20；浅色主题显式写 0
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

    /// <summary>按当前主题切换原生滚动条/下拉箭头；不支持时静默忽略。</summary>
    public static void TryDarkNative(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, Current.IsDark ? "DarkMode_Explorer" : null, null); } catch { }
    }

    /// <summary>运行时切换主题：DWM、控件配色、菜单/状态栏、原生子控件一起刷新。</summary>
    public static void ApplyTheme(Form form, bool light)
    {
        ThemePalette old = Current;
        SetLight(light);
        ApplyWindow(form);
        Recolor(form, old, Current);
        StyleRecursive(form);
        if (form.MainMenuStrip is not null) StyleMenu(form.MainMenuStrip);
        foreach (Control control in AllControls(form))
            if (control is StatusStrip status) StyleStatus(status);
        RefreshNative(form);
        form.Invalidate(true);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in AllControls(child))
                yield return nested;
        }
    }

    /// <summary>把显式写成旧 palette 颜色的面板/标签换成新 palette 颜色。</summary>
    private static void Recolor(Control control, ThemePalette old, ThemePalette now)
    {
        if (control.BackColor.A == 255)
        {
            Color mapped = Map(control.BackColor, old, now);
            if (mapped != control.BackColor) control.BackColor = mapped;
        }
        Color fore = Map(control.ForeColor, old, now);
        if (fore != control.ForeColor) control.ForeColor = fore;
        foreach (Control child in control.Controls) Recolor(child, old, now);
    }

    private static Color Map(Color color, ThemePalette old, ThemePalette now)
    {
        if (color == old.Canvas) return now.Canvas;
        if (color == old.Surface) return now.Surface;
        if (color == old.Surface2) return now.Surface2;
        if (color == old.Raised) return now.Raised;
        if (color == old.Hover) return now.Hover;
        if (color == old.Pressed) return now.Pressed;
        if (color == old.Border) return now.Border;
        if (color == old.BorderSoft) return now.BorderSoft;
        if (color == old.Field) return now.Field;
        if (color == old.Log) return now.Log;
        if (color == old.Text) return now.Text;
        if (color == old.Muted) return now.Muted;
        if (color == old.Dim) return now.Dim;
        if (color == old.Accent) return now.Accent;
        if (color == old.AccentBright) return now.AccentBright;
        if (color == old.AccentDeep) return now.AccentDeep;
        if (color == old.AccentInk) return now.AccentInk;
        if (color == old.Amber) return now.Amber;
        if (color == old.AmberInk) return now.AmberInk;
        if (color == old.AmberText) return now.AmberText;
        if (color == old.StatusSend) return now.StatusSend;
        if (color == old.CheckFill) return now.CheckFill;
        if (color == old.CheckBorder) return now.CheckBorder;
        if (color == old.CheckActiveBorder) return now.CheckActiveBorder;
        if (color == old.Danger) return now.Danger;
        if (color == old.Green) return now.Green;
        if (color == old.VerifyField) return now.VerifyField;
        if (color == old.StatusCritical) return now.StatusCritical;
        if (color == old.StatusHigh) return now.StatusHigh;
        if (color == old.StatusMid) return now.StatusMid;
        if (color == old.StatusLow) return now.StatusLow;
        if (color == old.TxWash) return now.TxWash;
        if (color == old.SearchHighlight) return now.SearchHighlight;
        return color;
    }

    private static void RefreshNative(Control root)
    {
        if (root.IsHandleCreated) TryDarkNative(root.Handle);
        foreach (Control child in root.Controls) RefreshNative(child);
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

    public static void StyleContextMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ThemeMenuRenderer();
        foreach (ToolStripItem item in menu.Items) item.ForeColor = Text;
    }

    public static void StyleStatus(StatusStrip strip)
    {
        strip.BackColor = Surface2;
        strip.ForeColor = Muted;
        strip.SizingGrip = false;
        strip.RenderMode = ToolStripRenderMode.Professional;
        strip.Renderer = new ThemeStatusRenderer();
    }

    /// <summary>按当前主题刷新控件配色；自绘控件只重绘，原生控件补底色。</summary>
    public static void StyleRecursive(Control root)
    {
        StyleOne(root);
        foreach (Control child in root.Controls) StyleRecursive(child);
    }

    private static void StyleOne(Control control)
    {
        switch (control)
        {
            case ThemeButton button:
                button.ForeColor = Muted;
                button.Invalidate();
                break;
            case ThemeCheckChip chip:
                chip.BackColor = Surface2;
                chip.ForeColor = Muted;
                chip.Invalidate();
                break;
            case ThemeCheckBox check:
                check.ForeColor = Muted;
                check.Invalidate();
                break;
            case ThemeComboBox themeCombo:
                themeCombo.BackColor = Field;
                themeCombo.ForeColor = Text;
                themeCombo.Invalidate();
                break;
            case ThemeTextBox themeBox:
                themeBox.BackColor = (themeBox.Tag as string) == "log" ? Log : Field;
                themeBox.ForeColor = themeBox.ReadOnly ? Muted : Text;
                themeBox.Invalidate();
                break;
            case RichTextBox rich:
                rich.BackColor = Log;
                rich.ForeColor = Text;
                break;
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
                control.Invalidate();
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

/// <summary>一套完整的主题颜色；Theme 通过 Current 在深/浅之间切换。</summary>
internal sealed class ThemePalette
{
    public bool IsDark;
    public Color Canvas, Surface, Surface2, Raised, Hover, Pressed, Border, BorderSoft, Field, Log;
    public Color Text, Muted, Dim, Accent, AccentBright, AccentDeep, AccentInk;
    public Color Amber, AmberInk, AmberText, StatusSend, CheckFill, CheckBorder, CheckActiveBorder, Danger, Green, VerifyField;
    public Color StatusCritical, StatusHigh, StatusMid, StatusLow;
    public Color TxWash, SearchHighlight;
    public int WarmFillAlpha, WarmBorderAlpha;
}

/// <summary>菜单/下拉/提示条的配色表。</summary>
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
