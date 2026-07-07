// src/MinecraftLauncher.Core/Models/GameProfile.cs
namespace MinecraftLauncher.Core.Models;

public enum ModLoader
{
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public ModLoader Loader { get; set; } = ModLoader.Vanilla;
    public string? LoaderVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
    public int PlayCount { get; set; }

    public int? OverrideMinRam { get; set; }
    public int? OverrideMaxRam { get; set; }
    public string? OverrideJavaId { get; set; }

    public bool IsolatedGameDir { get; set; }

    public string DisplayLoader => Loader switch
    {
        ModLoader.Vanilla => "Vanilla",
        ModLoader.Fabric => $"Fabric {LoaderVersion}",
        ModLoader.Forge => $"Forge {LoaderVersion}",
        ModLoader.Quilt => $"Quilt {LoaderVersion}",
        ModLoader.NeoForge => $"NeoForge {LoaderVersion}",
        _ => "Unknown"
    };

    public string LoaderShortName => Loader switch
    {
        ModLoader.Vanilla => "Vanilla",
        ModLoader.Fabric => "Fabric",
        ModLoader.Forge => "Forge",
        ModLoader.Quilt => "Quilt",
        ModLoader.NeoForge => "NeoForge",
        _ => "Unknown"
    };

    public string LoaderColor => Loader switch
    {
        ModLoader.Vanilla => "#22c55e",
        ModLoader.Fabric => "#a855f7",
        ModLoader.Forge => "#ea580c",
        ModLoader.Quilt => "#ec4899",
        ModLoader.NeoForge => "#f97316",
        _ => "#71717a"
    };

    /// <summary>
    /// Возвращает ID итоговой версии в папке versions/
    /// Например: "fabric-loader-0.15.11-1.21.4"
    /// </summary>
    public string GetVersionFolderName()
    {
        return Loader switch
        {
            ModLoader.Vanilla => MinecraftVersion,
            ModLoader.Fabric => $"fabric-loader-{LoaderVersion}-{MinecraftVersion}",
            ModLoader.Quilt => $"quilt-loader-{LoaderVersion}-{MinecraftVersion}",
            ModLoader.Forge => $"{MinecraftVersion}-forge-{LoaderVersion}",
            ModLoader.NeoForge => $"neoforge-{LoaderVersion}",
            _ => MinecraftVersion
        };
    }

    public string GetGameDirectory(string baseGameDir)
    {
        if (!IsolatedGameDir)
            return baseGameDir;

        var safeName = string.Join("_", Name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(baseGameDir, "profiles", safeName);
    }
}
