using System.ComponentModel;
using System.Diagnostics;

namespace KIT.Infrastructure.Windows;

internal static class WindowsProcessFinder
{
    public static IReadOnlyList<Process> FindByExecutablePath(string executablePath)
    {
        var expectedPath = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var actualPath = process.MainModule?.FileName;
                if (actualPath is not null && PathsEqual(actualPath, expectedPath))
                    result.Add(process);
                else
                    process.Dispose();
            }
            catch (InvalidOperationException) { process.Dispose(); }
            catch (Win32Exception) { process.Dispose(); }
        }
        return result;
    }

    public static bool ProcessMatches(int processId, string executablePath, out Process? process)
    {
        process = null;
        try
        {
            var candidate = Process.GetProcessById(processId);
            var actualPath = candidate.MainModule?.FileName;
            if (actualPath is not null && PathsEqual(actualPath, executablePath))
            {
                process = candidate;
                return true;
            }
            candidate.Dispose();
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        return false;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}

