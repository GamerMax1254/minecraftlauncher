using System;
using System.Collections.ObjectModel;
using System.IO;
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

namespace MinecraftLauncher.ViewModels.Pages;

public partial class HomePageViewModel : PageViewModelBase
{
    public override string Title => "Главная";
    public override string Icon => "🏠";

    private readonly HttpClient _http = new();
    private readonly SettingsManager _settings;
    private VersionManager _versionManager;
    private VersionInstaller _versionInstaller;
    private GameLauncher _gameLauncher;

    [ObservableProperty] private string _username = Environment.UserName;
    [ObservableProperty] private string _statusText = "Готов к запуску";
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressDetail = "";
    [ObservableProperty] private MinecraftVersion? _selectedVersion;
    [ObservableProperty] private string _lastPlayedText = "Никогда";

    public ObservableCollection<MinecraftVersion> QuickVersions { get; } = new();

    public HomePageViewModel(SettingsManager settings)
    {
        _settings = settings;
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();

        Username = string.IsNullOrEmpty(_settings.Settings.LastUsername)
            ? Environment.UserName
            : _settings.Settings.LastUsername;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = LoadQuickVersionsAsync();
    }

    public void ReloadForGameDir()
    {
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();
    }

    public void SetVersionAndLaunch(MinecraftVersion version)
    {
        // Добавляем в список если её там нет
        if (!QuickVersions.Any(v => v.Id == version.Id))
            QuickVersions.Insert(0, version);
        SelectedVersion = version;
    }

    private void SubscribeLauncher()
    {
        _gameLauncher.OnGameExited += code =>
        {
            StatusText = $"Игра завершена (код {code})";
            IsLaunching = false;
            ShowProgress = false;
        };
    }

    private async Task LoadQuickVersionsAsync()
    {
        if (QuickVersions.Count > 0) return; // уже загружено
        try
        {
            var versions = await _versionManager.GetAvailableVersionsAsync(false);
            foreach (var v in versions.Take(15))
                QuickVersions.Add(v);
            SelectedVersion ??= QuickVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load quick versions", ex);
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
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
        ProgressValue = 0;

        try
        {
            // Java
            string? javaPath = null;
            var selectedId = _settings.Settings.SelectedJavaId;
            if (!string.IsNullOrEmpty(selectedId))
            {
                var selected = _settings.Settings.JavaInstallations
                    .FirstOrDefault(j => j.Id == selectedId);
                if (selected != null && File.Exists(selected.Path))
                    javaPath = selected.Path;
            }
            javaPath ??= await JavaFinder.FindJavaAsync();
            if (string.IsNullOrEmpty(javaPath))
                throw new Exception("Java не найдена. Добавьте её в Настройках.");

            // Установка
            var progress = new Progress<(string Stage, int Done, int Total)>(p =>
            {
                StatusText = p.Stage;
                ProgressValue = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                ProgressDetail = p.Total > 0 ? $"{p.Done} / {p.Total} файлов" : "";
            });

            await _versionInstaller.InstallAsync(SelectedVersion, progress);

            // Запуск
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

            // Сохраняем ник
            _settings.Settings.LastUsername = Username;
            _settings.Save();

            LastPlayedText = "Только что";
            StatusText = "Игра запущена ✓";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            Logger.Error("Launch failed", ex);
            IsLaunching = false;
            ShowProgress = false;
        }
    }
}