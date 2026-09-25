using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IKitRepository
{
    Task<KitCatalog?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(KitCatalog catalog, CancellationToken cancellationToken = default);
}

