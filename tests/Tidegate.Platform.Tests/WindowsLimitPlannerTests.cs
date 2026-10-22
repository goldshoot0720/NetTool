using Tidegate.Core;
using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class WindowsLimitPlannerTests
{
    private static readonly IReadOnlyDictionary<int, string> Paths =
        new Dictionary<int, string> { [10] = @"C:\Apps\chrome.exe" };

    [Fact]
    public void Both_directions_become_one_structured_limit()
    {
        var plan = new WindowsLimitPlanner().Build(
            [new LimitedApp("chrome", 1, [10], 2_000, 500)],
            [],
            Paths);

        var limit = Assert.Single(plan.EffectiveLimits);
        Assert.Equal("chrome", limit.AppName);
        Assert.Equal(2_000, limit.DownloadBytesPerSecond);
        Assert.Equal(500, limit.UploadBytesPerSecond);

        Assert.Equal("chrome：下載 2.0 KB/s，上傳 500 B/s", Assert.Single(plan.Rules));
        Assert.True(plan.HasWork);
        Assert.Empty(plan.Pipes);
    }

    [Fact]
    public void Download_only_limits_are_kept_because_the_shaper_handles_both_directions()
    {
        var plan = new WindowsLimitPlanner().Build(
            [new LimitedApp("chrome", 1, [10], 1_000, null)],
            [],
            Paths);

        var limit = Assert.Single(plan.EffectiveLimits);
        Assert.Equal(1_000, limit.DownloadBytesPerSecond);
        Assert.Null(limit.UploadBytesPerSecond);
        Assert.Contains("下載 1.0 KB/s", plan.Rules[0], StringComparison.Ordinal);
        Assert.DoesNotContain("上傳", plan.Rules[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_without_any_cap_is_skipped()
    {
        var plan = new WindowsLimitPlanner().Build(
            [new LimitedApp("ghost", 1, [77], null, null)],
            [],
            Paths);

        Assert.Empty(plan.EffectiveLimits);
        Assert.Empty(plan.Rules);
        Assert.False(plan.HasWork);
    }
}
