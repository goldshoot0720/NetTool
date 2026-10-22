using System.Diagnostics;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 令牌桶：以設定速率累積「可以送出的位元組」，桶子有容量上限，允許一點突發。
///
/// 為什麼不用漏桶（每個封包預約一個送出時間）：那種做法只要 Sleep 多睡 1 毫秒，
/// 那段時間就永遠追不回來。1500 位元組的封包在 1 MB/s 上限下該隔 1.5 毫秒，
/// 睡成 2 毫秒，實際速度就只剩 750 KB/s——多睡的每一毫秒都變成永久的損失。
///
/// 令牌桶把「睡多了」記成存款：睡 2 毫秒就先存 2 毫秒的額度，下一批可以多送一點，
/// 只要桶子容量大於一次批次，長期平均就會貼著設定值，不會愈跑愈慢。
/// </summary>
public sealed class TokenBucket
{
    /// <summary>桶子容量＝20 毫秒的流量。要能吸收 Sleep 的 1 毫秒誤差，又不能讓封包成群爆衝。</summary>
    private const double BurstSeconds = 0.020;

    /// <summary>一個批次約 5 毫秒的量。批次要明顯小於容量，多睡的額度才存得進去。</summary>
    private const double BatchSeconds = 0.005;

    private const int Mtu = 1500;

    private readonly double _bytesPerSecond;
    private readonly double _capacity;
    private double _tokens;
    private long _lastTicks;

    public TokenBucket(double bytesPerSecond, long now)
    {
        if (!(bytesPerSecond > 0))
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), "速率必須大於 0。");

        _bytesPerSecond = bytesPerSecond;
        _capacity = Math.Max(4 * Mtu, bytesPerSecond * BurstSeconds);
        _tokens = _capacity;
        _lastTicks = now;
    }

    public double BytesPerSecond => _bytesPerSecond;

    /// <summary>桶子最多存多少位元組的額度。</summary>
    public double Capacity => _capacity;

    /// <summary>一次送出的批次上限：約 5 毫秒的量，至少一個 MTU。</summary>
    public int BatchBytes => Math.Max(Mtu, (int)(_capacity * (BatchSeconds / BurstSeconds)));

    /// <summary>
    /// 取用額度，回傳需要等待多久。額度不足時會記成欠債，
    /// 所以只要照著回傳的時間等，長期平均就是設定速率。
    /// </summary>
    public TimeSpan Take(int bytes, long now)
    {
        if (bytes <= 0)
            return TimeSpan.Zero;

        Refill(now);
        _tokens -= bytes;

        if (_tokens >= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(-_tokens / _bytesPerSecond);
    }

    private void Refill(long now)
    {
        var elapsed = now - _lastTicks;
        if (elapsed <= 0)
            return;

        _lastTicks = now;
        _tokens += elapsed * (_bytesPerSecond / Stopwatch.Frequency);
        if (_tokens > _capacity)
            _tokens = _capacity;
    }
}
