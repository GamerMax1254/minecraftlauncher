namespace MinecraftLauncher.Core.Models;

public class LaunchSettings
{
    public string GameDir { get; set; } = "";
    public string JavaPath { get; set; } = "java";
    public string VersionId { get; set; } = "";
    public int MinRam { get; set; } = 512;
    public int MaxRam { get; set; } = 4096;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public string Username { get; set; } = "Player";
    public string Uuid { get; set; } = "";
    public string? AccessToken { get; set; }
    public string? ExtraJvmArgs { get; set; }
}