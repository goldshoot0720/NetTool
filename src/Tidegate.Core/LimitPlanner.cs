using System.Net;
using System.Net.Sockets;

namespace Tidegate.Core;

public static class LimitPlanner
{
    public const int MaxApps = 199;
    public const int MaxRules = 2000;
    public const int DownloadPipeBase = 1000;
    public const int UploadPipeBase = 2000;

    public static int DownloadPipe(int index) => DownloadPipeBase + index;

    public static int UploadPipe(int index) => UploadPipeBase + index;

    public static LimitPlan Build(IReadOnlyList<LimitedApp> apps, IReadOnlyList<FlowSocket> sockets)
    {
        var pipes = new List<PipeSpec>();
        var rules = new List<string>();
        var notes = new List<string>();
        var overflowNoted = false;
        var skippedNoted = false;

        foreach (var app in apps.OrderBy(item => item.Index))
        {
            if (app.DownloadBytesPerSecond is null && app.UploadBytesPerSecond is null)
                continue;
            if (app.Index < 1 || app.Index > MaxApps)
            {
                if (!overflowNoted)
                {
                    notes.Add("上限的應用程式超過 199 個，其餘這次不套用。");
                    overflowNoted = true;
                }
                continue;
            }

            var owned = sockets.Where(socket => app.ProcessIds.Contains(socket.ProcessId)).ToArray();
            var matched = 0;
            var downloadPipe = DownloadPipe(app.Index);
            var uploadPipe = UploadPipe(app.Index);
            var wantDownload = app.DownloadBytesPerSecond is double;
            var wantUpload = app.UploadBytesPerSecond is double;
            var ruleCapHit = false;

            foreach (var socket in owned)
            {
                if (!TryRule(socket, out var family, out var local, out var remote))
                {
                    skippedNoted = true;
                    continue;
                }
                if (rules.Count + (wantDownload ? 1 : 0) + (wantUpload ? 1 : 0) > MaxRules)
                {
                    ruleCapHit = true;
                    break;
                }

                if (wantDownload)
                    rules.Add(Rule("in", family, socket.Protocol, remote, local, downloadPipe));
                if (wantUpload)
                    rules.Add(Rule("out", family, socket.Protocol, local, remote, uploadPipe));
                matched++;
            }

            if (ruleCapHit)
            {
                notes.Add("規則超過 2000 條，其餘連線這次不套用。");
                if (matched > 0)
                {
                    if (wantDownload && app.DownloadBytesPerSecond is double partialDownload)
                        pipes.Add(new PipeSpec(downloadPipe, Bits(partialDownload)));
                    if (wantUpload && app.UploadBytesPerSecond is double partialUpload)
                        pipes.Add(new PipeSpec(uploadPipe, Bits(partialUpload)));
                    notes.Add(Describe(app, matched));
                }
                break;
            }

            if (matched == 0)
            {
                notes.Add($"{app.Name} 目前沒有可塑形的外部連線。");
                continue;
            }

            if (wantDownload && app.DownloadBytesPerSecond is double download)
                pipes.Add(new PipeSpec(downloadPipe, Bits(download)));
            if (wantUpload && app.UploadBytesPerSecond is double upload)
                pipes.Add(new PipeSpec(uploadPipe, Bits(upload)));

            notes.Add(Describe(app, matched));
        }

        if (skippedNoted)
            notes.Add("已略過迴圈、多播、萬用位址，或沒有對端的插座。");

        return new LimitPlan(pipes, rules, notes);
    }

    public static long Bits(double bytesPerSecond) =>
        Math.Max(1, (long)Math.Round(bytesPerSecond * 8d, MidpointRounding.AwayFromZero));

    private static string Describe(LimitedApp app, int flows)
    {
        var download = app.DownloadBytesPerSecond is double down ? RateText.Format(down) : "不限制";
        var upload = app.UploadBytesPerSecond is double up ? RateText.Format(up) : "不限制";
        return $"{app.Name}：下載 {download}，上傳 {upload}，{flows} 條連線。";
    }

    private static string Rule(string direction, string family, string protocol, HostPort from, HostPort to, int pipe) =>
        $"dummynet {direction} quick {family} proto {protocol} from {from} to {to} allow-opts pipe {pipe}";

    private static bool TryRule(FlowSocket socket, out string family, out HostPort local, out HostPort remote)
    {
        family = "";
        local = default;
        remote = default;
        if (socket.Protocol is not ("tcp" or "udp"))
            return false;
        if (socket.LocalPort is < 1 or > 65535 || socket.RemotePort is < 1 or > 65535)
            return false;
        if (socket.RemoteAddress is null || socket.RemotePort is null)
            return false;
        if (!TryUsable(socket.LocalAddress, out var localIp) || !TryUsable(socket.RemoteAddress, out var remoteIp))
            return false;
        if (localIp.AddressFamily != remoteIp.AddressFamily)
            return false;

        family = localIp.AddressFamily == AddressFamily.InterNetwork ? "inet" : "inet6";
        local = new HostPort(localIp.ToString(), socket.LocalPort);
        remote = new HostPort(remoteIp.ToString(), socket.RemotePort.Value);
        return true;
    }

    private static bool TryUsable(string address, out IPAddress ip)
    {
        ip = IPAddress.None;
        if (string.IsNullOrWhiteSpace(address) || address.Contains('%') || address == "*")
            return false;
        if (!IPAddress.TryParse(address, out ip!))
            return false;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return false;
        if (IsMulticast(ip))
            return false;

        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily != AddressFamily.InterNetwork || bytes[0] != 255;
    }

    private static bool IsMulticast(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] is >= 224 and <= 239;
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes[0] == 0xFF;
    }

    private readonly record struct HostPort(string Address, int Port)
    {
        public override string ToString() => $"{Address} port {Port}";
    }
}
