namespace KIT.Core.Models;

public sealed record ClosedApplication(string ExecutablePath);

public sealed record LaunchedApplication(string ExecutablePath, int ProcessId);

public sealed record AppliedKitState(
    Guid KitId,
    string KitName,
    DateTimeOffset AppliedAtUtc,
    List<ClosedApplication> ClosedApplications,
    List<LaunchedApplication> LaunchedApplications);

public sealed record SessionRecoveryState(GameSession Session, AppliedKitState AppliedKit);

public sealed record ActionExecutionResult(AppliedKitState State, IReadOnlyList<string> Warnings);

public sealed record RestoreExecutionResult(IReadOnlyList<string> Warnings);

