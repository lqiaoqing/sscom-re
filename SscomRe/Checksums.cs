namespace SscomRe;

/// <summary>
/// 校验算法（规格「校验算法」表的模式 1–7）。
/// 参与范围 = 完整数据去掉帧首保留 <paramref name="headSkip"/> 个字节和帧尾保留 <paramref name="tailSkip"/> 个字节。
/// 字节序：模式 1/2/3/7 都是低字节在前（模式 7 与 1/2 同构，推断）。
/// </summary>
public static class Checksums
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>计算校验字节串；模式 0 或越界无数据时返回空数组。</summary>
    public static byte[] Compute(byte[] data, int mode, int headSkip, int tailSkip)
    {
        if (mode < 1 || mode > 7) return new byte[0];

        int start = Fx.Clamp(headSkip, 0, data.Length);
        int count = data.Length - start - Math.Max(0, tailSkip);
        if (count < 0) count = 0;                       // 推断：保留数大于长度时按空范围处理

        switch (mode)
        {
            case 1: return ModbusCrc16(data, start, count);
            case 2: return CcittCrc16(data, start, count);
            case 3: return Crc32(data, start, count);
            case 4: return new[] { Add8Neg(data, start, count) };
            case 5: return new[] { Add8(data, start, count) };
            case 6: return new[] { Xor8(data, start, count) };
            case 7: return Add16(data, start, count);
            default: return new byte[0];
        }
    }

    // 模式 1：初值 0xFFFF，按位反转多项式 0xA001；输出 [低, 高]。
    private static byte[] ModbusCrc16(byte[] data, int start, int count)
    {
        ushort crc = 0xFFFF;
        int end = start + count;
        for (int i = start; i < end; i++)
        {
            crc ^= data[i];
            for (int b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    // 模式 2：初值 0，左移多项式 0x1021；输出 [低, 高]。
    private static byte[] CcittCrc16(byte[] data, int start, int count)
    {
        ushort crc = 0;
        int end = start + count;
        for (int i = start; i < end; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (int b = 0; b < 8; b++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    // 模式 3：初值 0xFFFFFFFF，表多项式 0xEDB88320，末尾按位取反；输出低字节在前。
    private static byte[] Crc32(byte[] data, int start, int count)
    {
        uint crc = 0xFFFFFFFF;
        int end = start + count;
        for (int i = start; i < end; i++)
            crc = Crc32Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        crc = ~crc;
        return new[] { (byte)crc, (byte)(crc >> 8), (byte)(crc >> 16), (byte)(crc >> 24) };
    }

    // 模式 4：8 位累加和取补码（和 + 校验字节 = 0）。
    private static byte Add8Neg(byte[] data, int start, int count) => (byte)(0 - Add8(data, start, count));

    // 模式 5：8 位累加和。
    private static byte Add8(byte[] data, int start, int count)
    {
        int sum = 0;
        int end = start + count;
        for (int i = start; i < end; i++) sum = (sum + data[i]) & 0xFF;
        return (byte)sum;
    }

    // 模式 6：8 位异或，初值 0。
    private static byte Xor8(byte[] data, int start, int count)
    {
        byte x = 0;
        int end = start + count;
        for (int i = start; i < end; i++) x ^= data[i];
        return x;
    }

    // 模式 7：16 位累加和；输出 [低, 高]（与模式 1/2 同构，推断）。
    private static byte[] Add16(byte[] data, int start, int count)
    {
        ushort sum = 0;
        int end = start + count;
        for (int i = start; i < end; i++) sum = (ushort)(sum + data[i]);
        return new[] { (byte)(sum & 0xFF), (byte)(sum >> 8) };
    }
}
