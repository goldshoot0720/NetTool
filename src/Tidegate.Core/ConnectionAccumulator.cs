namespace Tidegate.Core;

/// <summary>
/// 單一連線的累計位元組。Windows 的 ESTATS 是「每條 TCP 連線」的計數，
/// 連線一關閉數字就消失，所以要先把差值累積到程序身上。
/// </summary>
public readonly record struct ConnectionCounters(string Key, int ProcessId, long BytesIn, long BytesOut);

/// <summary>
/// 把每條連線的累計位元組換成每個程序的單調累計值。
/// 連線結束後它貢獻的位元組仍然留在總量裡，速度才不會忽然掉到零。
/// </summary>
public sealed class ConnectionAccumulator
{
    public const int IdleSamplesBeforeForget = 300;

    private readonly Dictionary<string, (long In, long Out)> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (long In, long Out)> _totals = new();
    private readonly Dictionary<int, int> _idle = new();

    public IReadOnlyList<ProcessTraffic> Update(
        IReadOnlyList<ConnectionCounters> sample,
        IReadOnlyDictionary<int, string> names)
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<int>();

        foreach (var connection in sample)
        {
            if (connection.ProcessId <= 0)
                continue;

            live.Add(connection.Key);
            active.Add(connection.ProcessId);

            var previousIn = 0L;
            var previousOut = 0L;
            if (_seen.TryGetValue(connection.Key, out var previous))
            {
                previousIn = previous.In;
                previousOut = previous.Out;
            }

            var deltaIn = Math.Max(0, connection.BytesIn - previousIn);
            var deltaOut = Math.Max(0, connection.BytesOut - previousOut);
            (long In, long Out) totals = _totals.TryGetValue(connection.ProcessId, out var known) ? known : (0, 0);
            _totals[connection.ProcessId] = (totals.In + deltaIn, totals.Out + deltaOut);
            _seen[connection.Key] = (connection.BytesIn, connection.BytesOut);
        }

        foreach (var key in _seen.Keys.Where(key => !live.Contains(key)).ToArray())
            _seen.Remove(key);

        foreach (var pid in _totals.Keys.ToArray())
        {
            if (active.Contains(pid))
            {
                _idle[pid] = 0;
                continue;
            }

            var idle = (_idle.TryGetValue(pid, out var knownIdle) ? knownIdle : 0) + 1;
            if (idle > IdleSamplesBeforeForget)
            {
                _totals.Remove(pid);
                _idle.Remove(pid);
                continue;
            }

            _idle[pid] = idle;
        }

        var rows = new List<ProcessTraffic>(active.Count);
        foreach (var pid in active.OrderBy(pid => pid))
        {
            var totals = _totals[pid];
            var name = names.TryGetValue(pid, out var found) && !string.IsNullOrWhiteSpace(found) ? found : "";
            rows.Add(new ProcessTraffic(name, pid, totals.In, totals.Out));
        }

        return rows;
    }
}
