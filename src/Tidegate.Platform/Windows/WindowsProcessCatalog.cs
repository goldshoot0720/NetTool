using System.Text;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 用 QueryFullProcessImageName 取得每個程序的可執行檔完整路徑。
/// 這比讀 MainModule 寬鬆，不需要對每個程序都有完整權限。
/// </summary>
public sealed class WindowsProcessCatalog : IProcessCatalog
{
    private readonly Dictionary<int, string> _cache = new();

    public Task<IReadOnlyDictionary<int, ProcessEntry>> ProcessesAsync(CancellationToken cancellationToken)
    {
        // Windows 這條路不提供父行程，名稱照可執行檔路徑還原（不做子行程歸戶）。
        var map = new Dictionary<int, ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pid = process.Id;
                if (_cache.TryGetValue(pid, out var cached))
                {
                    map[pid] = new ProcessEntry(0, cached);
                    continue;
                }

                var path = Query(pid);
                if (path is null)
                    continue;

                _cache[pid] = path;
                map[pid] = new ProcessEntry(0, path);
            }
        }

        foreach (var pid in _cache.Keys.Where(pid => !map.ContainsKey(pid)).ToArray())
            _cache.Remove(pid);

        return Task.FromResult<IReadOnlyDictionary<int, ProcessEntry>>(map);
    }

    private static string? Query(int pid)
    {
        var handle = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!Win32.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                return null;
            return size > 0 ? buffer.ToString(0, size) : null;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }
}
