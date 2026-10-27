namespace Tidegate.Core;

public static class AppName
{
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat", ".cmd", ".scr"];

    public static string FromCommand(string? command, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            var bundle = BundleNameOf(command);
            if (bundle is not null)
                return bundle;

            var file = FileNameOf(command);
            foreach (var extension in ExecutableExtensions)
            {
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    file = file[..^extension.Length];
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(file))
                return file;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "未知應用" : fallback;
    }

    /// <summary>路徑在 .app 裡就回傳 bundle 名稱（不含 .app），否則 null。</summary>
    public static string? BundleNameOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var marker = command.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 && command.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            marker = command.Length - 4;
        if (marker <= 0)
            return null;

        var start = Math.Max(command.LastIndexOf('/', marker - 1), command.LastIndexOf('\\', marker - 1));
        var name = command.Substring(start + 1, marker - (start + 1));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string FileNameOf(string command)
    {
        var separator = command.LastIndexOfAny(['/', '\\']);
        return separator >= 0 ? command[(separator + 1)..] : command;
    }
}
