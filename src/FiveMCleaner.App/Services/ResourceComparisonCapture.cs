using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Captures a coarse before/after resource snapshot around an optimization run
/// and detects regressions this product can attribute with reasonable
/// confidence. Extracted from <see cref="AppOptimizationService"/> to isolate
/// the comparison logic and make the pure regression rules independently
/// testable.
/// </summary>
internal sealed class ResourceComparisonCapture
{
    private readonly ILocalizationService localization;

    public ResourceComparisonCapture(ILocalizationService localization)
    {
        this.localization = localization;
    }

    public ResourceComparisonSnapshot? TryCaptureSnapshot()
    {
        try
        {
            var resources = new WindowsResourceUsageInspector().GetSnapshot();
            var thermal = new WindowsThermalInspector().GetSnapshot();
            var network = new WindowsNetworkHealthInspector().GetSnapshot();
            var system = new WindowsSystemResourceInspector().GetSnapshot();
            return new ResourceComparisonSnapshot
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CpuPercent = resources.CpuPercent,
                GpuPercent = resources.GpuPercent,
                DiskPercent = resources.DiskPercent,
                AvailableMemoryGiB = system.AvailableMemoryBytes / 1024d / 1024d / 1024d,
                ThermalElevated = thermal is { IsAvailable: true, HighestCelsius: >= 85 },
                NetworkIssuesDetected = network.DiscardedPackets > 0 || network.ErrorPackets > 0
            };
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // A comparação antes/depois é um extra informativo; nunca deve
            // interromper nem fazer a otimização real falhar.
            return null;
        }
    }

    public async Task<OptimizationComparisonResult?> CaptureComparisonAsync(
        ResourceComparisonSnapshot? beforeSnapshot)
    {
        if (beforeSnapshot is null)
        {
            return null;
        }

        // A curta espera deixa a atividade de disco/CPU da própria otimização
        // assentar antes de medir "depois", evitando comparar o trabalho da
        // otimização em si com o estado real pós-otimização.
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
        var afterSnapshot = TryCaptureSnapshot();
        if (afterSnapshot is null)
        {
            return null;
        }

        return BuildComparison(beforeSnapshot, afterSnapshot);
    }

    private OptimizationComparisonResult BuildComparison(
        ResourceComparisonSnapshot before,
        ResourceComparisonSnapshot after)
    {
        var reasonKeys = ComputeRegressionReasonKeys(before, after);
        var cpuName = GetCpuName(localization);
        var gpuNames = GetGpuNames();
        var totalMemoryGiB = new WindowsSystemResourceInspector().GetSnapshot().TotalMemoryBytes
            / 1024d / 1024d / 1024d;

        return new OptimizationComparisonResult
        {
            HardwareProfileSignature = HardwareProfileSignature.Compute(cpuName, gpuNames, totalMemoryGiB),
            Before = before,
            After = after,
            RegressionSuspected = reasonKeys.Count > 0,
            RegressionReasons = reasonKeys.Select(localization.GetString).ToArray()
        };
    }

    /// <summary>
    /// Pure regression-detection rule set, kept intentionally conservative:
    /// only signals this product can attribute with reasonable confidence to
    /// something having gotten worse (never derived from FPS, which this
    /// product does not measure live). Returns localization keys so this can
    /// be tested without a real <see cref="ILocalizationService"/>.
    /// </summary>
    internal static IReadOnlyList<string> ComputeRegressionReasonKeys(
        ResourceComparisonSnapshot before,
        ResourceComparisonSnapshot after)
    {
        var reasons = new List<string>();
        if (!before.ThermalElevated && after.ThermalElevated)
        {
            reasons.Add("Comparison.Reason.NewThermalSignal");
        }

        if (before.AvailableMemoryGiB > 1
            && after.AvailableMemoryGiB < before.AvailableMemoryGiB * 0.5)
        {
            reasons.Add("Comparison.Reason.MemoryDropped");
        }

        return reasons;
    }

    public static string GetCpuName(ILocalizationService localization)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim()
                ?? localization.GetString("Diagnosis.CpuUnknown");
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return localization.GetString("Diagnosis.CpuUnknown");
        }
    }

    public static IReadOnlyList<string> GetGpuNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var video = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return [];
            }

            foreach (var deviceKeyName in video.GetSubKeyNames())
            {
                using var device = video.OpenSubKey(deviceKeyName);
                if (device is null)
                {
                    continue;
                }

                foreach (var adapterKeyName in device.GetSubKeyNames()
                             .Where(name => name.Length == 4 && name.All(char.IsDigit)))
                {
                    using var adapter = device.OpenSubKey(adapterKeyName);
                    var name = (adapter?.GetValue("DriverDesc") as string)?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)
                        && !name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch
        {
            // O diagnóstico continua sem iniciar PowerShell, WMI ou ferramentas externas.
        }

        return names.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
