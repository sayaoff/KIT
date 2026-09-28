namespace KIT.Core.Models;

public sealed record ApplicationDefinition(
    Guid Id,
    string DisplayName,
    string ExecutablePath);

public enum CleanCloseMode
{
    Normal,
    ForceIfNeeded
}

public sealed record CleanModeAction(
    Guid ApplicationId,
    CleanCloseMode CloseMode,
    int DelaySeconds,
    bool RestoreAfterSession);

public sealed record LaunchAppAction(
    Guid ApplicationId,
    int DelaySeconds,
    bool CloseAfterSession);

public sealed record KitDefinition(
    Guid Id,
    string Name,
    bool IsVanilla,
    List<CleanModeAction> CleanModeActions,
    List<LaunchAppAction> LaunchAppActions);

public sealed record CleanModeExecution(
    ApplicationDefinition Application,
    CleanCloseMode CloseMode,
    int DelaySeconds,
    bool RestoreAfterSession);

public sealed record LaunchAppExecution(
    ApplicationDefinition Application,
    int DelaySeconds,
    bool CloseAfterSession);

public sealed record KitExecutionPlan(
    Guid KitId,
    string KitName,
    IReadOnlyList<CleanModeExecution> CleanModeActions,
    IReadOnlyList<LaunchAppExecution> LaunchAppActions);

public sealed record KitCatalog(
    int SchemaVersion,
    Guid ActiveKitId,
    List<ApplicationDefinition> Applications,
    List<KitDefinition> Kits)
{
    public const int CurrentSchemaVersion = 3;
    public static readonly Guid VanillaKitId = Guid.Parse("8b93b3e7-a93c-4cd8-b7a8-40c80ad90d01");

    public static KitCatalog CreateDefault()
    {
        var vanilla = new KitDefinition(VanillaKitId, "Vanilla", true, [], []);
        return new KitCatalog(CurrentSchemaVersion, vanilla.Id, [], [vanilla]);
    }
}
