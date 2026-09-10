using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Infrastructure;
using Microsoft.Win32;

namespace Ralven.Windows.Actions;

/// <summary>
/// Read-only hardware/system diagnostics. Every action here always resolves to
/// <see cref="WindowsActionApplyResult.NoChange"/> with an honest message; none
/// of them ever writes to the system, installs a driver, or claims data it
/// could not actually read.
/// </summary>
public sealed class CpuDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const double SignificantClockDropRatio = 0.5d;

    private readonly ICpuInspector inspector;

    public CpuDetailsDiagnosisAction(ICpuInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCpuDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(CpuSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return WindowsActionText.Format("ActionResults.CpuDetails.Unavailable");
        }

        var message = WindowsActionText.Format(
            "ActionResults.CpuDetails.Summary",
            snapshot.PhysicalCores,
            snapshot.LogicalThreads,
            snapshot.CurrentClockMhz,
            snapshot.MaxClockMhz);
        if (snapshot.MaxClockMhz > 0 && snapshot.CurrentClockMhz < snapshot.MaxClockMhz * SignificantClockDropRatio)
        {
            message += WindowsActionText.Format("ActionResults.CpuDetails.LowClockSuffix");
        }

        return message;
    }
}

public sealed class GpuDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IGpuDetailsInspector inspector;

    public GpuDetailsDiagnosisAction(IGpuDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseGpuDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(IReadOnlyList<GpuAdapterDetails> adapters)
    {
        if (adapters.Count == 0)
        {
            return WindowsActionText.Format("ActionResults.GpuDetails.Unavailable");
        }

        var parts = adapters.Select(adapter =>
        {
            var vram = adapter.VramBytes is > 0
                ? WindowsActionText.Format(
                    "ActionResults.GpuDetails.Vram",
                    adapter.VramBytes.Value / (double)DiagnosticSignals.GiB)
                : WindowsActionText.Format("ActionResults.GpuDetails.VramUnavailable");
            var kind = adapter.KindGuess switch
            {
                GpuKindGuess.LikelyIntegrated => WindowsActionText.Format("ActionResults.GpuDetails.Integrated"),
                GpuKindGuess.LikelyDiscrete => WindowsActionText.Format("ActionResults.GpuDetails.Discrete"),
                _ => WindowsActionText.Format("ActionResults.GpuDetails.UnknownKind")
            };
            return WindowsActionText.Format("ActionResults.GpuDetails.Adapter", adapter.DriverDescription, vram, kind);
        });

        return WindowsActionText.Format("ActionResults.GpuDetails.Summary", string.Join("; ", parts));
    }
}

public sealed class RamDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IRamDetailsInspector inspector;

    public RamDetailsDiagnosisAction(IRamDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseRamDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(RamDetailsSnapshot snapshot)
    {
        if (snapshot.Modules.Count == 0)
        {
            return WindowsActionText.Format("ActionResults.RamDetails.Unavailable");
        }

        var count = snapshot.Modules.Count;
        var configured = snapshot.Modules
            .Select(module => module.ConfiguredClockMhz)
            .Where(clock => clock > 0)
            .DefaultIfEmpty(0u)
            .Max();

        var frequencyLabel = configured > 0
            ? WindowsActionText.Format("ActionResults.RamDetails.ConfiguredClock", configured)
            : WindowsActionText.Format("ActionResults.RamDetails.ClockUnavailable");

        return WindowsActionText.Format("ActionResults.RamDetails.Summary", count, frequencyLabel);
    }
}

public sealed class StorageHealthDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IStorageHealthInspector inspector;

    public StorageHealthDiagnosisAction(IStorageHealthInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseStorageHealth);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(StorageHealthSnapshot snapshot)
    {
        if (snapshot.Disks.Count == 0)
        {
            return WindowsActionText.Format("ActionResults.StorageHealth.Unavailable");
        }

        var unhealthyCount = snapshot.Disks.Count(disk => !disk.IsHealthy);
        var summary = string.Join("; ", snapshot.Disks.Select(disk =>
            WindowsActionText.Format(
                "ActionResults.StorageHealth.Disk",
                disk.FriendlyName,
                disk.MediaTypeLabel,
                disk.HealthStatusLabel)));

        return unhealthyCount > 0
            ? WindowsActionText.Format(
                unhealthyCount == 1
                    ? "ActionResults.StorageHealth.WarningSingular"
                    : "ActionResults.StorageHealth.Warning",
                unhealthyCount,
                summary)
            : WindowsActionText.Format("ActionResults.StorageHealth.Healthy", summary);
    }
}

public sealed class DriverVersionsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const int OldVideoDriverThresholdMonths = 18;

    private readonly IDriverVersionInspector inspector;

    public DriverVersionsDiagnosisAction(IDriverVersionInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDriverVersions);

    protected override string Describe()
    {
        var buildLabel = OperatingSystem.IsWindows()
            ? WindowsActionText.Format("ActionResults.DriverVersions.Build", Environment.OSVersion.Version.Build)
            : WindowsActionText.Format("ActionResults.DriverVersions.UnknownBuild");
        var snapshot = inspector.GetSnapshot();
        var message = WindowsActionText.Format("ActionResults.DriverVersions.Summary", buildLabel, Classify(snapshot));
        var oldDriverWarning = ClassifyOldDrivers(snapshot, DateTimeOffset.UtcNow);
        return oldDriverWarning is null ? message : $"{message} {oldDriverWarning}";
    }

    internal static string Classify(DriverVersionSnapshot snapshot)
    {
        var groups = new (string Label, IReadOnlyList<DriverVersionInfo> Items)[]
        {
            (WindowsActionText.Format("ActionResults.DriverVersions.Video"), snapshot.Video),
            (WindowsActionText.Format("ActionResults.DriverVersions.Network"), snapshot.Network),
            (WindowsActionText.Format("ActionResults.DriverVersions.Audio"), snapshot.Audio),
            (WindowsActionText.Format("ActionResults.DriverVersions.Chipset"), snapshot.Chipset),
            (WindowsActionText.Format("ActionResults.DriverVersions.Storage"), snapshot.Storage),
            ("USB", snapshot.Usb),
            ("Bluetooth", snapshot.Bluetooth)
        };

        var parts = groups
            .Where(group => group.Items.Count > 0)
            .Select(group => $"{group.Label}: {string.Join(", ", group.Items.Select(item => $"{item.DeviceName} {item.DriverVersion}"))}");

        var joined = string.Join(" | ", parts);
        return string.IsNullOrEmpty(joined)
            ? WindowsActionText.Format("ActionResults.DriverVersions.Unavailable")
            : joined;
    }

    /// <summary>
    /// Flags a video driver whose <c>DriverDate</c> (WMI-reported, the same
    /// date shown in Device Manager's Driver tab) is older than
    /// <see cref="OldVideoDriverThresholdMonths"/> months -- an objective,
    /// verifiable signal rather than guessing from the version string, which
    /// vendors format inconsistently. Only video drivers are checked: the
    /// graphics driver is what this product's optimizations actually depend on.
    /// Returns null when there is nothing to flag (date unavailable, or driver
    /// recent enough) -- never a false alarm.
    /// </summary>
    internal static string? ClassifyOldDrivers(DriverVersionSnapshot snapshot, DateTimeOffset now)
    {
        var threshold = now.AddMonths(-OldVideoDriverThresholdMonths);
        var old = snapshot.Video
            .Where(item => item.DriverDate is { } date && date < threshold)
            .ToArray();
        if (old.Length == 0)
        {
            return null;
        }

        var names = string.Join(", ", old.Select(item =>
            $"{item.DeviceName} ({item.DriverDate!.Value:yyyy-MM})"));
        return WindowsActionText.Format(
            "ActionResults.DriverVersions.OldVideoDriver",
            OldVideoDriverThresholdMonths,
            names);
    }
}

/// <summary>
/// Read-only guidance for G-SYNC/VRR: orients the user on how to check and
/// enable it (NVIDIA Control Panel/monitor OSD) and on the FPS limit NVIDIA
/// itself recommends (a few frames below the monitor's maximum refresh
/// rate, to keep the frame rate inside G-SYNC's variable range) -- reusing
/// the same <c>-frameLimit</c> launch parameter this product already
/// supports. Never enables G-SYNC itself: there is no public, documented
/// API for that (see docs/graphics-optimizations-backlog.md, seção 10).
/// </summary>
public sealed class GSyncGuidanceDiagnosisAction : ReadOnlyDiagnosticAction
{
    /// <summary>
    /// Frames subtracted from the monitor's maximum refresh rate to keep the
    /// frame rate inside the variable range, as the vendors recommend.
    /// </summary>
    private const int VariableRangeHeadroomFps = 3;

    private readonly IDisplayConfigurationInspector inspector;
    private readonly IGpuVendorInspector gpuVendor;

    public GSyncGuidanceDiagnosisAction(
        IDisplayConfigurationInspector inspector,
        IGpuVendorInspector gpuVendor)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.gpuVendor = gpuVendor ?? throw new ArgumentNullException(nameof(gpuVendor));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuideGSync);

    protected override string Describe() => Classify(inspector.GetSnapshot(), gpuVendor.GetSnapshot());

    internal static string Classify(DisplayConfigurationSnapshot? snapshot, GpuVendorSnapshot gpuSnapshot)
    {
        var baseGuidance = WindowsActionText.Format(
            "ActionResults.Vrr.BaseGuidance",
            DescribeControlPanel(gpuSnapshot));
        if (snapshot is null || snapshot.MaxRefreshHzAtCurrentResolution <= 0)
        {
            return baseGuidance;
        }

        var recommendedCap = Math.Max(1, snapshot.MaxRefreshHzAtCurrentResolution - VariableRangeHeadroomFps);
        return WindowsActionText.Format(
            "ActionResults.Vrr.RefreshGuidance",
            baseGuidance,
            snapshot.MaxRefreshHzAtCurrentResolution,
            recommendedCap);
    }

    /// <summary>
    /// Names the correct vendor panel (NVIDIA Control Panel for G-SYNC, AMD
    /// Software: Adrenalin Edition for FreeSync) when exactly one vendor is
    /// detected; otherwise falls back to a vendor-neutral phrasing rather
    /// than guessing which one applies.
    /// </summary>
    private static string DescribeControlPanel(GpuVendorSnapshot gpuSnapshot)
    {
        var vendors = gpuSnapshot.DriverDescriptions
            .Select(GpuVendorClassifier.VendorOf)
            .Distinct()
            .ToArray();

        return vendors switch
        {
            ["NVIDIA"] => WindowsActionText.Format("ActionResults.Vrr.NvidiaPanel"),
            ["AMD"] => "AMD Software: Adrenalin Edition (FreeSync)",
            _ => WindowsActionText.Format("ActionResults.Vrr.VendorPanel")
        };
    }
}

/// <summary>
/// Guided repair, never automatic: points the user to the official steps
/// for a clean NVIDIA driver reinstall (DDU in Safe Mode, then the latest
/// driver from nvidia.com/drivers) when there is a suspected driver
/// corruption issue. This product never downloads, installs, or removes a
/// display driver itself -- picking the wrong driver package or interrupting
/// a driver install can leave a machine without video output, which is
/// exactly the kind of irreversible, high-blast-radius risk this product's
/// safety model (docs/safety.md) exists to avoid.
/// </summary>
public sealed class GuidedDriverReinstallAction : ReadOnlyDiagnosticAction
{
    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuideDriverReinstall);

    protected override string Describe()
    {
        return WindowsActionText.Format("ActionResults.DriverReinstall.Instructions");
    }
}

public sealed class DisplayConfigurationDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IDisplayConfigurationInspector inspector;

    public DisplayConfigurationDiagnosisAction(IDisplayConfigurationInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDisplayConfiguration);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(DisplayConfigurationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return WindowsActionText.Format("ActionResults.Display.Unavailable");
        }

        var hags = snapshot.HardwareGpuScheduling switch
        {
            HardwareGpuSchedulingState.Enabled => WindowsActionText.Format("ActionResults.State.Enabled"),
            HardwareGpuSchedulingState.Disabled => WindowsActionText.Format("ActionResults.State.Disabled"),
            _ => WindowsActionText.Format("ActionResults.Display.HagsUnavailable")
        };

        var refreshNote = snapshot.CurrentRefreshHz < snapshot.MaxRefreshHzAtCurrentResolution
            ? WindowsActionText.Format(
                "ActionResults.Display.RefreshBelowMaximum",
                snapshot.CurrentRefreshHz,
                snapshot.MaxRefreshHzAtCurrentResolution)
            : string.Empty;

        return WindowsActionText.Format(
            "ActionResults.Display.Summary",
            snapshot.Width,
            snapshot.Height,
            snapshot.CurrentRefreshHz,
            refreshNote,
            hags);
    }
}

/// <summary>
/// Reads the session-relevant Windows settings. Unlike the other diagnostics
/// here it queries the active power plan asynchronously, so it implements
/// <see cref="WindowsOptimizationAction"/> directly instead of deriving from
/// <see cref="ReadOnlyDiagnosticAction"/>; it is still strictly read-only.
/// </summary>
public sealed class SessionSettingsDiagnosisAction : WindowsOptimizationAction
{
    private static readonly RegistryAddress GameModeAddress = new(
        RegistryHive.CurrentUser,
        @"Software\Microsoft\GameBar",
        "AutoGameModeEnabled");

    private static readonly RegistryAddress FullscreenOptimizationsAddress = new(
        RegistryHive.CurrentUser,
        @"System\GameConfigStore",
        "GameDVR_FSEBehaviorMode");

    private static readonly IReadOnlyDictionary<Guid, string> KnownPowerSchemes =
        new Dictionary<Guid, string>
        {
            [new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")] = "ActionResults.PowerPlan.Balanced",
            [new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "ActionResults.PowerPlan.HighPerformance",
            [new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")] = "ActionResults.PowerPlan.PowerSaver",
            [new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "ActionResults.PowerPlan.UltimatePerformance"
        };

    private readonly IRegistryStore registry;
    private readonly IPowerPlanController powerPlans;

    public SessionSettingsDiagnosisAction(IRegistryStore registry, IPowerPlanController powerPlans)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.powerPlans = powerPlans ?? throw new ArgumentNullException(nameof(powerPlans));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseSessionSettings);

    public override async Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameMode = registry.Read(GameModeAddress);
        var fullscreenOptimizations = registry.Read(FullscreenOptimizationsAddress);

        string powerPlanLabel;
        try
        {
            var scheme = await powerPlans.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
            powerPlanLabel = KnownPowerSchemes.TryGetValue(scheme, out var known)
                ? WindowsActionText.Format(known)
                : scheme.ToString("D");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            powerPlanLabel = WindowsActionText.Format("ActionResults.State.Unavailable");
        }

        return WindowsActionApplyResult.NoChange(Classify(gameMode, fullscreenOptimizations, powerPlanLabel));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(
        RegistryValueState gameMode,
        RegistryValueState fullscreenOptimizations,
        string powerPlanLabel)
    {
        var gameModeLabel = gameMode is { Exists: true, NumericValue: 1 }
            ? WindowsActionText.Format("ActionResults.State.Enabled")
            : WindowsActionText.Format("ActionResults.SessionSettings.DisabledOrDefault");
        // GameDVR_FSEBehaviorMode = 2 means Windows-wide "Disable fullscreen optimizations" is on.
        var fseLabel = fullscreenOptimizations is { Exists: true, NumericValue: 2 }
            ? WindowsActionText.Format("ActionResults.SessionSettings.FseDisabled")
            : WindowsActionText.Format("ActionResults.SessionSettings.FseEnabled");

        return WindowsActionText.Format(
            "ActionResults.SessionSettings.Summary",
            gameModeLabel,
            fseLabel,
            powerPlanLabel);
    }
}

public sealed class ThrottlingSignalDiagnosisAction : ReadOnlyDiagnosticAction
{
    /// <summary>
    /// Frequency below this fraction of the maximum, while the CPU is busy,
    /// is what this product calls a clock drop under load.
    /// </summary>
    private const double ClockDropRatio = 0.6d;
    private const double BusyCpuPercent = 50d;

    private readonly ICpuInspector cpu;
    private readonly IResourceUsageInspector usage;
    private readonly IHardwareStabilityInspector stability;
    private readonly IThermalInspector thermal;

    public ThrottlingSignalDiagnosisAction(
        ICpuInspector cpu,
        IResourceUsageInspector usage,
        IHardwareStabilityInspector stability,
        IThermalInspector thermal)
    {
        this.cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        this.usage = usage ?? throw new ArgumentNullException(nameof(usage));
        this.stability = stability ?? throw new ArgumentNullException(nameof(stability));
        this.thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseThrottlingSignal);

    protected override string Describe()
    {
        return Classify(
            cpu.GetSnapshot(),
            usage.GetSnapshot(),
            stability.GetSnapshot(),
            thermal.GetSnapshot());
    }

    internal static string Classify(
        CpuSnapshot? cpuSnapshot,
        ResourceUsageSnapshot usageSnapshot,
        HardwareStabilitySnapshot stabilitySnapshot,
        ThermalSnapshot thermalSnapshot)
    {
        var clockDropUnderLoad = cpuSnapshot is not null
            && cpuSnapshot.MaxClockMhz > 0
            && cpuSnapshot.CurrentClockMhz < cpuSnapshot.MaxClockMhz * ClockDropRatio
            && usageSnapshot.CpuPercent > BusyCpuPercent;
        var wheaSignal = stabilitySnapshot.RecentWheaEventCount > 0;
        var thermalSignal = DiagnosticSignals.IsTemperatureElevated(thermalSnapshot);

        if (clockDropUnderLoad && (thermalSignal || wheaSignal))
        {
            return WindowsActionText.Format(
                "ActionResults.Throttling.Combined",
                WindowsActionText.Format(thermalSignal
                    ? "ActionResults.Throttling.TemperatureSignal"
                    : "ActionResults.Throttling.WheaSignal"));
        }

        if (clockDropUnderLoad)
        {
            return WindowsActionText.Format("ActionResults.Throttling.ClockDrop");
        }

        if (wheaSignal)
        {
            return WindowsActionText.Format("ActionResults.Throttling.WheaOnly");
        }

        return WindowsActionText.Format("ActionResults.Throttling.None");
    }
}

public sealed class ResourceUsageDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IResourceUsageInspector inspector;

    public ResourceUsageDiagnosisAction(IResourceUsageInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseResourceUsage);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(ResourceUsageSnapshot snapshot)
    {
        return WindowsActionText.Format(
            "ActionResults.ResourceUsage.Summary",
            FormatPercent(snapshot.CpuPercent),
            FormatPercent(snapshot.DiskPercent),
            FormatPercent(snapshot.GpuPercent),
            snapshot.NetworkThroughputMBps);
    }

    private static string FormatPercent(double? value)
    {
        return value is { } percent
            ? WindowsActionText.Format("ActionResults.ResourceUsage.Percent", percent)
            : WindowsActionText.Format("ActionResults.State.Unavailable");
    }
}

public sealed class PciLinkDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IPciLinkInspector inspector;

    public PciLinkDiagnosisAction(IPciLinkInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnosePciLink);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(IReadOnlyList<PciLinkSnapshot> adapters)
    {
        var withData = adapters.Where(adapter =>
            adapter.CurrentLinkWidth is not null || adapter.CurrentLinkSpeedGtPerSecondTimesTen is not null).ToArray();

        if (withData.Length == 0)
        {
            return WindowsActionText.Format("ActionResults.PciLink.Unavailable");
        }

        var parts = withData.Select(adapter =>
        {
            var current = FormatLink(adapter.CurrentLinkWidth, adapter.CurrentLinkSpeedGtPerSecondTimesTen);
            var max = FormatLink(adapter.MaxLinkWidth, adapter.MaxLinkSpeedGtPerSecondTimesTen);
            return WindowsActionText.Format("ActionResults.PciLink.Adapter", adapter.AdapterName, current, max);
        });

        return string.Join("; ", parts) + ".";
    }

    private static string FormatLink(int? width, int? speedTimesTen)
    {
        var widthLabel = width is { } w ? $"x{w}" : "x?";
        var speedLabel = speedTimesTen is { } s
            ? WindowsActionText.Format("ActionResults.PciLink.Speed", s / 10d)
            : "?";
        return $"{widthLabel} @ {speedLabel}";
    }
}

public sealed class HardwareStabilityDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const int OldBiosThresholdYears = 3;

    private readonly IHardwareStabilityInspector inspector;

    public HardwareStabilityDiagnosisAction(IHardwareStabilityInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseHardwareStability);

    protected override string Describe() => Classify(inspector.GetSnapshot(), DateTimeOffset.UtcNow);

    internal static string Classify(HardwareStabilitySnapshot snapshot, DateTimeOffset nowUtc)
    {
        var biosLabel = snapshot.BiosReleaseDateUtc is { } releaseDate
            ? BuildBiosLabel(releaseDate, nowUtc)
            : WindowsActionText.Format("ActionResults.HardwareStability.BiosDateUnavailable");

        var wheaLabel = snapshot.RecentWheaEventCount <= 0
            ? WindowsActionText.Format("ActionResults.HardwareStability.NoWhea")
            : snapshot.RecentMemoryFlavoredWheaEventCount > 0
                ? WindowsActionText.Format(
                    "ActionResults.HardwareStability.WheaWithMemory",
                    snapshot.RecentWheaEventCount,
                    snapshot.RecentMemoryFlavoredWheaEventCount)
                : WindowsActionText.Format(
                    "ActionResults.HardwareStability.Whea",
                    snapshot.RecentWheaEventCount);

        return WindowsActionText.Format("ActionResults.HardwareStability.Summary", biosLabel, wheaLabel);
    }

    private static string BuildBiosLabel(DateTimeOffset releaseDate, DateTimeOffset nowUtc)
    {
        var ageYears = (nowUtc - releaseDate).TotalDays / 365.25;
        return ageYears >= OldBiosThresholdYears
            ? WindowsActionText.Format(
                "ActionResults.HardwareStability.OldBios",
                releaseDate,
                OldBiosThresholdYears)
            : WindowsActionText.Format("ActionResults.HardwareStability.RecentBios", releaseDate);
    }
}

/// <summary>
/// The bottleneck classification requested for the benchmark/
/// comparison feature. It only reuses signals already read by the other
/// diagnostics in this file — no new system access beyond the background
/// process CPU reader — and returns the first category whose threshold
/// fires, in the priority order documented in <see cref="Classify"/>.
/// When no local signal stands out, it reports exactly that instead of
/// guessing an external bottleneck.
/// </summary>
public sealed class BottleneckClassificationAction : ReadOnlyDiagnosticAction
{
    private const double HighUtilizationPercent = 90d;
    private const double ModerateUtilizationPercent = 85d;
    private const double BackgroundProcessCpuThresholdPercent = 20d;
    private const long SmallVramBytes = 4L * DiagnosticSignals.GiB;

    private static readonly IReadOnlyCollection<string> ExcludedProcessNames =
    [
        "FiveM", "FiveM_", "CitizenFX", "CitizenFX_", "GTA5", "GTA5_Enhanced",
        "Ralven", "Ralven.Broker"
    ];

    private readonly ISystemResourceInspector systemResources;
    private readonly IResourceUsageInspector resourceUsage;
    private readonly IThermalInspector thermal;
    private readonly IGpuDetailsInspector gpuDetails;
    private readonly IBackgroundProcessInspector backgroundProcess;

    public BottleneckClassificationAction(
        ISystemResourceInspector systemResources,
        IResourceUsageInspector resourceUsage,
        IThermalInspector thermal,
        IGpuDetailsInspector gpuDetails,
        IBackgroundProcessInspector backgroundProcess)
    {
        this.systemResources = systemResources ?? throw new ArgumentNullException(nameof(systemResources));
        this.resourceUsage = resourceUsage ?? throw new ArgumentNullException(nameof(resourceUsage));
        this.thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
        this.gpuDetails = gpuDetails ?? throw new ArgumentNullException(nameof(gpuDetails));
        this.backgroundProcess = backgroundProcess ?? throw new ArgumentNullException(nameof(backgroundProcess));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ClassifyBottleneck);

    protected override string Describe()
    {
        return Classify(new BottleneckClassificationInput(
            systemResources.GetSnapshot(),
            resourceUsage.GetSnapshot(),
            thermal.GetSnapshot(),
            gpuDetails.GetSnapshot(),
            backgroundProcess.GetTopConsumer(ExcludedProcessNames)));
    }

    internal static string Classify(BottleneckClassificationInput input)
    {
        var logicalProcessors = Math.Max(1, input.SystemResources.LogicalProcessorCount);

        // 1. Térmico: alta temperatura disponível é o sinal mais direto que temos.
        if (DiagnosticSignals.IsTemperatureElevated(input.Thermal))
        {
            return WindowsActionText.Format(
                "ActionResults.BottleneckClassification.Thermal",
                input.Thermal.HighestCelsius);
        }

        // 2. Processo de fundo: outro processo consumindo CPU de forma relevante.
        if (input.BackgroundProcess is { } process
            && process.CpuPercent / logicalProcessors >= BackgroundProcessCpuThresholdPercent)
        {
            return WindowsActionText.Format(
                "ActionResults.BottleneckClassification.BackgroundProcess",
                process.ProcessName);
        }

        // 3. Disco: tempo ativo elevado.
        if (input.ResourceUsage.DiskPercent >= HighUtilizationPercent)
        {
            return WindowsActionText.Format("ActionResults.BottleneckClassification.Disk");
        }

        // 4. RAM: pouca memória disponível.
        if (DiagnosticSignals.IsMemoryUnderPressure(input.SystemResources))
        {
            return WindowsActionText.Format("ActionResults.BottleneckClassification.Memory");
        }

        // 5. VRAM: só acusa pouca VRAM quando nenhum adaptador conhecido tem mais de 4 GB.
        var highestVram = input.GpuDetails
            .Where(gpu => gpu.VramBytes is > 0)
            .Select(gpu => gpu.VramBytes!.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (input.ResourceUsage.GpuPercent >= HighUtilizationPercent
            && highestVram is > 0 and <= SmallVramBytes)
        {
            return WindowsActionText.Format("ActionResults.BottleneckClassification.Vram");
        }

        // 6. GPU: GPU saturada com CPU folgada.
        if (input.ResourceUsage.GpuPercent >= HighUtilizationPercent
            && input.ResourceUsage.CpuPercent < ModerateUtilizationPercent)
        {
            return WindowsActionText.Format("ActionResults.BottleneckClassification.Gpu");
        }

        // 7. CPU: CPU saturada com GPU não saturada.
        if (input.ResourceUsage.CpuPercent >= ModerateUtilizationPercent
            && input.ResourceUsage.GpuPercent < HighUtilizationPercent)
        {
            return WindowsActionText.Format("ActionResults.BottleneckClassification.Cpu");
        }

        // Nenhum sinal local se destacou. Isso não autoriza inferir uma causa externa.
        return WindowsActionText.Format("ActionResults.BottleneckClassification.None");
    }
}

public sealed record BottleneckClassificationInput(
    SystemResourceSnapshot SystemResources,
    ResourceUsageSnapshot ResourceUsage,
    ThermalSnapshot Thermal,
    IReadOnlyList<GpuAdapterDetails> GpuDetails,
    BackgroundProcessUsage? BackgroundProcess);
