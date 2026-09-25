using System.Text.Json;
using System.Text.Json.Serialization;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonLinesActivityLog : IActivityLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly LocalDataPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonLinesActivityLog(LocalDataPaths paths) => _paths = paths;

    public async Task AppendAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var line = JsonSerializer.Serialize(activityEvent, SerializerOptions) + Environment.NewLine;

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_paths.ActivityLogFile, line, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0 || !File.Exists(_paths.ActivityLogFile))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_paths.ActivityLogFile, cancellationToken);
        var events = new List<ActivityEvent>();
        foreach (var line in lines.TakeLast(maximumCount))
        {
            try
            {
                var activity = JsonSerializer.Deserialize<ActivityEvent>(line, SerializerOptions);
                if (activity is not null) events.Add(activity);
            }
            catch (JsonException)
            {
                // A damaged line does not hide the remaining activity history.
            }
        }

        return events;
    }
}
