// src/MinecraftLauncher/ViewModels/Pages/CreateProfileDialogViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.Core.Download;
using MinecraftLauncher.Core.Download.Modded;
using MinecraftLauncher.Core.Models;

namespace MinecraftLauncher.ViewModels.Pages;

public partial class CreateProfileDialogViewModel : ViewModelBase
{
    private readonly ProfileManager _profiles;
    private readonly HttpClient _http;

    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private ModLoader _selectedLoader = ModLoader.Vanilla;
    [ObservableProperty] private string? _selectedVersion;
    [ObservableProperty] private string? _selectedLoaderVersion;
    [ObservableProperty] private bool _includeSnapshots;
    [ObservableProperty] private bool _isolatedGameDir = true;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public ObservableCollection<string> AvailableLoaderVersions { get; } = new();
    public ObservableCollection<ModLoader> AvailableLoaders { get; } = new()
    {
        ModLoader.Vanilla,
        ModLoader.Fabric,
        ModLoader.Quilt,
        ModLoader.Forge,
        ModLoader.NeoForge
    };

    private List<MinecraftVersion> _allVanillaVersions = new();
    private List<FabricLoaderVersion> _fabricLoaderVersions = new();
    private List<FabricGameVersion> _fabricGameVersions = new();
    private List<FabricLoaderVersion> _quiltLoaderVersions = new();
    private List<FabricGameVersion> _quiltGameVersions = new();

    public bool IsLoaderVersionVisible => SelectedLoader != ModLoader.Vanilla;
    public bool IsLoaderSupported => SelectedLoader != ModLoader.Forge && SelectedLoader != ModLoader.NeoForge;

    public event Action<GameProfile?>? CloseRequested;

    public CreateProfileDialogViewModel(ProfileManager profiles, HttpClient http)
    {
        _profiles = profiles;
        _http = http;
    }

    public async Task LoadVersionsAsync()
    {
        IsLoading = true;
        try
        {
            // Ванильные версии
            var vm = new VersionManager(_http, "");
            _allVanillaVersions = await vm.GetAvailableVersionsAsync(includeSnapshots: true);

            // Fabric
            var fabric = new FabricInstaller(_http, "");
            _fabricLoaderVersions = await fabric.GetLoaderVersionsAsync();
            _fabricGameVersions = await fabric.GetSupportedGameVersionsAsync();

            // Quilt
            var quilt = new QuiltInstaller(_http, "");
            _quiltLoaderVersions = await quilt.GetLoaderVersionsAsync();
            _quiltGameVersions = await quilt.GetSupportedGameVersionsAsync();

            RefreshVersionList();
            RefreshLoaderVersions();
        }
        catch (Exception ex)
        {
            ErrorText = $"Не удалось загрузить версии: {ex.Message}";
        }
        IsLoading = false;
    }

    partial void OnIncludeSnapshotsChanged(bool value) => RefreshVersionList();

    partial void OnSelectedLoaderChanged(ModLoader value)
    {
        OnPropertyChanged(nameof(IsLoaderVersionVisible));
        OnPropertyChanged(nameof(IsLoaderSupported));
        IsolatedGameDir = value != ModLoader.Vanilla;
        RefreshVersionList();
        RefreshLoaderVersions();
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrEmpty(value))
        {
            ProfileName = SelectedLoader == ModLoader.Vanilla
                ? value
                : $"{value} {SelectedLoader}";
        }
        RefreshLoaderVersions();
    }

    private void RefreshVersionList()
    {
        AvailableVersions.Clear();

        IEnumerable<string> filtered = SelectedLoader switch
        {
            ModLoader.Fabric => _fabricGameVersions
                .Where(v => IncludeSnapshots || v.Stable)
                .Select(v => v.Version),

            ModLoader.Quilt => _quiltGameVersions
                .Where(v => IncludeSnapshots || v.Stable)
                .Select(v => v.Version),

            _ => _allVanillaVersions
                .Where(v => IncludeSnapshots || v.Type == "release")
                .Select(v => v.Id)
        };

        foreach (var v in filtered) AvailableVersions.Add(v);
    }

    private void RefreshLoaderVersions()
    {
        AvailableLoaderVersions.Clear();

        IEnumerable<string> versions = SelectedLoader switch
        {
            ModLoader.Fabric => _fabricLoaderVersions
                .Where(v => IncludeSnapshots || v.Stable)
                .Select(v => v.Version),

            ModLoader.Quilt => _quiltLoaderVersions
                .Where(v => IncludeSnapshots || v.Stable)
                .Select(v => v.Version),

            _ => Enumerable.Empty<string>()
        };

        foreach (var v in versions) AvailableLoaderVersions.Add(v);
        SelectedLoaderVersion = AvailableLoaderVersions.FirstOrDefault();
    }

    [RelayCommand]
    private void Create()
    {
        ErrorText = "";

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            ErrorText = "Введите имя профиля";
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            ErrorText = "Выберите версию";
            return;
        }
        if (_profiles.NameExists(ProfileName))
        {
            ErrorText = $"Профиль с именем «{ProfileName}» уже существует";
            return;
        }
        if (!IsLoaderSupported)
        {
            ErrorText = $"{SelectedLoader} пока в разработке. Скоро!";
            return;
        }
        if (SelectedLoader != ModLoader.Vanilla &&
            string.IsNullOrWhiteSpace(SelectedLoaderVersion))
        {
            ErrorText = "Выберите версию загрузчика";
            return;
        }

        var profile = new GameProfile
        {
            Name = ProfileName.Trim(),
            MinecraftVersion = SelectedVersion,
            Loader = SelectedLoader,
            LoaderVersion = SelectedLoader == ModLoader.Vanilla ? null : SelectedLoaderVersion,
            IsolatedGameDir = IsolatedGameDir
        };

        CloseRequested?.Invoke(profile);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
