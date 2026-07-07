using System.Text.Json;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Config;

public class ProfileManager
{
    private readonly string _profilesPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<GameProfile> Profiles { get; private set; } = new();

    public ProfileManager(string appDir)
    {
        _profilesPath = Path.Combine(appDir, "profiles.json");
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_profilesPath))
            {
                Profiles = new List<GameProfile>();
                Save();
                return;
            }

            var json = File.ReadAllText(_profilesPath);
            Profiles = JsonSerializer.Deserialize<List<GameProfile>>(json, JsonOptions)
                       ?? new List<GameProfile>();
        }
        catch
        {
            Profiles = new List<GameProfile>();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_profilesPath)!);
        var json = JsonSerializer.Serialize(Profiles, JsonOptions);
        File.WriteAllText(_profilesPath, json);
    }

    public bool NameExists(string name, string? excludeId = null)
    {
        return Profiles.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            p.Id != excludeId);
    }

    public void Add(GameProfile profile)
    {
        Profiles.Add(profile);
        Save();
    }

    public void Remove(GameProfile profile)
    {
        Profiles.Remove(profile);
        Save();
    }

    public void Update(GameProfile profile)
    {
        Save();
    }
}