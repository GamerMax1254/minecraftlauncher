// src/MinecraftLauncher.Core/Launcher/GameLauncher.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Launcher;

public class GameLauncher
{
    private readonly string _gameDir;

    public event Action<string>? OnLog;
    public event Action<int>? OnGameExited;

    public GameLauncher(string gameDir)
    {
        _gameDir = gameDir;
    }

    public async Task<Process> LaunchAsync(LaunchSettings settings)
    {
        Logger.Info($"Launching Minecraft {settings.VersionId}");
        Logger.Info($"Java: {settings.JavaPath}");
        Logger.Info($"Game dir: {settings.GameDir}");
        Logger.Info($"RAM: {settings.MinRam}-{settings.MaxRam} MB");

        var versionDir = Path.Combine(_gameDir, "versions", settings.VersionId);
        var versionJson = Path.Combine(versionDir, $"{settings.VersionId}.json");
        var versionJar = Path.Combine(versionDir, $"{settings.VersionId}.jar");

        if (!File.Exists(versionJson))
            throw new FileNotFoundException($"Version JSON not found: {versionJson}");

        var json = await File.ReadAllTextAsync(versionJson);
        using var versionData = JsonDocument.Parse(json);
        var root = versionData.RootElement;

        var mainClass = root.GetProperty("mainClass").GetString()
            ?? throw new Exception("mainClass not found");

        var classpath = BuildClasspath(root, versionJar);
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);

        // Формируем подстановки для аргументов
        var replacements = BuildReplacements(settings, root, classpath, nativesDir);

        // JVM аргументы
        var jvmArgs = BuildJvmArguments(root, settings, replacements);

        // Game аргументы
        var gameArgs = BuildGameArguments(root, settings, replacements);

        var fullArgs = new StringBuilder();
        fullArgs.Append(string.Join(" ", jvmArgs));
        fullArgs.Append(' ').Append(mainClass);
        fullArgs.Append(' ').Append(string.Join(" ", gameArgs));

        var finalArgs = fullArgs.ToString();
        Logger.Info($"Command: {settings.JavaPath} {finalArgs}");

        var gameLogPath = Logger.CreateGameLogFile(settings.VersionId);
        Logger.Info($"Game log will be written to: {gameLogPath}");

        var psi = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            Arguments = finalArgs,
            WorkingDirectory = _gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };

        var logWriter = new StreamWriter(gameLogPath, append: false)
        {
            AutoFlush = true
        };

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

    // ======================================================
    //   Подстановки переменных типа ${auth_player_name}
    // ======================================================
    private Dictionary<string, string> BuildReplacements(
    LaunchSettings settings,
    JsonElement root,
    string classpath,
    string nativesDir)
    {
        var assetIndexId = root.TryGetProperty("assetIndex", out var ai)
            ? ai.GetProperty("id").GetString() ?? "legacy"
            : "legacy";

        var assetsDir = Path.Combine(_gameDir, "assets");

        return new Dictionary<string, string>
        {
            // Игрок
            ["auth_player_name"] = settings.Username,
            ["auth_uuid"] = settings.Uuid,
            ["auth_access_token"] = settings.AccessToken ?? "0",
            ["auth_session"] = settings.AccessToken ?? "0",
            ["auth_xuid"] = "0",
            ["clientid"] = "minecraft-launcher",
            ["user_type"] = "msa",
            ["user_properties"] = "{}",

            // Версия
            ["version_name"] = settings.VersionId,
            ["version_type"] = "release",

            // Пути (БЕЗ кавычек!)
            ["game_directory"] = _gameDir,
            ["assets_root"] = assetsDir,
            ["game_assets"] = assetsDir,
            ["assets_index_name"] = assetIndexId,

            // Окно
            ["resolution_width"] = settings.Width.ToString(),
            ["resolution_height"] = settings.Height.ToString(),

            // JVM (БЕЗ кавычек!)
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = "MinecraftLauncher",
            ["launcher_version"] = "1.0",
            ["classpath"] = classpath,
            ["classpath_separator"] = OperatingSystem.IsWindows() ? ";" : ":",
            ["library_directory"] = Path.Combine(_gameDir, "libraries"),
        };
    }

    private static string ApplyReplacements(string value, Dictionary<string, string> repl)
    {
        foreach (var (key, val) in repl)
            value = value.Replace($"${{{key}}}", val);
        return value;
    }

    // ======================================================
    //   JVM аргументы
    // ======================================================
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
                {
                    args.Add(QuoteIfNeeded(ApplyReplacements(arg.GetString()!, repl)));
                }
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
            // Старый формат (до 1.13)
            args.Add(QuoteIfNeeded($"-Djava.library.path={repl["natives_directory"]}"));
            args.Add("-cp");
            args.Add(QuoteIfNeeded(repl["classpath"]));
        }

        if (!string.IsNullOrEmpty(settings.ExtraJvmArgs))
            args.Add(settings.ExtraJvmArgs);

        return args;
    }

    // ======================================================
    //   Game аргументы
    // ======================================================
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
                {
                    args.Add(QuoteIfNeeded(ApplyReplacements(arg.GetString()!, repl)));
                }
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

    // ======================================================
    //   Проверка правил (OS-специфичные аргументы/либы)
    // ======================================================
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

            // features (демо, custom_resolution и т.д.) — пропускаем для простоты
            if (rule.TryGetProperty("features", out _))
                matches = false;

            if (matches)
                allowed = action == "allow";
        }
        return allowed;
    }

    // ======================================================
    //   Classpath
    // ======================================================
    private string BuildClasspath(JsonElement root, string versionJar)
    {
        var libraries = new List<string>();
        var libsDir = Path.Combine(_gameDir, "libraries");
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

    /// <summary>
    /// Оборачивает аргумент в кавычки только если в нём есть пробелы
    /// </summary>
    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.Contains(' ') && !arg.StartsWith("\""))
            return $"\"{arg}\"";
        return arg;
    }
}