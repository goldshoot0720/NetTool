using System.Runtime.InteropServices;

namespace Feng33WinDivert;

/// <summary>
/// 封包標頭解析。只用受控程式碼讀，不需要驅動，因此可以單獨測試。
/// </summary>
public static class WindivertHelper
{
    private const int Ipv4MinimumLength = 20;
    private const int Ipv6HeaderLength = 40;
    private const int TcpHeaderLength = 20;
    private const byte ProtocolTcp = 6;
    private const byte ProtocolUdp = 17;

    /// <summary>取出 IP 標頭裡的協定編號（6 = TCP、17 = UDP）。</summary>
    public static bool TryGetProtocol(ReadOnlySpan<byte> packet, out byte protocol)
    {
        protocol = 0;
        if (packet.Length < Ipv4MinimumLength)
            return false;

        switch (packet[0] >> 4)
        {
            case 4:
                protocol = packet[9];
                return true;
            case 6:
                if (packet.Length < Ipv6HeaderLength)
                    return false;
                protocol = packet[6];
                return true;
            default:
                return false;
        }
    }

    /// <summary>取出 TCP 的來源與目的埠。</summary>
    public static bool TryGetTcpPorts(ReadOnlySpan<byte> packet, out ushort source, out ushort destination)
    {
        source = 0;
        destination = 0;

        if (!TryGetTransportOffset(packet, ProtocolTcp, out var offset))
            return false;
        if (packet.Length < offset + TcpHeaderLength)
            return false;

        source = ReadUInt16BigEndian(packet, offset);
        destination = ReadUInt16BigEndian(packet, offset + 2);
        return true;
    }

    /// <summary>取出 UDP 的來源與目的埠。</summary>
    public static bool TryGetUdpPorts(ReadOnlySpan<byte> packet, out ushort source, out ushort destination)
    {
        source = 0;
        destination = 0;

        if (!TryGetTransportOffset(packet, ProtocolUdp, out var offset))
            return false;
        if (packet.Length < offset + 8)
            return false;

        source = ReadUInt16BigEndian(packet, offset);
        destination = ReadUInt16BigEndian(packet, offset + 2);
        return true;
    }

    /// <summary>取出「本機這一端」的埠號：送出時是來源埠，收到時是目的埠。</summary>
    public static bool TryGetLocalPort(ReadOnlySpan<byte> packet, bool outbound, out int port)
    {
        port = 0;
        if (!TryGetTcpPorts(packet, out var source, out var destination))
            return false;

        port = outbound ? source : destination;
        return port > 0;
    }

    /// <summary>用 WinDivert 的過濾語言判斷封包是否符合條件（需要 DLL）。</summary>
    public static bool Matches(string filter, ReadOnlySpan<byte> packet, in WindivertAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);

        var packetBuffer = Marshal.AllocHGlobal(Math.Max(packet.Length, 1));
        var addressBuffer = Marshal.AllocHGlobal(WindivertAddress.Size);
        try
        {
            var packetBytes = new byte[packet.Length];
            packet.CopyTo(packetBytes);
            Marshal.Copy(packetBytes, 0, packetBuffer, packetBytes.Length);
            address.WriteTo(addressBuffer);

            try
            {
                NativeLibraryLoader.EnsureRegistered();
                return WindivertNative.WinDivertHelperEvalFilter(
                    filter,
                    packetBuffer,
                    (uint)packetBytes.Length,
                    addressBuffer);
            }
            catch (DllNotFoundException exception)
            {
                throw new WindivertException(
                    WindivertError.DriverMissing,
                    WindivertError.Describe(WindivertError.DriverMissing),
                    exception);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(packetBuffer);
            Marshal.FreeHGlobal(addressBuffer);
        }
    }

    private static bool TryGetTransportOffset(ReadOnlySpan<byte> packet, byte expectedProtocol, out int offset)
    {
        offset = 0;
        if (packet.Length < Ipv4MinimumLength)
            return false;

        switch (packet[0] >> 4)
        {
            case 4:
            {
                var headerLength = (packet[0] & 0x0F) * 4;
                if (headerLength < Ipv4MinimumLength || packet.Length < headerLength)
                    return false;
                if (packet[9] != expectedProtocol)
                    return false;
                offset = headerLength;
                return true;
            }
            case 6:
                if (packet.Length < Ipv6HeaderLength)
                    return false;
                // 有擴充標頭就不處理，交給系統。
                if (packet[6] != expectedProtocol)
                    return false;
                offset = Ipv6HeaderLength;
                return true;
            default:
                return false;
        }
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> packet, int offset) =>
        (ushort)((packet[offset] << 8) | packet[offset + 1]);
}
