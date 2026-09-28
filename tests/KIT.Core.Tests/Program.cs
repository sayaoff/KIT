using KIT.Core.Abstractions;
using KIT.Core.Models;
using KIT.Core.Services;
using KIT.Data;

var tests = new (string Name, Func<Task> Run)[]
{
    ("starts unconfigured with Vanilla Kit", StartsUnconfiguredAsync),
    ("records and restores a Kit session", RecordsSessionAsync),
    ("creates and activates a Kit", ManagesKitsAsync),
    ("recovers actions after an interrupted session", RecoversInterruptedSessionAsync),
    ("rejects a non-CS2 executable", RejectsNonCs2ExecutableAsync),
    ("persists configuration as JSON", PersistsConfigurationAsync),
    ("migrates legacy Kits into the App Library", MigratesLegacyKitsAsync),
    ("shares App Library entries across Kits", SharesApplicationsAcrossKitsAsync),
    ("persists language preference", PersistsLanguagePreferenceAsync),
    ("persists completed sessions", PersistsSessionsAsync)
};
var failures = new List<string>();
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception exception)
    {
        failures.Add($"FAIL  {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static async Task StartsUnconfiguredAsync()
{
    var context = new TestContext();
    await using var service = context.CreateService();
    TrackingStatus? status = null;
    service.StatusChanged += (_, value) => status = value;
    await service.StartAsync();
    Assert(status?.State is TrackingState.NotConfigured, "Expected NotConfigured.");
    Assert(service.ActiveKit.IsVanilla, "Vanilla must exist by default.");
    Assert(!context.Watcher.IsRunning, "Watcher must remain stopped.");
}

static async Task RecordsSessionAsync()
{
    const string path = @"C:\Games\CS2\cs2.exe";
    var cleanApp = new ApplicationDefinition(Guid.NewGuid(), "Chat", @"C:\Apps\chat.exe");
    var launchApp = new ApplicationDefinition(Guid.NewGuid(), "Music", @"C:\Apps\music.exe");
    var custom = new KitDefinition(Guid.NewGuid(), "Competitive", false,
        [cleanApp.Id], [launchApp.Id]);
    var context = new TestContext(new GameConfiguration(path), new KitCatalog(
        KitCatalog.CurrentSchemaVersion, custom.Id, [cleanApp, launchApp],
        [KitCatalog.CreateDefault().Kits[0], custom]));
    await using var service = context.CreateService();
    await service.StartAsync();
    await context.Watcher.EmitAsync(new GameProcessChange(GameProcessChangeKind.Started, 42, path));
    context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(20);
    await context.Watcher.EmitAsync(new GameProcessChange(GameProcessChangeKind.Stopped, 42, path));

    Assert(context.Actions.ApplyCount == 1 && context.Actions.RestoreCount == 1, "Kit must apply and restore once.");
    Assert(context.Sessions.Records.Count == 1, "Completed session was not persisted.");
    Assert(context.Sessions.Records[0].Metrics.SampleCount == 2, "Session metrics were not persisted.");
    Assert(context.Recovery.State is null, "Recovery state must clear after restore.");
    Assert(context.Log.Events.Any(value => value.Kind is ActivityEventKind.KitApplied), "KitApplied missing.");
    var ended = context.Log.Events.Single(value => value.Kind is ActivityEventKind.SessionEnded);
    Assert(ended.Duration == TimeSpan.FromMinutes(20), "Session duration is wrong.");
}

static async Task ManagesKitsAsync()
{
    var context = new TestContext();
    await using var service = context.CreateService();
    await service.StartAsync();
    var kit = await service.CreateKitAsync("Competitive");
    await service.SetActiveKitAsync(kit.Id);
    Assert(service.ActiveKit.Name == "Competitive", "New Kit was not activated.");
    Assert(context.Kits.Catalog?.Kits.Count == 2, "Kit catalog was not persisted.");
}

static async Task RecoversInterruptedSessionAsync()
{
    const string path = @"C:\Games\CS2\cs2.exe";
    var session = new GameSession(Guid.NewGuid(), 9, path, DateTimeOffset.UtcNow.AddMinutes(-5));
    var applied = new AppliedKitState(Guid.NewGuid(), "Focus", DateTimeOffset.UtcNow.AddMinutes(-5),
        [new ClosedApplication(@"C:\Apps\chat.exe")], []);
    var context = new TestContext(recovery: new SessionRecoveryState(session, applied));
    await using var service = context.CreateService();
    await service.StartAsync();
    Assert(context.Actions.RestoreCount == 1, "Interrupted actions were not restored.");
    Assert(context.Recovery.State is null, "Recovery file was not cleared.");
    Assert(context.Log.Events.Any(value => value.Kind is ActivityEventKind.RecoveryRestored), "Recovery event missing.");
}

static async Task RejectsNonCs2ExecutableAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var file = Path.Combine(directory, "other.exe");
    await File.WriteAllTextAsync(file, "");
    try
    {
        await using var service = new TestContext().CreateService();
        await AssertThrowsAsync<ArgumentException>(() => service.ConfigureAsync(file));
    }
    finally { Directory.Delete(directory, true); }
}

static async Task PersistsConfigurationAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-data-" + Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonGameConfigurationRepository(new LocalDataPaths(directory));
        var expected = new GameConfiguration(@"C:\Steam\cs2.exe");
        await repository.SaveAsync(expected);
        Assert(await repository.LoadAsync() == expected, "Configuration did not round-trip.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static async Task MigratesLegacyKitsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-migration-" + Guid.NewGuid().ToString("N"));
    try
    {
        var paths = new LocalDataPaths(directory);
        Directory.CreateDirectory(directory);
        var kitId = Guid.NewGuid();
        await File.WriteAllTextAsync(paths.KitsFile, $$"""
        {
          "activeKitId": "{{kitId}}",
          "kits": [
            {
              "id": "{{kitId}}",
              "name": "Legacy",
              "isVanilla": false,
              "cleanModeApps": [ { "executablePath": "C:\\Apps\\chat.exe", "displayName": "Chat" } ],
              "launchApps": [ { "executablePath": "C:\\Apps\\chat.exe", "displayName": "Chat" } ]
            }
          ]
        }
        """);

        var migrated = await new JsonKitRepository(paths).LoadAsync();
        Assert(migrated?.SchemaVersion == KitCatalog.CurrentSchemaVersion, "Schema version was not migrated.");
        Assert(File.Exists(paths.KitsV1BackupFile), "The legacy Kits backup was not created.");
        Assert(migrated?.Applications.Count == 1, "A shared legacy executable should become one library app.");
        Assert(migrated?.Kits.Single().CleanModeAppIds.Single() == migrated?.Applications.Single().Id,
            "Clean Mode did not reference the migrated app.");
        Assert(migrated?.Kits.Single().LaunchAppIds.Single() == migrated?.Applications.Single().Id,
            "Launch Apps did not reference the migrated app.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static async Task SharesApplicationsAcrossKitsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-library-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var firstPath = Path.Combine(directory, "chat.exe");
        var secondPath = Path.Combine(directory, "chat-new.exe");
        await File.WriteAllTextAsync(firstPath, "");
        await File.WriteAllTextAsync(secondPath, "");
        var context = new TestContext();
        await using var service = context.CreateService();
        await service.StartAsync();
        var application = await service.RegisterApplicationAsync(firstPath, "Chat");
        var first = await service.CreateKitAsync("First");
        var second = await service.CreateKitAsync("Second");
        await service.SaveKitAsync(first with { CleanModeAppIds = [application.Id] });
        await service.SaveKitAsync(second with { LaunchAppIds = [application.Id] });
        await service.UpdateApplicationAsync(application with { ExecutablePath = secondPath });

        Assert(service.ResolveApplications(service.Catalog.Kits.Single(kit => kit.Id == first.Id).CleanModeAppIds)
            .Single().ExecutablePath == Path.GetFullPath(secondPath), "First Kit did not receive the shared path update.");
        Assert(service.ResolveApplications(service.Catalog.Kits.Single(kit => kit.Id == second.Id).LaunchAppIds)
            .Single().ExecutablePath == Path.GetFullPath(secondPath), "Second Kit did not receive the shared path update.");
    }
    finally { Directory.Delete(directory, true); }
}

static async Task PersistsLanguagePreferenceAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-preferences-" + Guid.NewGuid().ToString("N"));
    try
    {
        var paths = new LocalDataPaths(directory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(paths.PreferencesFile, "{\"language\":\"ru\"}");
        var repository = new JsonUserPreferencesRepository(paths);
        var migrated = await repository.LoadAsync();
        Assert(migrated == new UserPreferences("ru", "dark", "calm"),
            "Legacy language preference did not receive appearance defaults.");
        await repository.SaveAsync(new UserPreferences("ru", "light", "aggressive", true));
        var saved = await repository.LoadAsync();
        Assert(saved == new UserPreferences("ru", "light", "aggressive", true),
            "Appearance and startup preferences did not round-trip.");
        Assert(repository.Load() == saved,
            "Synchronous startup preference loading did not match async loading.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static async Task PersistsSessionsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "kit-sessions-" + Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonLinesSessionRepository(new LocalDataPaths(directory));
        var expected = new SessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Competitive",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10),
            new SessionMetrics(5, 12, 30, 400_000_000, 500_000_000));
        await repository.AppendAsync(expected);
        var actual = await repository.ReadRecentAsync(10);
        Assert(actual.Count == 1 && actual[0] == expected, "Completed session did not round-trip.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

internal sealed class TestContext
{
    public TestContext(GameConfiguration? configuration = null, KitCatalog? catalog = null,
        SessionRecoveryState? recovery = null)
    {
        Configuration = new FakeConfigurationRepository(configuration);
        Kits = new FakeKitRepository(catalog);
        Recovery = new FakeRecoveryRepository(recovery);
    }
    public FakeConfigurationRepository Configuration { get; }
    public FakeKitRepository Kits { get; }
    public FakeRecoveryRepository Recovery { get; }
    public RecordingActivityLog Log { get; } = new();
    public FakeSessionRepository Sessions { get; } = new();
    public FakeWatcher Watcher { get; } = new();
    public FakeResourceMonitor Monitor { get; } = new();
    public FakeInspector Inspector { get; } = new();
    public FakeActions Actions { get; } = new();
    public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero) };
    public GameTrackingService CreateService() => new(Configuration, Kits, Recovery, Log, Sessions,
        Watcher, Monitor, Inspector, Actions, Clock);
}

internal sealed class FakeConfigurationRepository(GameConfiguration? value) : IGameConfigurationRepository
{
    public Task<GameConfiguration?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
    public Task SaveAsync(GameConfiguration configuration, CancellationToken cancellationToken = default) { value = configuration; return Task.CompletedTask; }
}
internal sealed class FakeKitRepository(KitCatalog? catalog) : IKitRepository
{
    public KitCatalog? Catalog { get; private set; } = catalog;
    public Task<KitCatalog?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Catalog);
    public Task SaveAsync(KitCatalog value, CancellationToken cancellationToken = default) { Catalog = value; return Task.CompletedTask; }
}
internal sealed class FakeRecoveryRepository(SessionRecoveryState? state) : IRecoveryStateRepository
{
    public SessionRecoveryState? State { get; private set; } = state;
    public Task<SessionRecoveryState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
    public Task SaveAsync(SessionRecoveryState value, CancellationToken cancellationToken = default) { State = value; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken = default) { State = null; return Task.CompletedTask; }
}
internal sealed class RecordingActivityLog : IActivityLog
{
    public List<ActivityEvent> Events { get; } = [];
    public Task AppendAsync(ActivityEvent value, CancellationToken cancellationToken = default) { Events.Add(value); return Task.CompletedTask; }
    public Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityEvent>>(Events.TakeLast(maximumCount).ToList());
}
internal sealed class FakeSessionRepository : ISessionRepository
{
    public List<SessionRecord> Records { get; } = [];
    public Task AppendAsync(SessionRecord session, CancellationToken cancellationToken = default)
    { Records.Add(session); return Task.CompletedTask; }
    public Task<IReadOnlyList<SessionRecord>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionRecord>>(Records.TakeLast(maximumCount).ToList());
}
internal sealed class FakeWatcher : IGameProcessWatcher
{
    private Func<GameProcessChange, CancellationToken, ValueTask>? _handler;
    public bool IsRunning { get; private set; }
    public Task StartAsync(string path, Func<GameProcessChange, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default)
    { IsRunning = true; _handler = handler; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; _handler = null; return Task.CompletedTask; }
    public ValueTask EmitAsync(GameProcessChange value) => _handler?.Invoke(value, CancellationToken.None) ?? ValueTask.CompletedTask;
    public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
}
internal sealed class FakeInspector : IGameProcessInspector
{
    public bool IsRunning { get; set; }
    public bool IsExecutableRunning(string executablePath) => IsRunning;
}
internal sealed class FakeResourceMonitor : IProcessResourceMonitor
{
    public Task StartAsync(int processId, Func<ResourceSample, CancellationToken, ValueTask> onSample,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SessionMetrics> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SessionMetrics(2, 12.5, 20, 500_000_000, 600_000_000));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class FakeActions : ISessionActionCoordinator
{
    public int ApplyCount { get; private set; }
    public int RestoreCount { get; private set; }
    public Task<ActionExecutionResult> ApplyAsync(KitExecutionPlan kit, DateTimeOffset time, CancellationToken cancellationToken = default)
    {
        ApplyCount++;
        return Task.FromResult(new ActionExecutionResult(new AppliedKitState(kit.KitId, kit.KitName, time,
            kit.CleanModeApps.Select(value => new ClosedApplication(value.ExecutablePath)).ToList(),
            kit.LaunchApps.Select((value, index) => new LaunchedApplication(value.ExecutablePath, index + 100)).ToList()), []));
    }
    public Task<RestoreExecutionResult> RestoreAsync(AppliedKitState state, CancellationToken cancellationToken = default)
    { RestoreCount++; return Task.FromResult(new RestoreExecutionResult([])); }
}
internal sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } }
