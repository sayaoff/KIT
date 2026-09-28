using System.Diagnostics;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Infrastructure.Windows;

public sealed class WindowsSessionActionCoordinator : ISessionActionCoordinator
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(2);

    public async Task<ActionExecutionResult> ApplyAsync(KitExecutionPlan kit, DateTimeOffset appliedAtUtc,
        Func<AppliedKitState, CancellationToken, Task> onStateChanged,
        CancellationToken cancellationToken = default)
    {
        var closed = new List<ClosedApplication>();
        var launched = new List<LaunchedApplication>();
        var warnings = new List<string>();
        var schedule = kit.CleanModeActions.Select((action, index) =>
                new ScheduledKitAction(action.DelaySeconds, 0, index, action, null))
            .Concat(kit.LaunchAppActions.Select((action, index) =>
                new ScheduledKitAction(action.DelaySeconds, 1, index, null, action)))
            .OrderBy(action => action.DelaySeconds)
            .ThenBy(action => action.Phase)
            .ThenBy(action => action.Order)
            .ToList();
        var timer = Stopwatch.StartNew();

        foreach (var scheduled in schedule)
        {
            var remainingDelay = TimeSpan.FromSeconds(scheduled.DelaySeconds) - timer.Elapsed;
            if (remainingDelay > TimeSpan.Zero)
                await Task.Delay(remainingDelay, cancellationToken);

            if (scheduled.CleanMode is { } cleanAction)
            {
                var target = cleanAction.Application;
                var processes = WindowsProcessFinder.FindByExecutablePath(target.ExecutablePath);
                var closedAny = false;
                foreach (var process in processes)
                {
                    using (process)
                    {
                        if (await StopProcessAsync(process, target.DisplayName,
                                cleanAction.CloseMode is CleanCloseMode.ForceIfNeeded, warnings, cancellationToken))
                            closedAny = true;
                    }
                }
                if (!closedAny) continue;
                closed.Add(new ClosedApplication(target.ExecutablePath)
                {
                    RestoreAfterSession = cleanAction.RestoreAfterSession
                });
            }
            else if (scheduled.Launch is { } launchAction)
            {
                var target = launchAction.Application;
                var existing = WindowsProcessFinder.FindByExecutablePath(target.ExecutablePath);
                if (existing.Count > 0)
                {
                    foreach (var process in existing) process.Dispose();
                    continue;
                }

                try
                {
                    var process = Process.Start(new ProcessStartInfo(target.ExecutablePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath) ?? Environment.CurrentDirectory
                    });
                    if (process is null)
                        warnings.Add($"Could not launch {target.DisplayName}.");
                    else
                    {
                        launched.Add(new LaunchedApplication(target.ExecutablePath, process.Id)
                        {
                            CloseAfterSession = launchAction.CloseAfterSession
                        });
                        process.Dispose();
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    warnings.Add($"Could not launch {target.DisplayName}: {exception.Message}");
                }
            }

            await onStateChanged(CreateState(kit, appliedAtUtc, closed, launched), cancellationToken);
        }

        return new ActionExecutionResult(CreateState(kit, appliedAtUtc, closed, launched), warnings);
    }

    public async Task<RestoreExecutionResult> RestoreAsync(AppliedKitState state,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        foreach (var launched in state.LaunchedApplications.Where(application => application.CloseAfterSession))
        {
            if (!WindowsProcessFinder.ProcessMatches(launched.ProcessId, launched.ExecutablePath, out var process))
                continue;
            using (process)
                await StopProcessAsync(process!, Path.GetFileNameWithoutExtension(launched.ExecutablePath),
                    forceAfterTimeout: true, warnings, cancellationToken);
        }

        foreach (var closed in state.ClosedApplications.Where(application => application.RestoreAfterSession))
        {
            var running = WindowsProcessFinder.FindByExecutablePath(closed.ExecutablePath);
            if (running.Count > 0)
            {
                foreach (var process in running) process.Dispose();
                continue;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo(closed.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(closed.ExecutablePath) ?? Environment.CurrentDirectory
                });
                process?.Dispose();
                if (process is null) warnings.Add($"Could not restore {closed.ExecutablePath}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"Could not restore {closed.ExecutablePath}: {exception.Message}");
            }
        }
        return new RestoreExecutionResult(warnings);
    }

    private static AppliedKitState CreateState(KitExecutionPlan kit, DateTimeOffset appliedAtUtc,
        IEnumerable<ClosedApplication> closed, IEnumerable<LaunchedApplication> launched) =>
        new(kit.KitId, kit.KitName, appliedAtUtc, [.. closed], [.. launched]);

    private static async Task<bool> StopProcessAsync(Process process, string displayName,
        bool forceAfterTimeout, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited) return false;
            var closeRequested = process.CloseMainWindow();
            if (closeRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(GracefulCloseTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                    return true;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }

            if (!forceAfterTimeout)
            {
                if (process.HasExited) return true;
                warnings.Add(closeRequested
                    ? $"{displayName} did not exit within two seconds; Normal mode will not force it."
                    : $"{displayName} does not expose a normal close request and was left running.");
                return closeRequested;
            }
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            warnings.Add($"{displayName} stayed active in the background and was terminated for Clean Mode.");
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception exception)
        {
            warnings.Add($"Could not close {displayName}: {exception.Message}");
            return false;
        }
    }

    private sealed record ScheduledKitAction(int DelaySeconds, int Phase, int Order,
        CleanModeExecution? CleanMode, LaunchAppExecution? Launch);
}
