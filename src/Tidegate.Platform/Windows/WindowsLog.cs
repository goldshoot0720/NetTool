using System.Text;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 只在讀不到數字時才寫一行到 %APPDATA%\Tidegate\windows.log，
/// 讓「畫面上什麼都沒有」有辦法追。正常運作時不會產生這個檔案。
/// </summary>
internal static class WindowsLog
{
    private const long MaxBytes = 200_000;

    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tidegate",
        "windows.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrEmpty(directory))
                    return;

                Directory.CreateDirectory(directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    file.Delete();

                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
