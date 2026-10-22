using System.Diagnostics;
using Tidegate.Platform.Windows;

namespace Tidegate.Platform.Tests;

public class LeakyBucketTests
{
    [Fact]
    public void The_bucket_spaces_packets_by_the_configured_rate()
    {
        var now = Stopwatch.GetTimestamp();
        var bucket = new LeakyBucket(1_000, now);

        Assert.Equal(TimeSpan.Zero, bucket.Reserve(500, now));

        var second = bucket.Reserve(500, now);
        Assert.InRange(second.TotalSeconds, 0.45, 0.55);

        var third = bucket.Reserve(1_000, now);
        Assert.InRange(third.TotalSeconds, 0.95, 1.05);
    }

    [Fact]
    public void A_bucket_that_has_been_idle_does_not_accumulate_a_burst()
    {
        var start = Stopwatch.GetTimestamp();
        var bucket = new LeakyBucket(1_000, start);
        bucket.Reserve(1_000, start);

        var later = start + Stopwatch.Frequency * 10;
        Assert.Equal(TimeSpan.Zero, bucket.Reserve(100, later));
    }

    [Fact]
    public void A_zero_rate_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeakyBucket(0, Stopwatch.GetTimestamp()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeakyBucket(-5, Stopwatch.GetTimestamp()));
    }
}
