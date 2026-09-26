using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IProcessResourceMonitor : IAsyncDisposable
{
    Task StartAsync(
        int processId,
        Func<ResourceSample, CancellationToken, ValueTask> onSample,
        CancellationToken cancellationToken = default);

    Task<SessionMetrics> StopAsync(CancellationToken cancellationToken = default);
}

