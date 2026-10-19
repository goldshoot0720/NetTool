namespace Tidegate.Core;

public static class ChannelAggregator
{
    public static IReadOnlyList<AppChannel> Aggregate(IEnumerable<ProcessRate> rates)
    {
        return rates
            .GroupBy(rate => rate.DisplayName, StringComparer.Ordinal)
            .Select(group => new AppChannel(
                group.Key,
                group.Select(rate => rate.ProcessId).Distinct().OrderBy(pid => pid).ToArray(),
                group.Sum(rate => rate.DownloadBytesPerSecond),
                group.Sum(rate => rate.UploadBytesPerSecond)))
            .ToArray();
    }
}
