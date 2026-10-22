using System.Runtime.InteropServices;

namespace Tidegate.Platform.Windows;

/// <summary>
/// TCP 延伸統計（ESTATS）。Windows 預設不收集，必須對每條連線先呼叫
/// SetPerTcpConnectionEStats 開啟，而且需要管理者權限；讀取前要確認
/// EnableCollection 為真，否則回傳的數字是沒有意義的。
/// </summary>
internal static class WindowsTcpStats
{
    /// <summary>TCP_ESTATS_DATA_RW_v0：只有一個 BOOLEAN EnableCollection。</summary>
    internal const int DataRwSize = 1;

    /// <summary>TCP_ESTATS_DATA_ROD_v0 是 96 位元組，給小了會回 ERROR_INVALID_USER_BUFFER。</summary>
    internal const int DataRodSize = 96;

    private const int DataBytesOutOffset = 0;
    private const int DataBytesInOffset = 16;

    /// <summary>開啟這條連線的資料統計。回傳 0 表示成功。</summary>
    public static uint Enable(TcpConnection connection, IntPtr rw)
    {
        Marshal.WriteByte(rw, 0, 1);
        if (connection.IsIPv6)
        {
            var row = connection.Row6;
            return Win32.SetPerTcp6ConnectionEStats(
                ref row, Win32.TcpConnectionEstatsData, rw, 0, DataRwSize,
                IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0);
        }

        var row4 = connection.Row4;
        return Win32.SetPerTcpConnectionEStats(
            ref row4, Win32.TcpConnectionEstatsData, rw, 0, DataRwSize,
            IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0);
    }

    /// <summary>讀取累計位元組。回傳 0 表示成功，且數字可信。</summary>
    public static uint Read(TcpConnection connection, IntPtr rw, IntPtr rod, out long bytesIn, out long bytesOut)
    {
        bytesIn = 0;
        bytesOut = 0;

        uint result;
        if (connection.IsIPv6)
        {
            var row = connection.Row6;
            result = Win32.GetPerTcp6ConnectionEStats(
                ref row, Win32.TcpConnectionEstatsData, rw, 0, DataRwSize,
                IntPtr.Zero, 0, 0, rod, 0, DataRodSize);
        }
        else
        {
            var row4 = connection.Row4;
            result = Win32.GetPerTcpConnectionEStats(
                ref row4, Win32.TcpConnectionEstatsData, rw, 0, DataRwSize,
                IntPtr.Zero, 0, 0, rod, 0, DataRodSize);
        }

        if (result != Win32.ErrorSuccess)
            return result;

        if (Marshal.ReadByte(rw) == 0)
            return Win32.ErrorAccessDenied;

        bytesOut = Marshal.ReadInt64(rod, DataBytesOutOffset);
        bytesIn = Marshal.ReadInt64(rod, DataBytesInOffset);
        return Win32.ErrorSuccess;
    }
}
