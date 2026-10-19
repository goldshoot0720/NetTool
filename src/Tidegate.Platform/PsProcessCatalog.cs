using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class PsProcessCatalog : IProcessCatalog
{
    public async Task<IReadOnlyDictionary<int, string>> CommandPathsAsync(CancellationToken cancellationToken)
    {
        var output = await ProcessText.ReadAsync(
            "/bin/ps",
            ["-ax", "-o", "pid=,comm="],
            cancellationToken);
        return ProcessTableParser.Parse(output);
    }
}
