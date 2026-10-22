using System.Collections.Concurrent;
using System.Diagnostics;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 用 WinDivert 在網路層做逐應用程式的雙向節流。
///
/// 只在使用者按了「套用上限」之後才開 handle；handle 一關，所有封包恢復原本路徑。
/// 節流方式是「排隊＋令牌桶」而不是丟包，佇列滿的時候寧可丟棄也不放行——
/// 放行等於沒有限速，丟棄會讓 TCP 重傳並退讓，速度自然壓回上限。
/// </summary>
public sealed class WinDivertShaper : IDisposable
{
    private const string Filter = "tcp";
    // WinDivert 自己的佇列也要夠深，不然驅動先丟，我們連排隊的機會都沒有。
    private const ulong QueueLength = 8192;
    private const ulong QueueTimeMilliseconds = 1000;
    private const int PortRefreshIntervalMs = 250;
    private const int MaxConsecutiveReceiveFailures = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, PacedSender<QueuedPacket>> _pacers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, string> _ports = new();
    private readonly WindowsProcessCatalog _catalog = new();

    private WindivertHandle? _handle;
    private CancellationTokenSource? _cancellation;
    private Thread? _receiveThread;
    private Thread? _portThread;
    private bool _disposed;
    private long _receivedPackets;
    private long _matchedPackets;
    private long _queuedPackets;
    private long _passedPackets;
    private long _parseFailures;
    private long _directionMismatches;
    private long _droppedPackets;
    private long _sentPackets;
    private long _sentBytes;
    private readonly ConcurrentQueue<string> _samplePairs = new();

    public bool IsRunning { get; private set; }

    /// <summary>把每次埠表刷新寫進 windows.log，除錯用。</summary>
    public bool Verbose { get; set; }

    public IReadOnlyCollection<string> Apps => _pacers.Keys;

    /// <summary>診斷用的封包計數。</summary>
    public string DescribeStats() =>
        $"recv={Interlocked.Read(ref _receivedPackets)} "
        + $"match={Interlocked.Read(ref _matchedPackets)} "
        + $"queued={Interlocked.Read(ref _queuedPackets)} "
        + $"drop={Interlocked.Read(ref _droppedPackets)} "
        + $"pass={Interlocked.Read(ref _passedPackets)} "
        + $"sent={Interlocked.Read(ref _sentPackets)} "
        + $"sentBytes={Interlocked.Read(ref _sentBytes)} "
        + $"parseFail={Interlocked.Read(ref _parseFailures)} "
        + $"dirMismatch={Interlocked.Read(ref _directionMismatches)} "
        + $"ports={_ports.Count} "
        + $"pairs=[{string.Join(",", _samplePairs.Take(6))}]";

    public LimitApplyResult Start(IReadOnlyList<LimitRule> limits)
    {
        lock (_gate)
        {
            StopLocked();
            if (!limits.Any(limit => limit.DownloadBytesPerSecond is not null || limit.UploadBytesPerSecond is not null))
                return new LimitApplyResult(false, "沒有可套用的上限。");

            WindivertHandle handle;
            try
            {
                handle = WindivertHandle.Open(Filter, WindivertLayer.Network);
            }
            catch (WindivertException exception)
            {
                return new LimitApplyResult(false, Describe(exception));
            }
            catch (PlatformNotSupportedException exception)
            {
                return new LimitApplyResult(false, exception.Message);
            }

            handle.TrySetParam(WindivertParam.QueueLength, QueueLength);
            handle.TrySetParam(WindivertParam.QueueTime, QueueTimeMilliseconds);

            _handle = handle;
            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            Win32.BeginHighResolutionTimers();

            BuildPacers(limits);
            var token = _cancellation.Token;

            _receiveThread = new Thread(() => ReceiveLoop(token))
            {
                IsBackground = true,
                Name = "tidegate-shaper-receive",
            };
            _receiveThread.Start();

            _portThread = new Thread(() => PortLoop(token))
            {
                IsBackground = true,
                Name = "tidegate-shaper-ports",
            };
            _portThread.Start();

            var names = string.Join("、", _pacers.Keys.OrderBy(name => name, StringComparer.Ordinal));
            return new LimitApplyResult(true, $"已開始封包節流：{names}。關閉程式或按「清除系統規則」就會停止。");
        }
    }

    public void Update(IReadOnlyList<LimitRule> limits)
    {
        lock (_gate)
        {
            if (!IsRunning)
                return;

            var next = limits
                .Where(limit => limit.DownloadBytesPerSecond is not null || limit.UploadBytesPerSecond is not null)
                .ToArray();

            foreach (var name in _pacers.Keys.ToArray())
            {
                var match = next.FirstOrDefault(limit => string.Equals(limit.AppName, name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    var removed = _pacers[name];
                    _pacers.Remove(name);
                    removed.Dispose();
                    continue;
                }

                _pacers[name].Retarget(match);
            }

            foreach (var limit in next)
            {
                if (_pacers.ContainsKey(limit.AppName))
                    continue;
                var pacer = CreatePacer(limit);
                _pacers[limit.AppName] = pacer;
                pacer.Start();
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private void StopLocked()
    {
        if (!IsRunning && _handle is null)
            return;

        IsRunning = false;
        _cancellation?.Cancel();

        // 先把佇列裡的封包送完，handle 還開著才送得出去。
        var pacers = _pacers.Values.ToArray();
        foreach (var pacer in pacers)
            pacer.Stop();
        foreach (var pacer in pacers)
            pacer.Dispose();

        _pacers.Clear();
        _ports.Clear();

        var handle = _handle;
        _handle = null;
        handle?.Close();

        _receiveThread?.Join(500);
        _receiveThread = null;

        // 收封包的執行緒結束後，handle 才能安全釋放緩衝區。
        handle?.Dispose();

        _portThread?.Join(500);
        _portThread = null;

        _cancellation?.Dispose();
        _cancellation = null;
        Win32.EndHighResolutionTimers();
    }

    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[WindivertHandle.MtuMax];
        var failures = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var handle = _handle;
                if (handle is null || !handle.IsOpen)
                    break;

                int length;
                WindivertAddress address;
                try
                {
                    length = handle.Receive(buffer, out address);
                }
                catch (WindivertException exception)
                {
                    WindowsLog.Write("shaper receive failed: " + exception.Message);
                    if (++failures > MaxConsecutiveReceiveFailures)
                        break;
                    continue;
                }

                if (length <= 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    if (++failures > MaxConsecutiveReceiveFailures)
                        break;
                    continue;
                }

                failures = 0;
                Interlocked.Increment(ref _receivedPackets);
                var data = new byte[length];
                Array.Copy(buffer, data, length);
                Dispatch(data, address);
            }
        }
        catch (Exception exception)
        {
            WindowsLog.Write("shaper loop failed: " + exception.Message);
        }
    }

    private void Dispatch(byte[] data, WindivertAddress address)
    {
        var outbound = address.Outbound;
        if (!WindivertHelper.TryGetTcpPorts(data, out var source, out var destination)
            || !WindivertHelper.TryGetTcpPayloadLength(data, out var payload))
        {
            Interlocked.Increment(ref _parseFailures);
            Interlocked.Increment(ref _passedPackets);
            Send(data, address);
            return;
        }

        if (_ports.Count > 0 && _samplePairs.Count < 10)
            _samplePairs.Enqueue($"{source}>{destination}{(outbound ? "o" : "i")}");

        var preferred = outbound ? (int)source : destination;
        var fallback = outbound ? destination : (int)source;

        var pacer = FindPacer(preferred, outbound);
        if (pacer is null)
            pacer = FindPacer(fallback, outbound, countMismatch: true);

        if (pacer is null)
        {
            // 沒有限速或對不到應用程式：直接放行。
            Interlocked.Increment(ref _passedPackets);
            Send(data, address);
            return;
        }

        Interlocked.Increment(ref _matchedPackets);

        // 純 ACK 沒有酬載，不佔額度也不該排隊：塞進佇列只會擠掉資料封包，
        // 讓整條連線一起卡住。
        if (payload <= 0)
        {
            Interlocked.Increment(ref _passedPackets);
            Send(data, address);
            return;
        }

        if (pacer.TryEnqueue(new QueuedPacket(data, address, payload), outbound))
        {
            Interlocked.Increment(ref _queuedPackets);
            return;
        }

        // 佇列滿了：丟棄，不要放行。
        // 放行等於完全沒有限速；丟棄會讓 TCP 重傳並退讓，速度自然壓回上限。
        Interlocked.Increment(ref _droppedPackets);
    }

    private PacedSender<QueuedPacket>? FindPacer(int port, bool outbound, bool countMismatch = false)
    {
        if (!_ports.TryGetValue(port, out var app))
            return null;
        if (!_pacers.TryGetValue(app, out var pacer))
            return null;
        if (!pacer.Limits(outbound))
            return null;

        if (countMismatch)
            Interlocked.Increment(ref _directionMismatches);

        return pacer;
    }

    private void Send(byte[] data, WindivertAddress address)
    {
        var handle = _handle;
        if (handle is null || !handle.IsOpen)
            return;

        try
        {
            handle.TrySend(data, address);
        }
        catch (WindivertException exception)
        {
            WindowsLog.Write("shaper send failed: " + exception.Message);
        }
    }

    /// <summary>節流器把封包送出時呼叫：計數後才真的注入。</summary>
    private void Deliver(QueuedPacket packet)
    {
        Interlocked.Increment(ref _sentPackets);
        Interlocked.Add(ref _sentBytes, packet.Payload);
        Send(packet.Data, packet.Address);
    }

    private void PortLoop(CancellationToken cancellationToken)
    {
        var ticks = 0;
        var lastTicks = Stopwatch.GetTimestamp();
        var lastBytes = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RefreshPorts(cancellationToken);
            }
            catch (Exception exception)
            {
                WindowsLog.Write("shaper port refresh failed: " + exception.Message);
            }

            // 每 10 秒留一行統計，事後要追「為什麼沒限到」才有依據。
            if (++ticks % (10_000 / PortRefreshIntervalMs) == 0)
            {
                var now = Stopwatch.GetTimestamp();
                var bytes = Interlocked.Read(ref _sentBytes);
                var seconds = Stopwatch.GetElapsedTime(lastTicks, now).TotalSeconds;
                var rate = seconds > 0 ? (bytes - lastBytes) / seconds / 1000 : 0;
                lastTicks = now;
                lastBytes = bytes;
                WindowsLog.Write($"shaper {DescribeStats()} sentRate={rate:0}KB/s");
            }

            if (cancellationToken.WaitHandle.WaitOne(PortRefreshIntervalMs))
                break;
        }
    }

    private void RefreshPorts(CancellationToken cancellationToken)
    {
        if (_pacers.Count == 0)
            return;

        var started = Stopwatch.GetTimestamp();
        var paths = _catalog.CommandPathsAsync(cancellationToken).GetAwaiter().GetResult();
        var connections = WindowsTcpTable.Read();
        var map = new Dictionary<int, string>();

        foreach (var connection in connections)
        {
            if (connection.ProcessId <= 0 || connection.LocalPort <= 0)
                continue;
            if (!paths.TryGetValue(connection.ProcessId, out var path))
                continue;

            var app = AppName.FromCommand(path, "");
            if (!_pacers.ContainsKey(app))
                continue;

            map[connection.LocalPort] = app;
        }

        _ports.Clear();

        // 樣本只留最近一次刷新之後看到的封包，不然舊的連線會一直留在統計裡誤導人。
        _samplePairs.Clear();
        foreach (var entry in map)
            _ports[entry.Key] = entry.Value;

        if (Verbose)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var sample = string.Join(",", map.Take(4).Select(entry => $"{entry.Key}:{entry.Value}"));
            WindowsLog.Write($"ports conns={connections.Count} paths={paths.Count} mapped={map.Count} took={elapsed:0}ms [{sample}]");
        }
    }

    private void BuildPacers(IReadOnlyList<LimitRule> limits)
    {
        foreach (var limit in limits)
        {
            if (limit.DownloadBytesPerSecond is null && limit.UploadBytesPerSecond is null)
                continue;
            var pacer = CreatePacer(limit);
            _pacers[limit.AppName] = pacer;
            pacer.Start();
        }
    }

    private PacedSender<QueuedPacket> CreatePacer(LimitRule limit) =>
        new(limit.AppName, limit, packet => packet.Payload, Deliver);

    private static string Describe(WindivertException exception)
    {
        if (exception.ErrorCode == WindivertError.DriverMissing)
        {
            return exception.Message
                + $" 程式目錄：{AppContext.BaseDirectory}。"
                + "若檔案已經放入，請確認 Windows Defender 沒有把它隔離（把該資料夾加入排除清單）。";
        }

        return exception.Message;
    }

    private sealed class QueuedPacket(byte[] data, WindivertAddress address, int payload)
    {
        public byte[] Data { get; } = data;

        public WindivertAddress Address { get; } = address;

        /// <summary>TCP 酬載長度，令牌桶就是按這個數字扣額度。</summary>
        public int Payload { get; } = payload;
    }
}
