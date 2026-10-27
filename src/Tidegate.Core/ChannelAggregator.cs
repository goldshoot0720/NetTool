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
                group.Sum(rate => rate.UploadBytesPerSecond),
                group.Select(rate => rate.ProcessName)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, group.Key, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }
}
