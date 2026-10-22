namespace Tidegate.Platform.Windows;

/// <summary>WinDivert 呼叫失敗時擲出。</summary>
internal sealed class WindivertException : Exception
{
    public WindivertException(int errorCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Windows 錯誤碼；驅動或 DLL 不存在時是 <see cref="WindivertError.DriverMissing"/>。</summary>
    public int ErrorCode { get; }
}

/// <summary>WinDivert 常見錯誤碼與對應說明。</summary>
internal static class WindivertError
{
    public const int FileNotFound = 2;
    public const int AccessDenied = 5;
    public const int InvalidParameter = 87;
    public const int ModuleNotFound = 126;
    public const int DriverBlocked = 577;
    public const int DriverFailedPriorUnload = 1275;
    public const int OperationAborted = 995;

    /// <summary>本封裝自訂：找不到 WinDivert.dll 或驅動。</summary>
    public const int DriverMissing = -100;

    /// <summary>把錯誤碼翻成可以給使用者看的句子。</summary>
    public static string Describe(int errorCode) => errorCode switch
    {
        DriverMissing => "找不到 WinDivert.dll 或 WinDivert64.sys。",
        FileNotFound => "找不到 WinDivert.dll 或 WinDivert64.sys。",
        ModuleNotFound => "WinDivert.dll 載入失敗（相依檔案不完整，或位元數不符）。",
        AccessDenied => "無法載入 WinDivert 驅動：存取被拒。請以系統管理員身分執行。",
        DriverBlocked => "Windows 拒絕載入 WinDivert 驅動（簽章或安全原則）。請檢查 Defender／應用程式控制。",
        DriverFailedPriorUnload => "先前的 WinDivert 驅動沒有卸載乾淨，請重新開機後再試。",
        InvalidParameter => "WinDivert 不接受這個參數（通常是過濾條件語法錯誤）。",
        OperationAborted => "操作已中止。",
        _ => $"WinDivert 呼叫失敗（錯誤 {errorCode}）。",
    };
}
