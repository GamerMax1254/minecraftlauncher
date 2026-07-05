using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Download;

public class VersionManager
{
    private const string ManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly HttpClient _http;
    private readonly string _gameDir;

    public VersionManager(HttpClient http, string gameDir)
    {
        _http = http;
        _gameDir = gameDir;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync(
        bool includeSnapshots = false)
    {
        var response = await _http.GetFromJsonAsync<ManifestResponse>(ManifestUrl);
        if (response is null) return new();

        var versions = response.Versions
            .Select(v => new MinecraftVersion
            {
                Id = v.Id,
                Type = v.Type,
                Url = v.Url,
                ReleaseTime = v.ReleaseTime
            });

        if (!includeSnapshots)
            versions = versions.Where(v => v.Type == "release");

        return versions.ToList();
    }

    public string GetVersionDir(string versionId) =>
        Path.Combine(_gameDir, "versions", versionId);

    public bool IsVersionInstalled(string versionId) =>
        File.Exists(Path.Combine(GetVersionDir(versionId), $"{versionId}.jar"));

    // JSON mapping для Mojang API
    private class ManifestResponse
    {
        [JsonPropertyName("versions")]
        public List<ManifestVersion> Versions { get; set; } = new();
    }

    private class ManifestVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("releaseTime")]
        public DateTime ReleaseTime { get; set; }
    }
}