namespace Tidegate.Core;

public static class ProcessTableParser
{
    /// <summary>
    /// 解析 <c>ps -ax -o pid=,ppid=,comm=</c>。第二欄不是數字時視為沒有 ppid 欄，
    /// 整段當成可執行檔路徑（路徑本身可能含空白）。
    /// </summary>
    public static Dictionary<int, ProcessEntry> Parse(string text)
    {
        var map = new Dictionary<int, ProcessEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var space = line.IndexOf(' ');
            if (space <= 0)
                continue;
            if (!int.TryParse(line[..space], out var pid) || pid <= 0)
                continue;

            var rest = line[(space + 1)..].TrimStart();
            if (rest.Length == 0)
                continue;

            var second = rest.IndexOf(' ');
            if (second > 0 && int.TryParse(rest[..second], out var parentId) && parentId >= 0)
            {
                var command = rest[(second + 1)..].Trim();
                if (command.Length > 0)
                    map[pid] = new ProcessEntry(parentId, command);
                continue;
            }

            map[pid] = new ProcessEntry(0, rest);
        }

        return map;
    }
}
