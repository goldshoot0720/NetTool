using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 每秒讀一次系統的 TCP 連線表，向每條連線要累計位元組，再換算成每個程序的速度。
/// 只涵蓋 TCP：Windows 沒有提供 UDP 的逐連線位元組統計。
/// </summary>
public sealed class WindowsTrafficSource : ITrafficSource, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IntPtr _rw = Marshal.AllocHGlobal(WindowsTcpStats.DataRwSize);
    private readonly IntPtr _rod = Marshal.AllocHGlobal(WindowsTcpStats.DataRodSize);
    private readonly ConnectionAccumulator _accumulator = new();
    private readonly HashSet<string> _enabled = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _names = new();
    private int _emptyStreak;
    private bool _loggedFirstSuccess;
    private bool _disposed;

    public async IAsyncEnumerable<IReadOnlyList<ProcessTraffic>> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Sample();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Marshal.FreeHGlobal(_rw);
        Marshal.FreeHGlobal(_rod);
    }

    private IReadOnlyList<ProcessTraffic> Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connections = WindowsTcpTable.Read();
        var counters = new List<ConnectionCounters>(connections.Count);
        var denied = false;
        var failures = 0;
        var lastError = 0u;

        foreach (var connection in connections)
        {
            if (connection.ProcessId <= 0
                || connection.IsListening
                || connection.State == Win32.TcpStateDeleteTcb
                || connection.RemotePort == 0)
            {
                continue;
            }

            var key = connection.Key;
            if (!_enabled.Contains(key))
            {
                var enable = WindowsTcpStats.Enable(connection, _rw);
                if (enable != Win32.ErrorSuccess)
                {
                    denied |= enable == Win32.ErrorAccessDenied;
                    if (enable != Win32.ErrorNotFound)
                    {
                        failures++;
                        lastError = enable;
                    }

                    continue;
                }

                _enabled.Add(key);
            }

            var read = WindowsTcpStats.Read(connection, _rw, _rod, out var bytesIn, out var bytesOut);
            if (read != Win32.ErrorSuccess)
            {
                denied |= read == Win32.ErrorAccessDenied;
                _enabled.Remove(key);

                // 1168 是連線在讀表格與讀統計之間結束，屬正常流動，不算失敗。
                if (read != Win32.ErrorNotFound)
                {
                    failures++;
                    lastError = read;
                }

                continue;
            }

            counters.Add(new ConnectionCounters(key, connection.ProcessId, bytesIn, bytesOut));
        }

        PruneEnabled(counters);

        if (counters.Count == 0 || failures > 0)
            WindowsLog.Write($"sample conns={connections.Count} ok={counters.Count} fail={failures} last={lastError} enabled={_enabled.Count}");
        else if (!_loggedFirstSuccess)
        {
            _loggedFirstSuccess = true;
            WindowsLog.Write($"first sample conns={connections.Count} ok={counters.Count} bytesIn={counters.Sum(c => c.BytesIn)} bytesOut={counters.Sum(c => c.BytesOut)}");
        }

        if (denied && counters.Count == 0)
            throw new IOException("需要系統管理員權限才能讀取每個應用程式的流量。請以管理員身分重新啟動潮閘。");

        // 連續讀不到任何數字，而且每次都失敗，代表這個系統的 ESTATS 用不了。
        // 只是沒有連線時 failures 會是 0，不會誤報。
        if (counters.Count == 0 && failures > 0)
        {
            _emptyStreak++;
            if (_emptyStreak >= 10)
                throw new IOException($"讀不到 TCP 延伸統計（錯誤 {lastError}）。這台電腦可能不支援 ESTATS。");
        }
        else
        {
            _emptyStreak = 0;
        }

        return _accumulator.Update(counters, Names(counters));
    }

    private void PruneEnabled(List<ConnectionCounters> counters)
    {
        if (_enabled.Count <= counters.Count * 2 + 32)
            return;

        var live = counters.Select(counter => counter.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _enabled.Where(key => !live.Contains(key)).ToArray())
            _enabled.Remove(key);
    }

    private IReadOnlyDictionary<int, string> Names(List<ConnectionCounters> counters)
    {
        foreach (var pid in counters.Select(counter => counter.ProcessId).Distinct())
        {
            if (_names.ContainsKey(pid))
                continue;

            try
            {
                using var process = Process.GetProcessById(pid);
                _names[pid] = process.ProcessName;
            }
            catch (ArgumentException)
            {
                _names[pid] = "";
            }
            catch (InvalidOperationException)
            {
                _names[pid] = "";
            }
        }

        if (_names.Count > 2048)
        {
            var live = counters.Select(counter => counter.ProcessId).ToHashSet();
            foreach (var pid in _names.Keys.Where(pid => !live.Contains(pid)).ToArray())
                _names.Remove(pid);
        }

        return _names;
    }
}
