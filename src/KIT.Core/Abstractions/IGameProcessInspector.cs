namespace KIT.Core.Abstractions;

public interface IGameProcessInspector
{
    bool IsExecutableRunning(string executablePath);
}

