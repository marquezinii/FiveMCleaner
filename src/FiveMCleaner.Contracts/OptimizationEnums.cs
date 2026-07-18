namespace FiveMCleaner.Contracts;

public enum OptimizationProfile
{
    Light,
    Balanced,
    Aggressive
}

public enum FiveMEdition
{
    Unknown,
    Legacy,
    Enhanced
}

public enum ActionCategory
{
    Safety,
    Storage,
    WindowsGaming,
    Power,
    Appearance,
    FiveMGraphics
}

public enum ActionRisk
{
    Informational,
    Low,
    Moderate,
    High
}

public enum ActionReversibility
{
    ReadOnly,
    FullyReversible,
    SessionScoped,
    RebuildableData,
    Irreversible
}

public enum RequiredPrivilege
{
    StandardUser,
    Administrator
}

public enum CacheRepairPolicy
{
    Off,
    WhenOversized,
    RepairNow
}

public enum PlanBlockCode
{
    EditionNotDetected,
    EnhancedNotSupported
}

public enum PlanNoticeSeverity
{
    Information,
    Warning
}
