namespace SscomRe;

/// <summary>
/// 多条发送状态机（规格 Send_N_StringTimerTimer，0x4105D8）。纯逻辑、无界面依赖，便于测试。
/// 输入是“排序后的表”（顺序号快照 + 行号），数据文本按延迟读取由调用方拼好。
/// 一拍最多发一条；连续跳过 99 次返回 Stop（对应自动取消“循环发送”）。
/// </summary>
public sealed class MultiSendPlanner
{
    public enum Kind
    {
        None,   // 本拍不发（越界回卷后仍无目标等）
        Send,   // 发送该行
        Stop,   // 自动取消勾选“循环发送”
    }

    public readonly record struct Step(Kind Kind, int Row, int Order);

    public bool StartFlag { get; set; }         // 0x4ED64E
    public int Cursor { get; private set; }     // 0x4ED650
    public int SkipKey { get; private set; }    // 0x4ED658：顺序号为 0 的跳过计数
    public int SkipEmpty { get; private set; }  // 0x4ED654：数据为空的跳过计数

    /// <summary>处理一拍。rows 必须是按顺序号稳定升序排好的 (顺序号快照, 数据文本, 行号)。</summary>
    public Step Tick(IReadOnlyList<(int Order, string Data, int Row)> rows)
    {
        if (StartFlag)
        {
            Cursor = 0;
            SkipKey = 0;
            SkipEmpty = 0;
            StartFlag = false;
        }

        int guard = 0;
        while (true)
        {
            // 防御：混合跳过时（部分顺序号 0 + 部分空数据）原状态机在回卷处反复清零，
            // 永远凑不满 99 次跳过。原程序会在这里死循环；重写限定每拍最多扫两遍。
            if (++guard > 2 * 99 + 2)
                return new Step(Kind.None, -1, 0);

            if (Cursor >= 99)                   // 到表尾：清零回表头
            {
                Cursor = 0;
                SkipKey = 0;
                SkipEmpty = 0;
            }

            (int order, string data, int row) = rows[Cursor];
            if (order == 0)                     // 顺序号 0：跳过
            {
                SkipKey++;
                if (SkipKey >= 99) return new Step(Kind.Stop, -1, 0);
                Cursor++;
                continue;
            }
            if (data.Length == 0)               // 数据空：跳过
            {
                SkipEmpty++;
                if (SkipEmpty >= 99) return new Step(Kind.Stop, -1, 0);
                Cursor++;
                continue;
            }

            Cursor++;
            return new Step(Kind.Send, row, order);
        }
    }
}
