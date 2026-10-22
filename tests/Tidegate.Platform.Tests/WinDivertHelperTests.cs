using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class WindivertHelperTests
{
    [Fact]
    public void Ipv4_tcp_ports_are_read_big_endian()
    {
        var packet = Ipv4Tcp(8080, 443);

        Assert.True(WindivertHelper.TryGetTcpPorts(packet, out var source, out var destination));
        Assert.Equal(8080, source);
        Assert.Equal(443, destination);
        Assert.True(WindivertHelper.TryGetProtocol(packet, out var protocol));
        Assert.Equal(6, protocol);
    }

    [Fact]
    public void Ipv6_tcp_ports_are_read_from_the_fixed_header()
    {
        var packet = Ipv6Tcp(50000, 8443);

        Assert.True(WindivertHelper.TryGetTcpPorts(packet, out var source, out var destination));
        Assert.Equal(50000, source);
        Assert.Equal(8443, destination);
    }

    [Fact]
    public void The_local_port_depends_on_the_direction()
    {
        var packet = Ipv4Tcp(51000, 443);

        Assert.True(WindivertHelper.TryGetLocalPort(packet, outbound: true, out var outgoing));
        Assert.Equal(51000, outgoing);

        Assert.True(WindivertHelper.TryGetLocalPort(packet, outbound: false, out var incoming));
        Assert.Equal(443, incoming);
    }

    [Fact]
    public void Udp_ports_are_read_when_the_protocol_says_udp()
    {
        var packet = Ipv4Tcp(1234, 53);
        packet[9] = 17;

        Assert.True(WindivertHelper.TryGetUdpPorts(packet, out var source, out var destination));
        Assert.Equal(1234, source);
        Assert.Equal(53, destination);
        Assert.False(WindivertHelper.TryGetTcpPorts(packet, out _, out _));
    }

    [Fact]
    public void Malformed_packets_are_rejected_instead_of_throwing()
    {
        Assert.False(WindivertHelper.TryGetTcpPorts(ReadOnlySpan<byte>.Empty, out _, out _));
        Assert.False(WindivertHelper.TryGetTcpPorts(new byte[10], out _, out _));
        Assert.False(WindivertHelper.TryGetProtocol(new byte[10], out _));

        var truncated = new byte[30];
        truncated[0] = 0x45;
        truncated[9] = 6;
        Assert.False(WindivertHelper.TryGetTcpPorts(truncated, out _, out _));
    }

    [Fact]
    public void Options_in_the_ipv4_header_shift_the_tcp_header()
    {
        var packet = Ipv4Tcp(60000, 80, headerLengthWords: 6);

        Assert.True(WindivertHelper.TryGetTcpPorts(packet, out var source, out var destination));
        Assert.Equal(60000, source);
        Assert.Equal(80, destination);
    }

    [Fact]
    public void Error_codes_have_readable_descriptions()
    {
        Assert.Contains("WinDivert.dll", WindivertError.Describe(WindivertError.DriverMissing), StringComparison.Ordinal);
        Assert.Contains("系統管理員", WindivertError.Describe(WindivertError.AccessDenied), StringComparison.Ordinal);
        Assert.Contains("Defender", WindivertError.Describe(WindivertError.DriverBlocked), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(WindivertError.Describe(9999)));
    }

    private static byte[] Ipv4Tcp(int sourcePort, int destinationPort, int headerLengthWords = 5)
    {
        var headerLength = headerLengthWords * 4;
        var packet = new byte[headerLength + 20];
        packet[0] = (byte)(0x40 | headerLengthWords);
        packet[9] = 6;
        WritePorts(packet, headerLength, sourcePort, destinationPort);
        return packet;
    }

    private static byte[] Ipv6Tcp(int sourcePort, int destinationPort)
    {
        var packet = new byte[40 + 20];
        packet[0] = 0x60;
        packet[6] = 6;
        WritePorts(packet, 40, sourcePort, destinationPort);
        return packet;
    }

    private static void WritePorts(byte[] packet, int offset, int source, int destination)
    {
        packet[offset] = (byte)(source >> 8);
        packet[offset + 1] = (byte)source;
        packet[offset + 2] = (byte)(destination >> 8);
        packet[offset + 3] = (byte)destination;
    }
}
