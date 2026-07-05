namespace MinecraftLauncher.Core.Models;

public class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default";
    public string VersionId { get; set; } = "";
    public string? ModLoader { get; set; } // "forge", "fabric", null
    public string? ModLoaderVersion { get; set; }
    public int MinRamMb { get; set; } = 512;
    public int MaxRamMb { get; set; } = 4096;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string? JavaPath { get; set; }
    public string? ExtraJvmArgs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}