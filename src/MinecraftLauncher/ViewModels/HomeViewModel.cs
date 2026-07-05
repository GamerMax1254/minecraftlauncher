using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Download;
using MinecraftLauncher.Core.Launcher;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly HttpClient _http = new();
    private readonly SettingsManager _settings;
    private VersionManager _versionManager;
    private VersionInstaller _versionInstaller;
    private GameLauncher _gameLauncher;

    [ObservableProperty] private string _username = Environment.UserName;
    [ObservableProperty] private string _statusText = "Готов к запуску";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private MinecraftVersion? _selectedVersion;
    [ObservableProperty] private string _gameLog = "";

    public ObservableCollection<MinecraftVersion> Versions { get; } = new();

    public HomeViewModel(SettingsManager settings)
    {
        _settings = settings;
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();
    }

    private void SubscribeLauncher()
    {
        _gameLauncher.OnLog += line => GameLog += line + "\n";
        _gameLauncher.OnGameExited += code =>
        {
            StatusText = $"Игра завершена (код: {code})";
            IsLaunching = false;
        };
    }

    public void ReloadForGameDir()
    {
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();
        Logger.Info($"HomeViewModel reloaded for game dir: {gameDir}");
    }

    [RelayCommand]
    public async Task LoadVersionsAsync()
    {
        StatusText = "Загрузка списка версий...";
        Logger.Info("Loading versions list");
        try
        {
            var includeSnapshots = _settings.Settings.ShowSnapshots;
            var versions = await _versionManager.GetAvailableVersionsAsync(includeSnapshots);
            Versions.Clear();
            foreach (var v in versions)
                Versions.Add(v);

            SelectedVersion = Versions.FirstOrDefault();
            StatusText = $"Загружено {Versions.Count} версий";
            Logger.Info($"Loaded {Versions.Count} versions");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            Logger.Error("Failed to load versions", ex);
        }
    }

    [RelayCommand]
    public async Task LaunchGameAsync()
    {
        if (SelectedVersion is null)
        {
            StatusText = "Выберите версию!";
            return;
        }
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusText = "Введите ник!";
            return;
        }

        IsLaunching = true;
        ShowProgress = true;
        GameLog = "";
        ProgressValue = 0;

        try
        {
            Logger.Info($"Preparing launch of {SelectedVersion.Id}");

            // 1. Ищем Java
            var javaPath = !string.IsNullOrEmpty(_settings.Settings.JavaPath)
                ? _settings.Settings.JavaPath
                : await JavaFinder.FindJavaAsync();

            if (string.IsNullOrEmpty(javaPath))
                throw new Exception("Java не найдена! Укажите путь во вкладке «Настройки».");

            // 2. Скачиваем/проверяем файлы версии
            StatusText = $"Установка {SelectedVersion.Id}...";
            var progress = new Progress<(string Stage, int Done, int Total)>(p =>
            {
                StatusText = $"{p.Stage} ({p.Done}/{p.Total})";
                ProgressValue = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
            });

            await _versionInstaller.InstallAsync(SelectedVersion, progress);

            // 3. Запускаем
            StatusText = "Запуск игры...";
            ProgressValue = 100;

            var launchSettings = new LaunchSettings
            {
                GameDir = _settings.Settings.GameDirectory,
                JavaPath = javaPath,
                VersionId = SelectedVersion.Id,
                Username = Username,
                Uuid = Guid.NewGuid().ToString("N"),
                AccessToken = "0",
                MaxRam = _settings.Settings.DefaultMaxRam,
                MinRam = _settings.Settings.DefaultMinRam,
                Width = 1280,
                Height = 720
            };

            await _gameLauncher.LaunchAsync(launchSettings);

            StatusText = "Игра запущена!";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            Logger.Error("Launch failed", ex);
            IsLaunching = false;
        }

        ShowProgress = false;
    }
}