using System.Collections.Concurrent;

namespace MinecraftLauncher.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public static class Logger
{
    private static string _logDir = "";
    private static string _mainLogPath = "";
    private static readonly BlockingCollection<string> LogQueue = new();
    private static Task? _writerTask;
    private static readonly object InitLock = new();
    private static bool _initialized;

    public static event Action<LogLevel, string>? OnLog;

    public static void Initialize(string logDirectory)
    {
        lock (InitLock)
        {
            if (_initialized) return;

            _logDir = logDirectory;
            Directory.CreateDirectory(_logDir);

            // Ротация: создаём файл на каждый запуск
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _mainLogPath = Path.Combine(_logDir, $"launcher_{timestamp}.log");

            // Оставляем только последние 10 логов
            CleanupOldLogs("launcher_*.log", 10);

            // Поток-писатель
            _writerTask = Task.Run(WriterLoop);
            _initialized = true;

            Info($"===== Launcher started =====");
            Info($"OS: {Environment.OSVersion}");
            Info($"Runtime: {Environment.Version}");
            Info($"Log file: {_mainLogPath}");
        }
    }

    private static void CleanupOldLogs(string pattern, int keepCount)
    {
        try
        {
            var files = Directory.GetFiles(_logDir, pattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(keepCount);

            foreach (var file in files)
            {
                try { file.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static async Task WriterLoop()
    {
        foreach (var line in LogQueue.GetConsumingEnumerable())
        {
            try
            {
                await File.AppendAllTextAsync(_mainLogPath, line + "\n");
            }
            catch { }
        }
    }

    public static void Log(LogLevel level, string message, Exception? ex = null)
    {
        if (!_initialized) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(7);
        var line = $"[{timestamp}] [{levelStr}] {message}";

        if (ex != null)
        {
            line += $"\n{ex}";
        }

        LogQueue.Add(line);
        OnLog?.Invoke(level, line);
    }

    public static void Debug(string msg) => Log(LogLevel.Debug, msg);
    public static void Info(string msg) => Log(LogLevel.Info, msg);
    public static void Warn(string msg) => Log(LogLevel.Warning, msg);
    public static void Error(string msg, Exception? ex = null) => Log(LogLevel.Error, msg, ex);
    public static void Fatal(string msg, Exception? ex = null) => Log(LogLevel.Fatal, msg, ex);

    /// <summary>
    /// Создаёт отдельный файл для логов игры (stdout/stderr Minecraft)
    /// </summary>
    public static string CreateGameLogFile(string versionId)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(_logDir, $"game_{versionId}_{timestamp}.log");
        CleanupOldLogs("game_*.log", 20);
        return path;
    }

    public static string LogDirectory => _logDir;

    public static void Shutdown()
    {
        LogQueue.CompleteAdding();
        _writerTask?.Wait(TimeSpan.FromSeconds(3));
    }
}