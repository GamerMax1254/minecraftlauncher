using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Logging;

namespace MinecraftLauncher.ViewModels.Pages;

public partial class LogsPageViewModel : PageViewModelBase
{
    public override string Title => "Логи";
    public override string Icon => "📋";

    [ObservableProperty] private string _selectedLogFile = "";
    [ObservableProperty] private string _logContent = "";
    [ObservableProperty] private string _statusText = "";

    public ObservableCollection<string> LogFiles { get; } = new();

    public LogsPageViewModel() { }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        RefreshLogsList();
    }

    [RelayCommand]
    private void RefreshLogsList()
    {
        LogFiles.Clear();
        try
        {
            if (!Directory.Exists(Logger.LogDirectory))
            {
                StatusText = "Папка логов пуста";
                return;
            }

            var files = Directory.GetFiles(Logger.LogDirectory, "*.log")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToList();

            foreach (var f in files)
                if (f != null) LogFiles.Add(f);

            if (LogFiles.Count > 0)
                SelectedLogFile = LogFiles[0];

            StatusText = $"Найдено {LogFiles.Count} лог-файлов";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    partial void OnSelectedLogFileChanged(string value)
    {
        LoadLogContent();
    }

    private void LoadLogContent()
    {
        if (string.IsNullOrEmpty(SelectedLogFile))
        {
            LogContent = "";
            return;
        }

        try
        {
            var path = Path.Combine(Logger.LogDirectory, SelectedLogFile);
            if (!File.Exists(path))
            {
                LogContent = "Файл не найден";
                return;
            }
            // Читаем с шаринг-разрешением на запись (файл может быть открыт логгером)
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            LogContent = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            LogContent = $"Не удалось прочитать файл: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            if (!Directory.Exists(Logger.LogDirectory))
                Directory.CreateDirectory(Logger.LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Logger.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearAllLogs()
    {
        try
        {
            foreach (var f in LogFiles.ToList())
            {
                var path = Path.Combine(Logger.LogDirectory, f);
                try { File.Delete(path); } catch { }
            }
            RefreshLogsList();
            StatusText = "✓ Логи очищены";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }
}