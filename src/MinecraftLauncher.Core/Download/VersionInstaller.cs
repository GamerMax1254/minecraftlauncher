using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Download;

public class VersionInstaller
{
    private readonly HttpClient _http;
    private readonly DownloadManager _downloader;
    private readonly string _gameDir;

    public VersionInstaller(HttpClient http, string gameDir)
    {
        _http = http;
        _downloader = new DownloadManager(http);
        _gameDir = gameDir;
    }

    /// <summary>
    /// Устанавливает версию: JSON, JAR, библиотеки, ассеты
    /// </summary>
    public async Task InstallAsync(
        MinecraftVersion version,
        IProgress<(string Stage, int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        Logger.Info($"Installing version {version.Id}");

        var versionDir = Path.Combine(_gameDir, "versions", version.Id);
        Directory.CreateDirectory(versionDir);

        var versionJsonPath = Path.Combine(versionDir, $"{version.Id}.json");
        var versionJarPath = Path.Combine(versionDir, $"{version.Id}.jar");

        // 1. Скачиваем JSON версии
        if (!File.Exists(versionJsonPath))
        {
            Logger.Info($"Downloading version JSON: {version.Url}");
            progress?.Report(("Загрузка манифеста версии...", 0, 1));
            await _downloader.DownloadFileAsync(version.Url, versionJsonPath, ct: ct);
        }

        // 2. Парсим JSON
        var json = await File.ReadAllTextAsync(versionJsonPath, ct);
        var versionData = JsonSerializer.Deserialize<VersionData>(json)
            ?? throw new Exception("Failed to parse version JSON");

        // 3. Скачиваем клиентский JAR
        if (!File.Exists(versionJarPath) && versionData.Downloads?.Client != null)
        {
            Logger.Info($"Downloading client JAR: {versionData.Downloads.Client.Url}");
            progress?.Report(("Загрузка клиента...", 0, 1));
            await _downloader.DownloadFileAsync(
                versionData.Downloads.Client.Url, versionJarPath, ct: ct);
        }

        // 4. Скачиваем библиотеки
        if (versionData.Libraries != null)
        {
            await DownloadLibrariesAsync(versionData.Libraries, progress, ct);
        }

        // 5. Скачиваем ассеты
        if (versionData.AssetIndex != null)
        {
            await DownloadAssetsAsync(versionData.AssetIndex, progress, ct);
        }

        Logger.Info($"Version {version.Id} installed successfully");
    }

    private async Task DownloadLibrariesAsync(
        List<Library> libraries,
        IProgress<(string Stage, int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var libsDir = Path.Combine(_gameDir, "libraries");
        var toDownload = new List<(string Url, string Path)>();

        foreach (var lib in libraries)
        {
            if (!ShouldIncludeLibrary(lib)) continue;
            if (lib.Downloads?.Artifact == null) continue;

            var artifact = lib.Downloads.Artifact;
            if (string.IsNullOrEmpty(artifact.Path)) continue;

            var localPath = Path.Combine(libsDir,
                artifact.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(localPath))
                toDownload.Add((artifact.Url, localPath));
        }

        Logger.Info($"Libraries to download: {toDownload.Count}");

        if (toDownload.Count == 0) return;

        int done = 0;
        var reportProgress = new Progress<(int, int, string)>(p =>
        {
            done = p.Item1;
            progress?.Report(("Загрузка библиотек...", p.Item1, p.Item2));
        });

        await _downloader.DownloadMultipleAsync(
            toDownload, reportProgress, maxParallel: 8, ct: ct);
    }

    private async Task DownloadAssetsAsync(
        AssetIndex assetIndex,
        IProgress<(string Stage, int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var indexesDir = Path.Combine(_gameDir, "assets", "indexes");
        var objectsDir = Path.Combine(_gameDir, "assets", "objects");
        Directory.CreateDirectory(indexesDir);
        Directory.CreateDirectory(objectsDir);

        var indexPath = Path.Combine(indexesDir, $"{assetIndex.Id}.json");

        if (!File.Exists(indexPath))
        {
            Logger.Info($"Downloading asset index: {assetIndex.Url}");
            progress?.Report(("Индекс ассетов...", 0, 1));
            await _downloader.DownloadFileAsync(assetIndex.Url, indexPath, ct: ct);
        }

        var indexJson = await File.ReadAllTextAsync(indexPath, ct);
        var indexData = JsonSerializer.Deserialize<AssetIndexData>(indexJson);

        if (indexData?.Objects == null) return;

        var toDownload = new List<(string Url, string Path)>();

        foreach (var (_, obj) in indexData.Objects)
        {
            if (string.IsNullOrEmpty(obj.Hash) || obj.Hash.Length < 2) continue;

            var subDir = obj.Hash.Substring(0, 2);
            var localPath = Path.Combine(objectsDir, subDir, obj.Hash);
            var url = $"https://resources.download.minecraft.net/{subDir}/{obj.Hash}";

            if (!File.Exists(localPath))
                toDownload.Add((url, localPath));
        }

        Logger.Info($"Assets to download: {toDownload.Count}");

        if (toDownload.Count == 0) return;

        var reportProgress = new Progress<(int, int, string)>(p =>
        {
            progress?.Report(("Загрузка ресурсов...", p.Item1, p.Item2));
        });

        await _downloader.DownloadMultipleAsync(
            toDownload, reportProgress, maxParallel: 16, ct: ct);
    }

    private bool ShouldIncludeLibrary(Library lib)
    {
        if (lib.Rules == null || lib.Rules.Count == 0) return true;

        bool allowed = false;
        foreach (var rule in lib.Rules)
        {
            bool matches = rule.Os?.Name == null || IsCurrentOs(rule.Os.Name);
            if (matches)
                allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private bool IsCurrentOs(string osName)
    {
        return osName switch
        {
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            "osx" => OperatingSystem.IsMacOS(),
            _ => false
        };
    }

    // === DTO ===
    private class VersionData
    {
        [JsonPropertyName("downloads")] public Downloads? Downloads { get; set; }
        [JsonPropertyName("libraries")] public List<Library>? Libraries { get; set; }
        [JsonPropertyName("assetIndex")] public AssetIndex? AssetIndex { get; set; }
    }

    private class Downloads
    {
        [JsonPropertyName("client")] public DownloadInfo? Client { get; set; }
    }

    private class DownloadInfo
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    private class Library
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
        [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
    }

    private class LibraryDownloads
    {
        [JsonPropertyName("artifact")] public Artifact? Artifact { get; set; }
    }

    private class Artifact
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    private class Rule
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("os")] public RuleOs? Os { get; set; }
    }

    private class RuleOs
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class AssetIndex
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("url")] public string Url { get; set; } = "";
    }

    private class AssetIndexData
    {
        [JsonPropertyName("objects")]
        public Dictionary<string, AssetObject>? Objects { get; set; }
    }

    private class AssetObject
    {
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}