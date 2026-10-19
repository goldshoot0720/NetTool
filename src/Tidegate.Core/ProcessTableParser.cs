namespace Tidegate.Core;

public static class ProcessTableParser
{
    public static Dictionary<int, string> Parse(string text)
    {
        var map = new Dictionary<int, string>();
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

            var command = line[(space + 1)..].Trim();
            if (command.Length > 0)
                map[pid] = command;
        }

        return map;
    }
}
