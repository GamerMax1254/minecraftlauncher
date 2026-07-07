using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Launcher;

public class GameLauncher
{
    private readonly string _baseGameDir;
    private readonly string _profileGameDir;

    public event Action<string>? OnLog;
    public event Action<int>? OnGameExited;

    public GameLauncher(string gameDir)
    {
        _baseGameDir = gameDir;
        _profileGameDir = gameDir;
    }

    public GameLauncher(string baseGameDir, string profileGameDir)
    {
        _baseGameDir = baseGameDir;
        _profileGameDir = profileGameDir;
    }

    public async Task<Process> LaunchAsync(LaunchSettings settings)
    {
        Logger.Info($"Launching Minecraft {settings.VersionId}");
        Logger.Info($"Base dir: {_baseGameDir}");
        Logger.Info($"Profile dir: {_profileGameDir}");

        var versionDir = Path.Combine(_baseGameDir, "versions", settings.VersionId);
        var versionJson = Path.Combine(versionDir, $"{settings.VersionId}.json");
        var versionJar = Path.Combine(versionDir, $"{settings.VersionId}.jar");

        if (!File.Exists(versionJson))
            throw new FileNotFoundException($"Version JSON not found: {versionJson}");

        Directory.CreateDirectory(_profileGameDir);

        // Собираем цепочку наследования (Fabric → Vanilla → ...)
        var mergedRoot = await LoadWithInheritanceAsync(versionJson);

        var mainClass = mergedRoot.GetProperty("mainClass").GetString()
            ?? throw new Exception("mainClass not found");

        var classpath = BuildClasspathFromMerged(mergedRoot, versionJar);
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);

        var replacements = BuildReplacements(settings, mergedRoot, classpath, nativesDir);
        var jvmArgs = BuildJvmArguments(mergedRoot, settings, replacements);
        var gameArgs = BuildGameArguments(mergedRoot, settings, replacements);

        var fullArgs = new StringBuilder();
        fullArgs.Append(string.Join(" ", jvmArgs));
        fullArgs.Append(' ').Append(mainClass);
        fullArgs.Append(' ').Append(string.Join(" ", gameArgs));

        var finalArgs = fullArgs.ToString();
        Logger.Info($"Command: {settings.JavaPath} {finalArgs}");

        var gameLogPath = Logger.CreateGameLogFile(settings.VersionId);
        var psi = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            Arguments = finalArgs,
            WorkingDirectory = _profileGameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };
        var logWriter = new StreamWriter(gameLogPath, append: false) { AutoFlush = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            OnLog?.Invoke(e.Data);
            try { logWriter.WriteLine(e.Data); } catch { }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var line = $"[ERR] {e.Data}";
            OnLog?.Invoke(line);
            try { logWriter.WriteLine(line); } catch { }
        };
        process.Exited += (_, _) =>
        {
            Logger.Info($"Game exited with code {process.ExitCode}");
            try { logWriter.Dispose(); } catch { }
            OnGameExited?.Invoke(process.ExitCode);
        };

        process.EnableRaisingEvents = true;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Logger.Info($"Game process started (PID: {process.Id})");
        return process;
    }

    /// <summary>
    /// Загружает version.json с поддержкой inheritsFrom (Fabric/Quilt/Forge)
    /// </summary>
    private async Task<JsonElement> LoadWithInheritanceAsync(string jsonPath)
    {
        var json = await File.ReadAllTextAsync(jsonPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();

        if (!root.TryGetProperty("inheritsFrom", out var parentIdElem))
            return root;

        var parentId = parentIdElem.GetString();
        if (string.IsNullOrEmpty(parentId)) return root;

        Logger.Info($"Version inherits from: {parentId}");

        var parentJsonPath = Path.Combine(_baseGameDir, "versions", parentId, $"{parentId}.json");
        if (!File.Exists(parentJsonPath))
            throw new FileNotFoundException($"Parent version not found: {parentJsonPath}");

        var parentRoot = await LoadWithInheritanceAsync(parentJsonPath);
        return MergeVersions(parentRoot, root);
    }

    private JsonElement MergeVersions(JsonElement parent, JsonElement child)
    {
        // Строим merged JSON через словарь
        var merged = new Dictionary<string, JsonElement>();

        foreach (var prop in parent.EnumerateObject())
            merged[prop.Name] = prop.Value;

        foreach (var prop in child.EnumerateObject())
        {
            if (prop.Name == "libraries" && merged.ContainsKey("libraries"))
            {
                // Объединяем библиотеки: сначала child (loader), потом parent (vanilla)
                merged["libraries"] = MergeJsonArrays(prop.Value, merged["libraries"]);
            }
            else if (prop.Name == "arguments" && merged.ContainsKey("arguments"))
            {
                merged["arguments"] = MergeArguments(merged["arguments"], prop.Value);
            }
            else
            {
                merged[prop.Name] = prop.Value;
            }
        }

        // Сериализуем обратно в JsonElement
        var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in merged)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        ms.Position = 0;
        var mergedDoc = JsonDocument.Parse(ms);
        return mergedDoc.RootElement.Clone();
    }

    private JsonElement MergeJsonArrays(JsonElement first, JsonElement second)
    {
        var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            foreach (var item in first.EnumerateArray()) item.WriteTo(writer);
            foreach (var item in second.EnumerateArray()) item.WriteTo(writer);
            writer.WriteEndArray();
        }
        ms.Position = 0;
        return JsonDocument.Parse(ms).RootElement.Clone();
    }

    private JsonElement MergeArguments(JsonElement parent, JsonElement child)
    {
        var merged = new Dictionary<string, JsonElement>();

        if (parent.TryGetProperty("game", out var pg))
            merged["game"] = pg;
        if (parent.TryGetProperty("jvm", out var pj))
            merged["jvm"] = pj;

        if (child.TryGetProperty("game", out var cg))
        {
            merged["game"] = merged.ContainsKey("game")
                ? MergeJsonArrays(merged["game"], cg)
                : cg;
        }
        if (child.TryGetProperty("jvm", out var cj))
        {
            merged["jvm"] = merged.ContainsKey("jvm")
                ? MergeJsonArrays(merged["jvm"], cj)
                : cj;
        }

        var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in merged)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        ms.Position = 0;
        return JsonDocument.Parse(ms).RootElement.Clone();
    }

    /// <summary>
    /// Строим classpath из объединённого JSON (без ShouldIncludeLibrary — Fabric libs идут без rules)
    /// </summary>
    private string BuildClasspathFromMerged(JsonElement root, string versionJar)
    {
        var libraries = new List<string>();
        var libsDir = Path.Combine(_baseGameDir, "libraries");
        var separator = OperatingSystem.IsWindows() ? ";" : ":";
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("libraries", out var libs))
        {
            foreach (var lib in libs.EnumerateArray())
            {
                if (!ShouldIncludeLibrary(lib)) continue;

                string? libPath = null;

                // Стиль Mojang: downloads.artifact.path
                if (lib.TryGetProperty("downloads", out var downloads) &&
                    downloads.TryGetProperty("artifact", out var artifact) &&
                    artifact.TryGetProperty("path", out var pathElem))
                {
                    libPath = Path.Combine(libsDir,
                        pathElem.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                }
                // Стиль Fabric: только name
                else if (lib.TryGetProperty("name", out var nameElem))
                {
                    var name = nameElem.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        var parts = name.Split(':');
                        if (parts.Length >= 3)
                        {
                            var group = parts[0].Replace('.', '/');
                            var art = parts[1];
                            var ver = parts[2];
                            var rel = $"{group}/{art}/{ver}/{art}-{ver}.jar";
                            libPath = Path.Combine(libsDir,
                                rel.Replace('/', Path.DirectorySeparatorChar));
                        }
                    }
                }

                if (libPath != null)
                {
                    if (File.Exists(libPath))
                    {
                        if (addedPaths.Add(libPath))
                            libraries.Add(libPath);
                    }
                    else
                    {
                        Logger.Warn($"Library missing at: {libPath}");
                    }
                }
            }
        }

        libraries.Add(versionJar);
        return string.Join(separator, libraries);
    }

    private Dictionary<string, string> BuildReplacements(
        LaunchSettings settings, JsonElement root, string classpath, string nativesDir)
    {
        var assetIndexId = root.TryGetProperty("assetIndex", out var ai)
            ? ai.GetProperty("id").GetString() ?? "legacy"
            : "legacy";

        var assetsDir = Path.Combine(_baseGameDir, "assets");

        return new Dictionary<string, string>
        {
            ["auth_player_name"] = settings.Username,
            ["auth_uuid"] = settings.Uuid,
            ["auth_access_token"] = settings.AccessToken ?? "0",
            ["auth_session"] = settings.AccessToken ?? "0",
            ["auth_xuid"] = "0",
            ["clientid"] = "minecraft-launcher",
            ["user_type"] = "msa",
            ["user_properties"] = "{}",

            ["version_name"] = settings.VersionId,
            ["version_type"] = "release",

            // Игровая папка = профиль (там будут saves, mods, config, ...)
            ["game_directory"] = _profileGameDir.TrimEnd('\\', '/'),
            // Ассеты — общие (из base)
            ["assets_root"] = assetsDir.TrimEnd('\\', '/'),
            ["game_assets"] = assetsDir.TrimEnd('\\', '/'),
            ["assets_index_name"] = assetIndexId,

            ["resolution_width"] = settings.Width.ToString(),
            ["resolution_height"] = settings.Height.ToString(),

            ["natives_directory"] = nativesDir.TrimEnd('\\', '/'),
            ["launcher_name"] = "MinecraftLauncher",
            ["launcher_version"] = "1.0",
            ["classpath"] = classpath,
            ["classpath_separator"] = OperatingSystem.IsWindows() ? ";" : ":",
            // Библиотеки — общие
            ["library_directory"] = Path.Combine(_baseGameDir, "libraries").TrimEnd('\\', '/'),
        };
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.Contains(' ') && !arg.StartsWith("\""))
            return $"\"{arg}\"";
        return arg;
    }

    private static string ApplyReplacements(string value, Dictionary<string, string> repl)
    {
        foreach (var (key, val) in repl)
            value = value.Replace($"${{{key}}}", val);
        return value;
    }

    private List<string> BuildJvmArguments(
        JsonElement root, LaunchSettings settings, Dictionary<string, string> repl)
    {
        var args = new List<string>
        {
            $"-Xms{settings.MinRam}M",
            $"-Xmx{settings.MaxRam}M"
        };

        if (root.TryGetProperty("arguments", out var argsElem) &&
            argsElem.TryGetProperty("jvm", out var jvmArgs))
        {
            foreach (var arg in jvmArgs.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                    args.Add(QuoteIfNeeded(ApplyReplacements(arg.GetString()!, repl)));
                else if (arg.ValueKind == JsonValueKind.Object)
                {
                    if (!CheckRules(arg)) continue;
                    if (arg.TryGetProperty("value", out var val))
                    {
                        if (val.ValueKind == JsonValueKind.String)
                            args.Add(QuoteIfNeeded(ApplyReplacements(val.GetString()!, repl)));
                        else if (val.ValueKind == JsonValueKind.Array)
                            foreach (var v in val.EnumerateArray())
                                args.Add(QuoteIfNeeded(ApplyReplacements(v.GetString()!, repl)));
                    }
                }
            }
        }
        else
        {
            args.Add(QuoteIfNeeded($"-Djava.library.path={repl["natives_directory"]}"));
            args.Add("-cp");
            args.Add(QuoteIfNeeded(repl["classpath"]));
        }

        if (!string.IsNullOrEmpty(settings.ExtraJvmArgs))
            args.Add(settings.ExtraJvmArgs);

        return args;
    }

    private List<string> BuildGameArguments(
        JsonElement root, LaunchSettings settings, Dictionary<string, string> repl)
    {
        var args = new List<string>();

        if (root.TryGetProperty("arguments", out var argsElem) &&
            argsElem.TryGetProperty("game", out var gameArgs))
        {
            foreach (var arg in gameArgs.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                    args.Add(QuoteIfNeeded(ApplyReplacements(arg.GetString()!, repl)));
                else if (arg.ValueKind == JsonValueKind.Object)
                {
                    if (!CheckRules(arg)) continue;
                    if (arg.TryGetProperty("value", out var val))
                    {
                        if (val.ValueKind == JsonValueKind.String)
                            args.Add(QuoteIfNeeded(ApplyReplacements(val.GetString()!, repl)));
                        else if (val.ValueKind == JsonValueKind.Array)
                            foreach (var v in val.EnumerateArray())
                                args.Add(QuoteIfNeeded(ApplyReplacements(v.GetString()!, repl)));
                    }
                }
            }
        }
        else if (root.TryGetProperty("minecraftArguments", out var oldArgs))
        {
            var argStr = ApplyReplacements(oldArgs.GetString()!, repl);
            args.AddRange(argStr.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return args;
    }

    private bool CheckRules(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules)) return true;

        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.GetProperty("action").GetString();
            bool matches = true;

            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name))
                {
                    var osName = name.GetString();
                    var currentOs = OperatingSystem.IsWindows() ? "windows"
                        : OperatingSystem.IsLinux() ? "linux" : "osx";
                    if (osName != currentOs) matches = false;
                }
                if (os.TryGetProperty("arch", out var arch))
                {
                    var archName = arch.GetString();
                    var currentArch = System.Runtime.InteropServices.RuntimeInformation
                        .OSArchitecture.ToString().ToLower();
                    if (archName != null && !currentArch.Contains(archName))
                        matches = false;
                }
            }

            if (rule.TryGetProperty("features", out _))
                matches = false;

            if (matches) allowed = action == "allow";
        }
        return allowed;
    }

    private string BuildClasspath(JsonElement root, string versionJar)
    {
        var libraries = new List<string>();
        var libsDir = Path.Combine(_baseGameDir, "libraries");
        var separator = OperatingSystem.IsWindows() ? ";" : ":";

        if (root.TryGetProperty("libraries", out var libs))
        {
            foreach (var lib in libs.EnumerateArray())
            {
                if (!ShouldIncludeLibrary(lib)) continue;

                if (lib.TryGetProperty("downloads", out var downloads) &&
                    downloads.TryGetProperty("artifact", out var artifact) &&
                    artifact.TryGetProperty("path", out var path))
                {
                    var libPath = Path.Combine(libsDir,
                        path.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(libPath))
                        libraries.Add(libPath);
                    else
                        Logger.Warn($"Library file missing: {libPath}");
                }
            }
        }

        libraries.Add(versionJar);
        return string.Join(separator, libraries);
    }

    private bool ShouldIncludeLibrary(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules)) return true;

        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.GetProperty("action").GetString();
            bool matches = true;

            if (rule.TryGetProperty("os", out var os) &&
                os.TryGetProperty("name", out var name))
            {
                var osName = name.GetString();
                var currentOs = OperatingSystem.IsWindows() ? "windows"
                    : OperatingSystem.IsLinux() ? "linux" : "osx";
                matches = osName == currentOs;
            }

            if (matches) allowed = action == "allow";
        }
        return allowed;
    }
}
