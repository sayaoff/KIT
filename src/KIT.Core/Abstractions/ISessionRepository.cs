using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface ISessionRepository
{
    Task AppendAsync(SessionRecord session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionRecord>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);
}
