using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Download;
using MinecraftLauncher.Core.Download.Modded;
using MinecraftLauncher.Core.Launcher;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

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
            StatusText = "Создайте профиль во вкладке «Профили»!";
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

            var progress = new Progress<(string Stage, int Done, int Total)>(p =>
            {
                StatusText = p.Stage;
                ProgressValue = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                ProgressDetail = p.Total > 0 ? $"{p.Done} / {p.Total} файлов" : "";
            });

            // 1. Устанавливаем ванильную версию (всегда нужна)
            var vanillaDir = Path.Combine(_settings.Settings.GameDirectory,
                "versions", SelectedProfile.MinecraftVersion);
            if (!Directory.Exists(vanillaDir))
            {
                var vm = new VersionManager(_http, _settings.Settings.GameDirectory);
                var allVersions = await vm.GetAvailableVersionsAsync(includeSnapshots: true);
                var vanilla = allVersions.FirstOrDefault(v => v.Id == SelectedProfile.MinecraftVersion);
                if (vanilla == null)
                    throw new Exception($"Версия {SelectedProfile.MinecraftVersion} не найдена");

                await _versionInstaller.InstallAsync(vanilla, progress);
            }
            else
            {
                // Всё равно проверим ассеты/либы (может не хватать чего-то)
                var vm = new VersionManager(_http, _settings.Settings.GameDirectory);
                var allVersions = await vm.GetAvailableVersionsAsync(includeSnapshots: true);
                var vanilla = allVersions.FirstOrDefault(v => v.Id == SelectedProfile.MinecraftVersion);
                if (vanilla != null)
                    await _versionInstaller.InstallAsync(vanilla, progress);
            }

            // 2. Устанавливаем модовый загрузчик если нужно
            if (SelectedProfile.Loader != ModLoader.Vanilla)
            {
                StatusText = $"Установка {SelectedProfile.Loader}...";

                switch (SelectedProfile.Loader)
                {
                    case ModLoader.Fabric:
                        var fabric = new FabricInstaller(_http, _settings.Settings.GameDirectory);
                        await fabric.InstallAsync(
                            SelectedProfile.MinecraftVersion,
                            SelectedProfile.LoaderVersion!,
                            progress);
                        break;

                    case ModLoader.Quilt:
                        var quilt = new QuiltInstaller(_http, _settings.Settings.GameDirectory);
                        await quilt.InstallAsync(
                            SelectedProfile.MinecraftVersion,
                            SelectedProfile.LoaderVersion!,
                            progress);
                        break;

                    // Forge/NeoForge — в следующем сообщении
                    default:
                        throw new Exception($"{SelectedProfile.Loader} пока не реализован");
                }
            }

            StatusText = "Запуск игры...";
            ProgressValue = 100;

            // Папка профиля
            var profileGameDir = SelectedProfile.GetGameDirectory(_settings.Settings.GameDirectory);
            Directory.CreateDirectory(profileGameDir);

            _gameLauncher = new GameLauncher(
                _settings.Settings.GameDirectory,
                profileGameDir);
            SubscribeLauncher();

            // Используем имя папки версии (для Fabric = fabric-loader-x-1.21.4)
            var launchSettings = new LaunchSettings
            {
                GameDir = profileGameDir,
                JavaPath = javaPath,
                VersionId = SelectedProfile.GetVersionFolderName(),   // ← важно!
                Username = Username,
                Uuid = Guid.NewGuid().ToString("N"),
                AccessToken = "0",
                MaxRam = SelectedProfile.OverrideMaxRam ?? _settings.Settings.DefaultMaxRam,
                MinRam = SelectedProfile.OverrideMinRam ?? _settings.Settings.DefaultMinRam,
                Width = 1280,
                Height = 720
            };

            await _gameLauncher.LaunchAsync(launchSettings);

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
