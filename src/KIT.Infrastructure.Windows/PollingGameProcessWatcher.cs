using System.Diagnostics;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Infrastructure.Windows;

public sealed class PollingGameProcessWatcher : IGameProcessWatcher
{
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<int, string> _trackedProcesses = [];
    private CancellationTokenSource? _watchCancellation;
    private Task? _watchTask;

    public PollingGameProcessWatcher(TimeSpan? pollInterval = null) =>
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);

    public Task StartAsync(
        string executablePath,
        Func<GameProcessChange, CancellationToken, ValueTask> onChange,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(onChange);

        if (_watchTask is not null)
        {
            throw new InvalidOperationException("The process watcher is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _watchCancellation = new CancellationTokenSource();
        _watchTask = WatchLoopAsync(Path.GetFullPath(executablePath), onChange, _watchCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_watchTask is null)
        {
            return;
        }

        _watchCancellation!.Cancel();
        try
        {
            await _watchTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_watchCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _watchCancellation.Dispose();
            _watchCancellation = null;
            _watchTask = null;
            _trackedProcesses.Clear();
        }
    }

    private async Task WatchLoopAsync(
        string expectedPath,
        Func<GameProcessChange, CancellationToken, ValueTask> onChange,
        CancellationToken cancellationToken)
    {
        var processName = Path.GetFileNameWithoutExtension(expectedPath);

        while (!cancellationToken.IsCancellationRequested)
        {
            var matchingProcesses = FindMatchingProcesses(processName, expectedPath);

            foreach (var process in matchingProcesses)
            {
                if (_trackedProcesses.TryAdd(process.Id, expectedPath))
                {
                    await onChange(
                        new GameProcessChange(GameProcessChangeKind.Started, process.Id, expectedPath),
                        cancellationToken);
                }
            }

            foreach (var tracked in _trackedProcesses.ToArray())
            {
                if (matchingProcesses.All(process => process.Id != tracked.Key))
                {
                    _trackedProcesses.Remove(tracked.Key);
                    await onChange(
                        new GameProcessChange(GameProcessChangeKind.Stopped, tracked.Key, tracked.Value),
                        cancellationToken);
                }
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }

    private static IReadOnlyList<ProcessIdentity> FindMatchingProcesses(string processName, string expectedPath)
    {
        var result = new List<ProcessIdentity>();

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var actualPath = process.MainModule?.FileName;
                    if (actualPath is not null && PathsEqual(actualPath, expectedPath))
                    {
                        result.Add(new ProcessIdentity(process.Id));
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // An inaccessible process is ignored. KIT never escalates privileges to inspect it.
                }
            }
        }

        return result;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed record ProcessIdentity(int Id);
}
