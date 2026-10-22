using System.Globalization;
using Tidegate.Platform.Windows;

namespace Tidegate.App;

/// <summary>
/// 本機自測：不開介面、不裝驅動、不提權，直接量節流器實際放行的速度。
///
/// 量的是 <c>PacedSender</c> 本身，也就是按「套用上限」之後真正決定封包節奏的那一段。
/// 結果同時寫到 %APPDATA%\Tidegate\selftest.log。
/// </summary>
internal static class SelfTest
{
    private const string Usage = "用法：Tidegate.App.exe --selftest [KBps ...]（不給速率就跑預設那幾組）";
    private const int SecondsPerRate = 2;

    public static int Run(string[] args)
    {
        var rates = ParseRates(args);
        if (rates is null)
        {
            Write(Usage);
            return 2;
        }

        if (rates.Length == 0)
            rates = [.. PacerBenchmark.DefaultRatesKiloBytes];

        Reset();
        Write($"節流器自測：每組 {SecondsPerRate} 秒，封包酬載 1460 位元組（不需要驅動與網路）。");

        var failed = 0;
        foreach (var kiloBytes in rates)
        {
            var result = PacerBenchmark.Run(kiloBytes * 1000, TimeSpan.FromSeconds(SecondsPerRate));
            var ratio = result.Ratio;
            var ok = ratio is >= 0.97 and <= 1.03;
            if (!ok)
                failed++;

            Write(
                $"{kiloBytes,8:0} KB/s → 實測 {result.AchievedBytesPerSecond / 1000,8:0} KB/s"
                + $"（{ratio * 100,6:0.0}%，{result.Packets} 個封包，{result.Elapsed.TotalSeconds:0.00} 秒）"
                + (ok ? "" : "  ← 超出 ±3%"));
        }

        Write(failed == 0 ? "全部在 ±3% 內。" : $"有 {failed} 組超出 ±3%。");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>回傳 null 代表參數不是數字。</summary>
    private static double[]? ParseRates(string[] args)
    {
        var rates = new List<double>();
        for (var index = 1; index < args.Length; index++)
        {
            if (!double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate <= 0)
                return null;
            rates.Add(rate);
        }

        return rates.ToArray();
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tidegate",
        "selftest.log");

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
