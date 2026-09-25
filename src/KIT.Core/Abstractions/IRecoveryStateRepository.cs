using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IRecoveryStateRepository
{
    Task<SessionRecoveryState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SessionRecoveryState state, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

