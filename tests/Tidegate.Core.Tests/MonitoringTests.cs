using Tidegate.Core;

namespace Tidegate.Core.Tests;

public class MonitoringTests
{
    [Fact]
    public void Terminal_cleaner_drops_control_characters_without_eating_the_header()
    {
        var cleaned = TerminalText.Clean("\u0004\b\b,bytes_in,bytes_out,");
        Assert.Equal(",bytes_in,bytes_out,", cleaned);
        Assert.True(NettopCsvParser.TryParseLine(TerminalText.Clean("firefox.1258,10,2,"), out var row));
        Assert.Equal("firefox", row.ProcessName);
    }

    [Fact]
    public void Parser_splits_dotted_app_names_and_keeps_the_pid()
    {
        const string sample = """
            ,bytes_in,bytes_out,
            firefox.1258,63644119,3467321,
            grok-1.0.62-mac.70934,272975,258170,
            not-a-row
            ,bytes_in,bytes_out,
            """;

        var rows = NettopCsvParser.Parse(sample);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.ProcessName == "firefox" && row.ProcessId == 1258 && row.BytesIn == 63644119);
        Assert.Contains(rows, row => row.ProcessName == "grok-1.0.62-mac" && row.ProcessId == 70934);
    }

    [Fact]
    public void Rates_use_the_delta_and_skip_new_or_reset_counters()
    {
        var previous = new[]
        {
            new ProcessTraffic("firefox", 1, 1000, 50),
            new ProcessTraffic("old", 2, 500, 500),
        };
        var current = new[]
        {
            new ProcessTraffic("firefox", 1, 2500, 90),
            new ProcessTraffic("old", 2, 100, 500),
            new ProcessTraffic("chrome", 3, 9000, 9000),
        };

        var rates = RateCalculator.FromCounters(previous, current, 2);

        var firefox = Assert.Single(rates, rate => rate.ProcessId == 1);
        Assert.Equal(750, firefox.DownloadBytesPerSecond);
        Assert.Equal(20, firefox.UploadBytesPerSecond);
        var reset = Assert.Single(rates, rate => rate.ProcessId == 2);
        Assert.Equal(0, reset.DownloadBytesPerSecond);
        Assert.DoesNotContain(rates, rate => rate.ProcessId == 3);
        Assert.Empty(RateCalculator.FromCounters(previous, current, 0));
    }

    [Fact]
    public void App_names_come_from_the_bundle_and_same_apps_are_summed()
    {
        Assert.Equal("Firefox", AppName.FromCommand("/Applications/Firefox.app/Contents/MacOS/firefox", "firefox"));
        Assert.Equal("My App", AppName.FromCommand("/Applications/My App.app/Contents/MacOS/bin", "bin"));
        Assert.Equal("helper", AppName.FromCommand("/usr/libexec/helper", "helper"));

        var rates = new[]
        {
            new ProcessRate(1, "firefox", "Firefox", 100, 10),
            new ProcessRate(2, "plugin-container", "Firefox", 40, 5),
            new ProcessRate(3, "curl", "curl", 1, 1),
        };

        var channels = ChannelAggregator.Aggregate(rates);
        var firefox = Assert.Single(channels, channel => channel.Name == "Firefox");
        Assert.Equal(140, firefox.DownloadBytesPerSecond);
        Assert.Equal(15, firefox.UploadBytesPerSecond);
        Assert.Equal([1, 2], firefox.ProcessIds);
    }

    [Fact]
    public void Process_table_keeps_paths_that_contain_spaces()
    {
        const string text = """
              1258 /Applications/Firefox.app/Contents/MacOS/firefox
              88 /Applications/My App.app/Contents/MacOS/bin
            """;

        var table = ProcessTableParser.Parse(text);

        Assert.Equal("/Applications/Firefox.app/Contents/MacOS/firefox", table[1258]);
        Assert.Equal("/Applications/My App.app/Contents/MacOS/bin", table[88]);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(100, "100 B/s")]
    [InlineData(1500, "1.5 KB/s")]
    [InlineData(2_500_000, "2.5 MB/s")]
    public void Rate_text_uses_decimal_units(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, RateText.Format(bytesPerSecond));
    }
}
