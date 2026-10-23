namespace SscomRe;

/// <summary>.NET Framework 4.8 没有 Math.Clamp，集中放一个等价实现。</summary>
internal static class Fx
{
    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
