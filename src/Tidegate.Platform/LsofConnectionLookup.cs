using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class LsofConnectionLookup : IConnectionLookup
{
    public async Task<IReadOnlyList<FlowSocket>> LookupAsync(
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken)
    {
        if (processIds.Count == 0)
            return [];

        var selected = processIds.Where(pid => pid > 0).Distinct().ToArray();
        if (selected.Length == 0)
            return [];

        var output = await ProcessText.ReadAsync(
            "/usr/sbin/lsof",
            ["-nP", "-iTCP", "-iUDP", "-a", "-p", string.Join(',', selected), "-F", "pcPn"],
            cancellationToken,
            allowExitCode1: true);
        return LsofFieldParser.Parse(output, selected.ToHashSet());
    }
}
