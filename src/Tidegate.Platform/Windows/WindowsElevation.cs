using System.ComponentModel;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 判斷目前是否以系統管理員身分執行。讀取 TCP 延伸統計與建立 QoS 原則都需要提權。
/// </summary>
public sealed class WindowsElevation : IElevation
{
    public bool IsElevated { get; } = IsProcessElevated();

    public bool TryRestartElevated(out string message)
    {
        if (TryStartElevated())
        {
            message = "已開啟新的視窗。";
            return true;
        }

        message = _lastError ?? "已取消管理者授權。";
        return false;
    }

    /// <summary>用 UAC 重新啟動一份提權的自己。使用者取消時回傳 false。</summary>
    public bool TryStartElevated() => TryStartElevated([]);

    /// <summary>用 UAC 重新啟動一份提權的自己，並把參數帶過去。</summary>
    public bool TryStartElevated(IEnumerable<string> arguments)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            _lastError = "找不到目前執行檔的路徑，請手動以管理員身分開啟。";
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };

            var list = arguments.ToArray();
            if (list.Length > 0)
                start.Arguments = string.Join(' ', list.Select(Quote));

            Process.Start(start);
            return true;
        }
        catch (Win32Exception)
        {
            _lastError = "已取消管理者授權。";
            return false;
        }
        catch (Exception exception)
        {
            _lastError = "無法重新啟動：" + exception.Message;
            return false;
        }
    }

    private static string Quote(string value) =>
        value.Contains(' ') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;

    private string? _lastError;

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TokenQuery, out var token))
            return false;

        try
        {
            if (!Win32.GetTokenInformation(token, Win32.TokenElevation, out var elevated, sizeof(int), out _))
                return false;
            return elevated != 0;
        }
        finally
        {
            Win32.CloseHandle(token);
        }
    }
}
