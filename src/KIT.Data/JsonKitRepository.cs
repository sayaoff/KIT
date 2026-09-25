using System.Text.Json;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonKitRepository : IKitRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;

    public JsonKitRepository(LocalDataPaths paths) => _paths = paths;

    public async Task<KitCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.KitsFile)) return null;
        await using var stream = File.OpenRead(_paths.KitsFile);
        return await JsonSerializer.DeserializeAsync<KitCatalog>(stream, Options, cancellationToken);
    }

    public Task SaveAsync(KitCatalog catalog, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_paths.KitsFile, catalog, Options, cancellationToken);
}

