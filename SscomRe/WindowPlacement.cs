namespace SscomRe;

/// <summary>
/// 窗口几何夹取（规格 N1082–N1085）。
/// 宽默认 700，小于 400 取 400，比屏幕宽取 600。
/// 高默认 600，小于 300 取 300，比屏幕高取 400。
/// left 默认 250，放不下取 50。top 默认 180，放不下取 50。
/// </summary>
public static class WindowPlacement
{
    public readonly record struct Clamped(int Width, int Height, int Left, int Top);

    public static Clamped Clamp(int? width, int? height, int? left, int? top, int screenWidth, int screenHeight)
    {
        int w = width ?? 700;
        if (w < 400) w = 400;
        if (w > screenWidth) w = 600;

        int h = height ?? 600;
        if (h < 300) h = 300;
        if (h > screenHeight) h = 400;

        int l = left ?? 250;
        if (l < 0 || l + w > screenWidth) l = 50;

        int t = top ?? 180;
        if (t < 0 || t + h > screenHeight) t = 50;

        return new Clamped(w, h, l, t);
    }
}
