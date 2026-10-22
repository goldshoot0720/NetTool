using Tidegate.Core;

namespace Tidegate.Platform.Windows;

public sealed class WindowsConnectionLookup : IConnectionLookup
{
    public Task<IReadOnlyList<FlowSocket>> LookupAsync(
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken)
    {
        var wanted = processIds.Where(pid => pid > 0).ToHashSet();
        if (wanted.Count == 0)
            return Task.FromResult<IReadOnlyList<FlowSocket>>([]);

        var sockets = WindowsTcpTable.Read()
            .Where(connection => wanted.Contains(connection.ProcessId)
                && !connection.IsListening
                && connection.State != Win32.TcpStateDeleteTcb
                && connection.RemotePort != 0)
            .Select(connection => new FlowSocket(
                connection.ProcessId,
                "tcp",
                connection.LocalAddress,
                connection.LocalPort,
                connection.RemoteAddress,
                connection.RemotePort))
            .Distinct()
            .ToArray();

        return Task.FromResult<IReadOnlyList<FlowSocket>>(sockets);
    }
}
