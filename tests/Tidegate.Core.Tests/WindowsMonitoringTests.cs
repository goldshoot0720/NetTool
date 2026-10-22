using Tidegate.Core;

namespace Tidegate.Core.Tests;

public class WindowsMonitoringTests
{
    [Fact]
    public void Process_totals_survive_a_closed_connection()
    {
        var accumulator = new ConnectionAccumulator();
        var names = new Dictionary<int, string> { [7] = "chrome" };

        var first = accumulator.Update([new ConnectionCounters("a", 7, 1_000, 100)], names);
        Assert.Equal(1_000, Assert.Single(first).BytesIn);

        var second = accumulator.Update([new ConnectionCounters("a", 7, 1_500, 250)], names);
        Assert.Equal(1_500, Assert.Single(second).BytesIn);

        var third = accumulator.Update([new ConnectionCounters("b", 7, 400, 40)], names);
        var row = Assert.Single(third);
        Assert.Equal(1_900, row.BytesIn);
        Assert.Equal(290, row.BytesOut);
        Assert.Equal("chrome", row.ProcessName);
    }

    [Fact]
    public void A_reset_counter_never_moves_the_total_backwards()
    {
        var accumulator = new ConnectionAccumulator();
        var names = new Dictionary<int, string>();

        accumulator.Update([new ConnectionCounters("a", 3, 800, 50)], names);
        var row = Assert.Single(accumulator.Update([new ConnectionCounters("a", 3, 200, 10)], names));

        Assert.Equal(800, row.BytesIn);
        Assert.Equal(50, row.BytesOut);
    }

    [Fact]
    public void Connections_seen_for_the_first_time_keep_the_bytes_they_already_moved()
    {
        var accumulator = new ConnectionAccumulator();

        var row = Assert.Single(accumulator.Update(
            [new ConnectionCounters("fresh", 9, 12_345, 678)],
            new Dictionary<int, string>()));

        Assert.Equal(12_345, row.BytesIn);
        Assert.Equal(678, row.BytesOut);
    }

    [Fact]
    public void Windows_executables_lose_the_directory_and_the_extension()
    {
        Assert.Equal(
            "chrome",
            AppName.FromCommand(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome"));
        Assert.Equal(
            "Code",
            AppName.FromCommand(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", "Code"));
        Assert.Equal(
            "python3.11",
            AppName.FromCommand(@"C:\Python311\python3.11", "python"));
    }
}
