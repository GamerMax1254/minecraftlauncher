using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Launcher;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.ViewModels.Pages;

public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string Title => "Настройки";
    public override string Icon => "⚙";

    private readonly SettingsManager _settingsManager;

    [ObservableProperty] private string _gameDirectory = "";
    [ObservableProperty] private int _defaultMinRam = 1024;
    [ObservableProperty] private int _defaultMaxRam = 4096;
    [ObservableProperty] private bool _showSnapshots;
    [ObservableProperty] private bool _closeLauncherOnGameStart;
    [ObservableProperty] private bool _showLogsPage;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isDetectingJava;

    // Java менеджер
    [ObservableProperty] private JavaInstallation? _selectedJava;
    [ObservableProperty] private string _customJavaPath = "";
    [ObservableProperty] private string _customJavaName = "";

    public ObservableCollection<JavaInstallation> JavaInstallations { get; } = new();

    public event Action? SettingsSaved;

    public int TotalSystemRamMb { get; }

    public SettingsPageViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        try
        {
            var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            TotalSystemRamMb = Math.Max((int)(totalBytes / 1024 / 1024), 4096);
        }
        catch { TotalSystemRamMb = 16384; }

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsManager.Settings;
        GameDirectory = s.GameDirectory;
        DefaultMinRam = s.DefaultMinRam;
        DefaultMaxRam = s.DefaultMaxRam;
        ShowSnapshots = s.ShowSnapshots;
        CloseLauncherOnGameStart = s.CloseLauncherOnGameStart;
        ShowLogsPage = s.ShowLogsPage;

        JavaInstallations.Clear();
        foreach (var j in s.JavaInstallations)
            JavaInstallations.Add(j);

        SelectedJava = JavaInstallations.FirstOrDefault(j => j.Id == s.SelectedJavaId)
                       ?? JavaInstallations.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _settingsManager.Settings;
        s.GameDirectory = GameDirectory.TrimEnd('\\', '/');
        s.DefaultMinRam = DefaultMinRam;
        s.DefaultMaxRam = DefaultMaxRam;
        s.ShowSnapshots = ShowSnapshots;
        s.CloseLauncherOnGameStart = CloseLauncherOnGameStart;
        s.ShowLogsPage = ShowLogsPage;
        s.JavaInstallations = JavaInstallations.ToList();
        s.SelectedJavaId = SelectedJava?.Id;
        s.JavaPath = SelectedJava?.Path;

        await _settingsManager.SaveAsync();
        StatusText = $"✓ Сохранено ({DateTime.Now:HH:mm:ss})";
        SettingsSaved?.Invoke();
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        IsDetectingJava = true;
        StatusText = "🔍 Поиск Java в системе...";
        try
        {
            var found = await JavaFinder.FindAllJavaInstallationsAsync();
            int added = 0;

            foreach (var java in found)
            {
                if (JavaInstallations.Any(j =>
                    string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                java.Name = $"JDK {java.MajorVersion} (auto)";
                JavaInstallations.Add(java);
                added++;
            }

            SelectedJava ??= JavaInstallations.FirstOrDefault();
            StatusText = added > 0
                ? $"✓ Найдено {added} новых Java"
                : $"Новых Java не найдено (всего: {JavaInstallations.Count})";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ Ошибка: {ex.Message}";
        }
        IsDetectingJava = false;
    }

    [RelayCommand]
    private async Task AddCustomJavaAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomJavaPath))
        {
            StatusText = "⚠ Укажите путь к java.exe";
            return;
        }
        if (!File.Exists(CustomJavaPath))
        {
            StatusText = "⚠ Файл не найден";
            return;
        }

        try
        {
            var version = await JavaFinder.GetJavaVersionAsync(CustomJavaPath);
            if (version == null)
            {
                StatusText = "⚠ Не удалось определить версию Java";
                return;
            }

            var java = new JavaInstallation
            {
                Path = CustomJavaPath,
                Version = version,
                MajorVersion = JavaFinder.ParseMajorVersion(version),
                Name = string.IsNullOrWhiteSpace(CustomJavaName)
                    ? $"JDK {JavaFinder.ParseMajorVersion(version)} (custom)"
                    : CustomJavaName,
                IsAutoDetected = false
            };

            JavaInstallations.Add(java);
            SelectedJava = java;
            CustomJavaPath = "";
            CustomJavaName = "";
            StatusText = $"✓ Добавлено: {java.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveJava(JavaInstallation? java)
    {
        if (java == null) return;
        JavaInstallations.Remove(java);
        if (SelectedJava == java)
            SelectedJava = JavaInstallations.FirstOrDefault();
        StatusText = $"✓ Удалено: {java.Name}";
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        GameDirectory = AppSettings.GetDefaultGameDir();
        DefaultMinRam = 1024;
        DefaultMaxRam = 4096;
        ShowSnapshots = false;
        CloseLauncherOnGameStart = false;
        StatusText = "↺ Сброшено (нажмите «Сохранить»)";
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            if (!Directory.Exists(GameDirectory))
                Directory.CreateDirectory(GameDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GameDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = $"✗ {ex.Message}";
        }
    }

    partial void OnGameDirectoryChanged(string value)
    {
        try
        {
            var trimmed = value.TrimEnd('\\', '/');
            if (!string.IsNullOrWhiteSpace(trimmed) && !Directory.Exists(trimmed))
                Directory.CreateDirectory(trimmed);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠ {ex.Message}";
        }
    }

    partial void OnDefaultMinRamChanged(int value)
    {
        if (value > DefaultMaxRam) DefaultMaxRam = value;
        OnPropertyChanged(nameof(DefaultMinRamGb));
    }

    partial void OnDefaultMaxRamChanged(int value)
    {
        if (value < DefaultMinRam) DefaultMinRam = value;
        OnPropertyChanged(nameof(DefaultMaxRamGb));
    }

    public double DefaultMinRamGb
    {
        get => DefaultMinRam / 1024.0;
        set
        {
            var mb = (int)Math.Round(value * 1024);
            if (mb != DefaultMinRam)
                DefaultMinRam = mb;
        }
    }

    public double DefaultMaxRamGb
    {
        get => DefaultMaxRam / 1024.0;
        set
        {
            var mb = (int)Math.Round(value * 1024);
            if (mb != DefaultMaxRam)
                DefaultMaxRam = mb;
        }
    }

    public double TotalSystemRamGb => TotalSystemRamMb / 1024.0;
}