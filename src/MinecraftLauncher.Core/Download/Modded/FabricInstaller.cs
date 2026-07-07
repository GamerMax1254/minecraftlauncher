// src/MinecraftLauncher.Core/Download/Modded/FabricInstaller.cs
using System.Net.Http.Json;
using System.Text.Json;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Download.Modded;

public class FabricInstaller
{
    private const string FabricMetaBase = "https://meta.fabricmc.net/v2";

    private readonly HttpClient _http;
    private readonly DownloadManager _downloader;
    private readonly string _gameDir;

    public FabricInstaller(HttpClient http, string gameDir)
    {
        _http = http;
        _downloader = new DownloadManager(http);
        _gameDir = gameDir;
    }

    /// <summary>
    /// Возвращает список версий Minecraft, поддерживающих Fabric
    /// </summary>
    public async Task<List<FabricGameVersion>> GetSupportedGameVersionsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<FabricGameVersion>>(
                $"{FabricMetaBase}/versions/game", ct);
            return list ?? new List<FabricGameVersion>();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch Fabric game versions", ex);
            return new List<FabricGameVersion>();
        }
    }

    /// <summary>
    /// Возвращает список версий Fabric loader
    /// </summary>
    public async Task<List<FabricLoaderVersion>> GetLoaderVersionsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<FabricLoaderVersion>>(
                $"{FabricMetaBase}/versions/loader", ct);
            return list ?? new List<FabricLoaderVersion>();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch Fabric loader versions", ex);
            return new List<FabricLoaderVersion>();
        }
    }

    /// <summary>
    /// Устанавливает Fabric поверх ванильной версии
    /// </summary>
    public async Task InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        IProgress<(string Stage, int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        Logger.Info($"Installing Fabric {loaderVersion} for MC {minecraftVersion}");
        progress?.Report(("Загрузка манифеста Fabric...", 0, 1));

        // 1. Получаем JSON профиля Fabric (там мета + библиотеки)
        var profileUrl = $"{FabricMetaBase}/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
        var profileJson = await _http.GetStringAsync(profileUrl, ct);

        // 2. Сохраняем как отдельную версию
        var versionFolderName = $"fabric-loader-{loaderVersion}-{minecraftVersion}";
        var versionDir = Path.Combine(_gameDir, "versions", versionFolderName);
        Directory.CreateDirectory(versionDir);

        var versionJsonPath = Path.Combine(versionDir, $"{versionFolderName}.json");
        await File.WriteAllTextAsync(versionJsonPath, profileJson, ct);

        // 3. Fabric НЕ имеет собственного JAR — использует ванильный
        // Создаём пустой JAR-заглушку (некоторые лаунчеры так делают) ИЛИ копируем ванильный
        var vanillaJar = Path.Combine(_gameDir, "versions", minecraftVersion, $"{minecraftVersion}.jar");
        var fabricJar = Path.Combine(versionDir, $"{versionFolderName}.jar");

        if (File.Exists(vanillaJar) && !File.Exists(fabricJar))
            File.Copy(vanillaJar, fabricJar);

        // 4. Скачиваем библиотеки Fabric
        using var doc = JsonDocument.Parse(profileJson);
        if (doc.RootElement.TryGetProperty("libraries", out var libs))
        {
            await DownloadFabricLibrariesAsync(libs, progress, ct);
        }

        Logger.Info($"Fabric {loaderVersion} installed successfully");
    }

    private async Task DownloadFabricLibrariesAsync(
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
                ? urlElem.GetString() ?? "https://maven.fabricmc.net/"
                : "https://maven.fabricmc.net/";

            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            var fullUrl = baseUrl + relativePath;

            if (!File.Exists(localPath))
            {
                Logger.Debug($"Fabric lib to download: {name} → {fullUrl}");
                toDownload.Add((fullUrl, localPath));
            }
            else
            {
                Logger.Debug($"Fabric lib exists: {name}");
            }
        }

        Logger.Info($"Fabric libraries to download: {toDownload.Count}");
        if (toDownload.Count == 0) return;

        int done = 0;
        var reportProgress = new Progress<(int, int, string)>(p =>
        {
            done = p.Item1;
            progress?.Report(("Загрузка библиотек Fabric...", p.Item1, p.Item2));
        });

        await _downloader.DownloadMultipleAsync(
            toDownload, reportProgress, maxParallel: 6, ct: ct);

        // Проверка что всё скачалось
        var missing = new List<string>();
        foreach (var (_, path) in toDownload)
        {
            if (!File.Exists(path))
                missing.Add(Path.GetFileName(path));
        }

        if (missing.Count > 0)
        {
            Logger.Warn($"Missing Fabric libraries after download: {string.Join(", ", missing)}");
            throw new Exception($"Не удалось скачать {missing.Count} библиотек Fabric. Проверьте интернет.");
        }
    }
}
