using System.Collections.Concurrent;
using System.Diagnostics;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 用 WinDivert 在網路層做逐應用程式的雙向節流。
///
/// 只在使用者按了「套用上限」之後才開 handle；handle 一關，所有封包恢復原本路徑。
/// 節流方式是「排隊＋漏桶」而不是丟包，佇列滿的時候寧可直接放行（fail open），
/// 避免因為節流器跟不上而讓整個連線停住。
/// </summary>
public sealed class WinDivertShaper : IDisposable
{
    private const string Filter = "tcp";
    private const int QueueCapacity = 512;
    private const ulong QueueLength = 8192;
    private const ulong QueueTimeMilliseconds = 1000;
    private const int PortRefreshIntervalMs = 1000;
    private const int MaxConsecutiveReceiveFailures = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, AppPacer> _pacers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, string> _ports = new();
    private readonly WindowsProcessCatalog _catalog = new();

    private WindivertHandle? _handle;
    private CancellationTokenSource? _cancellation;
    private Thread? _receiveThread;
    private Thread? _portThread;
    private bool _disposed;

    public bool IsRunning { get; private set; }

    public IReadOnlyCollection<string> Apps => _pacers.Keys;

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
                var pacer = new AppPacer(limit, () => _handle, Send);
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
            pacer.Queue.CompleteAdding();
        foreach (var pacer in pacers)
            pacer.WaitForExit(500);
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
        if (WindivertHelper.TryGetLocalPort(data, outbound, out var port)
            && _ports.TryGetValue(port, out var app)
            && _pacers.TryGetValue(app, out var pacer)
            && (outbound ? pacer.Upload is not null : pacer.Download is not null)
            && pacer.Queue.TryAdd(new QueuedPacket(data, address, outbound)))
        {
            return;
        }

        // 沒有限速、對不到應用程式，或佇列滿了：直接放行。
        Send(data, address);
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

    private void PortLoop(CancellationToken cancellationToken)
    {
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

            if (cancellationToken.WaitHandle.WaitOne(PortRefreshIntervalMs))
                break;
        }
    }

    private void RefreshPorts(CancellationToken cancellationToken)
    {
        if (_pacers.Count == 0)
            return;

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
        foreach (var entry in map)
            _ports[entry.Key] = entry.Value;
    }

    private void BuildPacers(IReadOnlyList<LimitRule> limits)
    {
        foreach (var limit in limits)
        {
            if (limit.DownloadBytesPerSecond is null && limit.UploadBytesPerSecond is null)
                continue;
            var pacer = new AppPacer(limit, () => _handle, Send);
            _pacers[limit.AppName] = pacer;
            pacer.Start();
        }
    }

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

    private readonly record struct QueuedPacket(byte[] Data, WindivertAddress Address, bool Outbound);

    private sealed class AppPacer : IDisposable
    {
        private readonly Func<WindivertHandle?> _handle;
        private readonly Action<byte[], WindivertAddress> _send;
        private readonly CancellationTokenSource _cancellation = new();
        private Thread? _worker;
        private double? _downloadRate;
        private double? _uploadRate;
        private LeakyBucket? _download;
        private LeakyBucket? _upload;

        public AppPacer(LimitRule limit, Func<WindivertHandle?> handle, Action<byte[], WindivertAddress> send)
        {
            Name = limit.AppName;
            _handle = handle;
            _send = send;
            Queue = new BlockingCollection<QueuedPacket>(QueueCapacity);
            Retarget(limit);
        }

        public string Name { get; }

        public BlockingCollection<QueuedPacket> Queue { get; }

        public LeakyBucket? Download => _download;

        public LeakyBucket? Upload => _upload;

        public void Retarget(LimitRule limit)
        {
            if (limit.DownloadBytesPerSecond is double download && download != _downloadRate)
            {
                _downloadRate = download;
                _download = new LeakyBucket(download, Stopwatch.GetTimestamp());
            }
            else if (limit.DownloadBytesPerSecond is null)
            {
                _downloadRate = null;
                _download = null;
            }

            if (limit.UploadBytesPerSecond is double upload && upload != _uploadRate)
            {
                _uploadRate = upload;
                _upload = new LeakyBucket(upload, Stopwatch.GetTimestamp());
            }
            else if (limit.UploadBytesPerSecond is null)
            {
                _uploadRate = null;
                _upload = null;
            }
        }

        public void Start()
        {
            _worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "tidegate-shaper-" + Name,
            };
            _worker.Start();
        }

        public void WaitForExit(int milliseconds) => _worker?.Join(milliseconds);

        public void Dispose()
        {
            _cancellation.Cancel();
            Queue.CompleteAdding();
            _worker?.Join(300);
            _cancellation.Dispose();
        }

        private void Run()
        {
            var token = _cancellation.Token;
            while (!token.IsCancellationRequested)
            {
                QueuedPacket packet;
                try
                {
                    if (!Queue.TryTake(out packet, 100, token))
                        continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                Deliver(packet, paced: true);
            }

            // 停止時把剩下的封包送掉，不要讓連線卡住。
            while (Queue.TryTake(out var remaining))
                Deliver(remaining, paced: false);
        }

        private void Deliver(QueuedPacket packet, bool paced)
        {
            if (_handle() is null)
                return;

            if (paced)
            {
                var bucket = packet.Outbound ? _upload : _download;
                if (bucket is not null)
                {
                    var wait = bucket.Reserve(packet.Data.Length, Stopwatch.GetTimestamp());
                    if (wait > TimeSpan.Zero && wait < TimeSpan.FromSeconds(5))
                        Thread.Sleep(wait);
                }
            }

            _send(packet.Data, packet.Address);
        }
    }
}
