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
    public override string Title => "Профили";
    public override string Icon => "📦";

    private readonly HttpClient _http = new();
    private readonly SettingsManager _settings;
    private readonly ProfileManager _profiles;
    private VersionManager _versionManager;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _showCreateDialog;
    [ObservableProperty] private CreateProfileDialogViewModel? _createDialog;

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public event Action<GameProfile>? LaunchRequested;
    public event Action? ProfilesChanged;

    public VersionsPageViewModel(SettingsManager settings, ProfileManager profiles)
    {
        _settings = settings;
        _profiles = profiles;
        var gameDir = _settings.Settings.GameDirectory;
        _versionManager = new VersionManager(_http, gameDir);

        RefreshList();
    }

    public void ReloadForGameDir()
    {
        _versionManager = new VersionManager(_http, _settings.Settings.GameDirectory);
        RefreshList();
    }

    private void RefreshList()
    {
        Profiles.Clear();
        foreach (var p in _profiles.Profiles.OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt))
            Profiles.Add(p);
        StatusText = $"Профилей: {Profiles.Count}";
    }

    [RelayCommand]
    private async Task OpenCreateDialogAsync()
    {
        CreateDialog = new CreateProfileDialogViewModel(_profiles, _http);
        await CreateDialog.LoadVersionsAsync();
        CreateDialog.CloseRequested += (created) =>
        {
            ShowCreateDialog = false;
            if (created != null)
            {
                _profiles.Add(created);
                RefreshList();
                ProfilesChanged?.Invoke();
                StatusText = $"✓ Профиль «{created.Name}» создан";
            }
            CreateDialog = null;
        };
        ShowCreateDialog = true;
    }

    [RelayCommand]
    private void CancelCreateDialog()
    {
        ShowCreateDialog = false;
        CreateDialog = null;
    }

    [RelayCommand]
    private void Launch(GameProfile? profile)
    {
        if (profile == null) return;
        LaunchRequested?.Invoke(profile);
    }

    [RelayCommand]
    private void DeleteProfile(GameProfile? profile)
    {
        if (profile == null) return;
        _profiles.Remove(profile);
        RefreshList();
        ProfilesChanged?.Invoke();
        StatusText = $"✓ Профиль «{profile.Name}» удалён";
    }

    [RelayCommand]
    private void DeleteVersionFiles(GameProfile? profile)
    {
        if (profile == null) return;
        try
        {
            var dir = Path.Combine(_settings.Settings.GameDirectory, "versions", profile.MinecraftVersion);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            StatusText = $"✓ Файлы версии {profile.MinecraftVersion} удалены";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ Ошибка: {ex.Message}";
        }
    }
}