using System.Diagnostics;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Infrastructure.Windows;

public sealed class WindowsSessionActionCoordinator : ISessionActionCoordinator
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(2);

    public async Task<ActionExecutionResult> ApplyAsync(KitDefinition kit, DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var closed = new List<ClosedApplication>();
        var launched = new List<LaunchedApplication>();
        var warnings = new List<string>();

        foreach (var target in kit.CleanModeApps)
        {
            var processes = WindowsProcessFinder.FindByExecutablePath(target.ExecutablePath);
            var closedAny = false;
            foreach (var process in processes)
            {
                using (process)
                {
                    if (await StopProcessAsync(process, target.DisplayName, forceAfterTimeout: true,
                            warnings, cancellationToken))
                        closedAny = true;
                }
            }
            if (closedAny) closed.Add(new ClosedApplication(target.ExecutablePath));
        }

        foreach (var target in kit.LaunchApps)
        {
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
                    launched.Add(new LaunchedApplication(target.ExecutablePath, process.Id));
                    process.Dispose();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"Could not launch {target.DisplayName}: {exception.Message}");
            }
        }

        return new ActionExecutionResult(
            new AppliedKitState(kit.Id, kit.Name, appliedAtUtc, closed, launched), warnings);
    }

    public async Task<RestoreExecutionResult> RestoreAsync(AppliedKitState state,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        foreach (var launched in state.LaunchedApplications)
        {
            if (!WindowsProcessFinder.ProcessMatches(launched.ProcessId, launched.ExecutablePath, out var process))
                continue;
            using (process)
                await StopProcessAsync(process!, Path.GetFileNameWithoutExtension(launched.ExecutablePath),
                    forceAfterTimeout: true, warnings, cancellationToken);
        }

        foreach (var closed in state.ClosedApplications)
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

            if (!forceAfterTimeout) return false;
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
}
