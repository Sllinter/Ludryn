namespace Ludryn.Models;

public sealed class ComputerProgramEntry
{
    public string Title { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
