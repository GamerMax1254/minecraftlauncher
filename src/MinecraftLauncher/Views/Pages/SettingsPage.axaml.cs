using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MinecraftLauncher.ViewModels.Pages;

namespace MinecraftLauncher.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void OnBrowseGameDirClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Выберите папку .minecraft",
                AllowMultiple = false
            });

        if (folder.Count > 0)
            vm.GameDirectory = folder[0].Path.LocalPath;
    }

    private async void OnBrowseJavaClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Выберите java.exe",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Java")
                    {
                        Patterns = new[] { "java.exe", "java" }
                    }
                }
            });

        if (file.Count > 0)
            vm.CustomJavaPath = file[0].Path.LocalPath;
    }
}