using Tidegate.Platform.Windows;

namespace Tidegate.App.ViewModels;

/// <summary>本機自測的一列：目標速率、實測速率、達成率。</summary>
public sealed class SelfTestRowViewModel(PacerBenchmarkResult result)
{
    public string TargetText { get; } = $"{result.TargetBytesPerSecond / 1000:0} KB/s";

    public string AchievedText { get; } = $"{result.AchievedBytesPerSecond / 1000:0} KB/s";

    public string RatioText { get; } = $"{result.Ratio * 100:0.0}%";

    /// <summary>±3% 以內算過關。超過就代表節流器沒貼住上限。</summary>
    public bool IsWithinTolerance { get; } = result.Ratio is >= 0.97 and <= 1.03;
}
