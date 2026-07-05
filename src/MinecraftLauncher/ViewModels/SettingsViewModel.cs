using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Launcher;

namespace MinecraftLauncher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty] private string _gameDirectory = "";
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private string _javaVersion = "Не определена";
    [ObservableProperty] private int _defaultMinRam = 1024;
    [ObservableProperty] private int _defaultMaxRam = 4096;
    [ObservableProperty] private bool _showSnapshots;
    [ObservableProperty] private bool _closeLauncherOnGameStart;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isDetectingJava;

    public event Action? SettingsSaved;

    public SettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsManager.Settings;
        GameDirectory = s.GameDirectory;
        JavaPath = s.JavaPath;
        DefaultMinRam = s.DefaultMinRam;
        DefaultMaxRam = s.DefaultMaxRam;
        ShowSnapshots = s.ShowSnapshots;
        CloseLauncherOnGameStart = s.CloseLauncherOnGameStart;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var s = _settingsManager.Settings;
        s.GameDirectory = GameDirectory;
        s.JavaPath = JavaPath;
        s.DefaultMinRam = DefaultMinRam;
        s.DefaultMaxRam = DefaultMaxRam;
        s.ShowSnapshots = ShowSnapshots;
        s.CloseLauncherOnGameStart = CloseLauncherOnGameStart;

        await _settingsManager.SaveAsync();
        StatusText = $"✓ Сохранено в {DateTime.Now:HH:mm:ss}";
        SettingsSaved?.Invoke();
    }

    [RelayCommand]
    public async Task DetectJavaAsync()
    {
        IsDetectingJava = true;
        StatusText = "Поиск Java...";
        try
        {
            var java = await JavaFinder.FindJavaAsync();
            if (java != null)
            {
                JavaPath = java;
                JavaVersion = await JavaFinder.GetJavaVersionAsync(java) ?? "Неизвестно";
                StatusText = $"✓ Java найдена";
            }
            else
            {
                JavaVersion = "Не найдена";
                StatusText = "✗ Java не найдена. Укажите путь вручную.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"✗ Ошибка: {ex.Message}";
        }
        IsDetectingJava = false;
    }

    [RelayCommand]
    public void ResetToDefault()
    {
        GameDirectory = AppSettings.GetDefaultGameDir();
        JavaPath = null;
        DefaultMinRam = 1024;
        DefaultMaxRam = 4096;
        ShowSnapshots = false;
        CloseLauncherOnGameStart = false;
        StatusText = "↺ Сброшено (нажмите «Сохранить»)";
    }

    partial void OnGameDirectoryChanged(string value)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value) && !Directory.Exists(value))
                Directory.CreateDirectory(value);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠ {ex.Message}";
        }
    }
}