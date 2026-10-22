using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// Windows 的限速計畫：不再是 QoS 原則，而是一組交給封包節流器的結構化上限。
/// 節流器會自己在網路層把封包排隊，所以不需要逐連線的規則。
/// </summary>
public sealed class WindowsLimitPlanner : ILimitPlanner
{
    public LimitPlan Build(
        IReadOnlyList<LimitedApp> apps,
        IReadOnlyList<FlowSocket> sockets,
        IReadOnlyDictionary<int, string> paths)
    {
        var rules = new List<string>();
        var notes = new List<string>();
        var limits = new List<LimitRule>();

        foreach (var app in apps.OrderBy(item => item.Index))
        {
            if (app.DownloadBytesPerSecond is null && app.UploadBytesPerSecond is null)
                continue;

            limits.Add(new LimitRule(app.Name, app.DownloadBytesPerSecond, app.UploadBytesPerSecond));

            var parts = new List<string>();
            if (app.DownloadBytesPerSecond is double download)
                parts.Add("下載 " + RateText.Format(download));
            if (app.UploadBytesPerSecond is double upload)
                parts.Add("上傳 " + RateText.Format(upload));
            rules.Add($"{app.Name}：{string.Join("，", parts)}");
        }

        if (limits.Count == 0)
            notes.Add("沒有可套用的上限。先選一條航道，填入 KB/s。");
        else
            notes.Add("用 WinDivert 在網路層排隊節流，下載與上傳都有效；需要 WinDivert 2.2 的 DLL 與驅動。");

        return new LimitPlan([], rules, notes, limits);
    }
}
