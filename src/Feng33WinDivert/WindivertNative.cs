using System.Runtime.InteropServices;

namespace Feng33WinDivert;

/// <summary>
/// WinDivert 2.2 的原生進入點。函式簽章與 include/windivert.h 一致。
/// </summary>
internal static class WindivertNative
{
    internal static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("WinDivert.dll", SetLastError = true)]
    internal static extern IntPtr WinDivertOpen(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        int layer,
        short priority,
        ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertRecv(
        IntPtr handle,
        IntPtr packet,
        uint packetLen,
        out uint recvLen,
        IntPtr address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertSend(
        IntPtr handle,
        IntPtr packet,
        uint packetLen,
        out uint sendLen,
        IntPtr address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertHelperEvalFilter(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        IntPtr packet,
        uint packetLen,
        IntPtr address);
}
