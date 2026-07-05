namespace MinecraftLauncher.Core.Models;

public class JavaInstallation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";       // "JDK 21 (auto)" или пользовательское
    public string Path { get; set; } = "";       // путь к java.exe
    public string Version { get; set; } = "";    // "21.0.2", "17.0.9"
    public int MajorVersion { get; set; }        // 8, 17, 21
    public bool IsAutoDetected { get; set; }

    public override string ToString() => $"{Name} ({Version})";
}