namespace MinecraftLauncher.Core.Models;

public enum ModLoader
{
    Vanilla,
    Fabric,
    Forge,
    Quilt,
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

    /// <summary>
    /// Использовать изолированную папку для этого профиля
    /// (по умолчанию: true для модовых, false для ванилы)
    /// </summary>
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
    /// Возвращает путь к папке игры для этого профиля
    /// </summary>
    public string GetGameDirectory(string baseGameDir)
    {
        if (!IsolatedGameDir)
            return baseGameDir;

        // Санитизируем имя для использования в пути
        var safeName = string.Join("_", Name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(baseGameDir, "profiles", safeName);
    }
}
