namespace MinecraftLauncher.ViewModels.Pages;

public class AboutPageViewModel : PageViewModelBase
{
    public override string Title => "О программе";
    public override string Icon => "ℹ";

    public string Version => "0.1.0";
    public string BuildDate => "2025";
    public string Author => "GamerMax1254";
    public string GitHubUrl => "https://github.com/GamerMax1254/minecraftlauncher";
}