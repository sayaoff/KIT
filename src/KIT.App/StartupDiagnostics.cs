using System.Text;
using System.IO;
using KIT.Data;

namespace KIT.App;

internal static class StartupDiagnostics
{
    public static void Write(LocalDataPaths paths, string message)
    {
        try
        {
            Directory.CreateDirectory(paths.RootDirectory);
            var line = $"{DateTimeOffset.Now:O} [PID {Environment.ProcessId}] {message}{Environment.NewLine}";
            File.AppendAllText(paths.StartupLogFile, line, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never prevent KIT from opening.
        }
    }
}
