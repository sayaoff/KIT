using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface IActivityLog
{
    Task AppendAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);
}
