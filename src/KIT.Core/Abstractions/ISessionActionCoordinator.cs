using KIT.Core.Models;

namespace KIT.Core.Abstractions;

public interface ISessionActionCoordinator
{
    Task<ActionExecutionResult> ApplyAsync(
        KitExecutionPlan kit,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken = default);

    Task<RestoreExecutionResult> RestoreAsync(
        AppliedKitState state,
        CancellationToken cancellationToken = default);
}
