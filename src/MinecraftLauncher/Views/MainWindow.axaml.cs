using Avalonia.Controls;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}