using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.ViewModels.Pages;

namespace MinecraftLauncher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly ProfileManager _profileManager;

    [ObservableProperty] private PageViewModelBase? _currentPage;

    public ObservableCollection<PageViewModelBase> Pages { get; }

    public HomePageViewModel Home { get; }
    public VersionsPageViewModel Versions { get; }
    public SettingsPageViewModel Settings { get; }
    public LogsPageViewModel Logs { get; }
    public CustomizationPageViewModel Customization { get; }
    public AboutPageViewModel About { get; }

    public MainViewModel()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MCLauncher");
        _settingsManager = new SettingsManager(appDir);
        _settingsManager.Load();

        _profileManager = new ProfileManager(appDir);
        _profileManager.Load();

        Home = new HomePageViewModel(_settingsManager, _profileManager);
        Versions = new VersionsPageViewModel(_settingsManager, _profileManager);
        Settings = new SettingsPageViewModel(_settingsManager);
        Logs = new LogsPageViewModel();
        Customization = new CustomizationPageViewModel();
        About = new AboutPageViewModel();

        Pages = new ObservableCollection<PageViewModelBase>
        {
            Home,
            Versions,
            Settings,
        };

        if (_settingsManager.Settings.ShowLogsPage)
            Pages.Add(Logs);

        Pages.Add(Customization);
        Pages.Add(About);

        Settings.SettingsSaved += OnSettingsSaved;
        Versions.LaunchRequested += profile =>
        {
            Home.SetProfileAndLaunch(profile);
            CurrentPage = Home;
        };
        Versions.ProfilesChanged += () => Home.RefreshProfiles();

        CurrentPage = Home;
    }

    partial void OnCurrentPageChanged(PageViewModelBase? oldValue, PageViewModelBase? newValue)
    {
        oldValue?.OnNavigatedFrom();
        newValue?.OnNavigatedTo();
    }

    [RelayCommand]
    private void Navigate(PageViewModelBase? page)
    {
        if (page != null) CurrentPage = page;
    }

    private void OnSettingsSaved()
    {
        Home.ReloadForGameDir();
        Versions.ReloadForGameDir();

        var showLogs = _settingsManager.Settings.ShowLogsPage;
        var hasLogs = Pages.Contains(Logs);

        if (showLogs && !hasLogs)
        {
            var customIndex = Pages.IndexOf(Customization);
            Pages.Insert(customIndex, Logs);
        }
        else if (!showLogs && hasLogs)
        {
            Pages.Remove(Logs);
            if (CurrentPage == Logs) CurrentPage = Home;
        }
    }
}