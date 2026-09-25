using KIT.Core.Abstractions;

namespace KIT.Infrastructure.Windows;

public sealed class WindowsGameProcessInspector : IGameProcessInspector
{
    public bool IsExecutableRunning(string executablePath) =>
        WindowsProcessFinder.FindByExecutablePath(executablePath).Count > 0;
}

