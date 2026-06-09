namespace Ludryn.Models;

public sealed class GameInstallation
{
    public string Launcher { get; set; } = string.Empty;
    public string LaunchId { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public bool IsDetected { get; set; }
}
