namespace Tidegate.Core;

public interface ITrafficSource
{
    IAsyncEnumerable<IReadOnlyList<ProcessTraffic>> WatchAsync(CancellationToken cancellationToken);
}

public interface IProcessCatalog
{
    Task<IReadOnlyDictionary<int, ProcessEntry>> ProcessesAsync(CancellationToken cancellationToken);
}

public interface IConnectionLookup
{
    Task<IReadOnlyList<FlowSocket>> LookupAsync(IReadOnlyCollection<int> processIds, CancellationToken cancellationToken);
}

public interface ILimitGateway
{
    Task<LimitApplyResult> ApplyAsync(LimitPlan plan, CancellationToken cancellationToken);

    Task<LimitApplyResult> ClearAsync(CancellationToken cancellationToken);

    void Stage(LimitPlan plan);

    void StopTracking();
}
