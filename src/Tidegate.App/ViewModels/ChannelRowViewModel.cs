using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidegate.Core;

namespace Tidegate.App.ViewModels;

public partial class ChannelRowViewModel : ViewModelBase
{
    public ChannelRowViewModel(string name)
    {
        Name = name;
        Initial = InitialOf(name);
    }

    public string Name { get; }

    public string Initial { get; }

    public double DownloadBytesPerSecond { get; private set; }

    public double UploadBytesPerSecond { get; private set; }

    public double? DownloadCapBytesPerSecond { get; private set; }

    public double? UploadCapBytesPerSecond { get; private set; }

    public double[] DownloadSeries { get; private set; } = [];

    public double[] UploadSeries { get; private set; } = [];

    [ObservableProperty]
    private string _downloadText = "0 B/s";

    [ObservableProperty]
    private string _uploadText = "0 B/s";

    [ObservableProperty]
    private string _totalText = "0 B/s";

    [ObservableProperty]
    private string _capText = "未設上限";

    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    private bool _hasCap;

    public void Update(ChannelSnapshot snapshot)
    {
        DownloadBytesPerSecond = snapshot.DownloadBytesPerSecond;
        UploadBytesPerSecond = snapshot.UploadBytesPerSecond;
        DownloadCapBytesPerSecond = snapshot.DownloadCapBytesPerSecond;
        UploadCapBytesPerSecond = snapshot.UploadCapBytesPerSecond;
        DownloadSeries = snapshot.History.Select(point => point.DownloadBytesPerSecond).ToArray();
        UploadSeries = snapshot.History.Select(point => point.UploadBytesPerSecond).ToArray();
        DownloadText = RateText.Format(snapshot.DownloadBytesPerSecond);
        UploadText = RateText.Format(snapshot.UploadBytesPerSecond);
        TotalText = RateText.Format(snapshot.TotalBytesPerSecond);
        HasCap = snapshot.DownloadCapBytesPerSecond is not null || snapshot.UploadCapBytesPerSecond is not null;
        CapText = DescribeCap(snapshot);
        Detail = DescribePids(snapshot.ProcessIds);
    }

    private static string DescribeCap(ChannelSnapshot snapshot)
    {
        if (snapshot.DownloadCapBytesPerSecond is null && snapshot.UploadCapBytesPerSecond is null)
            return "未設上限";

        var parts = new List<string>();
        if (snapshot.DownloadCapBytesPerSecond is double download)
            parts.Add("↓ " + RateText.Format(download));
        if (snapshot.UploadCapBytesPerSecond is double upload)
            parts.Add("↑ " + RateText.Format(upload));
        return string.Join("   ", parts);
    }

    private static string DescribePids(IReadOnlyList<int> processIds)
    {
        if (processIds.Count == 0)
            return "目前沒有程序";
        if (processIds.Count == 1)
            return "PID " + processIds[0].ToString(CultureInfo.InvariantCulture);
        var shown = string.Join(", ", processIds.Take(4));
        var extra = processIds.Count > 4 ? "…" : "";
        return $"{processIds.Count} 個程序 · {shown}{extra}";
    }

    private static string InitialOf(string name)
    {
        var rune = name.EnumerateRunes().FirstOrDefault();
        return rune.Value == 0 ? "?" : rune.ToString();
    }
}
