namespace FiveMCleaner.Core.Catalog;

public static class OptimizationActionIds
{
    public const string VerifyFiveMIsStopped = "safety.fivem-stopped.verify";
    public const string VerifyGtaVIsStopped = "safety.gtav-stopped.verify";
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
}
