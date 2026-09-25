using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IGameProcessWatcher : IAsyncDisposable
{
    Task StartAsync(
        string executablePath,
        Func<GameProcessChange, CancellationToken, ValueTask> onChange,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

