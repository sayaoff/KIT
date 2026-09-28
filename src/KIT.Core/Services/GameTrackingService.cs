using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Core.Services;

public sealed class GameTrackingService : IAsyncDisposable
{
    private readonly IGameConfigurationRepository _configurationRepository;
    private readonly IKitRepository _kitRepository;
    private readonly IRecoveryStateRepository _recoveryRepository;
    private readonly IActivityLog _activityLog;
    private readonly ISessionRepository _sessionRepository;
    private readonly IGameProcessWatcher _processWatcher;
    private readonly IProcessResourceMonitor _resourceMonitor;
    private readonly IGameProcessInspector _processInspector;
    private readonly ISessionActionCoordinator _actionCoordinator;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameSession? _currentSession;
    private AppliedKitState? _appliedKit;
    private bool _awaitingRecoveredProcess;
    private bool _started;

    public GameTrackingService(
        IGameConfigurationRepository configurationRepository,
        IKitRepository kitRepository,
        IRecoveryStateRepository recoveryRepository,
        IActivityLog activityLog,
        ISessionRepository sessionRepository,
        IGameProcessWatcher processWatcher,
        IProcessResourceMonitor resourceMonitor,
        IGameProcessInspector processInspector,
        ISessionActionCoordinator actionCoordinator,
        IClock clock)
    {
        _configurationRepository = configurationRepository;
        _kitRepository = kitRepository;
        _recoveryRepository = recoveryRepository;
        _activityLog = activityLog;
        _sessionRepository = sessionRepository;
        _processWatcher = processWatcher;
        _resourceMonitor = resourceMonitor;
        _processInspector = processInspector;
        _actionCoordinator = actionCoordinator;
        _clock = clock;
    }

    public event EventHandler<TrackingStatus>? StatusChanged;
    public event EventHandler<ActivityEvent>? ActivityRecorded;
    public event EventHandler<KitCatalog>? CatalogChanged;
    public event EventHandler<ResourceSample>? ResourceSampled;
    public event EventHandler<SessionRecord>? SessionSaved;

    public GameConfiguration? Configuration { get; private set; }
    public KitCatalog Catalog { get; private set; } = KitCatalog.CreateDefault();
    public bool IsGameRunning => _currentSession is not null;
    public KitDefinition ActiveKit => Catalog.Kits.First(kit => kit.Id == Catalog.ActiveKitId);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;

        _started = true;
        Configuration = await _configurationRepository.LoadAsync(cancellationToken);
        Catalog = NormalizeCatalog(await _kitRepository.LoadAsync(cancellationToken));
        await _kitRepository.SaveAsync(Catalog, cancellationToken);
        CatalogChanged?.Invoke(this, Catalog);
        await RecoverInterruptedSessionAsync(cancellationToken);

        if (Configuration is null)
        {
            PublishStatus(TrackingState.NotConfigured, "Select cs2.exe to begin monitoring.");
            return;
        }

        await StartWatcherAsync(Configuration.ExecutablePath, cancellationToken);
    }

    public async Task ConfigureAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("The game executable cannot be changed while CS2 is running.");
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var normalizedPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(normalizedPath), "cs2.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected file must be cs2.exe.", nameof(executablePath));
        if (!File.Exists(normalizedPath))
            throw new FileNotFoundException("The selected cs2.exe does not exist.", normalizedPath);

        var configuration = new GameConfiguration(normalizedPath);
        await _configurationRepository.SaveAsync(configuration, cancellationToken);
        Configuration = configuration;
        _started = true;
        await _processWatcher.StopAsync(cancellationToken);
        await StartWatcherAsync(normalizedPath, cancellationToken);
    }

    public async Task<KitDefinition> CreateKitAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("Kits cannot be changed while CS2 is running.");
        var normalizedName = ValidateKitName(name);
        EnsureUniqueName(normalizedName, null);
        var kit = new KitDefinition(Guid.NewGuid(), normalizedName, false, [], []);
        Catalog = Catalog with { Kits = [.. Catalog.Kits, kit] };
        await SaveCatalogAsync(cancellationToken);
        return kit;
    }

    public async Task SaveKitAsync(KitDefinition kit, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("Kits cannot be changed while CS2 is running.");
        var existing = Catalog.Kits.FirstOrDefault(candidate => candidate.Id == kit.Id)
            ?? throw new InvalidOperationException("The selected Kit no longer exists.");
        if (existing.IsVanilla) throw new InvalidOperationException("Vanilla Kit cannot be edited.");

        var normalizedName = ValidateKitName(kit.Name);
        EnsureUniqueName(normalizedName, kit.Id);
        var normalized = kit with
        {
            Name = normalizedName,
            IsVanilla = false,
            CleanModeAppIds = NormalizeApplicationIds(kit.CleanModeAppIds),
            LaunchAppIds = NormalizeApplicationIds(kit.LaunchAppIds)
        };
        if (Configuration is not null && ResolveApplications(
                normalized.CleanModeAppIds.Concat(normalized.LaunchAppIds))
                .Any(target => string.Equals(target.ExecutablePath, Configuration.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("cs2.exe cannot be used as a Kit action target.");
        Catalog = Catalog with
        {
            Kits = Catalog.Kits.Select(candidate => candidate.Id == normalized.Id ? normalized : candidate).ToList()
        };
        await SaveCatalogAsync(cancellationToken);
    }

    public IReadOnlyList<ApplicationDefinition> ResolveApplications(IEnumerable<Guid> applicationIds)
    {
        var byId = Catalog.Applications.ToDictionary(application => application.Id);
        return applicationIds.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<ApplicationDefinition> RegisterApplicationAsync(string executablePath,
        string? displayName = null, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("The App Library cannot be changed while CS2 is running.");
        var normalizedPath = ValidateApplicationPath(executablePath);
        var existing = Catalog.Applications.FirstOrDefault(application =>
            string.Equals(application.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var application = new ApplicationDefinition(Guid.NewGuid(),
            NormalizeApplicationName(displayName, normalizedPath), normalizedPath);
        Catalog = Catalog with { Applications = [.. Catalog.Applications, application] };
        await SaveCatalogAsync(cancellationToken);
        return application;
    }

    public async Task UpdateApplicationAsync(ApplicationDefinition application,
        CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("The App Library cannot be changed while CS2 is running.");
        if (Catalog.Applications.All(candidate => candidate.Id != application.Id))
            throw new InvalidOperationException("The selected application no longer exists.");
        var path = ValidateApplicationPath(application.ExecutablePath);
        if (Catalog.Applications.Any(candidate => candidate.Id != application.Id &&
                string.Equals(candidate.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This executable is already in the App Library.");
        var normalized = application with
        {
            ExecutablePath = path,
            DisplayName = NormalizeApplicationName(application.DisplayName, path)
        };
        Catalog = Catalog with
        {
            Applications = Catalog.Applications.Select(candidate =>
                candidate.Id == normalized.Id ? normalized : candidate).ToList()
        };
        await SaveCatalogAsync(cancellationToken);
    }

    public async Task DeleteApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("The App Library cannot be changed while CS2 is running.");
        if (Catalog.Applications.All(application => application.Id != applicationId)) return;
        Catalog = Catalog with
        {
            Applications = Catalog.Applications.Where(application => application.Id != applicationId).ToList(),
            Kits = Catalog.Kits.Select(kit => kit with
            {
                CleanModeAppIds = kit.CleanModeAppIds.Where(id => id != applicationId).ToList(),
                LaunchAppIds = kit.LaunchAppIds.Where(id => id != applicationId).ToList()
            }).ToList()
        };
        await SaveCatalogAsync(cancellationToken);
    }

    public async Task SetActiveKitAsync(Guid kitId, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("The Active Kit cannot be switched while CS2 is running.");
        if (Catalog.Kits.All(kit => kit.Id != kitId))
            throw new InvalidOperationException("The selected Kit no longer exists.");
        Catalog = Catalog with { ActiveKitId = kitId };
        await SaveCatalogAsync(cancellationToken);
    }

    public async Task DeleteKitAsync(Guid kitId, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveSession("Kits cannot be changed while CS2 is running.");
        var kit = Catalog.Kits.FirstOrDefault(candidate => candidate.Id == kitId)
            ?? throw new InvalidOperationException("The selected Kit no longer exists.");
        if (kit.IsVanilla) throw new InvalidOperationException("Vanilla Kit cannot be deleted.");

        Catalog = Catalog with
        {
            ActiveKitId = Catalog.ActiveKitId == kitId ? KitCatalog.VanillaKitId : Catalog.ActiveKitId,
            Kits = Catalog.Kits.Where(candidate => candidate.Id != kitId).ToList()
        };
        await SaveCatalogAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ActivityEvent>> ReadRecentActivityAsync(int maximumCount = 100,
        CancellationToken cancellationToken = default) =>
        _activityLog.ReadRecentAsync(maximumCount, cancellationToken);

    public Task<IReadOnlyList<SessionRecord>> ReadRecentSessionsAsync(int maximumCount = 100,
        CancellationToken cancellationToken = default) =>
        _sessionRepository.ReadRecentAsync(maximumCount, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started) return;
        await _processWatcher.StopAsync(cancellationToken);
        await _resourceMonitor.StopAsync(cancellationToken);
        _currentSession = null;
        _appliedKit = null;
        _awaitingRecoveredProcess = false;
        _started = false;
        PublishStatus(TrackingState.Stopped, "Monitoring stopped.");
    }

    private async Task StartWatcherAsync(string executablePath, CancellationToken cancellationToken)
    {
        await _processWatcher.StartAsync(executablePath, HandleProcessChangeAsync, cancellationToken);
        PublishStatus(_currentSession is null ? TrackingState.Watching : TrackingState.GameRunning,
            _currentSession is null ? $"Watching · {ActiveKit.Name} Kit active" : $"CS2 is running · {_appliedKit?.KitName ?? ActiveKit.Name} Kit",
            _currentSession);
    }

    private async ValueTask HandleProcessChangeAsync(GameProcessChange change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (change.Kind is GameProcessChangeKind.Started)
            {
                if (_currentSession is null) await BeginSessionAsync(change, cancellationToken);
                else if (_awaitingRecoveredProcess) await ResumeRecoveredSessionAsync(change, cancellationToken);
            }
            else if (change.Kind is GameProcessChangeKind.Stopped && _currentSession?.ProcessId == change.ProcessId)
                await EndSessionAsync(change, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task BeginSessionAsync(GameProcessChange change, CancellationToken cancellationToken)
    {
        var kit = ActiveKit;
        _awaitingRecoveredProcess = false;
        _currentSession = new GameSession(Guid.NewGuid(), change.ProcessId, change.ExecutablePath, _clock.UtcNow);
        PublishStatus(TrackingState.GameRunning, $"CS2 is running · {kit.Name} Kit", _currentSession);
        try
        {
            await _resourceMonitor.StartAsync(change.ProcessId, OnResourceSampleAsync, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordWarningsAsync(["Resource monitoring could not start: " + exception.Message],
                _currentSession, kit, cancellationToken);
        }
        await RecordAsync(new ActivityEvent(Guid.NewGuid(), _currentSession.Id, ActivityEventKind.SessionStarted,
            _currentSession.StartedAtUtc, change.ProcessId, change.ExecutablePath,
            KitId: kit.Id, KitName: kit.Name), cancellationToken);

        var emptyState = new AppliedKitState(kit.Id, kit.Name, _clock.UtcNow, [], []);
        _appliedKit = emptyState;
        await _recoveryRepository.SaveAsync(new SessionRecoveryState(_currentSession, emptyState), cancellationToken);
        try
        {
            var result = await _actionCoordinator.ApplyAsync(CreateExecutionPlan(kit), _clock.UtcNow, cancellationToken);
            _appliedKit = result.State;
            await _recoveryRepository.SaveAsync(new SessionRecoveryState(_currentSession, result.State), cancellationToken);
            await RecordAsync(new ActivityEvent(Guid.NewGuid(), _currentSession.Id, ActivityEventKind.KitApplied,
                _clock.UtcNow, change.ProcessId, change.ExecutablePath, KitId: kit.Id, KitName: kit.Name,
                Details: $"Closed {result.State.ClosedApplications.Count}; launched {result.State.LaunchedApplications.Count}."), cancellationToken);
            await RecordWarningsAsync(result.Warnings, _currentSession, kit, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordWarningsAsync(["Kit apply failed: " + exception.Message], _currentSession, kit, cancellationToken);
        }
        PublishStatus(TrackingState.GameRunning, $"CS2 is running · {kit.Name} Kit", _currentSession);
    }

    private async Task ResumeRecoveredSessionAsync(GameProcessChange change, CancellationToken cancellationToken)
    {
        _awaitingRecoveredProcess = false;
        var session = _currentSession! with { ProcessId = change.ProcessId, ExecutablePath = change.ExecutablePath };
        _currentSession = session;
        if (_appliedKit is not null)
            await _recoveryRepository.SaveAsync(new SessionRecoveryState(session, _appliedKit), cancellationToken);
        try
        {
            await _resourceMonitor.StartAsync(change.ProcessId, OnResourceSampleAsync, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordWarningsAsync(["Resource monitoring could not resume: " + exception.Message], session,
                new KitDefinition(_appliedKit?.KitId ?? ActiveKit.Id, _appliedKit?.KitName ?? ActiveKit.Name,
                    false, [], []), cancellationToken);
        }
        PublishStatus(TrackingState.GameRunning,
            $"CS2 is running · {_appliedKit?.KitName ?? ActiveKit.Name} Kit", session);
    }

    private async Task EndSessionAsync(GameProcessChange change, CancellationToken cancellationToken)
    {
        var session = _currentSession!;
        var kit = Catalog.Kits.FirstOrDefault(candidate => candidate.Id == _appliedKit?.KitId) ?? ActiveKit;
        SessionMetrics metrics;
        try { metrics = await _resourceMonitor.StopAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics = SessionMetrics.Empty;
            await RecordWarningsAsync(["Resource monitoring could not finish: " + exception.Message],
                session, kit, cancellationToken);
        }
        if (_appliedKit is not null)
        {
            try
            {
                var restore = await _actionCoordinator.RestoreAsync(_appliedKit, cancellationToken);
                await RecordAsync(new ActivityEvent(Guid.NewGuid(), session.Id, ActivityEventKind.KitRestored,
                    _clock.UtcNow, session.ProcessId, session.ExecutablePath, KitId: kit.Id, KitName: kit.Name), cancellationToken);
                await RecordWarningsAsync(restore.Warnings, session, kit, cancellationToken);
                await _recoveryRepository.ClearAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordWarningsAsync(["Restore is incomplete and will be retried next time KIT starts: " + exception.Message], session, kit, cancellationToken);
            }
        }

        var endedAt = _clock.UtcNow;
        await RecordAsync(new ActivityEvent(Guid.NewGuid(), session.Id, ActivityEventKind.SessionEnded,
            endedAt, change.ProcessId, change.ExecutablePath, endedAt - session.StartedAtUtc, kit.Id, kit.Name), cancellationToken);
        var record = new SessionRecord(session.Id, kit.Id, kit.Name, session.StartedAtUtc, endedAt,
            endedAt - session.StartedAtUtc, metrics);
        await _sessionRepository.AppendAsync(record, cancellationToken);
        SessionSaved?.Invoke(this, record);
        _currentSession = null;
        _appliedKit = null;
        _awaitingRecoveredProcess = false;
        PublishStatus(TrackingState.Watching, $"Session ended · {ActiveKit.Name} Kit active");
    }

    private async Task RecoverInterruptedSessionAsync(CancellationToken cancellationToken)
    {
        var recovery = await _recoveryRepository.LoadAsync(cancellationToken);
        if (recovery is null) return;
        if (_processInspector.IsExecutableRunning(recovery.Session.ExecutablePath))
        {
            _currentSession = recovery.Session;
            _appliedKit = recovery.AppliedKit;
            _awaitingRecoveredProcess = true;
            return;
        }

        var result = await _actionCoordinator.RestoreAsync(recovery.AppliedKit, cancellationToken);
        await _recoveryRepository.ClearAsync(cancellationToken);
        var endedAt = _clock.UtcNow;
        await RecordAsync(new ActivityEvent(Guid.NewGuid(), recovery.Session.Id, ActivityEventKind.RecoveryRestored,
            endedAt, recovery.Session.ProcessId, recovery.Session.ExecutablePath,
            endedAt - recovery.Session.StartedAtUtc, recovery.AppliedKit.KitId, recovery.AppliedKit.KitName,
            "Recovered state after KIT was interrupted."), cancellationToken);
        await RecordWarningsAsync(result.Warnings, recovery.Session,
            new KitDefinition(recovery.AppliedKit.KitId, recovery.AppliedKit.KitName, false, [], []), cancellationToken);
        var record = new SessionRecord(recovery.Session.Id, recovery.AppliedKit.KitId,
            recovery.AppliedKit.KitName, recovery.Session.StartedAtUtc, endedAt,
            endedAt - recovery.Session.StartedAtUtc, SessionMetrics.Empty, true);
        await _sessionRepository.AppendAsync(record, cancellationToken);
        SessionSaved?.Invoke(this, record);
    }

    private async Task RecordWarningsAsync(IReadOnlyList<string> warnings, GameSession session,
        KitDefinition kit, CancellationToken cancellationToken)
    {
        foreach (var warning in warnings)
            await RecordAsync(new ActivityEvent(Guid.NewGuid(), session.Id, ActivityEventKind.ActionWarning,
                _clock.UtcNow, session.ProcessId, session.ExecutablePath,
                KitId: kit.Id, KitName: kit.Name, Details: warning), cancellationToken);
    }

    private async Task RecordAsync(ActivityEvent activity, CancellationToken cancellationToken)
    {
        await _activityLog.AppendAsync(activity, cancellationToken);
        ActivityRecorded?.Invoke(this, activity);
    }

    private ValueTask OnResourceSampleAsync(ResourceSample sample, CancellationToken cancellationToken)
    {
        ResourceSampled?.Invoke(this, sample);
        return ValueTask.CompletedTask;
    }

    private async Task SaveCatalogAsync(CancellationToken cancellationToken)
    {
        await _kitRepository.SaveAsync(Catalog, cancellationToken);
        CatalogChanged?.Invoke(this, Catalog);
        PublishStatus(TrackingState.Watching, $"Watching · {ActiveKit.Name} Kit active");
    }

    private static KitCatalog NormalizeCatalog(KitCatalog? catalog)
    {
        if (catalog is null) return KitCatalog.CreateDefault();
        var applications = (catalog.Applications ?? [])
            .Where(application => application.Id != Guid.Empty && !string.IsNullOrWhiteSpace(application.ExecutablePath))
            .Select(application => application with
            {
                DisplayName = NormalizeApplicationName(application.DisplayName, application.ExecutablePath),
                ExecutablePath = application.ExecutablePath.Trim()
            })
            .DistinctBy(application => application.Id)
            .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var validApplicationIds = applications.Select(application => application.Id).ToHashSet();
        var kits = (catalog.Kits ?? []).Where(kit => !kit.IsVanilla).Select(kit => kit with
        {
            CleanModeAppIds = (kit.CleanModeAppIds ?? []).Where(validApplicationIds.Contains).Distinct().ToList(),
            LaunchAppIds = (kit.LaunchAppIds ?? []).Where(validApplicationIds.Contains).Distinct().ToList()
        }).ToList();
        kits.Insert(0, KitCatalog.CreateDefault().Kits[0]);
        var activeId = kits.Any(kit => kit.Id == catalog.ActiveKitId) ? catalog.ActiveKitId : KitCatalog.VanillaKitId;
        return new KitCatalog(KitCatalog.CurrentSchemaVersion, activeId, applications, kits);
    }

    private static string ValidateKitName(string name)
    {
        var normalized = name.Trim();
        if (normalized.Length is < 1 or > 60)
            throw new ArgumentException("Kit name must contain 1–60 characters.", nameof(name));
        return normalized;
    }

    private void EnsureUniqueName(string name, Guid? exceptKitId)
    {
        if (Catalog.Kits.Any(kit => kit.Id != exceptKitId && string.Equals(kit.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A Kit with this name already exists.");
    }

    private List<Guid> NormalizeApplicationIds(IEnumerable<Guid> applicationIds)
    {
        var validIds = Catalog.Applications.Select(application => application.Id).ToHashSet();
        return applicationIds.Where(validIds.Contains).Distinct().ToList();
    }

    private KitExecutionPlan CreateExecutionPlan(KitDefinition kit) => new(
        kit.Id,
        kit.Name,
        ResolveApplications(kit.CleanModeAppIds),
        ResolveApplications(kit.LaunchAppIds));

    private string ValidateApplicationPath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var normalizedPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(normalizedPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The application must be a Windows .exe file.", nameof(executablePath));
        if (Configuration is not null && string.Equals(normalizedPath, Configuration.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("cs2.exe cannot be used as a Kit action target.");
        return normalizedPath;
    }

    private static string NormalizeApplicationName(string? displayName, string executablePath) =>
        string.IsNullOrWhiteSpace(displayName)
            ? PortableFileNameWithoutExtension(executablePath)
            : displayName.Trim();

    private static string PortableFileNameWithoutExtension(string path) =>
        Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    private void EnsureNoActiveSession(string message)
    {
        if (_currentSession is not null) throw new InvalidOperationException(message);
    }

    private void PublishStatus(TrackingState state, string message, GameSession? session = null) =>
        StatusChanged?.Invoke(this, new TrackingStatus(state, message, session));

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _processWatcher.DisposeAsync();
        await _resourceMonitor.DisposeAsync();
        _gate.Dispose();
    }
}
