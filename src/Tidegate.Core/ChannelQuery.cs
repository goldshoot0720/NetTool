namespace Tidegate.Core;

public static class ChannelQuery
{
    public const double IdleBytesPerSecond = 1000;

    public static IReadOnlyList<AppChannel> Filter(
        IEnumerable<AppChannel> channels,
        string? search,
        bool hideIdle,
        Func<string, bool> hasLimit)
    {
        IEnumerable<AppChannel> query = channels;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(channel =>
                channel.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        if (hideIdle)
        {
            query = query.Where(channel =>
                channel.TotalBytesPerSecond >= IdleBytesPerSecond || hasLimit(channel.Name));
        }

        return query.ToArray();
    }
}
