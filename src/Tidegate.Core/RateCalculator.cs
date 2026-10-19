namespace Tidegate.Core;

public static class RateCalculator
{
    public static IReadOnlyList<ProcessRate> FromCounters(
        IReadOnlyList<ProcessTraffic> previous,
        IReadOnlyList<ProcessTraffic> current,
        double elapsedSeconds)
    {
        if (elapsedSeconds < 0.2 || elapsedSeconds > 10 || double.IsNaN(elapsedSeconds))
            return [];

        var before = new Dictionary<int, ProcessTraffic>();
        foreach (var row in previous)
            before[row.ProcessId] = row;

        var rates = new List<ProcessRate>();
        var seen = new HashSet<int>();
        foreach (var row in current)
        {
            if (!seen.Add(row.ProcessId))
                continue;
            if (!before.TryGetValue(row.ProcessId, out var prior))
                continue;

            var down = Math.Max(0, row.BytesIn - prior.BytesIn) / elapsedSeconds;
            var up = Math.Max(0, row.BytesOut - prior.BytesOut) / elapsedSeconds;
            rates.Add(new ProcessRate(row.ProcessId, row.ProcessName, row.ProcessName, down, up));
        }

        return rates;
    }
}
