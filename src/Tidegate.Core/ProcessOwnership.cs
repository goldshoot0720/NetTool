namespace Tidegate.Core;

/// <summary>
/// 把子行程的流量歸給它所屬的 .app。像「影音轉檔大師」這種殼，下載其實是它叫起來的
/// yt-dlp（跑在 Homebrew 的 python 上），nettop 只會看到「Python」；沿父行程鏈找到
/// 所屬 bundle，使用者才對得起來，限速時也才會連子行程的連線一起算。
/// </summary>
public static class ProcessOwnership
{
    /// <summary>往上追幾層就好，避免被 reparent 到 launchd 的行程亂認親。</summary>
    public const int MaxAncestorDepth = 6;

    /// <summary>
    /// 終端機與編輯器底下的行程是使用者自己跑的指令，不該算在宿主身上。
    /// </summary>
    private static readonly HashSet<string> HostBundles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Terminal",
        "iTerm",
        "iTerm2",
        "Warp",
        "Alacritty",
        "kitty",
        "WezTerm",
        "Hyper",
        "Visual Studio Code",
        "VSCodium",
        "Cursor",
        "Zed",
        "Sublime Text",
    };

    public static string Resolve(int processId, IReadOnlyDictionary<int, ProcessEntry> processes, string fallback)
    {
        if (!processes.TryGetValue(processId, out var entry))
            return AppName.FromCommand(null, fallback);

        var ownName = AppName.FromCommand(entry.Command, fallback);
        if (AppName.BundleNameOf(entry.Command) is not null)
            return ownName;

        var parentId = entry.ParentProcessId;
        for (var depth = 0; depth < MaxAncestorDepth; depth++)
        {
            if (parentId <= 0 || parentId == processId || !processes.TryGetValue(parentId, out var ancestor))
                break;

            var bundle = AppName.BundleNameOf(ancestor.Command);
            if (bundle is not null)
                return HostBundles.Contains(bundle) ? ownName : bundle;

            parentId = ancestor.ParentProcessId;
        }

        return ownName;
    }
}
