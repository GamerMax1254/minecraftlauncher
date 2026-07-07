using System.Text.Json.Serialization;

namespace MinecraftLauncher.Core.Download.Modded;

public class FabricLoaderVersion
{
    [JsonPropertyName("separator")] public string Separator { get; set; } = "";
    [JsonPropertyName("build")] public int Build { get; set; }
    [JsonPropertyName("maven")] public string Maven { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

public class FabricGameVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}
