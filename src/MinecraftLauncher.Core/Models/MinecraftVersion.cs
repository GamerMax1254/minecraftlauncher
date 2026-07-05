namespace MinecraftLauncher.Core.Models;

public class MinecraftVersion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = ""; // release, snapshot, old_beta
    public string Url { get; set; } = "";
    public DateTime ReleaseTime { get; set; }
}

public class VersionManifest
{
    public LatestVersions Latest { get; set; } = new();
    public List<MinecraftVersion> Versions { get; set; } = new();
}

public class LatestVersions
{
    public string Release { get; set; } = "";
    public string Snapshot { get; set; } = "";
}