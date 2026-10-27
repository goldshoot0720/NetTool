using Tidegate.Core;

namespace Tidegate.Core.Tests;

public class SortTests
{
    private static readonly AppChannel[] Channels =
    [
        new("Safari", [3], 30, 30),
        new("Firefox", [1], 10, 0),
        new("chrome", [2], 0, 50),
    ];

    [Fact]
    public void Name_sort_is_alphabetical_in_both_directions()
    {
        var ascending = ChannelSorter.Sort(Channels, SortSelection.NameAscending).Select(channel => channel.Name);
        var descending = ChannelSorter.Sort(Channels, SortSelection.NameAscending.ToggleName()).Select(channel => channel.Name);

        Assert.Equal(["chrome", "Firefox", "Safari"], ascending);
        Assert.Equal(["Safari", "Firefox", "chrome"], descending);
    }

    [Fact]
    public void Speed_sort_can_use_total_download_or_upload()
    {
        Assert.Equal(
            ["Safari", "chrome", "Firefox"],
            Names(SpeedBasis.Total, SortDirection.Descending));
        Assert.Equal(
            ["Safari", "Firefox", "chrome"],
            Names(SpeedBasis.Download, SortDirection.Descending));
        Assert.Equal(
            ["chrome", "Safari", "Firefox"],
            Names(SpeedBasis.Upload, SortDirection.Descending));
        Assert.Equal(
            ["Firefox", "chrome", "Safari"],
            Names(SpeedBasis.Total, SortDirection.Ascending));
    }

    [Fact]
    public void Equal_speeds_fall_back_to_the_app_name()
    {
        var tied = new[]
        {
            new AppChannel("B", [2], 10, 0),
            new AppChannel("A", [1], 5, 5),
            new AppChannel("C", [3], 1, 0),
        };

        var names = ChannelSorter.Sort(
            tied,
            new SortSelection(SortKey.Speed, SortDirection.Descending, SpeedBasis.Total))
            .Select(channel => channel.Name);

        Assert.Equal(["A", "B", "C"], names);
    }

    [Fact]
    public void Clicking_the_same_sort_flips_direction_and_a_new_key_picks_a_default()
    {
        var speed = SortSelection.SpeedDescending;
        Assert.Equal(SortDirection.Ascending, speed.ToggleSpeed(SpeedBasis.Total).Direction);
        Assert.Equal(SortKey.Name, speed.ToggleName().Key);
        Assert.Equal(SortDirection.Ascending, speed.ToggleName().Direction);
        Assert.Equal(SortDirection.Descending, SortSelection.NameAscending.ToggleSpeed(SpeedBasis.Download).Direction);
        Assert.Equal(SpeedBasis.Download, SortSelection.NameAscending.ToggleSpeed(SpeedBasis.Download).Basis);
    }

    [Fact]
    public void Idle_rows_stay_when_they_have_a_limit_or_the_filter_is_off()
    {
        var quiet = new AppChannel("quiet", [9], 10, 0);
        var busy = new AppChannel("busy", [8], 5_000, 0);
        var channels = new[] { quiet, busy };

        var hidden = ChannelQuery.Filter(channels, search: null, hideIdle: true, hasLimit: _ => false);
        var kept = ChannelQuery.Filter(channels, search: "QUI", hideIdle: true, hasLimit: name => name == "quiet");

        Assert.Equal(["busy"], hidden.Select(channel => channel.Name));
        Assert.Equal(["quiet"], kept.Select(channel => channel.Name));
    }

    [Fact]
    public void Board_groups_bundles_and_resorts_without_a_new_sample()
    {
        var board = new TrafficBoard(new LimitCatalog());
        var t0 = DateTimeOffset.Parse("2026-10-19T00:00:00Z");
        var processes = new Dictionary<int, ProcessEntry>
        {
            [1] = new(0, "/Applications/Firefox.app/Contents/MacOS/firefox"),
            [2] = new(0, "/usr/bin/curl"),
        };

        board.Ingest(
            [new ProcessTraffic("firefox", 1, 0, 0), new ProcessTraffic("curl", 2, 0, 0)],
            processes,
            t0);
        var state = board.Ingest(
            [new ProcessTraffic("firefox", 1, 8_000, 2_000), new ProcessTraffic("curl", 2, 100, 0)],
            processes,
            t0.AddSeconds(1));

        Assert.Equal(["Firefox"], state.Rows.Select(row => row.Name));
        Assert.Equal(8_100, state.TotalDownloadBytesPerSecond);
        Assert.Equal(8_000, state.Rows[0].DownloadBytesPerSecond);

        board.WithHideIdle(false);
        var byName = board.WithSort(SortSelection.NameAscending);
        Assert.Equal(["curl", "Firefox"], byName.Rows.Select(row => row.Name));

        var limited = board.SetLimit("curl", 50_000, null);
        board.WithHideIdle(true);
        var visible = board.WithSort(SortSelection.SpeedDescending);
        Assert.Contains(visible.Rows, row => row.Name == "curl" && row.DownloadCapBytesPerSecond == 50_000);
        Assert.Equal(50_000, limited.Rows.Single(row => row.Name == "curl").DownloadCapBytesPerSecond);
        Assert.Single(visible.Rows.Single(row => row.Name == "Firefox").History);
    }

    private static IEnumerable<string> Names(SpeedBasis basis, SortDirection direction) =>
        ChannelSorter.Sort(Channels, new SortSelection(SortKey.Speed, direction, basis))
            .Select(channel => channel.Name);
}
