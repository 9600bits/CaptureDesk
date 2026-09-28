using System.Text.Json;

namespace CaptureDesk.Core;

public sealed class JsonConfigStore(string? filePath = null) : IConfigStore
{
    private readonly string _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureDesk", "settings.json");
    public string? LastLoadWarning { get; private set; }
    public AppSettings Load()
    {
        LastLoadWarning = null;
        if (!File.Exists(_filePath)) return new AppSettings();
        try { return Read(_filePath); }
        catch (Exception primaryError) when (primaryError is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            var backup = _filePath + ".bak";
            if (File.Exists(backup))
            {
                try
                {
                    var settings = Read(backup);
                    PreserveCorruptFile();
                    LastLoadWarning = "主配置文件无法读取，已从备份恢复。损坏的文件已另存保留。";
                    return settings;
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
            }
            PreserveCorruptFile();
            LastLoadWarning = "配置文件无法读取，已使用默认设置。原文件已另存为损坏配置备份。";
            return new AppSettings();
        }
    }
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            if (File.Exists(_filePath)) File.Replace(temporary, _filePath, _filePath + ".bak", true);
            else File.Move(temporary, _filePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static AppSettings Read(string path) => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new JsonException("配置文件为空。");

    private void PreserveCorruptFile()
    {
        try
        {
            var destination = _filePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_filePath, destination, false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
