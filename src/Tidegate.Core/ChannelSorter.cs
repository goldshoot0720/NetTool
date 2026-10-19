namespace Tidegate.Core;

public static class ChannelSorter
{
    public static IReadOnlyList<AppChannel> Sort(IEnumerable<AppChannel> channels, SortSelection sort)
    {
        IOrderedEnumerable<AppChannel> ordered = sort.Key == SortKey.Name
            ? sort.Direction == SortDirection.Ascending
                ? channels
                    .OrderBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(channel => channel.Name, StringComparer.Ordinal)
                : channels
                    .OrderByDescending(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(channel => channel.Name, StringComparer.Ordinal)
            : sort.Direction == SortDirection.Ascending
                ? channels
                    .OrderBy(channel => Speed(channel, sort.Basis))
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(channel => channel.Name, StringComparer.Ordinal)
                : channels
                    .OrderByDescending(channel => Speed(channel, sort.Basis))
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(channel => channel.Name, StringComparer.Ordinal);

        return ordered.ToArray();
    }

    public static double Speed(AppChannel channel, SpeedBasis basis) => basis switch
    {
        SpeedBasis.Download => channel.DownloadBytesPerSecond,
        SpeedBasis.Upload => channel.UploadBytesPerSecond,
        _ => channel.TotalBytesPerSecond,
    };
}
