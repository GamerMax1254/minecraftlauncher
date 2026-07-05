using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLauncher.Core.Config;
using MinecraftLauncher.ViewModels.Pages;

namespace MinecraftLauncher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty] private PageViewModelBase? _currentPage;

    public ObservableCollection<PageViewModelBase> Pages { get; }

    // Отдельные ссылки для быстрого доступа
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

        // Создаём страницы
        Home = new HomePageViewModel(_settingsManager);
        Versions = new VersionsPageViewModel(_settingsManager);
        Settings = new SettingsPageViewModel(_settingsManager);
        Logs = new LogsPageViewModel();
        Customization = new CustomizationPageViewModel();
        About = new AboutPageViewModel();

        // Собираем в коллекцию для сайдбара
        Pages = new ObservableCollection<PageViewModelBase>
        {
            Home,
            Versions,
            Settings,
        };

        // Логи показываем только если включено в настройках
        if (_settingsManager.Settings.ShowLogsPage)
            Pages.Add(Logs);

        Pages.Add(Customization);
        Pages.Add(About);

        // Реакции
        Settings.SettingsSaved += OnSettingsSaved;
        Versions.LaunchRequested += version =>
        {
            Home.SetVersionAndLaunch(version);
            CurrentPage = Home;
        };

        // Стартовая страница
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

        // Динамически добавляем/убираем страницу логов
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