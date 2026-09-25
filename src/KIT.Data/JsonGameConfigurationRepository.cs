using System.Text.Json;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonGameConfigurationRepository : IGameConfigurationRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LocalDataPaths _paths;

    public JsonGameConfigurationRepository(LocalDataPaths paths) => _paths = paths;

    public async Task<GameConfiguration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ConfigurationFile))
        {
            return null;
        }

        await using var stream = File.OpenRead(_paths.ConfigurationFile);
        return await JsonSerializer.DeserializeAsync<GameConfiguration>(stream, SerializerOptions, cancellationToken);
    }

    public async Task SaveAsync(GameConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var temporaryFile = _paths.ConfigurationFile + ".tmp";

        await using (var stream = new FileStream(
            temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryFile, _paths.ConfigurationFile, overwrite: true);
    }
}

