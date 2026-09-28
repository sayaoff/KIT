namespace KIT.Core.Models;

public sealed record ApplicationDefinition(
    Guid Id,
    string DisplayName,
    string ExecutablePath);

public sealed record KitDefinition(
    Guid Id,
    string Name,
    bool IsVanilla,
    List<Guid> CleanModeAppIds,
    List<Guid> LaunchAppIds);

public sealed record KitExecutionPlan(
    Guid KitId,
    string KitName,
    IReadOnlyList<ApplicationDefinition> CleanModeApps,
    IReadOnlyList<ApplicationDefinition> LaunchApps);

public sealed record KitCatalog(
    int SchemaVersion,
    Guid ActiveKitId,
    List<ApplicationDefinition> Applications,
    List<KitDefinition> Kits)
{
    public const int CurrentSchemaVersion = 2;
    public static readonly Guid VanillaKitId = Guid.Parse("8b93b3e7-a93c-4cd8-b7a8-40c80ad90d01");

    public static KitCatalog CreateDefault()
    {
        var vanilla = new KitDefinition(VanillaKitId, "Vanilla", true, [], []);
        return new KitCatalog(CurrentSchemaVersion, vanilla.Id, [], [vanilla]);
    }
}
