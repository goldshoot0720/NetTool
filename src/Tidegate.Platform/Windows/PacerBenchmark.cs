using System.Diagnostics;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>一次節流精度量測的結果。</summary>
public sealed record PacerBenchmarkResult(
    double TargetBytesPerSecond,
    double AchievedBytesPerSecond,
    long Packets,
    TimeSpan Elapsed)
{
    /// <summary>實測速率 ÷ 目標速率。1 代表剛好貼著上限。</summary>
    public double Ratio => TargetBytesPerSecond > 0 ? AchievedBytesPerSecond / TargetBytesPerSecond : 0;
}

/// <summary>
/// 本機自測：把真正的節流器（<see cref="PacedSender{TPacket}"/>）餵滿封包，量它實際放行的速度。
///
/// 不需要驅動、不需要管理員權限、不需要網路，所以任何一台機器都能跑，
/// 用來確認「節流器本身」有沒有貼住上限。真正上網時還會多一層 TCP 的行為
/// （丟包、重傳、壅塞窗），那是這個量測看不到的。
/// </summary>
public static class PacerBenchmark
{
    private const int PayloadBytes = 1460;

    /// <summary>沒有指定速率時跑的幾組（KB/s）。介面與命令列共用同一份。</summary>
    public static IReadOnlyList<double> DefaultRatesKiloBytes { get; } = [100, 500, 1000, 5000, 20000];

    public static PacerBenchmarkResult Run(double bytesPerSecond, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSecond);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "量測時間必須大於 0。");

        var packet = new BenchPacket(PayloadBytes);

        long firstTicks = 0;
        long bytes = 0;
        var packets = 0;
        var started = false;
        var counting = true;

        using var sender = new PacedSender<BenchPacket>(
            "benchmark",
            new LimitRule("benchmark", bytesPerSecond, null),
            packet => packet.Payload,
            packet =>
            {
                if (!Volatile.Read(ref counting))
                    return;

                var now = Stopwatch.GetTimestamp();
                if (!started)
                {
                    // 起步那一刻桶子是滿的，那不算穩態，跳過。
                    started = true;
                    firstTicks = now;
                    return;
                }

                bytes += packet.Payload;
                packets++;
            });

        sender.Start();

        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            // 模擬一條塞滿的連線：佇列一有空位就補，滿了就像真的丟包一樣跳過。
            if (!sender.TryEnqueue(packet, outbound: false))
                Thread.Sleep(1);
        }

        Volatile.Write(ref counting, false);
        var ended = Stopwatch.GetTimestamp();
        sender.Stop();

        var elapsed = Stopwatch.GetElapsedTime(firstTicks, ended);
        var achieved = elapsed.TotalSeconds > 0 ? bytes / elapsed.TotalSeconds : 0;
        return new PacerBenchmarkResult(bytesPerSecond, achieved, packets, elapsed);
    }

    private sealed class BenchPacket(int payload)
    {
        public int Payload { get; } = payload;
    }
}
