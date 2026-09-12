using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Actions;
using Ralven.Windows.Infrastructure;

namespace Ralven.App.Services;

/// <summary>
/// Coleta somente leitura do diagnóstico do PC e da instalação do FiveM,
/// incluindo o benchmark opcional do GTA V e a detecção de software de
/// transmissão.
/// </summary>
public sealed partial class AppOptimizationService
{
    public async Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        StartupTrace.Mark("diagnosis-start");
        if (demoMode && useSyntheticDiagnostic)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return demoSimulator.CreateDiagnostic();
        }

        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Run independent I/O-bound operations concurrently to reduce total diagnosis time
            var installationTask = Task.Run(() => DetectFiveMInstallationAsync(cancellationToken), cancellationToken);
            var systemResourcesTask = Task.Run(
                () => new WindowsSystemResourceInspector().GetSnapshot(),
                cancellationToken);
            var gpuDetailsTask = Task.Run(
                () => new WindowsGpuDetailsInspector().GetSnapshot(),
                cancellationToken);
            var cpuDetailsTask = Task.Run(
                () => new WindowsCpuInspector().GetSnapshot(),
                cancellationToken);
            var cpuNameTask = Task.Run(() => ResourceComparisonCapture.GetCpuName(localization), cancellationToken);
            var memoryLayoutTask = Task.Run(GetMemoryModuleLayout, cancellationToken);
            var osLabelTask = Task.Run(GetOperatingSystemLabel, cancellationToken);
            var archLabelTask = Task.Run(GetArchitectureLabel, cancellationToken);

            var installation = await installationTask.ConfigureAwait(false);
            StartupTrace.Mark("installation-ready");
            var gtaV = GtaVLocator.Detect(installation.Installation?.Root);
            var gtaVIsRunning = new WindowsGtaVProcessInspector()
                .IsRunningFrom(gtaV.InstallationRoot);
            detectedLegacyRoot = installation.IsLegacy
                ? installation.Installation!.Root
                : null;

            var systemResources = await systemResourcesTask.ConfigureAwait(false);
            StartupTrace.Mark("system-resources-ready");
            var cacheBytes = installation.IsLegacy
                ? GetLegacyServerCacheBytes(installation.Installation!.Root, cancellationToken)
                : 0L;

            var gpuDetails = await gpuDetailsTask.ConfigureAwait(false);
            var cpuDetails = await cpuDetailsTask.ConfigureAwait(false);
            StartupTrace.Mark("gpu-cpu-ready");
            var gpuNames = gpuDetails
                .Select(gpu => gpu.DriverDescription)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var gpuWasIdentified = gpuNames.Length > 0;
            var gpuName = gpuWasIdentified
                ? string.Join(" / ", gpuNames)
                : localization.GetString("Diagnosis.GpuFallback");

            var streamingSoftware = DetectStreamingSoftware(cancellationToken);
            var memoryGiB = systemResources.TotalMemoryBytes / 1024d / 1024d / 1024d;
            var availableMemoryGiB = systemResources.AvailableMemoryBytes / 1024d / 1024d / 1024d;
            var logicalProcessorCount = systemResources.LogicalProcessorCount;
            var freeDiskGiB = systemResources.SystemDriveFreeBytes / 1024d / 1024d / 1024d;
            var processInspector = new WindowsFiveMProcessInspector();
            var running = installation.Installation is { } detected
                ? processInspector.IsRunningFrom(detected.Root)
                : processInspector.IsAnyRunning();
            StartupTrace.Mark("processes-ready");

            var assessment = HardwareProfileAdvisor.Assess(
                memoryGiB,
                availableMemoryGiB,
                freeDiskGiB,
                cpuDetails,
                gpuDetails);

            var notices = BuildDiagnosticNotices(gtaV, cacheBytes, freeDiskGiB);

            // Await remaining parallel tasks
            var cpuName = await cpuNameTask.ConfigureAwait(false);
            var memoryModuleLayout = await memoryLayoutTask.ConfigureAwait(false);
            var osLabel = await osLabelTask.ConfigureAwait(false);
            var archLabel = await archLabelTask.ConfigureAwait(false);
            StartupTrace.Mark("diagnosis-ready");

            return new AppDiagnostic
            {
                Edition = installation.Status switch
                {
                    FiveMInstallationDetectionStatus.Legacy => FiveMEdition.Legacy,
                    FiveMInstallationDetectionStatus.Enhanced => FiveMEdition.Enhanced,
                    _ => FiveMEdition.Unknown
                },
                IsFiveMRunning = running,
                FiveMRoot = installation.Installation?.Root,
                GtaVDetected = gtaV.IsInstalled,
                GtaVIsRunning = gtaVIsRunning,
                GtaVExecutablePath = gtaV.ExecutablePath,
                GtaVGraphicsSettingsPath = gtaV.GraphicsSettingsPath,
                CpuName = cpuName,
                GpuName = gpuName,
                GpuNames = gpuNames,
                TotalMemoryGiB = memoryGiB,
                AvailableMemoryGiB = availableMemoryGiB,
                MemoryModuleLayout = memoryModuleLayout,
                LogicalProcessorCount = logicalProcessorCount,
                FreeDiskGiB = freeDiskGiB,
                LegacyCacheBytes = cacheBytes,
                OsLabel = osLabel,
                SystemArchitecture = archLabel,
                ReadinessScore = assessment.ReadinessScore,
                RecommendedProfile = assessment.RecommendedProfile,
                PerformancePressure = assessment.PerformancePressure,
                StreamingSoftware = streamingSoftware,
                Notices = notices
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<string> BuildDiagnosticNotices(
        GtaVInstallationInfo gtaV,
        long cacheBytes,
        double freeDiskGiB)
    {
        var notices = new List<string>();
        notices.Add(localization.GetString(gtaV.IsInstalled
            ? "Diagnostics.Notice.GtaVDetected"
            : "Diagnostics.Notice.GtaVNotDetected"));
        if (cacheBytes >= 8L * 1024 * 1024 * 1024)
        {
            notices.Add(localization.GetString("Diagnostics.Notice.LargeCache"));
        }
        else if (freeDiskGiB < 15)
        {
            notices.Add(localization.GetString("Diagnostics.Notice.LowDiskSpace"));
        }
        else
        {
            notices.Add(localization.GetString("Diagnostics.Notice.Stable"));
        }

        return notices;
    }

    public async Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
        int iterations,
        CancellationToken cancellationToken = default)
    {
        if (iterations < 1 || iterations > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "demo-mode",
                Iterations = []
            };
        }

        var gtaV = GtaVLocator.Detect(detectedLegacyRoot);
        if (!gtaV.IsInstalled || gtaV.ExecutablePath is null)
        {
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "gtav-not-detected",
                Iterations = []
            };
        }

        var running = new WindowsGtaVProcessInspector().IsRunningFrom(gtaV.InstallationRoot);
        if (running)
        {
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "gtav-still-running",
                Iterations = []
            };
        }

        var runner = new WindowsGtaVBenchmarkRunner();
        var result = await runner.RunAsync(
            gtaV.ExecutablePath,
            iterations,
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);

        return new AppGtaVBenchmarkResult
        {
            Succeeded = result.Succeeded,
            FailureReason = result.FailureReason,
            Iterations = result.Iterations.Select(ToAppIteration).ToArray(),
            Median = result.Median is null ? null : ToAppIteration(result.Median)
        };
    }

    private static AppGtaVBenchmarkIteration ToAppIteration(GtaVBenchmarkIterationResult iteration)
    {
        return new AppGtaVBenchmarkIteration(
            iteration.AverageFps,
            iteration.MinimumFps,
            iteration.OnePercentLowFps,
            iteration.PointOnePercentLowFps,
            iteration.AverageFrametimeMs,
            iteration.PeakFrametimeMs,
            iteration.SampleCount);
    }

    private static StreamingSoftwareSnapshot DetectStreamingSoftware(
        CancellationToken cancellationToken)
    {
        try
        {
            return new StreamingSoftwareDetector().Detect(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return StreamingSoftwareClassifier.CreateSnapshot(
                [],
                [],
                [],
                DateTimeOffset.UtcNow,
                processScanComplete: false,
                installationScanComplete: false);
        }
    }

    private async Task<FiveMInstallationDetectionResult> DetectFiveMInstallationAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var cachedRoot = demoMode
            ? null
            : await FiveMInstallationCache.ReadValidRootAsync(
                fiveMInstallationCachePath,
                cancellationToken).ConfigureAwait(false);
        var result = new FiveMInstallationLocator().Detect(settings.ManualFiveMInstallationRoot, cachedRoot);
        if (!demoMode && result.IsLegacy)
        {
            try
            {
                await FiveMInstallationCache.WriteAsync(
                    fiveMInstallationCachePath,
                    result.Installation!,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A discovery result remains valid even if the optional cache cannot be updated.
            }
        }

        return result;
    }

    private static long GetLegacyServerCacheBytes(string root, CancellationToken cancellationToken)
    {
        var dataRoot = Path.Combine(root, "FiveM.app", "data");
        var allowed = new[] { "server-cache", "server-cache-priv" };
        long total = 0;
        foreach (var name in allowed)
        {
            var path = Path.Combine(dataRoot, name);
            if (!Directory.Exists(path))
            {
                continue;
            }

            var rootInfo = new DirectoryInfo(path);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var pending = new Stack<DirectoryInfo>();
            pending.Push(rootInfo);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = directory.EnumerateFileSystemInfos();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (entry is FileInfo file)
                    {
                        total += file.Length;
                    }
                    else if (entry is DirectoryInfo child)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return total;
    }

    private static string GetArchitectureLabel() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        _ => RuntimeInformation.OSArchitecture.ToString()
    };

    private static string GetOperatingSystemLabel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        return Environment.OSVersion.Version.Build >= 22000
            ? "Microsoft Windows 11"
            : "Microsoft Windows 10";
    }

    private static string? GetMemoryModuleLayout()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            var modules = searcher.Get()
                .Cast<ManagementObject>()
                .Select(module => module["Capacity"])
                .OfType<ulong>()
                .Select(bytes => Math.Round(bytes / 1024d / 1024d / 1024d))
                .Where(size => size > 0)
                .GroupBy(size => size)
                .OrderByDescending(group => group.Key)
                .Select(group => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{group.Count()}×{group.Key:0} GB"))
                .ToArray();

            return modules.Length == 0 ? null : string.Join(" + ", modules);
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
