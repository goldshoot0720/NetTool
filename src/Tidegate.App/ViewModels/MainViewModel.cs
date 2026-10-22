using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tidegate.Core;
using Tidegate.Platform;
using SortKey = Tidegate.Core.SortKey;

namespace Tidegate.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly TrafficBoard _board;
    private readonly ITrafficSource _source;
    private readonly IProcessCatalog _catalog;
    private readonly IConnectionLookup _connections;
    private readonly ILimitPlanner _planner;
    private readonly ILimitGateway _gateway;
    private readonly IElevation _elevation;
    private readonly JsonLimitStore _store;
    private readonly LimitCatalog _limits;
    private readonly PlatformCapabilities _capabilities;
    private readonly Action? _shutdown;
    private CancellationTokenSource? _monitor;
    private volatile bool _shaping;

    public MainViewModel(
        TrafficBoard board,
        ITrafficSource source,
        IProcessCatalog catalog,
        IConnectionLookup connections,
        ILimitPlanner planner,
        ILimitGateway gateway,
        IElevation elevation,
        PlatformCapabilities capabilities,
        JsonLimitStore store,
        LimitCatalog limits,
        Action? shutdown = null)
    {
        _board = board;
        _source = source;
        _catalog = catalog;
        _connections = connections;
        _planner = planner;
        _gateway = gateway;
        _elevation = elevation;
        _capabilities = capabilities;
        _store = store;
        _limits = limits;
        _shutdown = shutdown;
        Rows = new ObservableCollection<ChannelRowViewModel>();
        HideIdle = board.HideIdle;
        StatusMessage = "正在讀取第一個樣本…";
        EmptyMessage = "正在測量，約一秒後顯示速度。";
        Publish(board.WithSort(board.Sort));
    }

    public ObservableCollection<ChannelRowViewModel> Rows { get; }

    public PlatformCapabilities Capabilities => _capabilities;

    public string PlatformName => _capabilities.Name;

    public string LimitHint => _capabilities.LimitHint;

    public string ObservationHint => _capabilities.ObservationHint;

    public bool SupportsDownloadLimit => _capabilities.SupportsDownloadLimit;

    public bool CanElevate => _capabilities.RequiresElevation && !_elevation.IsElevated;

    public string ElevationMessage =>
        $"{_capabilities.Name} 版要讀取每個應用程式的流量，需要系統管理員權限。按右邊的按鈕會開啟一個提權的視窗，這個視窗會關閉。";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _hideIdle;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _totalDownloadText = "0 B/s";

    [ObservableProperty]
    private string _totalUploadText = "0 B/s";

    [ObservableProperty]
    private string _sortDescription = "";

    [ObservableProperty]
    private string _directionText = "";

    [ObservableProperty]
    private bool _isNameSort;

    [ObservableProperty]
    private bool _isSpeedSort;

    [ObservableProperty]
    private bool _isTotalBasis;

    [ObservableProperty]
    private bool _isDownloadBasis;

    [ObservableProperty]
    private bool _isUploadBasis;

    [ObservableProperty]
    private bool _nameHeaderSelected;

    [ObservableProperty]
    private bool _downloadHeaderSelected;

    [ObservableProperty]
    private bool _uploadHeaderSelected;

    [ObservableProperty]
    private bool _totalHeaderSelected;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _downloadCapText = "";

    [ObservableProperty]
    private string _uploadCapText = "";

    [ObservableProperty]
    private string _planPreview = "還沒有規則預覽。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ChannelRowViewModel? _selected;

    [ObservableProperty]
    private double[] _selectedDownload = [];

    [ObservableProperty]
    private double[] _selectedUpload = [];

    [ObservableProperty]
    private double _selectedDownloadCap = double.NaN;

    [ObservableProperty]
    private double _selectedUploadCap = double.NaN;

    public void Start()
    {
        if (_capabilities.RequiresElevation && !_elevation.IsElevated)
        {
            StatusMessage = "尚未開始觀測。";
            EmptyMessage = "還沒有開始觀測。";
            return;
        }

        _monitor?.Cancel();
        _monitor = new CancellationTokenSource();
        _ = ObserveAsync(_monitor.Token);
    }

    public void Stop()
    {
        _shaping = false;
        _monitor?.Cancel();
        _gateway.StopTracking();
        if (_source is IDisposable disposable)
            disposable.Dispose();
    }

    private bool TryCommitDraft(out string? error)
    {
        error = null;
        if (Selected is null)
            return true;

        if (!RateText.TryParseKilobytes(DownloadCapText, out var download, out error)
            || !RateText.TryParseKilobytes(UploadCapText, out var upload, out error))
        {
            error ??= "上限格式不正確。";
            return false;
        }

        var name = Selected.Name;
        Publish(_board.SetLimit(name, download, upload));
        _store.Save(_limits);
        return true;
    }

    partial void OnSearchTextChanged(string value) => Publish(_board.WithSearch(value));

    partial void OnHideIdleChanged(bool value) => Publish(_board.WithHideIdle(value));

    partial void OnSelectedChanged(ChannelRowViewModel? value)
    {
        HasSelection = value is not null;
        DownloadCapText = FormatCap(value?.DownloadCapBytesPerSecond);
        UploadCapText = FormatCap(value?.UploadCapBytesPerSecond);
        RefreshSeries();
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (_elevation.TryRestartElevated(out var message))
        {
            _shutdown?.Invoke();
            return;
        }

        StatusMessage = message;
    }

    [RelayCommand]
    private void SortByName() => Publish(_board.WithSort(_board.Sort.ToggleName()));

    [RelayCommand]
    private void SortByTotal() => Publish(_board.WithSort(_board.Sort.ToggleSpeed(SpeedBasis.Total)));

    [RelayCommand]
    private void SortByDownload() => Publish(_board.WithSort(_board.Sort.ToggleSpeed(SpeedBasis.Download)));

    [RelayCommand]
    private void SortByUpload() => Publish(_board.WithSort(_board.Sort.ToggleSpeed(SpeedBasis.Upload)));

    [RelayCommand]
    private void ToggleDirection()
    {
        var next = _board.Sort.Key == SortKey.Name
            ? _board.Sort.ToggleName()
            : _board.Sort.ToggleSpeed(_board.Sort.Basis);
        Publish(_board.WithSort(next));
    }

    [RelayCommand]
    private void SaveLimit()
    {
        if (Selected is null)
        {
            StatusMessage = "先選一個應用程式。";
            return;
        }

        if (!TryCommitDraft(out var error))
        {
            StatusMessage = error ?? "上限格式不正確。";
            return;
        }

        var name = Selected?.Name ?? "";
        StatusMessage = string.IsNullOrWhiteSpace(DownloadCapText) && string.IsNullOrWhiteSpace(UploadCapText)
            ? $"已清除 {name} 的上限。還沒寫入系統。"
            : $"已保存 {name} 的上限。按「套用上限」才會寫入系統。";
    }

    [RelayCommand]
    private void ClearSelectedLimit()
    {
        DownloadCapText = "";
        UploadCapText = "";
        SaveLimit();
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!TryCommitDraft(out var error))
        {
            StatusMessage = error ?? "上限格式不正確。";
            return;
        }

        var plan = await BuildPlanAsync();
        if (plan is null)
            return;
        PlanPreview = Render(plan);
        StatusMessage = plan.HasWork
            ? "已產生規則預覽，尚未寫入系統。"
            : "沒有可套用的規則，原因寫在下面的預覽。";
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!TryCommitDraft(out var error))
        {
            StatusMessage = error ?? "上限格式不正確。";
            return;
        }

        var plan = await BuildPlanAsync();
        if (plan is null)
            return;
        PlanPreview = Render(plan);
        if (!plan.HasWork)
        {
            StatusMessage = "沒有可套用的規則，原因寫在下面的預覽。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在要求管理者權限。同意之後，上限才會作用在連線上。";
        try
        {
            var result = await _gateway.ApplyAsync(plan, _monitor?.Token ?? CancellationToken.None);
            _shaping = result.Applied;
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "套用已取消。";
        }
        catch (Exception ex)
        {
            StatusMessage = "套用失敗：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearSystemAsync()
    {
        _shaping = false;
        IsBusy = true;
        try
        {
            var result = await _gateway.ClearAsync(_monitor?.Token ?? CancellationToken.None);
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "清除已取消。";
        }
        catch (Exception ex)
        {
            StatusMessage = "清除失敗：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<LimitPlan?> BuildPlanAsync()
    {
        var apps = _board.LimitedApps();
        if (apps.Count == 0)
        {
            StatusMessage = "先輸入下載或上傳的 KB/s，再按套用上限。";
            return null;
        }

        try
        {
            var pids = apps.SelectMany(app => app.ProcessIds).Where(pid => pid > 0).Distinct().ToArray();
            var sockets = pids.Length == 0
                ? Array.Empty<FlowSocket>()
                : await _connections.LookupAsync(pids, _monitor?.Token ?? CancellationToken.None);
            var paths = await _catalog.CommandPathsAsync(_monitor?.Token ?? CancellationToken.None);
            return _planner.Build(apps, sockets, paths);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = "讀取連線失敗：" + ex.Message;
            return null;
        }
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in _source.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                var paths = await _catalog.CommandPathsAsync(cancellationToken).ConfigureAwait(false);
                var at = DateTimeOffset.UtcNow;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Publish(_board.Ingest(sample, paths, at));
                    if (string.IsNullOrWhiteSpace(StatusMessage) || StatusMessage.StartsWith("正在讀取", StringComparison.Ordinal))
                        StatusMessage = "外部流量觀測中。";
                });
                if (_shaping && _capabilities.TracksLiveConnections)
                    await RefreshShapingAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusMessage = "觀測中斷：" + ex.Message);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task RefreshShapingAsync(CancellationToken cancellationToken)
    {
        var apps = await Dispatcher.UIThread.InvokeAsync(_board.LimitedApps);
        if (apps.Count == 0)
        {
            _gateway.Stage(new LimitPlan([], [], []));
            _shaping = false;
            return;
        }

        var pids = apps.SelectMany(app => app.ProcessIds).Where(pid => pid > 0).Distinct().ToArray();
        var sockets = pids.Length == 0
            ? Array.Empty<FlowSocket>()
            : await _connections.LookupAsync(pids, cancellationToken);
        var paths = await _catalog.CommandPathsAsync(cancellationToken);
        _gateway.Stage(_planner.Build(apps, sockets, paths));
    }

    private void Publish(TrafficBoardState state)
    {
        TotalDownloadText = RateText.Format(state.TotalDownloadBytesPerSecond);
        TotalUploadText = RateText.Format(state.TotalUploadBytesPerSecond);
        UpdateSortFlags(state.Sort);

        var existing = Rows.ToDictionary(row => row.Name, StringComparer.Ordinal);
        var next = new List<ChannelRowViewModel>(state.Rows.Count);
        foreach (var snapshot in state.Rows)
        {
            if (!existing.Remove(snapshot.Name, out var row))
                row = new ChannelRowViewModel(snapshot.Name);
            row.Update(snapshot);
            next.Add(row);
        }

        for (var index = 0; index < next.Count; index++)
        {
            var found = Rows.IndexOf(next[index]);
            if (found < 0)
                Rows.Insert(index, next[index]);
            else if (found != index)
                Rows.Move(found, index);
        }

        while (Rows.Count > next.Count)
            Rows.RemoveAt(Rows.Count - 1);

        if (Selected is not null && !Rows.Contains(Selected))
            Selected = null;

        HasRows = Rows.Count > 0;
        EmptyMessage = HasRows
            ? ""
            : state.HasSample ? "沒有符合的應用程式。" : "正在測量，約一秒後顯示速度。";
        RefreshSeries();
    }

    private void UpdateSortFlags(SortSelection sort)
    {
        IsNameSort = sort.Key == SortKey.Name;
        IsSpeedSort = sort.Key == SortKey.Speed;
        IsTotalBasis = sort.Basis == SpeedBasis.Total;
        IsDownloadBasis = sort.Basis == SpeedBasis.Download;
        IsUploadBasis = sort.Basis == SpeedBasis.Upload;
        NameHeaderSelected = IsNameSort;
        DownloadHeaderSelected = IsSpeedSort && IsDownloadBasis;
        UploadHeaderSelected = IsSpeedSort && IsUploadBasis;
        TotalHeaderSelected = IsSpeedSort && IsTotalBasis;
        DirectionText = sort.Key == SortKey.Name
            ? sort.Direction == SortDirection.Ascending ? "由甲到乙" : "由乙到甲"
            : sort.Direction == SortDirection.Descending ? "由高到低" : "由低到高";
        var basis = sort.Basis switch
        {
            SpeedBasis.Download => "下載",
            SpeedBasis.Upload => "上傳",
            _ => "合計",
        };
        SortDescription = sort.Key == SortKey.Name
            ? $"依照應用程式名稱，{DirectionText}"
            : $"依照{basis}速度，{DirectionText}";
    }

    private void RefreshSeries()
    {
        if (Selected is null)
        {
            SelectedDownload = [];
            SelectedUpload = [];
            SelectedDownloadCap = double.NaN;
            SelectedUploadCap = double.NaN;
            return;
        }

        SelectedDownload = Selected.DownloadSeries;
        SelectedUpload = Selected.UploadSeries;
        SelectedDownloadCap = Selected.DownloadCapBytesPerSecond ?? double.NaN;
        SelectedUploadCap = Selected.UploadCapBytesPerSecond ?? double.NaN;
    }

    private static string FormatCap(double? bytesPerSecond) =>
        bytesPerSecond is null
            ? ""
            : (bytesPerSecond.Value / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Render(LimitPlan plan)
    {
        var builder = new StringBuilder();
        foreach (var note in plan.Notes)
            builder.AppendLine(note);
        builder.AppendLine(plan.Rules.Count == 0 ? "沒有規則。" : $"{plan.Rules.Count} 條規則：");
        foreach (var rule in plan.Rules.Take(40))
            builder.AppendLine(rule);
        if (plan.Rules.Count > 40)
            builder.AppendLine($"…其餘 {plan.Rules.Count - 40} 條");
        return builder.ToString().TrimEnd();
    }
}
