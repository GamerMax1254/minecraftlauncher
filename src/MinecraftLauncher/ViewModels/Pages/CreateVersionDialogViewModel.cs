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
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isolatedGameDir = true;

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public ObservableCollection<string> AvailableLoaderVersions { get; } = new();
    public ObservableCollection<ModLoader> AvailableLoaders { get; } = new()
    {
        ModLoader.Vanilla,
        ModLoader.Fabric,
        ModLoader.Forge,
        ModLoader.Quilt,
        ModLoader.NeoForge
    };

    private List<MinecraftVersion> _allVersions = new();

    public bool IsLoaderVersionVisible => SelectedLoader != ModLoader.Vanilla;

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
            var vm = new VersionManager(_http, "");
            _allVersions = await vm.GetAvailableVersionsAsync(includeSnapshots: true);
            RefreshVersionList();
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
        // По умолчанию: изоляция включена для модовых, выключена для ванилы
        IsolatedGameDir = value != ModLoader.Vanilla;
        RefreshLoaderVersions();
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        // Автоподстановка имени
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
        var filtered = _allVersions
            .Where(v => IncludeSnapshots || v.Type == "release")
            .Select(v => v.Id);
        foreach (var v in filtered) AvailableVersions.Add(v);
    }

    private void RefreshLoaderVersions()
    {
        AvailableLoaderVersions.Clear();
        if (SelectedLoader == ModLoader.Vanilla) return;

        // Пока заглушка. Позже здесь будет реальный запрос к Fabric/Forge Meta API.
        AvailableLoaderVersions.Add("(скоро — сейчас не поддерживается)");
        SelectedLoaderVersion = AvailableLoaderVersions[0];
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

        // Пока разрешаем только Vanilla
        if (SelectedLoader != ModLoader.Vanilla)
        {
            ErrorText = $"{SelectedLoader} пока не поддерживается. Скоро добавим!";
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
    private void Cancel()
    {
        CloseRequested?.Invoke(null);
    }
}
