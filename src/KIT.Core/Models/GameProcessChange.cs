namespace KIT.Core.Models;

public enum GameProcessChangeKind
{
    Started,
    Stopped
}

public sealed record GameProcessChange(
    GameProcessChangeKind Kind,
    int ProcessId,
    string ExecutablePath);

