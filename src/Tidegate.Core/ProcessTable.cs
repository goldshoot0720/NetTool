namespace Tidegate.Core;

public static class ProcessTable
{
    /// <summary>給只需要「pid → 可執行檔路徑」的地方（限速計畫、封包節流器）用。</summary>
    public static Dictionary<int, string> CommandPaths(IReadOnlyDictionary<int, ProcessEntry> processes)
    {
        var map = new Dictionary<int, string>(processes.Count);
        foreach (var entry in processes)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value.Command))
                map[entry.Key] = entry.Value.Command;
        }

        return map;
    }
}
