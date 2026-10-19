namespace Tidegate.Core;

public static class AppName
{
    public static string FromCommand(string? command, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            var marker = command.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0 && command.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                marker = command.Length - 4;
            if (marker > 0)
            {
                var start = command.LastIndexOf('/', marker - 1);
                var name = command.Substring(start + 1, marker - (start + 1));
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? "未知應用" : fallback;
    }
}
