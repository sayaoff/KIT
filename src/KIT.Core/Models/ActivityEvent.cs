namespace KIT.Core.Models;

public enum ActivityEventKind
{
    SessionStarted,
    KitApplied,
    KitRestored,
    ActionWarning,
    RecoveryRestored,
    SessionEnded
}

public sealed record ActivityEvent(
    Guid EventId,
    Guid SessionId,
    ActivityEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    int ProcessId,
    string ExecutablePath,
    TimeSpan? Duration = null,
    Guid? KitId = null,
    string? KitName = null,
    string? Details = null);
