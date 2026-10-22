using System.Net;
using System.Runtime.InteropServices;

namespace Tidegate.Platform.Windows;

internal readonly record struct TcpConnection(
    bool IsIPv6,
    uint State,
    int ProcessId,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    Win32.TcpRow Row4,
    Win32.Tcp6Row Row6)
{
    public bool IsListening => State == Win32.TcpStateListen;

    public string Key => $"{LocalAddress}:{LocalPort}-{RemoteAddress}:{RemotePort}";
}

/// <summary>
/// 讀取系統的 TCP 連線表（含擁有者 PID）。IPv4 與 IPv6 各讀一次。
/// </summary>
internal static class WindowsTcpTable
{
    public static IReadOnlyList<TcpConnection> Read()
    {
        var connections = new List<TcpConnection>(256);
        ReadFamily(Win32.AfInet, connections);
        ReadFamily(Win32.AfInet6, connections);
        return connections;
    }

    private static void ReadFamily(int family, List<TcpConnection> connections)
    {
        var size = 0;
        var result = Win32.GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, Win32.TcpTableOwnerPidAll, 0);
        if (result != Win32.ErrorInsufficientBuffer || size <= sizeof(int))
            return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = Win32.GetExtendedTcpTable(buffer, ref size, false, family, Win32.TcpTableOwnerPidAll, 0);
            if (result != Win32.ErrorSuccess)
                return;

            var count = Marshal.ReadInt32(buffer);
            var rowSize = family == Win32.AfInet
                ? Marshal.SizeOf<Win32.TcpRowOwnerPid>()
                : Marshal.SizeOf<Win32.Tcp6RowOwnerPid>();
            var cursor = IntPtr.Add(buffer, sizeof(int));

            for (var index = 0; index < count; index++)
            {
                var connection = family == Win32.AfInet
                    ? FromV4(Marshal.PtrToStructure<Win32.TcpRowOwnerPid>(cursor))
                    : FromV6(Marshal.PtrToStructure<Win32.Tcp6RowOwnerPid>(cursor));
                if (connection is { } value)
                    connections.Add(value);
                cursor = IntPtr.Add(cursor, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TcpConnection? FromV4(Win32.TcpRowOwnerPid row)
    {
        if (row.ProcessId == 0)
            return null;

        return new TcpConnection(
            IsIPv6: false,
            State: row.State,
            ProcessId: (int)row.ProcessId,
            LocalAddress: AddressV4(row.LocalAddress),
            LocalPort: Port(row.LocalPort),
            RemoteAddress: AddressV4(row.RemoteAddress),
            RemotePort: Port(row.RemotePort),
            Row4: new Win32.TcpRow
            {
                State = row.State,
                LocalAddress = row.LocalAddress,
                LocalPort = row.LocalPort,
                RemoteAddress = row.RemoteAddress,
                RemotePort = row.RemotePort,
            },
            Row6: default);
    }

    private static TcpConnection? FromV6(Win32.Tcp6RowOwnerPid row)
    {
        if (row.ProcessId == 0)
            return null;

        return new TcpConnection(
            IsIPv6: true,
            State: row.State,
            ProcessId: (int)row.ProcessId,
            LocalAddress: new IPAddress(row.LocalAddress).ToString(),
            LocalPort: Port(row.LocalPort),
            RemoteAddress: new IPAddress(row.RemoteAddress).ToString(),
            RemotePort: Port(row.RemotePort),
            Row4: default,
            Row6: new Win32.Tcp6Row
            {
                State = row.State,
                LocalAddress = row.LocalAddress,
                LocalScopeId = row.LocalScopeId,
                LocalPort = row.LocalPort,
                RemoteAddress = row.RemoteAddress,
                RemoteScopeId = row.RemoteScopeId,
                RemotePort = row.RemotePort,
            });
    }

    private static string AddressV4(uint address) =>
        new IPAddress(new[]
        {
            (byte)address,
            (byte)(address >> 8),
            (byte)(address >> 16),
            (byte)(address >> 24),
        }).ToString();

    /// <summary>連接埠在表格裡是網路位元組順序，只佔低 16 位。</summary>
    private static int Port(uint value) => ((int)(value & 0xFF) << 8) | (int)((value >> 8) & 0xFF);
}
