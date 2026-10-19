namespace Tidegate.Core;

public static class LsofFieldParser
{
    public static IReadOnlyList<FlowSocket> Parse(string text, IReadOnlySet<int>? processIds = null)
    {
        var sockets = new List<FlowSocket>();
        var pid = 0;
        var protocol = "";
        foreach (var raw in text.Split('\n'))
        {
            if (raw.Length < 2)
                continue;

            var value = raw[1..];
            switch (raw[0])
            {
                case 'p':
                    pid = int.TryParse(value, out var parsed) ? parsed : 0;
                    protocol = "";
                    break;
                case 'P':
                    protocol = value.Equals("TCP", StringComparison.OrdinalIgnoreCase) ? "tcp"
                        : value.Equals("UDP", StringComparison.OrdinalIgnoreCase) ? "udp"
                        : "";
                    break;
                case 'n' when pid > 0 && protocol.Length > 0 && (processIds is null || processIds.Contains(pid)):
                    if (TryParseName(value, out var localAddress, out var localPort, out var remoteAddress, out var remotePort))
                    {
                        sockets.Add(new FlowSocket(pid, protocol, localAddress, localPort, remoteAddress, remotePort));
                    }
                    break;
            }
        }

        return sockets;
    }

    public static bool TryParseName(
        string text,
        out string localAddress,
        out int localPort,
        out string? remoteAddress,
        out int? remotePort)
    {
        localAddress = "";
        localPort = 0;
        remoteAddress = null;
        remotePort = null;
        var token = text.Trim();
        if (token.Length == 0)
            return false;

        var arrow = token.IndexOf("->", StringComparison.Ordinal);
        var local = arrow >= 0 ? token[..arrow] : token;
        var remote = arrow >= 0 ? token[(arrow + 2)..] : "";
        if (!TryHostPort(local, out localAddress, out localPort))
            return false;
        if (remote.Length == 0)
            return true;
        if (!TryHostPort(remote, out var remoteHost, out var remoteHostPort))
            return false;

        remoteAddress = remoteHost;
        remotePort = remoteHostPort;
        return true;
    }

    public static bool TryHostPort(string text, out string address, out int port)
    {
        address = "";
        port = 0;
        var token = text.Trim();
        if (token.StartsWith('['))
        {
            var end = token.IndexOf(']');
            if (end <= 1 || end + 1 >= token.Length || token[end + 1] != ':')
                return false;
            address = token[1..end];
            return address.Length > 0 && int.TryParse(token[(end + 2)..], out port);
        }

        var colon = token.LastIndexOf(':');
        if (colon <= 0)
            return false;

        address = token[..colon];
        return address.Length > 0 && int.TryParse(token[(colon + 1)..], out port);
    }
}
