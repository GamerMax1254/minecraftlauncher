using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLauncher.Core.Config;

namespace MinecraftLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    public HomeViewModel Home { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private int _selectedTabIndex;

    public MainViewModel()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MCLauncher");

        _settingsManager = new SettingsManager(appDir);
        _settingsManager.Load();

        Home = new HomeViewModel(_settingsManager);
        Settings = new SettingsViewModel(_settingsManager);

        Settings.SettingsSaved += () => Home.ReloadForGameDir();
    }
}