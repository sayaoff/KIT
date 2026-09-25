using System.Text.Json;

namespace KIT.Data;

public sealed record UserPreferences(string Language);

public sealed class JsonUserPreferencesRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;

    public JsonUserPreferencesRepository(LocalDataPaths paths) => _paths = paths;

    public async Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.PreferencesFile)) return new UserPreferences("en");
        try
        {
            await using var stream = File.OpenRead(_paths.PreferencesFile);
            return await JsonSerializer.DeserializeAsync<UserPreferences>(stream, Options, cancellationToken)
                   ?? new UserPreferences("en");
        }
        catch (JsonException)
        {
            return new UserPreferences("en");
        }
    }

    public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_paths.PreferencesFile, preferences, Options, cancellationToken);
}
