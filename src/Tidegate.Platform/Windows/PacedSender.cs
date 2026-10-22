using System.Collections.Concurrent;
using System.Diagnostics;
using Tidegate.Core;

namespace Tidegate.Platform.Windows;

/// <summary>
/// 單一應用程式的節流器：兩個方向各一條佇列、各一個令牌桶，由一個執行緒輪流送出。
///
/// 關鍵在「批次」。逐封包 Sleep 的話，1 毫秒的排程誤差就吃掉一成以上的速度；
/// 這裡一次取出約 5 毫秒份量的封包，等到額度補齊再整批送出，
/// 誤差被令牌桶的突發容量吸收，長期平均才會貼著設定上限。
///
/// 佇列長度依速率換算成約 250 毫秒的量：太深會讓延遲爆增、而且在滿出來的時候
/// 一次丟一大串封包，TCP 退讓得比誰都兇。
/// </summary>
internal sealed class PacedSender<TPacket> : IDisposable where TPacket : class
{
    private const int MaxBatchPackets = 64;
    private const double QueueSeconds = 0.25;
    private const int AssumedPacketBytes = 1400;
    private const int MinQueuePackets = 32;
    private const int MaxQueuePackets = 2048;

    private readonly string _name;
    private readonly Func<TPacket, int> _size;
    private readonly Action<TPacket> _deliver;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly BlockingCollection<TPacket> _download;
    private readonly BlockingCollection<TPacket> _upload;
    private readonly BlockingCollection<TPacket>[] _queues;

    private volatile TokenBucket? _downloadBucket;
    private volatile TokenBucket? _uploadBucket;
    private double? _downloadRate;
    private double? _uploadRate;
    private Thread? _worker;

    public PacedSender(string name, LimitRule limit, Func<TPacket, int> size, Action<TPacket> deliver)
    {
        _name = name;
        _size = size;
        _deliver = deliver;

        var capacity = QueueCapacity(limit);
        _download = new BlockingCollection<TPacket>(capacity);
        _upload = new BlockingCollection<TPacket>(capacity);
        _queues = [_download, _upload];
        Apply(limit);
    }

    public string Name => _name;

    /// <summary>這個方向有沒有設上限。沒設的方向不該進佇列，直接放行。</summary>
    public bool Limits(bool outbound) => Bucket(outbound) is not null;

    public bool TryEnqueue(TPacket packet, bool outbound)
    {
        try
        {
            return (outbound ? _upload : _download).TryAdd(packet);
        }
        catch (InvalidOperationException)
        {
            // 已經停止收封包了。
            return false;
        }
    }

    public void Start()
    {
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "tidegate-shaper-" + _name,
        };
        _worker.Start();
    }

    public void Retarget(LimitRule limit) => Apply(limit);

    /// <summary>停止收新封包，讓執行緒把剩下的送完。</summary>
    public void Stop()
    {
        _download.CompleteAdding();
        _upload.CompleteAdding();
        _cancellation.Cancel();
        _worker?.Join(500);
    }

    public void Dispose()
    {
        Stop();

        // 執行緒還沒收乾淨就別釋放：它還會讀 token。
        if (_worker is null || !_worker.IsAlive)
            _cancellation.Dispose();
    }

    private TokenBucket? Bucket(bool outbound) => outbound ? _uploadBucket : _downloadBucket;

    private void Apply(LimitRule limit)
    {
        var now = Stopwatch.GetTimestamp();

        if (limit.DownloadBytesPerSecond is double download)
        {
            if (_downloadBucket is null || download != _downloadRate)
            {
                _downloadRate = download;
                _downloadBucket = new TokenBucket(download, now);
            }
        }
        else
        {
            _downloadRate = null;
            _downloadBucket = null;
        }

        if (limit.UploadBytesPerSecond is double upload)
        {
            if (_uploadBucket is null || upload != _uploadRate)
            {
                _uploadRate = upload;
                _uploadBucket = new TokenBucket(upload, now);
            }
        }
        else
        {
            _uploadRate = null;
            _uploadBucket = null;
        }
    }

    private void Run()
    {
        var token = _cancellation.Token;
        var batch = new List<TPacket>(MaxBatchPackets);

        while (!token.IsCancellationRequested)
        {
            int index;
            TPacket? first;
            try
            {
                index = BlockingCollection<TPacket>.TryTakeFromAny(_queues, out first, 100, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (index < 0 || first is null)
                continue;

            // _queues 的順序是 [下載, 上傳]。
            var bucket = Bucket(outbound: index == 1);
            if (bucket is null)
            {
                Deliver(first);
                continue;
            }

            var queue = _queues[index];
            batch.Clear();
            batch.Add(first);

            var bytes = _size(first);
            var batchLimit = bucket.BatchBytes;
            while (batch.Count < MaxBatchPackets && bytes < batchLimit && queue.TryTake(out var next) && next is not null)
            {
                batch.Add(next);
                bytes += _size(next);
            }

            Wait(bucket.Take(bytes, Stopwatch.GetTimestamp()));

            foreach (var packet in batch)
                Deliver(packet);
        }

        Drain();
    }

    /// <summary>
    /// 等到額度補齊。Sleep 的粒度是 1 毫秒（啟動時已把計時器精度提到 1 毫秒），
    /// 多睡掉的部分留在桶子裡，下一批會補回來，所以不必自旋去搶微秒級精度。
    /// </summary>
    private static void Wait(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
            return;

        var milliseconds = wait.TotalMilliseconds;
        if (milliseconds >= 1)
            Thread.Sleep((int)Math.Round(milliseconds));
    }

    private void Deliver(TPacket packet)
    {
        try
        {
            _deliver(packet);
        }
        catch (Exception exception)
        {
            WindowsLog.Write("shaper deliver failed: " + exception.Message);
        }
    }

    /// <summary>停止時把佇列裡剩下的封包直接送掉，不要讓連線卡住。</summary>
    private void Drain()
    {
        foreach (var queue in _queues)
        {
            while (queue.TryTake(out var packet))
            {
                if (packet is not null)
                    Deliver(packet);
            }
        }
    }

    private static int QueueCapacity(LimitRule limit)
    {
        var rate = Math.Max(limit.DownloadBytesPerSecond ?? 0, limit.UploadBytesPerSecond ?? 0);
        if (rate <= 0)
            return MinQueuePackets;

        return Math.Clamp((int)(rate * QueueSeconds / AssumedPacketBytes), MinQueuePackets, MaxQueuePackets);
    }
}
