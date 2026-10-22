using System.Runtime.InteropServices;
using System.Text;

namespace Tidegate.Platform.Windows;

internal static class Win32
{
    internal const int AfInet = 2;
    internal const int AfInet6 = 23;
    internal const int TcpTableOwnerPidAll = 5;
    internal const int TcpConnectionEstatsData = 1;
    internal const int TcpStateListen = 2;
    internal const int TcpStateDeleteTcb = 12;
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorInsufficientBuffer = 122;
    internal const uint ErrorAccessDenied = 5;
    internal const uint ErrorNotFound = 1168;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint TokenQuery = 0x0008;
    internal const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Tcp6Row
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint ProcessId;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int family,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll")]
    internal static extern uint GetPerTcpConnectionEStats(
        ref TcpRow row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll")]
    internal static extern uint GetPerTcp6ConnectionEStats(
        ref Tcp6Row row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll")]
    internal static extern uint SetPerTcpConnectionEStats(
        ref TcpRow row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll")]
    internal static extern uint SetPerTcp6ConnectionEStats(
        ref Tcp6Row row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr process,
        uint flags,
        StringBuilder name,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr token,
        int informationClass,
        out int information,
        int length,
        out int returned);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);

    /// <summary>
    /// 把系統計時器精度提到 1ms。節流器每個封包只睡 1~2ms，
    /// 用預設的 15.6ms 精度會把速度壓到遠低於設定的上限。
    /// </summary>
    internal static void BeginHighResolutionTimers()
    {
        try
        {
            TimeBeginPeriod(1);
        }
        catch (Exception)
        {
        }
    }

    internal static void EndHighResolutionTimers()
    {
        try
        {
            TimeEndPeriod(1);
        }
        catch (Exception)
        {
        }
    }
}
