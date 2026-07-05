using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MinecraftLauncher.Core.Launcher;

public static class JavaFinder
{
    public static async Task<string?> FindJavaAsync()
    {
        // 1. Проверяем JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaExe = Path.Combine(javaHome, "bin",
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "java.exe" : "java");
            if (File.Exists(javaExe)) return javaExe;
        }

        // 2. Проверяем PATH
        var pathJava = await TryGetJavaFromPath();
        if (pathJava != null) return pathJava;

        // 3. Типичные пути на Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = new[]
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var pf in programFiles)
            {
                var javaDir = Path.Combine(pf, "Java");
                if (!Directory.Exists(javaDir)) continue;

                var javaDirs = Directory.GetDirectories(javaDir)
                    .OrderByDescending(d => d);

                foreach (var dir in javaDirs)
                {
                    var exe = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }

        return null;
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

            return output.Split('\n').FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGetJavaFromPath()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? "java" : null;
        }
        catch
        {
            return null;
        }
    }
}