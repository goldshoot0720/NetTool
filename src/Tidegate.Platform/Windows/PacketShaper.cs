using System.Diagnostics;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 平滑的漏桶：每個封包先預約「可以送出的時間」，早到就等一下。
/// 比起超額就丟包，這種做法不會製造重傳，速度曲線也比較穩。
/// </summary>
public sealed class LeakyBucket
{
    private readonly double _bytesPerSecond;
    private long _nextFreeTicks;

    public LeakyBucket(double bytesPerSecond, long now)
    {
        if (bytesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), "速率必須大於 0。");
        _bytesPerSecond = bytesPerSecond;
        _nextFreeTicks = now;
    }

    public double BytesPerSecond => _bytesPerSecond;

    /// <summary>預約一個封包，回傳需要等待的時間。</summary>
    public TimeSpan Reserve(int bytes, long now)
    {
        if (bytes <= 0)
            return TimeSpan.Zero;

        var start = Math.Max(now, _nextFreeTicks);
        var cost = (long)(bytes / _bytesPerSecond * Stopwatch.Frequency);
        _nextFreeTicks = start + cost;

        var wait = start - now;
        return wait <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency);
    }
}
