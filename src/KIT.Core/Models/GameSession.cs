namespace KIT.Core.Models;

public sealed record GameSession(
    Guid Id,
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset StartedAtUtc);

