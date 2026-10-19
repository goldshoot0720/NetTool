using Tidegate.Core;

namespace Tidegate.Core.Tests;

public class LimitTests
{
    [Fact]
    public void Lsof_fields_keep_both_ends_of_a_connection()
    {
        const string text = """
            p1258
            cfirefox
            PTCP
            n192.168.0.128:57429->103.151.151.133:443
            PUDP
            n*:5353
            p99
            PTCP
            n127.0.0.1:1234->127.0.0.1:4321
            """;

        var sockets = LsofFieldParser.Parse(text);

        Assert.Equal(3, sockets.Count);
        Assert.Equal("tcp", sockets[0].Protocol);
        Assert.Equal("103.151.151.133", sockets[0].RemoteAddress);
        Assert.Equal(443, sockets[0].RemotePort);
        Assert.Equal("*", sockets[1].LocalAddress);
        Assert.Null(sockets[1].RemoteAddress);
    }

    [Fact]
    public void Planner_shapes_download_and_upload_on_separate_pipes()
    {
        var apps = new[]
        {
            new LimitedApp("Firefox", 1, [1258], 1_000, 2_000),
        };
        var sockets = new[]
        {
            new FlowSocket(1258, "tcp", "192.168.0.128", 57429, "103.151.151.133", 443),
            new FlowSocket(1258, "tcp", "127.0.0.1", 9, "127.0.0.1", 9),
            new FlowSocket(1258, "udp", "*", 5353, null, null),
        };

        var plan = LimitPlanner.Build(apps, sockets);

        Assert.Equal(
            "dummynet in quick inet proto tcp from 103.151.151.133 port 443 to 192.168.0.128 port 57429 allow-opts pipe 1001",
            plan.Rules[0]);
        Assert.Equal(
            "dummynet out quick inet proto tcp from 192.168.0.128 port 57429 to 103.151.151.133 port 443 allow-opts pipe 2001",
            plan.Rules[1]);
        Assert.Equal(8_000, plan.Pipes.Single(pipe => pipe.Number == 1001).BitsPerSecond);
        Assert.Equal(16_000, plan.Pipes.Single(pipe => pipe.Number == 2001).BitsPerSecond);
        Assert.Contains(plan.Notes, note => note.Contains("已略過", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Rules, rule => rule.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Upload_only_limit_does_not_create_a_download_pipe()
    {
        var plan = LimitPlanner.Build(
            [new LimitedApp("curl", 1, [4], null, 500)],
            [new FlowSocket(4, "tcp", "10.0.0.2", 40000, "1.1.1.1", 443)]);

        Assert.Single(plan.Rules);
        Assert.StartsWith("dummynet out ", plan.Rules[0]);
        Assert.Equal(2001, Assert.Single(plan.Pipes).Number);
    }

    [Fact]
    public void Missing_connections_are_reported_and_not_turned_into_rules()
    {
        var plan = LimitPlanner.Build(
            [new LimitedApp("quiet", 1, [], 1_000, null)],
            []);

        Assert.Empty(plan.Rules);
        Assert.Empty(plan.Pipes);
        Assert.Contains(plan.Notes, note => note.Contains("沒有可塑形", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_script_only_contains_pipe_numbers_and_our_anchor()
    {
        var script = LimitScript.BuildApply(
            "/Users/feng33/Library/Application Support/Tidegate",
            "/Users/feng33/Library/Application Support/Tidegate/pf.token");

        Assert.Equal("1001 8", LimitScript.FormatPipeLine(new PipeSpec(1001, 8_000)));
        Assert.Equal("2001 16", LimitScript.FormatPipeLine(new PipeSpec(2001, 16_000)));
        Assert.Contains("Kbit/s", script, StringComparison.Ordinal);
        Assert.Contains("/sbin/pfctl -a com.apple/tidegate -f", script, StringComparison.Ordinal);
        Assert.Contains("/sbin/pfctl -k", script, StringComparison.Ordinal);
        Assert.Contains("'/Users/feng33/Library/Application Support/Tidegate'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/etc/pf.conf", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-F all", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bad$(name)", script, StringComparison.Ordinal);
        var clear = LimitScript.BuildClear("/tmp/tidegate.token");
        Assert.Contains("pfctl -X", clear, StringComparison.Ordinal);
        Assert.Contains("pfctl -a com.apple/tidegate -F all", clear, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_round_trips_and_drops_an_empty_limit()
    {
        var catalog = new LimitCatalog();
        catalog.Set("Firefox", 1_000, null);
        catalog.Set("curl", null, null);

        var loaded = LimitCatalog.FromJson(catalog.ToJson());

        Assert.Equal(1_000, loaded.Find("Firefox")?.DownloadBytesPerSecond);
        Assert.Null(loaded.Find("Firefox")?.UploadBytesPerSecond);
        Assert.Null(loaded.Find("curl"));
    }

    [Fact]
    public void Kilobyte_parser_rejects_blank_as_unlimited_and_zero_as_invalid()
    {
        Assert.True(RateText.TryParseKilobytes("  ", out var unlimited, out var noError));
        Assert.Null(unlimited);
        Assert.Null(noError);
        Assert.True(RateText.TryParseKilobytes("12.5", out var value, out _));
        Assert.Equal(12_500, value);
        Assert.False(RateText.TryParseKilobytes("0", out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
