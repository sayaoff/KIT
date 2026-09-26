namespace KIT.Core.Models;

public sealed record ResourceSample(
    DateTimeOffset CapturedAtUtc,
    double CpuPercent,
    long WorkingSetBytes);

public sealed record SessionMetrics(
    int SampleCount,
    double AverageCpuPercent,
    double PeakCpuPercent,
    long AverageWorkingSetBytes,
    long PeakWorkingSetBytes)
{
    public static SessionMetrics Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record SessionRecord(
    Guid SessionId,
    Guid KitId,
    string KitName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    TimeSpan Duration,
    SessionMetrics Metrics,
    bool RecoveredAfterInterruption = false);

