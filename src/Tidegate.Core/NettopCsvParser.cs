namespace Tidegate.Core;

public static class NettopCsvParser
{
    public static IReadOnlyList<ProcessTraffic> Parse(string text)
    {
        var byPid = new Dictionary<int, ProcessTraffic>();
        foreach (var raw in text.Split('\n'))
        {
            if (TryParseLine(raw, out var row))
                byPid[row.ProcessId] = row;
        }

        return byPid.Values.ToArray();
    }

    public static bool TryParseLine(string raw, out ProcessTraffic row)
    {
        row = null!;
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith(",bytes_in", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = line.Split(',');
        if (parts.Length < 3)
            return false;
        if (!TrySplitProcess(parts[0], out var name, out var pid))
            return false;
        if (!long.TryParse(parts[1], out var bytesIn) || !long.TryParse(parts[2], out var bytesOut))
            return false;
        if (bytesIn < 0 || bytesOut < 0)
            return false;

        row = new ProcessTraffic(name, pid, bytesIn, bytesOut);
        return true;
    }

    public static bool TrySplitProcess(string label, out string name, out int pid)
    {
        name = "";
        pid = 0;
        var dot = label.LastIndexOf('.');
        if (dot <= 0 || dot >= label.Length - 1)
            return false;
        if (!int.TryParse(label[(dot + 1)..], out pid) || pid <= 0)
            return false;

        name = label[..dot];
        return name.Length > 0;
    }
}
