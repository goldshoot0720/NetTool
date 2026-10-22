using System.Globalization;
using Tidegate.Core;
using Tidegate.Platform.Windows;

namespace Tidegate.App;

/// <summary>
/// 命令列診斷模式：直接把封包節流器跑起來，不開介面。
/// 用途是驗證 WinDivert 這條路真的能攔到封包，結果寫到 %APPDATA%\Tidegate\shape-test.log。
/// </summary>
internal static class ShapeTest
{
    private const string Usage = "用法：Tidegate.App.exe --shape <應用程式> <下載KBps> <上傳KBps> [秒數]";

    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Write(Usage);
            return 2;
        }

        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var downloadKbps)
            || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var uploadKbps))
        {
            Write("速率要是數字（KB/s）。");
            return 2;
        }

        var seconds = args.Length > 4 && int.TryParse(args[4], out var parsed) ? parsed : 60;
        var app = args[1];

        var elevation = new WindowsElevation();
        if (!elevation.IsElevated)
        {
            Write("未提權，改用 UAC 重新啟動。");
            var started = elevation.TryStartElevated(
            [
                "--shape",
                app,
                args[2],
                args[3],
                seconds.ToString(CultureInfo.InvariantCulture),
            ]);
            Write(started ? "已送出提權請求。" : "提權失敗或已取消。");
            return started ? 0 : 1;
        }

        Reset();

        using var shaper = new WinDivertShaper { Verbose = true };
        var result = shaper.Start(
        [
            new LimitRule(
                app,
                downloadKbps > 0 ? downloadKbps * 1000 : null,
                uploadKbps > 0 ? uploadKbps * 1000 : null),
        ]);

        Write($"start applied={result.Applied} message={result.Message}");
        if (!result.Applied)
            return 1;

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(2000);
            Write($"running apps={string.Join(",", shaper.Apps)} {shaper.DescribeStats()}");
        }

        shaper.Stop();
        Write("stopped");
        return 0;
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tidegate",
        "shape-test.log");

    private static void Reset()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(LogPath, "");
        }
        catch (IOException)
        {
        }
    }

    private static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss} {message}";
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Console.WriteLine(line);
    }
}
