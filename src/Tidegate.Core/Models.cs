namespace Tidegate.Core;

public enum SortKey
{
    Name,
    Speed,
}

public enum SortDirection
{
    Ascending,
    Descending,
}

public enum SpeedBasis
{
    Total,
    Download,
    Upload,
}

public sealed record ProcessTraffic(string ProcessName, int ProcessId, long BytesIn, long BytesOut);

public sealed record ProcessRate(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond);

public sealed record AppChannel(
    string Name,
    IReadOnlyList<int> ProcessIds,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond)
{
    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;
}

public readonly record struct RatePoint(double DownloadBytesPerSecond, double UploadBytesPerSecond);

public sealed record ChannelSnapshot(
    string Name,
    IReadOnlyList<int> ProcessIds,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    double? DownloadCapBytesPerSecond,
    double? UploadCapBytesPerSecond,
    IReadOnlyList<RatePoint> History)
{
    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;
}

public sealed record TrafficBoardState(
    IReadOnlyList<ChannelSnapshot> Rows,
    double TotalDownloadBytesPerSecond,
    double TotalUploadBytesPerSecond,
    SortSelection Sort,
    bool HasSample);

public sealed record LimitRule(string AppName, double? DownloadBytesPerSecond, double? UploadBytesPerSecond);

public sealed record FlowSocket(
    int ProcessId,
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort);

public sealed record LimitedApp(
    string Name,
    int Index,
    IReadOnlyList<int> ProcessIds,
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond);

public sealed record PipeSpec(int Number, long BitsPerSecond);

public sealed record LimitPlan(
    IReadOnlyList<PipeSpec> Pipes,
    IReadOnlyList<string> Rules,
    IReadOnlyList<string> Notes)
{
    public bool HasWork => Pipes.Count > 0 && Rules.Count > 0;
}

public sealed record LimitApplyResult(bool Applied, string Message);
