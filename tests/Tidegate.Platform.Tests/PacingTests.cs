using System.Diagnostics;
using Tidegate.Core;
using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class TokenBucketTests
{
    [Fact]
    public void Sleeping_to_the_next_millisecond_does_not_slow_the_stream()
    {
        // 這是舊版漏桶掉速的根因：1500 位元組在 1 MB/s 下該隔 1.5 毫秒，
        // Sleep 只能睡整數毫秒，睡成 2 毫秒之後那段時間就永遠追不回來。
        const double rate = 1_000_000;
        var bucket = new TokenBucket(rate, 0);
        var ticksPerMillisecond = Stopwatch.Frequency / 1000.0;

        long now = 0;
        long bytes = 0;
        for (var i = 0; i < 20_000; i++)
        {
            var wait = bucket.Take(1500, now);
            now += (long)(Math.Ceiling(wait.TotalMilliseconds) * ticksPerMillisecond);
            bytes += 1500;
        }

        var seconds = now / (double)Stopwatch.Frequency;
        Assert.InRange(bytes / seconds, rate * 0.98, rate * 1.005);
    }

    [Fact]
    public void A_burst_is_limited_by_the_bucket_capacity()
    {
        var bucket = new TokenBucket(1_000_000, 0);
        var after_idle = Stopwatch.Frequency * 10;

        var wait = bucket.Take(1_000_000, after_idle);

        // 閒置十秒也不能存下十秒的額度，否則一次就能爆衝一整個檔案。
        Assert.True(
            wait >= TimeSpan.FromMilliseconds(900),
            $"閒置後的等待只有 {wait.TotalMilliseconds:0} 毫秒，額度存太多了。");
    }

    [Fact]
    public void A_batch_is_smaller_than_the_bucket_so_oversleep_can_be_paid_back()
    {
        var bucket = new TokenBucket(4_000_000, 0);

        Assert.True(bucket.BatchBytes >= 1500);
        Assert.True(bucket.BatchBytes <= bucket.Capacity / 2);
    }

    [Fact]
    public void A_zero_rate_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(0, Stopwatch.GetTimestamp()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(-5, Stopwatch.GetTimestamp()));
    }
}

public class PacedSenderTests
{
    [Fact]
    public void The_pacer_delivers_at_the_configured_rate()
    {
        const double rate = 1_000_000;
        var packet = new TestPacket(1500);

        long firstTicks = 0;
        long bytes = 0;
        var started = false;
        var counting = true;

        var sender = new PacedSender<TestPacket>(
            "test",
            new LimitRule("test", rate, null),
            p => p.Length,
            p =>
            {
                if (!counting)
                    return;

                var now = Stopwatch.GetTimestamp();
                if (!started)
                {
                    // 起步的突發不算，只看穩態。
                    started = true;
                    firstTicks = now;
                    return;
                }

                bytes += p.Length;
            });

        sender.Start();

        var deadline = Stopwatch.GetTimestamp() + (long)(1.5 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (!sender.TryEnqueue(packet, outbound: false))
                Thread.Sleep(1);
        }

        // 停止時會把佇列裡剩下的直接送掉，那不是節流的成果，不能算進速率。
        Volatile.Write(ref counting, false);
        var ended = Stopwatch.GetTimestamp();
        sender.Stop();

        var seconds = Stopwatch.GetElapsedTime(firstTicks, ended).TotalSeconds;
        Assert.True(seconds > 1, $"量測時間只有 {seconds:0.00} 秒，樣本不足。");
        Assert.InRange(bytes / seconds, rate * 0.95, rate * 1.03);
    }

    [Fact]
    public void An_unlimited_direction_passes_packets_straight_through()
    {
        var delivered = 0;
        var sender = new PacedSender<TestPacket>(
            "test",
            new LimitRule("test", 1_000_000, null),
            p => p.Length,
            _ => Interlocked.Increment(ref delivered));

        sender.Start();
        Assert.False(sender.Limits(outbound: true));
        Assert.True(sender.Limits(outbound: false));

        for (var i = 0; i < 10; i++)
            Assert.True(sender.TryEnqueue(new TestPacket(1500), outbound: true));

        sender.Stop();

        Assert.Equal(10, Volatile.Read(ref delivered));
    }

    private sealed class TestPacket(int length)
    {
        public int Length { get; } = length;
    }
}
