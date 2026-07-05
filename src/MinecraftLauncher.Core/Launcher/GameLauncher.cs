using System.Diagnostics;
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
        var versionData = JsonDocument.Parse(json);

        var mainClass = versionData.RootElement.GetProperty("mainClass").GetString()
            ?? throw new Exception("mainClass not found");

        var classpath = BuildClasspath(versionData, versionJar);
        var gameArgs = BuildGameArguments(versionData, settings);
        var jvmArgs = BuildJvmArguments(settings, classpath);

        var allArgs = $"{jvmArgs} {mainClass} {gameArgs}";

        Logger.Info($"Command: {settings.JavaPath} {allArgs}");

        // Отдельный файл для логов игры
        var gameLogPath = Logger.CreateGameLogFile(settings.VersionId);
        Logger.Info($"Game log will be written to: {gameLogPath}");

        var psi = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            Arguments = allArgs,
            WorkingDirectory = _gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };

        // Пишем логи игры и в UI, и в файл
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

    private string BuildJvmArguments(LaunchSettings settings, string classpath)
    {
        var nativesDir = Path.Combine(
            _gameDir, "versions", settings.VersionId, "natives");

        var args = new List<string>
        {
            $"-Xms{settings.MinRam}M",
            $"-Xmx{settings.MaxRam}M",
            $"-Djava.library.path=\"{nativesDir}\"",
            "-cp",
            $"\"{classpath}\""
        };

        if (!string.IsNullOrEmpty(settings.ExtraJvmArgs))
            args.Add(settings.ExtraJvmArgs);

        return string.Join(" ", args);
    }

    private string BuildGameArguments(JsonDocument versionData, LaunchSettings settings)
    {
        var args = new List<string>
        {
            "--username", settings.Username,
            "--version", settings.VersionId,
            "--gameDir", $"\"{_gameDir}\"",
            "--assetsDir", $"\"{Path.Combine(_gameDir, "assets")}\"",
            "--assetIndex", GetAssetIndex(versionData),
            "--uuid", settings.Uuid,
            "--accessToken", settings.AccessToken ?? "0",
            "--userType", "msa",
            "--width", settings.Width.ToString(),
            "--height", settings.Height.ToString()
        };

        return string.Join(" ", args);
    }

    private string BuildClasspath(JsonDocument versionData, string versionJar)
    {
        var libraries = new List<string>();
        var libsDir = Path.Combine(_gameDir, "libraries");
        var separator = OperatingSystem.IsWindows() ? ";" : ":";

        if (versionData.RootElement.TryGetProperty("libraries", out var libs))
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

    private string GetAssetIndex(JsonDocument versionData) =>
        versionData.RootElement.GetProperty("assetIndex")
            .GetProperty("id").GetString() ?? "legacy";
}