namespace Tidegate.Core;

/// <summary>
/// 平台能力描述。介面會依這份資料決定要顯示什麼、哪些欄位可以編輯。
/// </summary>
public sealed record PlatformCapabilities(
    string Name,
    bool SupportsDownloadLimit,
    bool SupportsUploadLimit,
    bool RequiresElevation,
    bool TracksLiveConnections,
    string ObservationHint,
    string LimitHint);
