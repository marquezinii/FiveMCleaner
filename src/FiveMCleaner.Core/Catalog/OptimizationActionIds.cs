namespace FiveMCleaner.Core.Catalog;

public static class OptimizationActionIds
{
    public const string VerifyFiveMIsStopped = "safety.fivem-stopped.verify";
    public const string VerifyGtaVIsStopped = "safety.gtav-stopped.verify";
    public const string DiagnoseBottleneck = "safety.bottleneck.diagnose";
    public const string DetectOverlaysAndCaptureSoftware = "windows.gaming.overlays.detect";
    public const string ReadFiveMLegacyLogs = "fivem.legacy.logs.read";
    public const string GuidePerformanceDiagnostics = "safety.performance-diagnostics.guide";
    public const string DiagnoseNetworkHealth = "safety.network-health.diagnose";
    public const string DiagnoseThermalThrottling = "safety.thermal.diagnose";
    public const string DiagnosePagefileCommit = "safety.pagefile-commit.diagnose";
    public const string DiagnoseCacheIntegrity = "fivem.legacy.cache-integrity.diagnose";
    public const string DetectGpuVendor = "windows.gaming.gpu-vendor.detect";
    public const string DiagnoseCpuDetails = "safety.cpu-details.diagnose";
    public const string DiagnoseGpuDetails = "windows.gaming.gpu-details.diagnose";
    public const string DiagnoseRamDetails = "safety.ram-details.diagnose";
    public const string DiagnoseStorageHealth = "fivem.legacy.storage-health.diagnose";
    public const string DiagnoseDriverVersions = "windows.system.driver-versions.diagnose";
    public const string DiagnoseDisplayConfiguration = "windows.gaming.display-configuration.diagnose";
    public const string DiagnoseSessionSettings = "windows.gaming.session-settings.diagnose";
    public const string DiagnoseThrottlingSignal = "safety.throttling-signal.diagnose";
    public const string DiagnoseResourceUsage = "safety.resource-usage.diagnose";
    public const string DiagnosePciLink = "windows.gaming.pcie-link.diagnose";
    public const string DiagnoseHardwareStability = "safety.hardware-stability.diagnose";
    public const string ClassifyBottleneck = "safety.bottleneck-classification.diagnose";
    public const string CleanUserTemporaryFiles = "storage.user-temporary-files.clean";
    public const string PruneLegacyCrashDumps = "fivem.legacy.crash-dumps.prune";
    public const string RepairLegacyServerCache = "fivem.legacy.server-cache.repair";
    public const string EnableGameMode = "windows.gaming.game-mode.enable";
    public const string PreferHighPerformanceGpu = "windows.gaming.high-performance-gpu.prefer";
    public const string DisableBackgroundCapture = "windows.gaming.background-capture.disable";
    public const string EnableSessionPerformancePowerPlan = "windows.power.performance-session.enable";
    public const string ApplyLightLegacyGraphics = "fivem.legacy.graphics.light.apply";
    public const string ApplyBalancedLegacyGraphics = "fivem.legacy.graphics.balanced.apply";
    public const string ApplyAggressiveLegacyGraphics = "fivem.legacy.graphics.aggressive.apply";
    public const string ApplyLightGtaVGraphics = "gtav.legacy.graphics.light.apply";
    public const string ApplyBalancedGtaVGraphics = "gtav.legacy.graphics.balanced.apply";
    public const string ApplyAggressiveGtaVGraphics = "gtav.legacy.graphics.aggressive.apply";
    public const string ReduceWindowsVisualEffects = "windows.appearance.visual-effects.reduce";
    public const string DiagnoseCacheStorage = "fivem.legacy.cache-storage.diagnose";
    public const string DiagnoseInstallationHealth = "fivem.legacy.installation-health.diagnose";
    public const string DiagnoseCrashPatterns = "fivem.legacy.crash-patterns.diagnose";
    public const string TerminateStuckFiveMProcess = "fivem.legacy.stuck-process.terminate";
    public const string RecreateFiveMLocalData = "fivem.legacy.local-data.recreate";
    public const string RepairStaleAuthData = "fivem.legacy.auth-data.repair";
}
