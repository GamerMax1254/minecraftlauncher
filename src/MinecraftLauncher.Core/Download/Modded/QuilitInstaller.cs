// src/MinecraftLauncher.Core/Download/Modded/QuiltInstaller.cs
using System.Net.Http.Json;
using System.Text.Json;
using MinecraftLauncher.Core.Logging;

namespace MinecraftLauncher.Core.Download.Modded;

public class QuiltInstaller
{
    private const string QuiltMetaBase = "https://meta.quiltmc.org/v3";

    private readonly HttpClient _http;
    private readonly DownloadManager _downloader;
    private readonly string _gameDir;

    public QuiltInstaller(HttpClient http, string gameDir)
    {
        _http = http;
        _downloader = new DownloadManager(http);
        _gameDir = gameDir;
    }

    public async Task<List<FabricGameVersion>> GetSupportedGameVersionsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<FabricGameVersion>>(
                $"{QuiltMetaBase}/versions/game", ct);
            return list ?? new();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch Quilt game versions", ex);
            return new();
        }
    }

    public async Task<List<FabricLoaderVersion>> GetLoaderVersionsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<FabricLoaderVersion>>(
                $"{QuiltMetaBase}/versions/loader", ct);
            return list ?? new();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch Quilt loader versions", ex);
            return new();
        }
    }

    public async Task InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        IProgress<(string Stage, int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        Logger.Info($"Installing Quilt {loaderVersion} for MC {minecraftVersion}");
        progress?.Report(("Загрузка манифеста Quilt...", 0, 1));

        var profileUrl = $"{QuiltMetaBase}/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
        var profileJson = await _http.GetStringAsync(profileUrl, ct);

        var versionFolderName = $"quilt-loader-{loaderVersion}-{minecraftVersion}";
        var versionDir = Path.Combine(_gameDir, "versions", versionFolderName);
        Directory.CreateDirectory(versionDir);

        var versionJsonPath = Path.Combine(versionDir, $"{versionFolderName}.json");
        await File.WriteAllTextAsync(versionJsonPath, profileJson, ct);

        var vanillaJar = Path.Combine(_gameDir, "versions", minecraftVersion, $"{minecraftVersion}.jar");
        var quiltJar = Path.Combine(versionDir, $"{versionFolderName}.jar");
        if (File.Exists(vanillaJar) && !File.Exists(quiltJar))
            File.Copy(vanillaJar, quiltJar);

        using var doc = JsonDocument.Parse(profileJson);
        if (doc.RootElement.TryGetProperty("libraries", out var libs))
        {
            await DownloadLibrariesAsync(libs, progress, ct);
        }

        Logger.Info($"Quilt {loaderVersion} installed successfully");
    }

    private async Task DownloadLibrariesAsync(
        JsonElement libraries,
        IProgress<(string Stage, int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var libsDir = Path.Combine(_gameDir, "libraries");
        var toDownload = new List<(string Url, string Path)>();

        foreach (var lib in libraries.EnumerateArray())
        {
            if (!lib.TryGetProperty("name", out var nameElem)) continue;
            var name = nameElem.GetString();
            if (string.IsNullOrEmpty(name)) continue;

            var parts = name.Split(':');
            if (parts.Length < 3) continue;

            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];

            var relativePath = $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
            var localPath = Path.Combine(libsDir,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            var baseUrl = lib.TryGetProperty("url", out var urlElem)
                ? urlElem.GetString() ?? "https://maven.quiltmc.org/repository/release/"
                : "https://maven.quiltmc.org/repository/release/";

            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            var fullUrl = baseUrl + relativePath;

            if (!File.Exists(localPath))
                toDownload.Add((fullUrl, localPath));
        }

        if (toDownload.Count == 0) return;

        var reportProgress = new Progress<(int, int, string)>(p =>
        {
            progress?.Report(("Загрузка библиотек Quilt...", p.Item1, p.Item2));
        });

        await _downloader.DownloadMultipleAsync(
            toDownload, reportProgress, maxParallel: 6, ct: ct);
    }
}
