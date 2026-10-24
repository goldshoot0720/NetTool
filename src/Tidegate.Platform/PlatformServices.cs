using Tidegate.Core;
using Tidegate.Platform.Windows;

namespace Tidegate.Platform;

/// <summary>
/// 依目前作業系統挑選觀測、查詢與限速的實作。介面只透過 Core 的介面與能力描述認識平台。
/// </summary>
public sealed record PlatformServices(
    ITrafficSource Traffic,
    IProcessCatalog Catalog,
    IConnectionLookup Connections,
    ILimitPlanner Planner,
    ILimitGateway Gateway,
    IElevation Elevation,
    PlatformCapabilities Capabilities)
{
    public static PlatformServices Create(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var elevation = new WindowsElevation();
            var shaper = new WinDivertShaper();
            WindowsLog.Write($"start elevated={elevation.IsElevated} os={Environment.OSVersion.Version}");
            return new PlatformServices(
                new WindowsTrafficSource(),
                new WindowsProcessCatalog(),
                new WindowsConnectionLookup(),
                new WindowsLimitPlanner(),
                new WindowsLimitGateway(shaper),
                elevation,
                new PlatformCapabilities(
                    Name: "Windows",
                    SupportsDownloadLimit: true,
                    SupportsUploadLimit: true,
                    RequiresElevation: true,
                    TracksLiveConnections: true,
                    ObservationHint: "每秒讀取系統 TCP 連線的延伸統計（ESTATS）累計位元組。UDP 流量目前不計入。",
                    LimitHint: "Windows 版用 WinDivert 在網路層把封包排隊節流，下載與上傳都有效。"
                        + "需要 WinDivert 2.2 的 WinDivert.dll 與 WinDivert64.sys 放在程式目錄；驅動常被 Defender 誤判，要先加排除。"
                        + "沒按「套用上限」時完全不會攔截封包。1 KB = 1000 位元組。"));
        }

        if (OperatingSystem.IsMacOS())
        {
            return new PlatformServices(
                new NettopTrafficSource(),
                new PsProcessCatalog(),
                new LsofConnectionLookup(),
                new MacLimitPlanner(),
                new MacLimitGateway(directory),
                new MacElevation(),
                new PlatformCapabilities(
                    Name: "macOS",
                    SupportsDownloadLimit: true,
                    SupportsUploadLimit: true,
                    RequiresElevation: false,
                    TracksLiveConnections: true,
                    ObservationHint: "每秒讀取 nettop 的外部介面累計位元組，用兩次樣本的差計算速度。",
                    LimitHint: "輸入 KB/s 後按套用上限。會先保存這個數字，再要求管理者權限。套用之後，新連線會自動補進同一條管線，"
                        + "現有連線也會重新套上上限。使用系統既有的 com.apple 錨點；主規則集若缺少這個錨點，"
                        + "會載入一次 /etc/pf.conf（不改寫檔案）。1 KB = 1000 位元組。"));
        }

        throw new PlatformNotSupportedException("潮閘目前支援 macOS 與 Windows。");
    }
}
