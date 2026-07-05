using System.Text.Json;

namespace MinecraftLauncher.Core.Config;

public class AppSettings
{
    public string GameDirectory { get; set; } = GetDefaultGameDir();
    public string? JavaPath { get; set; }
    public string LastProfileId { get; set; } = "";
    public bool ShowSnapshots { get; set; } = false;
    public string Language { get; set; } = "ru";
    public int DefaultMinRam { get; set; } = 1024;
    public int DefaultMaxRam { get; set; } = 4096;
    public bool CloseLauncherOnGameStart { get; set; } = false;

    public static string GetDefaultGameDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Application Support/minecraft");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".minecraft");
    }
}

public class SettingsManager
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsManager(string appDir)
    {
        _settingsPath = Path.Combine(appDir, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Save();
                return;
            }

            var json = File.ReadAllText(_settingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                       ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                await SaveAsync();
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                       ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}