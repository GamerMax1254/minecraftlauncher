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
    private readonly ProfileManager _profiles;
    private VersionInstaller _versionInstaller;
    private GameLauncher _gameLauncher;

    [ObservableProperty] private string _username = Environment.UserName;
    [ObservableProperty] private string _statusText = "Готов к запуску";
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressDetail = "";
    [ObservableProperty] private GameProfile? _selectedProfile;
    [ObservableProperty] private string _lastPlayedText = "Никогда";
    [ObservableProperty] private bool _hasProfiles;

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public HomePageViewModel(SettingsManager settings, ProfileManager profiles)
    {
        _settings = settings;
        _profiles = profiles;
        var gameDir = _settings.Settings.GameDirectory;
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();

        Username = string.IsNullOrEmpty(_settings.Settings.LastUsername)
            ? Environment.UserName
            : _settings.Settings.LastUsername;

        RefreshProfiles();
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        var currentId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var p in _profiles.Profiles.OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt))
            Profiles.Add(p);

        HasProfiles = Profiles.Count > 0;

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == currentId)
                          ?? Profiles.FirstOrDefault();

        if (SelectedProfile?.LastPlayedAt is DateTime dt)
            LastPlayedText = FormatRelativeTime(dt);
        else
            LastPlayedText = "Никогда";
    }

    public void ReloadForGameDir()
    {
        var gameDir = _settings.Settings.GameDirectory;
        _versionInstaller = new VersionInstaller(_http, gameDir);
        _gameLauncher = new GameLauncher(gameDir);
        SubscribeLauncher();
    }

    public void SetProfileAndLaunch(GameProfile profile)
    {
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
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

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Создайте профиль во вкладке «Версии»!";
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
            // Java (с учётом override профиля)
            var javaId = SelectedProfile.OverrideJavaId ?? _settings.Settings.SelectedJavaId;
            string? javaPath = null;
            if (!string.IsNullOrEmpty(javaId))
            {
                var selected = _settings.Settings.JavaInstallations
                    .FirstOrDefault(j => j.Id == javaId);
                if (selected != null && File.Exists(selected.Path))
                    javaPath = selected.Path;
            }
            javaPath ??= await JavaFinder.FindJavaAsync();
            if (string.IsNullOrEmpty(javaPath))
                throw new Exception("Java не найдена. Добавьте её в Настройках.");

            // TODO: для Fabric/Forge — установка loader'а поверх ванилы
            // Сейчас работает только Vanilla

            var version = new MinecraftVersion
            {
                Id = SelectedProfile.MinecraftVersion,
                Type = "release",
                Url = "" // не используется, если версия уже установлена
            };

            // Установка (если ещё не скачано)
            var progress = new Progress<(string Stage, int Done, int Total)>(p =>
            {
                StatusText = p.Stage;
                ProgressValue = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                ProgressDetail = p.Total > 0 ? $"{p.Done} / {p.Total} файлов" : "";
            });

            // Получаем URL версии из манифеста (если это первый запуск)
            var versionsDir = Path.Combine(_settings.Settings.GameDirectory, "versions", SelectedProfile.MinecraftVersion);
            if (!Directory.Exists(versionsDir))
            {
                var vm = new VersionManager(_http, _settings.Settings.GameDirectory);
                var allVersions = await vm.GetAvailableVersionsAsync(includeSnapshots: true);
                var real = allVersions.FirstOrDefault(v => v.Id == SelectedProfile.MinecraftVersion);
                if (real != null) version = real;
            }

            await _versionInstaller.InstallAsync(version, progress);

            StatusText = "Запуск игры...";
            ProgressValue = 100;
            // Получаем game dir для профиля (с учётом изоляции)
            var profileGameDir = SelectedProfile.GetGameDirectory(_settings.Settings.GameDirectory);
            Directory.CreateDirectory(profileGameDir);

            // Создаём launcher с раздельными путями
            _gameLauncher = new GameLauncher(
                _settings.Settings.GameDirectory,  // base — там versions/libraries/assets
                profileGameDir                      // profile — там saves/mods/config
            );
            SubscribeLauncher();

            var launchSettings = new LaunchSettings
            {
                GameDir = profileGameDir,   // тоже указываем сюда для совместимости
                JavaPath = javaPath,
                VersionId = SelectedProfile.MinecraftVersion,
                Username = Username,
                Uuid = Guid.NewGuid().ToString("N"),
                AccessToken = "0",
                MaxRam = SelectedProfile.OverrideMaxRam ?? _settings.Settings.DefaultMaxRam,
                MinRam = SelectedProfile.OverrideMinRam ?? _settings.Settings.DefaultMinRam,
                Width = 1280,
                Height = 720
            };

            await _gameLauncher.LaunchAsync(launchSettings);

            // Статистика профиля
            SelectedProfile.LastPlayedAt = DateTime.UtcNow;
            SelectedProfile.PlayCount++;
            _profiles.Update(SelectedProfile);

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

    private static string FormatRelativeTime(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "Только что";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} мин назад";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} ч назад";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} дн назад";
        return dt.ToLocalTime().ToString("dd.MM.yyyy");
    }
}
