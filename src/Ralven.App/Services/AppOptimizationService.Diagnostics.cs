using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Actions;
using Ralven.Windows.Infrastructure;
using Microsoft.Win32;

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
            var installationTask = Task.Run(() => DetectFiveMInstallation(), cancellationToken);
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
            var gtaV = GtaVLocator.Detect(installation.Root);
            var gtaVIsRunning = new WindowsGtaVProcessInspector()
                .IsRunningFrom(gtaV.InstallationRoot);
            detectedLegacyRoot = installation.Edition == FiveMEdition.Legacy
                ? installation.Root
                : null;

            var systemResources = await systemResourcesTask.ConfigureAwait(false);
            StartupTrace.Mark("system-resources-ready");
            var cacheBytes = installation.Edition == FiveMEdition.Legacy && installation.Root is not null
                ? GetLegacyServerCacheBytes(installation.Root, cancellationToken)
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
            var running = IsFiveMRunning();
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
                Edition = installation.Edition,
                IsFiveMRunning = running,
                FiveMRoot = installation.Root,
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

    private static IReadOnlyList<string> BuildDiagnosticNotices(
        GtaVInstallationInfo gtaV,
        long cacheBytes,
        double freeDiskGiB)
    {
        var notices = new List<string>();
        notices.Add(gtaV.IsInstalled
            ? "GTA V Legacy detectado; executável e settings.xml entrarão nas ações compatíveis."
            : "O executável do GTA V Legacy não foi confirmado automaticamente.");
        if (cacheBytes >= 8L * 1024 * 1024 * 1024)
        {
            notices.Add("O cache regenerável de servidores está acima de 8 GB; o reparo inteligente pode liberar espaço.");
        }
        else if (freeDiskGiB < 15)
        {
            notices.Add("Há pouco espaço livre na unidade do Windows; limpezas seguras podem melhorar a responsividade geral.");
        }
        else
        {
            notices.Add("O PC está estável; o perfil sugerido prioriza consistência sem tweaks de risco.");
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

    private static (FiveMEdition Edition, string? Root) DetectFiveMInstallation()
    {
        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localAppData, "FiveM"));

        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (var subkeyName in uninstall.GetSubKeyNames())
            {
                using var subkey = uninstall.OpenSubKey(subkeyName);
                var displayName = subkey?.GetValue("DisplayName") as string;
                var installLocation = subkey?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(displayName)
                    && displayName.Contains("FiveM", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(installLocation))
                {
                    if (displayName.Contains("Enhanced", StringComparison.OrdinalIgnoreCase))
                    {
                        return (FiveMEdition.Enhanced, Path.GetFullPath(installLocation));
                    }

                    candidates.Add(installLocation);
                }
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(fullPath, "FiveM.app", "data")))
                {
                    return (FiveMEdition.Legacy, fullPath);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed registry entries and continue with known locations.
            }
        }

        var enhancedCandidate = Path.Combine(localAppData, "FiveM Enhanced");
        return Directory.Exists(enhancedCandidate)
            ? (FiveMEdition.Enhanced, enhancedCandidate)
            : (FiveMEdition.Unknown, null);
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

    private static bool IsFiveMRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(process.ProcessName))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                }
            }
        }

        return false;
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
