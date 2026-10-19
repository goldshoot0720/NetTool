using System.Text.Json;

namespace Tidegate.Core;

public sealed class LimitCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, LimitRule> _rules = new(StringComparer.Ordinal);

    public IReadOnlyCollection<LimitRule> Rules => _rules.Values;

    public LimitRule? Find(string appName) =>
        _rules.TryGetValue(appName, out var rule) ? rule : null;

    public bool HasLimit(string appName) =>
        _rules.TryGetValue(appName, out var rule)
        && (rule.DownloadBytesPerSecond is not null || rule.UploadBytesPerSecond is not null);

    public void Set(string appName, double? downloadBytesPerSecond, double? uploadBytesPerSecond)
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException("應用程式名稱不能是空白。", nameof(appName));
        if (downloadBytesPerSecond is null && uploadBytesPerSecond is null)
        {
            _rules.Remove(appName);
            return;
        }

        _rules[appName] = new LimitRule(appName, downloadBytesPerSecond, uploadBytesPerSecond);
    }

    public string ToJson()
    {
        var payload = _rules.Values
            .OrderBy(rule => rule.AppName, StringComparer.Ordinal)
            .Select(rule => new StoredRule(rule.AppName, rule.DownloadBytesPerSecond, rule.UploadBytesPerSecond))
            .ToArray();
        return JsonSerializer.Serialize(new StoredCatalog(payload), JsonOptions);
    }

    public static LimitCatalog FromJson(string json)
    {
        var catalog = new LimitCatalog();
        if (string.IsNullOrWhiteSpace(json))
            return catalog;

        var stored = JsonSerializer.Deserialize<StoredCatalog>(json);
        if (stored?.Rules is null)
            return catalog;

        foreach (var rule in stored.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.AppName))
                continue;
            if (rule.DownloadBytesPerSecond is < 0 || rule.UploadBytesPerSecond is < 0)
                continue;
            catalog.Set(rule.AppName, rule.DownloadBytesPerSecond, rule.UploadBytesPerSecond);
        }

        return catalog;
    }

    private sealed record StoredCatalog(StoredRule[] Rules);

    private sealed record StoredRule(string AppName, double? DownloadBytesPerSecond, double? UploadBytesPerSecond);
}
