using System.Text.Json;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonLinesSessionRepository : ISessionRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly LocalDataPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonLinesSessionRepository(LocalDataPaths paths) => _paths = paths;

    public async Task AppendAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var line = JsonSerializer.Serialize(session, Options) + Environment.NewLine;
        await _writeGate.WaitAsync(cancellationToken);
        try { await File.AppendAllTextAsync(_paths.SessionsFile, line, cancellationToken); }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<SessionRecord>> ReadRecentAsync(int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0 || !File.Exists(_paths.SessionsFile)) return [];
        var lines = await File.ReadAllLinesAsync(_paths.SessionsFile, cancellationToken);
        var sessions = new List<SessionRecord>();
        foreach (var line in lines.TakeLast(maximumCount))
        {
            try
            {
                var session = JsonSerializer.Deserialize<SessionRecord>(line, Options);
                if (session is not null) sessions.Add(session);
            }
            catch (JsonException) { }
        }
        return sessions;
    }
}
