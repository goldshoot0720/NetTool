using System.Reflection;
using System.Runtime.InteropServices;

namespace Feng33WinDivert;

/// <summary>
/// 原生檔案可以改名，只要跟這個封裝放在同一個資料夾。
/// 載入順序：Feng33WinDivert.Native.dll → WinDivert.dll。
/// 驅動檔名不能改，WinDivert 內部寫死會去找 WinDivert64.sys。
/// </summary>
internal static class NativeLibraryLoader
{
    /// <summary>改名後的原生檔名（優先）。</summary>
    public const string RenamedLibrary = "Feng33WinDivert.Native.dll";

    /// <summary>官方原始檔名（退回用）。</summary>
    public const string OriginalLibrary = "WinDivert.dll";

    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // 已經有人註冊過解析器，交給它處理。
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, OriginalLibrary, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        foreach (var candidate in new[] { RenamedLibrary, OriginalLibrary })
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                return handle;
        }

        // 兩個都找不到：回傳 0 讓執行階段擲 DllNotFoundException，
        // 呼叫端會翻成「缺少驅動」的說明。
        return IntPtr.Zero;
    }
}
