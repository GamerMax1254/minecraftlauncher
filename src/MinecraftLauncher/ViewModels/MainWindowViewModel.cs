namespace MinecraftLauncher.ViewModels;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string Title { get; }
    public abstract string Icon { get; }

    /// <summary>Вызывается когда пользователь переходит на эту страницу</summary>
    public virtual void OnNavigatedTo() { }

    /// <summary>Вызывается когда пользователь уходит со страницы</summary>
    public virtual void OnNavigatedFrom() { }
}