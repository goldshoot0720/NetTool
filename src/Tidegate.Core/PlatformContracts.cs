namespace Tidegate.Core;

/// <summary>
/// 需要管理者權限的平台（Windows）用它判斷目前是否已經提權，以及重新啟動提權的視窗。
/// </summary>
public interface IElevation
{
    bool IsElevated { get; }

    bool TryRestartElevated(out string message);
}

/// <summary>
/// 依平台把「哪些應用程式、要多少上限」變成該平台可執行的規則。
/// macOS 產生 pf／dummynet 規則，Windows 產生 QoS 原則。
/// </summary>
public interface ILimitPlanner
{
    LimitPlan Build(
        IReadOnlyList<LimitedApp> apps,
        IReadOnlyList<FlowSocket> sockets,
        IReadOnlyDictionary<int, string> paths);
}
