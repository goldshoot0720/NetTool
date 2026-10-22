using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class WindivertAddressTests
{
    [Fact]
    public void The_address_struct_matches_the_native_eighty_bytes()
    {
        Assert.Equal(80, WindivertAddress.Size);
        Assert.Equal(80, new WindivertAddress().ToArray().Length);
    }

    [Fact]
    public void Outbound_and_layer_survive_a_round_trip()
    {
        var original = new WindivertAddress(
            timestamp: 123456789,
            layer: WindivertLayer.Network,
            @event: 0,
            outbound: true,
            ipv6: false,
            interfaceIndex: 7,
            subInterfaceIndex: 3);

        var restored = WindivertAddress.FromBytes(original.ToArray());

        Assert.Equal(123456789, restored.Timestamp);
        Assert.Equal(WindivertLayer.Network, restored.Layer);
        Assert.True(restored.Outbound);
        Assert.False(restored.Ipv6);
        Assert.Equal(7u, restored.InterfaceIndex);
        Assert.Equal(3u, restored.SubInterfaceIndex);
    }

    [Fact]
    public void Inbound_packets_have_the_outbound_bit_clear()
    {
        var inbound = new WindivertAddress(0, WindivertLayer.Network, 0, outbound: false);
        Assert.False(WindivertAddress.FromBytes(inbound.ToArray()).Outbound);

        var outbound = new WindivertAddress(0, WindivertLayer.Network, 0, outbound: true);
        Assert.True(WindivertAddress.FromBytes(outbound.ToArray()).Outbound);
    }

    [Fact]
    public void A_short_buffer_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WindivertAddress.FromBytes(new byte[79]));
        Assert.Throws<ArgumentException>(() => new WindivertAddress().WriteTo(new byte[79]));
    }
}
