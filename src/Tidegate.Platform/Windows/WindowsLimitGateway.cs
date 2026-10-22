using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 把限速計畫交給封包節流器。套用＝開啟 WinDivert 攔截並開始排隊，
/// 清除＝關閉攔截，封包立刻恢復原本路徑。
/// </summary>
public sealed class WindowsLimitGateway : ILimitGateway
{
    private readonly WinDivertShaper _shaper;

    public WindowsLimitGateway(WinDivertShaper shaper)
    {
        _shaper = shaper;
    }

    public Task<LimitApplyResult> ApplyAsync(LimitPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.HasWork)
            return Task.FromResult(new LimitApplyResult(false, "沒有可寫入的規則。"));

        return Task.FromResult(_shaper.Start(plan.EffectiveLimits));
    }

    public Task<LimitApplyResult> ClearAsync(CancellationToken cancellationToken)
    {
        var wasRunning = _shaper.IsRunning;
        _shaper.Stop();
        return Task.FromResult(new LimitApplyResult(
            true,
            wasRunning ? "已停止封包節流，流量恢復原本路徑。" : "目前沒有在節流。"));
    }

    public void Stage(LimitPlan plan) => _shaper.Update(plan.EffectiveLimits);

    public void StopTracking() => _shaper.Stop();
}
