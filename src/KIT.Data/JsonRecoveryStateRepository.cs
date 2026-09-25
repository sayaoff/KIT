using System.Text.Json;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonRecoveryStateRepository : IRecoveryStateRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;

    public JsonRecoveryStateRepository(LocalDataPaths paths) => _paths = paths;

    public async Task<SessionRecoveryState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.RecoveryFile)) return null;
        await using var stream = File.OpenRead(_paths.RecoveryFile);
        return await JsonSerializer.DeserializeAsync<SessionRecoveryState>(stream, Options, cancellationToken);
    }

    public Task SaveAsync(SessionRecoveryState state, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_paths.RecoveryFile, state, Options, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_paths.RecoveryFile)) File.Delete(_paths.RecoveryFile);
        return Task.CompletedTask;
    }
}

