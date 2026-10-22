using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Tidegate.Platform.Windows;

/// <summary>封包被攔截的層級。</summary>
internal enum WindivertLayer
{
    /// <summary>網路層，一般的封包攔截。</summary>
    Network = 0,

    /// <summary>轉送中的封包。</summary>
    NetworkForward = 1,

    /// <summary>連線層事件（含擁有者 PID）。</summary>
    Flow = 2,

    /// <summary>通訊端事件。</summary>
    Socket = 3,

    /// <summary>觀察 WinDivert 自己的事件。</summary>
    Reflect = 4,
}

/// <summary>開啟攔截時的旗標。</summary>
[Flags]
internal enum WindivertOpenFlags : ulong
{
    None = 0,

    /// <summary>只旁聽，不影響封包流向。</summary>
    Sniff = 0x0001,

    /// <summary>符合條件的封包直接丟棄。</summary>
    Drop = 0x0002,

    /// <summary>只接收，不注入。</summary>
    ReceiveOnly = 0x0004,

    /// <summary>只注入，不接收。</summary>
    SendOnly = 0x0008,

    /// <summary>不自動安裝驅動。</summary>
    NoInstall = 0x0010,

    /// <summary>連片段也一併攔截。</summary>
    Fragments = 0x0020,
}

/// <summary>可以調整的 handle 參數。</summary>
internal enum WindivertParam
{
    QueueLength = 0,
    QueueTime = 1,
    QueueSize = 2,
    VersionMajor = 3,
    VersionMinor = 4,
}

/// <summary>
/// 封包的脈絡資訊（WINDIVERT_ADDRESS，80 位元組）。
/// 送回去的時候要原封不動，否則封包會被丟棄。
/// </summary>
internal readonly struct WindivertAddress
{
    /// <summary>原生結構的大小。</summary>
    public const int Size = 80;

    private const int FlagsOffset = 8;
    private const int InterfaceIndexOffset = 16;
    private const int SubInterfaceIndexOffset = 20;
    private const int ReservedOffset = 24;

    private const uint OutboundBit = 1u << 17;
    private const uint SniffedBit = 1u << 16;
    private const uint LoopbackBit = 1u << 18;
    private const uint ImpostorBit = 1u << 19;
    private const uint Ipv6Bit = 1u << 20;
    private const uint IpChecksumBit = 1u << 21;
    private const uint TcpChecksumBit = 1u << 22;
    private const uint UdpChecksumBit = 1u << 23;

    private readonly long _timestamp;
    private readonly uint _flags;
    private readonly uint _reserved;
    private readonly uint _interfaceIndex;
    private readonly uint _subInterfaceIndex;

    public WindivertAddress(
        long timestamp,
        WindivertLayer layer,
        byte @event,
        bool outbound,
        bool ipv6 = false,
        uint interfaceIndex = 0,
        uint subInterfaceIndex = 0)
    {
        _timestamp = timestamp;
        _interfaceIndex = interfaceIndex;
        _subInterfaceIndex = subInterfaceIndex;
        _reserved = 0;
        _flags = (uint)layer & 0xFFu;
        _flags |= (uint)@event << 8;
        if (outbound)
            _flags |= OutboundBit;
        if (ipv6)
            _flags |= Ipv6Bit;
    }

    public long Timestamp => _timestamp;

    public WindivertLayer Layer => (WindivertLayer)(_flags & 0xFFu);

    public byte Event => (byte)((_flags >> 8) & 0xFFu);

    public bool Outbound => (_flags & OutboundBit) != 0;

    public bool Sniffed => (_flags & SniffedBit) != 0;

    public bool Loopback => (_flags & LoopbackBit) != 0;

    public bool Impostor => (_flags & ImpostorBit) != 0;

    public bool Ipv6 => (_flags & Ipv6Bit) != 0;

    public bool HasIpChecksum => (_flags & IpChecksumBit) != 0;

    public bool HasTcpChecksum => (_flags & TcpChecksumBit) != 0;

    public bool HasUdpChecksum => (_flags & UdpChecksumBit) != 0;

    public uint InterfaceIndex => _interfaceIndex;

    public uint SubInterfaceIndex => _subInterfaceIndex;

    /// <summary>從原生結構的 80 個位元組還原。</summary>
    public static WindivertAddress FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new ArgumentException($"需要 {Size} 個位元組。", nameof(bytes));

        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[FlagsOffset..]);
        var reserved = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(FlagsOffset + 4)..]);
        var interfaceIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes[InterfaceIndexOffset..]);
        var subInterfaceIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes[SubInterfaceIndexOffset..]);
        return new WindivertAddress(timestamp, flags, reserved, interfaceIndex, subInterfaceIndex);
    }

    /// <summary>寫回原生結構。</summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"需要 {Size} 個位元組。", nameof(destination));

        destination[..Size].Clear();
        BinaryPrimitives.WriteInt64LittleEndian(destination, _timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[FlagsOffset..], _flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[(FlagsOffset + 4)..], _reserved);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[InterfaceIndexOffset..], _interfaceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[SubInterfaceIndexOffset..], _subInterfaceIndex);
    }

    public byte[] ToArray()
    {
        var bytes = new byte[Size];
        WriteTo(bytes);
        return bytes;
    }

    internal void WriteTo(IntPtr destination)
    {
        Marshal.WriteInt64(destination, 0, _timestamp);
        Marshal.WriteInt32(destination, FlagsOffset, (int)_flags);
        Marshal.WriteInt32(destination, FlagsOffset + 4, (int)_reserved);
        Marshal.WriteInt32(destination, InterfaceIndexOffset, (int)_interfaceIndex);
        Marshal.WriteInt32(destination, SubInterfaceIndexOffset, (int)_subInterfaceIndex);
        for (var offset = ReservedOffset; offset < Size; offset += 8)
            Marshal.WriteInt64(destination, offset, 0);
    }

    internal static WindivertAddress ReadFrom(IntPtr source)
    {
        var timestamp = Marshal.ReadInt64(source, 0);
        var flags = (uint)Marshal.ReadInt32(source, FlagsOffset);
        var reserved = (uint)Marshal.ReadInt32(source, FlagsOffset + 4);
        var interfaceIndex = (uint)Marshal.ReadInt32(source, InterfaceIndexOffset);
        var subInterfaceIndex = (uint)Marshal.ReadInt32(source, SubInterfaceIndexOffset);
        return new WindivertAddress(timestamp, flags, reserved, interfaceIndex, subInterfaceIndex);
    }

    private WindivertAddress(long timestamp, uint flags, uint reserved, uint interfaceIndex, uint subInterfaceIndex)
    {
        _timestamp = timestamp;
        _flags = flags;
        _reserved = reserved;
        _interfaceIndex = interfaceIndex;
        _subInterfaceIndex = subInterfaceIndex;
    }
}
