using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Download;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.ViewModels.Pages;

public partial class VersionsPageViewModel : PageViewModelBase
{
    public override string Title => "Версии";
    public override string Icon => "📦";

    private readonly HttpClient _http = new();
    private readonly SettingsManager _settings;
    private VersionManager _versionManager;
    private VersionInstaller _versionInstaller;
    private List<MinecraftVersion> _allVersions = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showRelease = true;
    [ObservableProperty] private bool _showSnapshot = false;
    [ObservableProperty] private bool _showOldBeta = false;
    [ObservableProperty] private bool _showOldAlpha = false;
    [ObservableProperty] private bool _showInstalledOnly;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private VersionListItem? _selectedVersion;

    public ObservableCollection<VersionListItem> Versions { get; } = new();

    public event Action<MinecraftVersion>? LaunchRequested;

    public VersionsPageViewModel(SettingsManager settings)
    {
        _settings = settings;
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        if (_allVersions.Count == 0)
            _ = LoadVersionsAsync();
    }

    public void ReloadForGameDir()
    {
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);
        _versionInstaller = new VersionInstaller(_http, gameDir);
        RefreshList();
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        IsLoading = true;
        StatusText = "Загрузка списка версий...";
        try
        {
            _allVersions = await _versionManager.GetAvailableVersionsAsync(includeSnapshots: true);
            RefreshList();
            StatusText = $"Загружено {_allVersions.Count} версий";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            Logger.Error("Failed to load versions", ex);
        }
        IsLoading = false;
    }

    [RelayCommand]
    private async Task InstallAsync(VersionListItem? item)
    {
        if (item == null) return;

        item.IsInstalling = true;
        item.Progress = 0;
        item.ProgressText = "Начинаем...";
        StatusText = $"Установка {item.Version.Id}...";

        try
        {
            var progress = new Progress<(string Stage, int Done, int Total)>(p =>
            {
                item.Progress = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                item.ProgressText = $"{p.Stage} ({p.Done}/{p.Total})";
            });

            await _versionInstaller.InstallAsync(item.Version, progress);

            item.IsInstalled = true;
            item.IsInstalling = false;
            item.Progress = 100;
            item.ProgressText = "";
            StatusText = $"✓ {item.Version.Id} установлена";
        }
        catch (Exception ex)
        {
            item.IsInstalling = false;
            item.ProgressText = $"Ошибка: {ex.Message}";
            StatusText = $"✗ Ошибка: {ex.Message}";
            Logger.Error($"Failed to install {item.Version.Id}", ex);
        }
    }

    [RelayCommand]
    private void Launch(VersionListItem? item)
    {
        if (item == null) return;
        LaunchRequested?.Invoke(item.Version);
    }

    [RelayCommand]
    private void DeleteVersion(VersionListItem? item)
    {
        if (item == null) return;
        try
        {
            var versionDir = Path.Combine(
                _settings.Settings.GameDirectory, "versions", item.Version.Id);
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);

            item.IsInstalled = false;
            StatusText = $"✓ Удалено: {item.Version.Id}";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ Не удалось удалить: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshList();
    partial void OnShowReleaseChanged(bool value) => RefreshList();
    partial void OnShowSnapshotChanged(bool value) => RefreshList();
    partial void OnShowOldBetaChanged(bool value) => RefreshList();
    partial void OnShowOldAlphaChanged(bool value) => RefreshList();
    partial void OnShowInstalledOnlyChanged(bool value) => RefreshList();

    private void RefreshList()
    {
        Versions.Clear();

        var gameDir = _settings.Settings.GameDirectory;
        var installedDir = Path.Combine(gameDir, "versions");

        var filtered = _allVersions.Where(v =>
        {
            // Тип
            var typeMatch = v.Type switch
            {
                "release" => ShowRelease,
                "snapshot" => ShowSnapshot,
                "old_beta" => ShowOldBeta,
                "old_alpha" => ShowOldAlpha,
                _ => true
            };
            if (!typeMatch) return false;

            // Поиск
            if (!string.IsNullOrWhiteSpace(SearchText) &&
                !v.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        });

        foreach (var v in filtered)
        {
            var installed = File.Exists(
                Path.Combine(installedDir, v.Id, $"{v.Id}.json"));

            if (ShowInstalledOnly && !installed) continue;

            Versions.Add(new VersionListItem
            {
                Version = v,
                IsInstalled = installed
            });
        }
    }
}

public partial class VersionListItem : ObservableObject
{
    public MinecraftVersion Version { get; set; } = new();

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    public string TypeBadge => Version.Type switch
    {
        "release" => "RELEASE",
        "snapshot" => "SNAPSHOT",
        "old_beta" => "BETA",
        "old_alpha" => "ALPHA",
        _ => Version.Type.ToUpper()
    };

    public string TypeColor => Version.Type switch
    {
        "release" => "#22c55e",     // зелёный
        "snapshot" => "#f59e0b",    // оранжевый
        "old_beta" => "#a1a1aa",    // серый
        "old_alpha" => "#71717a",   // тёмно-серый
        _ => "#7c3aed"
    };

    public string ReleaseDate => Version.ReleaseTime.ToString("yyyy-MM-dd");
}