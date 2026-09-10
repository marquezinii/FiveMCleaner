using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace Ralven.Windows.Infrastructure;

public sealed record FiveMResourceUsageSnapshot(
    double? CpuPercent,
    double? WorkingSetGiB,
    int? ProcessCount);

/// <summary>
/// Reads aggregate CPU and working-set usage only from verified FiveM Legacy
/// processes. It uses process accounting exposed by Windows; it never reads
/// process memory contents, injects code, hooks rendering, or changes state.
/// </summary>
public sealed class WindowsFiveMResourceUsageInspector
{
    private static readonly TimeSpan MaximumComparableInterval = TimeSpan.FromSeconds(3);
    private Dictionary<FiveMProcessIdentity, TimeSpan> previousCpuTimes = [];
    private DateTimeOffset? previousCapture;
    private string? previousRoot;

    public FiveMResourceUsageSnapshot GetSnapshot(
        string installationRoot,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRoot(installationRoot, out var normalizedRoot))
        {
            Reset();
            return new(null, null, null);
        }

        if (!string.Equals(previousRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            previousRoot = normalizedRoot;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception) when (IsProcessInspectionFailure(exception))
        {
            ResetSamples(capturedAt);
            return new(null, null, null);
        }

        var currentCpuTimes = new Dictionary<FiveMProcessIdentity, TimeSpan>();
        long workingSetBytes = 0;
        var processCount = 0;
        var indeterminate = false;
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadProcessName(process, out var processName)
                    || !WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(processName))
                {
                    continue;
                }

                if (!TryReadProcess(process, processName, normalizedRoot, out var sample))
                {
                    indeterminate = true;
                    continue;
                }

                currentCpuTimes[sample.Identity] = sample.TotalProcessorTime;
                workingSetBytes = checked(workingSetBytes + sample.WorkingSetBytes);
                processCount++;
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        if (indeterminate)
        {
            ResetSamples(capturedAt);
            return new(null, null, null);
        }

        var cpuPercent = previousCapture is { } previous
            ? CalculateCpuPercent(
                previousCpuTimes,
                currentCpuTimes,
                capturedAt - previous,
                Environment.ProcessorCount)
            : null;

        previousCpuTimes = currentCpuTimes;
        previousCapture = capturedAt;
        const double bytesPerGiB = 1024d * 1024 * 1024;
        return new(
            processCount == 0 ? null : cpuPercent,
            processCount == 0 ? null : workingSetBytes / bytesPerGiB,
            processCount);
    }

    internal static double? CalculateCpuPercent(
        IReadOnlyDictionary<FiveMProcessIdentity, TimeSpan> previous,
        IReadOnlyDictionary<FiveMProcessIdentity, TimeSpan> current,
        TimeSpan elapsed,
        int logicalProcessorCount)
    {
        if (elapsed <= TimeSpan.Zero
            || elapsed > MaximumComparableInterval
            || logicalProcessorCount <= 0)
        {
            return null;
        }

        var processorTime = TimeSpan.Zero;
        var hasComparableProcess = false;
        foreach (var (identity, currentTime) in current)
        {
            if (!previous.TryGetValue(identity, out var previousTime))
            {
                continue;
            }

            var delta = currentTime - previousTime;
            if (delta >= TimeSpan.Zero)
            {
                processorTime += delta;
                hasComparableProcess = true;
            }
        }

        if (!hasComparableProcess)
        {
            return null;
        }

        return Math.Clamp(
            processorTime.TotalMilliseconds
                / (elapsed.TotalMilliseconds * logicalProcessorCount)
                * 100,
            0,
            100);
    }

    private static bool TryReadProcess(
        Process process,
        string processName,
        string normalizedRoot,
        out FiveMProcessSample sample)
    {
        sample = default;
        try
        {
            var imagePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(imagePath)
                || !WindowsFiveMProcessInspector.IsVerifiedFiveMExecutablePath(
                    processName,
                    imagePath,
                    normalizedRoot))
            {
                return false;
            }

            SafePath.EnsureNoReparsePoints(imagePath);
            sample = new FiveMProcessSample(
                new FiveMProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks),
                process.TotalProcessorTime,
                Math.Max(0, process.WorkingSet64));
            return true;
        }
        catch (Exception exception) when (IsProcessInspectionFailure(exception)
            || exception is ArgumentException or IOException)
        {
            return false;
        }
    }

    private static bool TryReadProcessName(Process process, out string processName)
    {
        try
        {
            processName = process.ProcessName;
            return true;
        }
        catch (Exception exception) when (IsProcessInspectionFailure(exception))
        {
            processName = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizeRoot(string installationRoot, out string normalizedRoot)
    {
        normalizedRoot = string.Empty;
        try
        {
            normalizedRoot = SafePath.Normalize(installationRoot);
            var appRoot = Path.Combine(normalizedRoot, "FiveM.app");
            if (!Directory.Exists(normalizedRoot) || !Directory.Exists(appRoot))
            {
                return false;
            }

            SafePath.EnsureNoReparsePoints(normalizedRoot);
            SafePath.EnsureNoReparsePoints(appRoot);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or SecurityException)
        {
            normalizedRoot = string.Empty;
            return false;
        }
    }

    private static bool IsProcessInspectionFailure(Exception exception) =>
        exception is InvalidOperationException
            or Win32Exception
            or NotSupportedException
            or UnauthorizedAccessException
            or SecurityException;

    private void Reset()
    {
        previousRoot = null;
        ResetSamples(capturedAt: null);
    }

    private void ResetSamples(DateTimeOffset? capturedAt)
    {
        previousCpuTimes = [];
        previousCapture = capturedAt;
    }

    private readonly record struct FiveMProcessSample(
        FiveMProcessIdentity Identity,
        TimeSpan TotalProcessorTime,
        long WorkingSetBytes);
}

internal readonly record struct FiveMProcessIdentity(int ProcessId, long StartTimeUtcTicks);
