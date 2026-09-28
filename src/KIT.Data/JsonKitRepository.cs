using System.Text.Json;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Data;

public sealed class JsonKitRepository : IKitRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;

    public JsonKitRepository(LocalDataPaths paths) => _paths = paths;

    public async Task<KitCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.KitsFile)) return null;
        await using var stream = File.OpenRead(_paths.KitsFile);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            var version = schemaVersion.GetInt32();
            if (version > KitCatalog.CurrentSchemaVersion)
                throw new NotSupportedException($"kits.json schema {version} is newer than this KIT version supports.");
            if (version == KitCatalog.CurrentSchemaVersion)
                return document.RootElement.Deserialize<KitCatalog>(Options);
            if (version == 2)
            {
                var versionTwo = document.RootElement.Deserialize<VersionTwoKitCatalog>(Options);
                if (versionTwo is null) return null;
                CreateBackup(_paths.KitsV2BackupFile);
                return MigrateVersionTwo(versionTwo);
            }
        }

        var legacy = document.RootElement.Deserialize<LegacyKitCatalog>(Options);
        if (legacy is null) return null;
        CreateBackup(_paths.KitsV1BackupFile);
        return MigrateLegacy(legacy);
    }

    public Task SaveAsync(KitCatalog catalog, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_paths.KitsFile, catalog, Options, cancellationToken);

    private void CreateBackup(string destination)
    {
        if (!File.Exists(destination)) File.Copy(_paths.KitsFile, destination);
    }

    private static KitCatalog MigrateVersionTwo(VersionTwoKitCatalog catalog)
    {
        var kits = (catalog.Kits ?? []).Select(kit => new KitDefinition(
            kit.Id,
            kit.Name ?? "Kit",
            kit.IsVanilla,
            (kit.CleanModeAppIds ?? []).Distinct().Select(id =>
                new CleanModeAction(id, CleanCloseMode.ForceIfNeeded, 0, true)).ToList(),
            (kit.LaunchAppIds ?? []).Distinct().Select(id =>
                new LaunchAppAction(id, 0, true)).ToList())).ToList();
        return new KitCatalog(KitCatalog.CurrentSchemaVersion, catalog.ActiveKitId,
            catalog.Applications ?? [], kits);
    }

    private static KitCatalog MigrateLegacy(LegacyKitCatalog legacy)
    {
        var applications = new List<ApplicationDefinition>();
        var byPath = new Dictionary<string, ApplicationDefinition>(StringComparer.OrdinalIgnoreCase);

        Guid Register(LegacyApplicationTarget target)
        {
            var path = target.ExecutablePath?.Trim() ?? "";
            if (byPath.TryGetValue(path, out var existing)) return existing.Id;
            var name = string.IsNullOrWhiteSpace(target.DisplayName)
                ? PortableFileNameWithoutExtension(path)
                : target.DisplayName.Trim();
            var application = new ApplicationDefinition(Guid.NewGuid(), name, path);
            applications.Add(application);
            byPath[path] = application;
            return application.Id;
        }

        var kits = (legacy.Kits ?? []).Select(kit => new KitDefinition(
            kit.Id,
            kit.Name ?? "Kit",
            kit.IsVanilla,
            (kit.CleanModeApps ?? []).Where(IsValidTarget).Select(Register).Distinct().Select(id =>
                new CleanModeAction(id, CleanCloseMode.ForceIfNeeded, 0, true)).ToList(),
            (kit.LaunchApps ?? []).Where(IsValidTarget).Select(Register).Distinct().Select(id =>
                new LaunchAppAction(id, 0, true)).ToList())).ToList();

        return new KitCatalog(KitCatalog.CurrentSchemaVersion, legacy.ActiveKitId, applications, kits);
    }

    private static bool IsValidTarget(LegacyApplicationTarget target) =>
        !string.IsNullOrWhiteSpace(target.ExecutablePath);

    private static string PortableFileNameWithoutExtension(string path) =>
        Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    private sealed record LegacyKitCatalog(Guid ActiveKitId, List<LegacyKitDefinition>? Kits);
    private sealed record LegacyKitDefinition(Guid Id, string? Name, bool IsVanilla,
        List<LegacyApplicationTarget>? CleanModeApps, List<LegacyApplicationTarget>? LaunchApps);
    private sealed record LegacyApplicationTarget(string? ExecutablePath, string? DisplayName);
    private sealed record VersionTwoKitCatalog(Guid ActiveKitId, List<ApplicationDefinition>? Applications,
        List<VersionTwoKitDefinition>? Kits);
    private sealed record VersionTwoKitDefinition(Guid Id, string? Name, bool IsVanilla,
        List<Guid>? CleanModeAppIds, List<Guid>? LaunchAppIds);
}
