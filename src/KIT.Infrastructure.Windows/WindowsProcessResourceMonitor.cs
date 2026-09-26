using System.Diagnostics;
using KIT.Core.Abstractions;
using KIT.Core.Models;

namespace KIT.Infrastructure.Windows;

public sealed class WindowsProcessResourceMonitor : IProcessResourceMonitor
{
    private readonly TimeSpan _interval;
    private readonly List<ResourceSample> _samples = [];
    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;

    public WindowsProcessResourceMonitor(TimeSpan? interval = null) =>
        _interval = interval ?? TimeSpan.FromSeconds(2);

    public Task StartAsync(int processId, Func<ResourceSample, CancellationToken, ValueTask> onSample,
        CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null) throw new InvalidOperationException("Resource monitoring is already active.");
        cancellationToken.ThrowIfCancellationRequested();
        _samples.Clear();
        _cancellation = new CancellationTokenSource();
        _monitorTask = MonitorAsync(processId, onSample, _cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task<SessionMetrics> StopAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null)
        {
            _cancellation!.Cancel();
            try { await _monitorTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            _cancellation.Dispose();
            _cancellation = null;
            _monitorTask = null;
        }

        if (_samples.Count == 0) return SessionMetrics.Empty;
        return new SessionMetrics(
            _samples.Count,
            _samples.Average(sample => sample.CpuPercent),
            _samples.Max(sample => sample.CpuPercent),
            (long)_samples.Average(sample => sample.WorkingSetBytes),
            _samples.Max(sample => sample.WorkingSetBytes));
    }

    private async Task MonitorAsync(int processId,
        Func<ResourceSample, CancellationToken, ValueTask> onSample,
        CancellationToken cancellationToken)
    {
        using var process = Process.GetProcessById(processId);
        var previousCpu = process.TotalProcessorTime;
        var previousTime = Stopwatch.GetTimestamp();

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, cancellationToken);
            try
            {
                process.Refresh();
                if (process.HasExited) break;
                var now = Stopwatch.GetTimestamp();
                var totalCpu = process.TotalProcessorTime;
                var elapsedSeconds = (now - previousTime) / (double)Stopwatch.Frequency;
                var cpuSeconds = (totalCpu - previousCpu).TotalSeconds;
                var cpuPercent = elapsedSeconds <= 0
                    ? 0
                    : Math.Clamp(cpuSeconds / elapsedSeconds / Environment.ProcessorCount * 100, 0, 100);
                var sample = new ResourceSample(DateTimeOffset.UtcNow, cpuPercent, process.WorkingSet64);
                _samples.Add(sample);
                await onSample(sample, cancellationToken);
                previousCpu = totalCpu;
                previousTime = now;
            }
            catch (InvalidOperationException) { break; }
            catch (System.ComponentModel.Win32Exception) { break; }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
