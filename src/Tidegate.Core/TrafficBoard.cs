namespace Tidegate.Core;

public sealed class TrafficBoard
{
    public const int HistoryLength = 60;

    private readonly LimitCatalog _limits;
    private readonly Dictionary<string, Queue<RatePoint>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int[]> _pids = new(StringComparer.Ordinal);
    private IReadOnlyList<ProcessTraffic>? _previous;
    private DateTimeOffset? _previousAt;
    private IReadOnlyList<AppChannel> _channels = [];
    private bool _hasSample;

    public TrafficBoard(LimitCatalog limits)
    {
        _limits = limits;
        Sort = SortSelection.SpeedDescending;
        HideIdle = true;
    }

    public SortSelection Sort { get; private set; }

    public string Search { get; private set; } = "";

    public bool HideIdle { get; private set; }

    public TrafficBoardState Ingest(
        IReadOnlyList<ProcessTraffic> sample,
        IReadOnlyDictionary<int, ProcessEntry> processes,
        DateTimeOffset at)
    {
        if (_previous is not null && _previousAt is { } previousAt)
        {
            var elapsed = (at - previousAt).TotalSeconds;
            if (elapsed is >= 0.2 and <= 10)
            {
                var rates = RateCalculator.FromCounters(_previous, sample, elapsed)
                    .Select(rate => rate with
                    {
                        DisplayName = ProcessOwnership.Resolve(rate.ProcessId, processes, rate.ProcessName),
                    })
                    .ToArray();
                _channels = ChannelAggregator.Aggregate(rates);
                Remember(_channels);
                _hasSample = true;
            }
        }

        _previous = sample;
        _previousAt = at;
        return Project();
    }

    public TrafficBoardState WithSort(SortSelection sort)
    {
        Sort = sort;
        return Project();
    }

    public TrafficBoardState WithSearch(string? search)
    {
        Search = search ?? "";
        return Project();
    }

    public TrafficBoardState WithHideIdle(bool hideIdle)
    {
        HideIdle = hideIdle;
        return Project();
    }

    public TrafficBoardState SetLimit(string appName, double? downloadBytesPerSecond, double? uploadBytesPerSecond)
    {
        _limits.Set(appName, downloadBytesPerSecond, uploadBytesPerSecond);
        return Project();
    }

    public IReadOnlyList<LimitedApp> LimitedApps()
    {
        var apps = new List<LimitedApp>();
        var index = 1;
        foreach (var rule in _limits.Rules.OrderBy(rule => rule.AppName, StringComparer.Ordinal))
        {
            if (rule.DownloadBytesPerSecond is null && rule.UploadBytesPerSecond is null)
                continue;

            var pids = _pids.TryGetValue(rule.AppName, out var known) ? known : [];
            apps.Add(new LimitedApp(
                rule.AppName,
                index,
                pids,
                rule.DownloadBytesPerSecond,
                rule.UploadBytesPerSecond));
            index++;
        }

        return apps;
    }

    private void Remember(IReadOnlyList<AppChannel> channels)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            seen.Add(channel.Name);
            if (!_history.TryGetValue(channel.Name, out var points))
            {
                points = new Queue<RatePoint>();
                _history[channel.Name] = points;
            }

            points.Enqueue(new RatePoint(channel.DownloadBytesPerSecond, channel.UploadBytesPerSecond));
            while (points.Count > HistoryLength)
                points.Dequeue();
            _pids[channel.Name] = channel.ProcessIds.ToArray();
        }

        foreach (var name in _history.Keys.Where(name => !seen.Contains(name)).ToArray())
            _history.Remove(name);
    }

    private IReadOnlyList<AppChannel> ChannelsForQuery()
    {
        var seen = new HashSet<string>(_channels.Select(channel => channel.Name), StringComparer.Ordinal);
        var saved = _limits.Rules
            .Where(rule => !seen.Contains(rule.AppName))
            .Select(rule => new AppChannel(
                rule.AppName,
                _pids.TryGetValue(rule.AppName, out var pids) ? pids : [],
                0,
                0));
        return saved.Any() ? _channels.Concat(saved).ToArray() : _channels;
    }

    private TrafficBoardState Project()
    {
        var visible = ChannelSorter.Sort(
            ChannelQuery.Filter(ChannelsForQuery(), Search, HideIdle, _limits.HasLimit),
            Sort);
        var rows = visible.Select(channel =>
        {
            var rule = _limits.Find(channel.Name);
            var history = _history.TryGetValue(channel.Name, out var points)
                ? (IReadOnlyList<RatePoint>)points.ToArray()
                : [];
            return new ChannelSnapshot(
                channel.Name,
                channel.ProcessIds,
                channel.DownloadBytesPerSecond,
                channel.UploadBytesPerSecond,
                rule?.DownloadBytesPerSecond,
                rule?.UploadBytesPerSecond,
                history);
        }).ToArray();

        return new TrafficBoardState(
            rows,
            _channels.Sum(channel => channel.DownloadBytesPerSecond),
            _channels.Sum(channel => channel.UploadBytesPerSecond),
            Sort,
            _hasSample);
    }
}
