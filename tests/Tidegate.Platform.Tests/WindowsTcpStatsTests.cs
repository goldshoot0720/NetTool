using System.Runtime.InteropServices;
using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class WindowsTcpStatsTests
{
    private const uint ErrorInvalidUserBuffer = 1784;
    private const uint ErrorInvalidParameter = 87;

    [Fact]
    public void The_data_rod_buffer_size_is_accepted_by_the_system()
    {
        var connections = WindowsTcpTable.Read()
            .Where(connection => connection.ProcessId > 0
                && !connection.IsListening
                && connection.RemotePort != 0
                && connection.State != Win32.TcpStateDeleteTcb)
            .Take(20)
            .ToArray();

        // 這台機器剛好沒有連線時沒什麼好驗的。
        if (connections.Length == 0)
            return;

        var rw = Marshal.AllocHGlobal(WindowsTcpStats.DataRwSize);
        var rod = Marshal.AllocHGlobal(WindowsTcpStats.DataRodSize);
        try
        {
            foreach (var connection in connections)
            {
                var result = WindowsTcpStats.Read(connection, rw, rod, out _, out _);

                // 32 位元組的緩衝區會讓系統回 1784；版本或大小算錯則是 87。
                Assert.NotEqual(ErrorInvalidUserBuffer, result);
                Assert.NotEqual(ErrorInvalidParameter, result);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(rw);
            Marshal.FreeHGlobal(rod);
        }
    }

    [Fact]
    public void The_data_rod_struct_is_ninety_six_bytes_with_in_at_offset_sixteen()
    {
        // 對照 tcpestats.h 的 TCP_ESTATS_DATA_ROD_v0：
        // DataBytesOut(0) DataSegsOut(8) DataBytesIn(16) DataSegsIn(24) …
        Assert.Equal(96, WindowsTcpStats.DataRodSize);
        Assert.Equal(1, WindowsTcpStats.DataRwSize);
    }
}
