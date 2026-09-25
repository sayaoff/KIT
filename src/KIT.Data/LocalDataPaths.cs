namespace KIT.Data;

public sealed class LocalDataPaths
{
    public LocalDataPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KIT");
    }

    public string RootDirectory { get; }
    public string ConfigurationFile => Path.Combine(RootDirectory, "configuration.json");
    public string KitsFile => Path.Combine(RootDirectory, "kits.json");
    public string RecoveryFile => Path.Combine(RootDirectory, "active-session.json");
    public string ActivityLogFile => Path.Combine(RootDirectory, "activity.jsonl");
}
