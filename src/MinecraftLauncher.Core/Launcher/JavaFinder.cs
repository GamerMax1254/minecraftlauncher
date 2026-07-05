using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.Core.Launcher;

public static class JavaFinder
{
    /// <summary>
    /// Возвращает первую найденную Java (для быстрого поиска)
    /// </summary>
    public static async Task<string?> FindJavaAsync()
    {
        var all = await FindAllJavaInstallationsAsync();
        return all.OrderByDescending(j => j.MajorVersion)
                  .FirstOrDefault()?.Path;
    }

    /// <summary>
    /// Ищет все установленные Java в системе
    /// </summary>
    public static async Task<List<JavaInstallation>> FindAllJavaInstallationsAsync()
    {
        var results = new List<JavaInstallation>();
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
            TryAddJava(Path.Combine(javaHome, "bin", GetJavaExeName()), results, foundPaths, "JAVA_HOME");

        // 2. PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                var exe = Path.Combine(p.Trim(), GetJavaExeName());
                TryAddJava(exe, results, foundPaths, "PATH");
            }
            catch { }
        }

        // 3. Типичные Windows пути
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var searchDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Java",
                @"C:\Program Files\Zulu",
                @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files (x86)\Java",
            };

            foreach (var baseDir in searchDirs.Distinct())
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;

                // Ищем в подпапках Java
                var candidates = new List<string>();
                try
                {
                    candidates.AddRange(Directory.GetDirectories(baseDir, "*", SearchOption.TopDirectoryOnly));
                }
                catch { continue; }

                foreach (var dir in candidates)
                {
                    var exe = Path.Combine(dir, "bin", "java.exe");
                    TryAddJava(exe, results, foundPaths, Path.GetFileName(dir));
                }
            }
        }
        else
        {
            // Linux/macOS типичные пути
            var unixPaths = new[]
            {
                "/usr/lib/jvm",
                "/usr/java",
                "/opt/java",
                "/Library/Java/JavaVirtualMachines"
            };

            foreach (var baseDir in unixPaths)
            {
                if (!Directory.Exists(baseDir)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(baseDir))
                    {
                        var exe = Path.Combine(dir, "bin", "java");
                        TryAddJava(exe, results, foundPaths, Path.GetFileName(dir));
                        // macOS
                        exe = Path.Combine(dir, "Contents", "Home", "bin", "java");
                        TryAddJava(exe, results, foundPaths, Path.GetFileName(dir));
                    }
                }
                catch { }
            }
        }

        // Определяем версии
        foreach (var java in results.ToList())
        {
            var version = await GetJavaVersionAsync(java.Path);
            if (version != null)
            {
                java.Version = version;
                java.MajorVersion = ParseMajorVersion(version);
            }
            else
            {
                results.Remove(java);
            }
        }

        return results;
    }

    private static void TryAddJava(string path, List<JavaInstallation> list,
                                    HashSet<string> found, string hint)
    {
        if (!File.Exists(path)) return;

        var normalized = Path.GetFullPath(path);
        if (!found.Add(normalized)) return;

        list.Add(new JavaInstallation
        {
            Path = normalized,
            Name = $"Java ({hint})",
            IsAutoDetected = true
        });
    }

    public static async Task<string?> GetJavaVersionAsync(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Пример: openjdk version "21.0.2" 2024-01-16
            var match = Regex.Match(output, @"version\s+""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    public static int ParseMajorVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return 0;

        // "1.8.0_301" → 8
        if (version.StartsWith("1."))
        {
            var parts = version.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
                return minor;
        }
        // "21.0.2" → 21
        var mainPart = version.Split('.', '-', '_')[0];
        return int.TryParse(mainPart, out var major) ? major : 0;
    }

    private static string GetJavaExeName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java";
}