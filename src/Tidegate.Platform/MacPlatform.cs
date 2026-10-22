using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class MacLimitPlanner : ILimitPlanner
{
    public LimitPlan Build(
        IReadOnlyList<LimitedApp> apps,
        IReadOnlyList<FlowSocket> sockets,
        IReadOnlyDictionary<int, string> paths) => LimitPlanner.Build(apps, sockets);
}

/// <summary>macOS 的每個動作各自向系統要權限，程式本身不需要提權。</summary>
public sealed class MacElevation : IElevation
{
    public bool IsElevated => true;

    public bool TryRestartElevated(out string message)
    {
        message = "macOS 版不需要以管理者身分重新啟動。";
        return false;
    }
}
