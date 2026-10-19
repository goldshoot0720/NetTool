using System.Text.Json;
using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class JsonLimitStore
{
    public JsonLimitStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tidegate");
        FilePath = Path.Combine(DirectoryPath, "limits.json");
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public LimitCatalog Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new LimitCatalog();
            return LimitCatalog.FromJson(File.ReadAllText(FilePath));
        }
        catch (JsonException)
        {
            return new LimitCatalog();
        }
    }

    public void Save(LimitCatalog catalog)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, catalog.ToJson());
    }
}
