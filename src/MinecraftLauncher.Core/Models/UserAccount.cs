namespace MinecraftLauncher.Core.Models;

public class UserAccount
{
    public string Username { get; set; } = "Player";
    public string Uuid { get; set; } = Guid.NewGuid().ToString("N");
    public string? AccessToken { get; set; }
    public bool IsOnline { get; set; } = false;
}