using System.Text.Json;

namespace CaptureDesk.Core;

public sealed class JsonConfigStore(string? filePath = null) : IConfigStore
{
    private readonly string _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureDesk", "settings.json");
    public AppSettings Load()
    {
        try { return File.Exists(_filePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings() : new AppSettings(); }
        catch { return new AppSettings(); }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
