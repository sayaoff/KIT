using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IGameConfigurationRepository
{
    Task<GameConfiguration?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GameConfiguration configuration, CancellationToken cancellationToken = default);
}

