namespace KIT.Core.Models;

public sealed record ApplicationTarget(string ExecutablePath, string DisplayName);

public sealed record KitDefinition(
    Guid Id,
    string Name,
    bool IsVanilla,
    List<ApplicationTarget> CleanModeApps,
    List<ApplicationTarget> LaunchApps);

public sealed record KitCatalog(Guid ActiveKitId, List<KitDefinition> Kits)
{
    public static readonly Guid VanillaKitId = Guid.Parse("8b93b3e7-a93c-4cd8-b7a8-40c80ad90d01");

    public static KitCatalog CreateDefault()
    {
        var vanilla = new KitDefinition(VanillaKitId, "Vanilla", true, [], []);
        return new KitCatalog(vanilla.Id, [vanilla]);
    }
}

