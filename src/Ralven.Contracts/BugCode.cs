namespace Ralven.Contracts;

/// <summary>
/// Stable bug classification codes for the Ralven application and updater.
/// These codes are durable contracts: they are persisted in bug reports, telemetry,
/// and the admin dashboard. Members may only be appended, never renamed, removed,
/// or renumbered. Each code maps to a specific failure domain and actionable area.
/// </summary>
/// <remarks>
/// The code format uses a category prefix followed by a specific reason:
/// - APP_: Main application (UI, optimization, diagnosis, settings)
/// - UPD_: Updater (download, verify, install, rollback, launcher)
/// - BRK_: Broker (elevation, execution, rollback, IPC)
/// - NET_: Network/connectivity (API, telemetry, bug report delivery)
/// - FIVEM_: FiveM Legacy specific (cache, installation, logs, crashes)
/// - GTAV_: GTA V Legacy specific (settings, launch, graphics)
/// - WIN_: Windows/UAC/environment (privilege, services, drivers)
/// - SEC_: Windows Security Center health (antivirus, firewall, automatic updates)
/// - CFG_: Configuration/settings (persistence, migration, validation)
/// - SYS_: System/runtime (memory, process, filesystem, serialization)
/// </remarks>
public enum BugCode
{
    /// <summary>Unknown or unclassified bug. Used when classification fails.</summary>
    Unknown = 0,

    // ========== APPLICATION (APP_) ==========

    /// <summary>WPF UI rendering, layout, or interaction failure.</summary>
    APP_UI_RENDER = 100,

    /// <summary>Navigation or page transition failure.</summary>
    APP_UI_NAVIGATION = 101,

    /// <summary>Localization/resource lookup failure.</summary>
    APP_UI_LOCALIZATION = 102,

    /// <summary>Theme or visual state application failure.</summary>
    APP_UI_THEME = 103,

    /// <summary>Progress reporting or cancellation UI failure.</summary>
    APP_UI_PROGRESS = 104,

    /// <summary>Optimization plan building failed.</summary>
    APP_OPT_PLAN_BUILD = 200,

    /// <summary>Optimization plan validation failed (pre-conditions not met).</summary>
    APP_OPT_PLAN_VALIDATION = 201,

    /// <summary>Local (standard-user) optimization phase failed.</summary>
    APP_OPT_LOCAL_PHASE = 202,

    /// <summary>Elevated (administrator) optimization phase failed.</summary>
    APP_OPT_ELEVATED_PHASE = 203,

    /// <summary>Optimization action execution failed.</summary>
    APP_OPT_ACTION_EXECUTION = 204,

    /// <summary>Optimization action rollback failed.</summary>
    APP_OPT_ACTION_ROLLBACK = 205,

    /// <summary>Optimization transaction commit failed.</summary>
    APP_OPT_TRANSACTION_COMMIT = 206,

    /// <summary>Optimization was cancelled by user.</summary>
    APP_OPT_CANCELLED = 207,

    /// <summary>Optimization completed with partial failures.</summary>
    APP_OPT_PARTIAL_FAILURE = 208,

    /// <summary>System diagnosis (hardware, FiveM, GTA V detection) failed.</summary>
    APP_DIAG_SYSTEM = 300,

    /// <summary>FiveM installation detection failed.</summary>
    APP_DIAG_FIVEM_DETECTION = 301,

    /// <summary>GTA V installation detection failed.</summary>
    APP_DIAG_GTAV_DETECTION = 302,

    /// <summary>Hardware profiling (CPU, GPU, RAM, disk) failed.</summary>
    APP_DIAG_HARDWARE = 303,

    /// <summary>Windows version/edition detection failed.</summary>
    APP_DIAG_WINDOWS = 304,

    /// <summary>Streaming software detection failed.</summary>
    APP_DIAG_STREAMING = 305,

    /// <summary>Settings load/save failed.</summary>
    APP_SETTINGS_PERSISTENCE = 400,

    /// <summary>Settings schema migration failed.</summary>
    APP_SETTINGS_MIGRATION = 401,

    /// <summary>Privacy consent handling failed.</summary>
    APP_SETTINGS_PRIVACY = 402,

    /// <summary>Account/authentication flow failed.</summary>
    APP_AUTH_FLOW = 403,

    /// <summary>Application startup/shutdown failure.</summary>
    APP_LIFECYCLE = 404,

    /// <summary>Single-instance guard failure.</summary>
    APP_INSTANCE_GUARD = 405,

    /// <summary>Tray icon or background service failure.</summary>
    APP_TRAY_SERVICE = 406,

    /// <summary>Aplicativos/Startup inventory scan failed (partial or total).</summary>
    APP_INV_SCAN = 407,

    // ========== UPDATER (UPD_) ==========

    /// <summary>Update manifest download failed.</summary>
    UPD_MANIFEST_DOWNLOAD = 500,

    /// <summary>Update manifest verification (signature, hash) failed.</summary>
    UPD_MANIFEST_VERIFICATION = 501,

    /// <summary>Update manifest parsing/deserialization failed.</summary>
    UPD_MANIFEST_PARSING = 502,

    /// <summary>Update installer download failed.</summary>
    UPD_INSTALLER_DOWNLOAD = 503,

    /// <summary>Update installer integrity check (size, SHA-256) failed.</summary>
    UPD_INSTALLER_INTEGRITY = 504,

    /// <summary>Update installer verification (signature) failed.</summary>
    UPD_INSTALLER_VERIFICATION = 505,

    /// <summary>Update staging (file copy, preparation) failed.</summary>
    UPD_STAGING = 506,

    /// <summary>Update activation (atomic switch) failed.</summary>
    UPD_ACTIVATION = 507,

    /// <summary>Update installer execution failed.</summary>
    UPD_INSTALLER_EXECUTION = 508,

    /// <summary>Update installer exit code indicated failure.</summary>
    UPD_INSTALLER_EXIT_CODE = 509,

    /// <summary>Update rollback (revert to previous version) failed.</summary>
    UPD_ROLLBACK = 510,

    /// <summary>Update health check (post-launch verification) failed.</summary>
    UPD_HEALTH_CHECK = 511,

    /// <summary>Parent process did not exit in time for update.</summary>
    UPD_PARENT_TIMEOUT = 512,

    /// <summary>Update handoff data parsing/validation failed.</summary>
    UPD_HANDOFF_INVALID = 513,

    /// <summary>Update security policy violation (redirect, domain mismatch).</summary>
    UPD_SECURITY_POLICY = 514,

    /// <summary>Launcher failed to start updated application.</summary>
    UPD_LAUNCH_FAILED = 515,

    /// <summary>Version floor check blocked update.</summary>
    UPD_VERSION_FLOOR = 516,

    // ========== BROKER (BRK_) ==========

    /// <summary>Broker process launch or connection failed.</summary>
    BRK_LAUNCH = 600,

    /// <summary>Named pipe IPC connection failed.</summary>
    BRK_IPC_CONNECTION = 601,

    /// <summary>Named pipe IPC communication failed (read/write).</summary>
    BRK_IPC_COMMUNICATION = 602,

    /// <summary>Broker request validation failed (malformed plan).</summary>
    BRK_REQUEST_VALIDATION = 603,

    /// <summary>Broker action execution failed (privileged operation).</summary>
    BRK_ACTION_EXECUTION = 604,

    /// <summary>Broker action rollback failed.</summary>
    BRK_ACTION_ROLLBACK = 605,

    /// <summary>Broker transaction not committed (incomplete state).</summary>
    BRK_TRANSACTION_INCOMPLETE = 606,

    /// <summary>Broker rollback not completed (inconsistent state).</summary>
    BRK_ROLLBACK_INCOMPLETE = 607,

    /// <summary>UAC elevation prompt cancelled by user.</summary>
    BRK_UAC_CANCELLED = 608,

    /// <summary>UAC elevation prompt denied/failed.</summary>
    BRK_UAC_DENIED = 609,

    /// <summary>Broker process crashed or exited unexpectedly.</summary>
    BRK_PROCESS_CRASH = 610,

    /// <summary>Broker files or signed integrity metadata failed pre-elevation validation.</summary>
    BRK_INTEGRITY_VALIDATION = 611,

    // ========== NETWORK (NET_) ==========

    /// <summary>API request failed (HTTP error, timeout).</summary>
    NET_API_REQUEST = 700,

    /// <summary>Telemetry event delivery failed.</summary>
    NET_TELEMETRY_DELIVERY = 701,

    /// <summary>Bug report delivery failed.</summary>
    NET_BUG_REPORT_DELIVERY = 702,

    /// <summary>Update check/download network failure.</summary>
    NET_UPDATE_NETWORK = 703,

    /// <summary>Authentication/OAuth network failure.</summary>
    NET_AUTH_NETWORK = 704,

    /// <summary>DNS resolution failed.</summary>
    NET_DNS = 705,

    /// <summary>TLS/SSL certificate validation failed.</summary>
    NET_TLS = 706,

    /// <summary>Rate limited by remote service.</summary>
    NET_RATE_LIMITED = 707,

    // ========== FIVEM LEGACY (FIVEM_) ==========

    /// <summary>FiveM Legacy cache operation failed.</summary>
    FIVEM_CACHE_OPERATION = 800,

    /// <summary>FiveM Legacy cache index (content_index.xml) corruption.</summary>
    FIVEM_CACHE_INDEX = 801,

    /// <summary>FiveM Legacy cache repair failed.</summary>
    FIVEM_CACHE_REPAIR = 802,

    /// <summary>FiveM Legacy log reading/parsing failed.</summary>
    FIVEM_LOG_PARSING = 803,

    /// <summary>FiveM Legacy crash dump analysis failed.</summary>
    FIVEM_CRASH_ANALYSIS = 804,

    /// <summary>FiveM Legacy installation health check failed.</summary>
    FIVEM_INSTALLATION_HEALTH = 805,

    /// <summary>FiveM Legacy storage/drive health check failed.</summary>
    FIVEM_STORAGE_HEALTH = 806,

    /// <summary>FiveM process detection failed.</summary>
    FIVEM_PROCESS_DETECTION = 807,

    // ========== GTA V LEGACY (GTAV_) ==========

    /// <summary>GTA V Legacy settings.xml read/write failed.</summary>
    GTAV_SETTINGS_IO = 900,

    /// <summary>GTA V Legacy graphics preset application failed.</summary>
    GTAV_GRAPHICS_PRESET = 901,

    /// <summary>GTA V Legacy launch parameters (commandline.txt) failed.</summary>
    GTAV_LAUNCH_PARAMS = 902,

    /// <summary>GTA V Legacy executable detection failed.</summary>
    GTAV_EXECUTABLE_DETECTION = 903,

    /// <summary>GTA V Legacy benchmark execution failed.</summary>
    GTAV_BENCHMARK = 904,

    // ========== WINDOWS/UAC (WIN_) ==========

    /// <summary>Windows privilege/UAC elevation failed.</summary>
    WIN_PRIVILEGE = 1000,

    /// <summary>Windows service management (start/stop/query) failed.</summary>
    WIN_SERVICE = 1001,

    /// <summary>Windows registry read/write failed.</summary>
    WIN_REGISTRY = 1002,

    /// <summary>Windows power plan application failed.</summary>
    WIN_POWER_PLAN = 1003,

    /// <summary>Windows Game Mode/fullscreen optimization toggle failed.</summary>
    WIN_GAMING_MODE = 1004,

    /// <summary>Windows driver version query failed.</summary>
    WIN_DRIVER_QUERY = 1005,

    /// <summary>Windows display configuration (HAGS, refresh rate) failed.</summary>
    WIN_DISPLAY_CONFIG = 1006,

    /// <summary>Windows PCIe link query failed.</summary>
    WIN_PCIE_QUERY = 1007,

    /// <summary>Windows thermal/throttling query failed.</summary>
    WIN_THERMAL = 1008,

    /// <summary>Windows pagefile/commit limit query failed.</summary>
    WIN_PAGEFILE = 1009,

    /// <summary>Windows hardware error (WHEA) query failed.</summary>
    WIN_WHEA = 1010,

    /// <summary>Windows BIOS/UEFI query failed.</summary>
    WIN_BIOS = 1011,

    // ========== SECURITY HEALTH (SEC_) ==========

    /// <summary>Windows Security Center health read (antivirus, firewall, updates) failed.</summary>
    SEC_HEALTH_QUERY = 1300,

    // ========== CONFIGURATION (CFG_) ==========

    /// <summary>Configuration file (JSON) read/parse failed.</summary>
    CFG_FILE_READ = 1100,

    /// <summary>Configuration file write/serialize failed.</summary>
    CFG_FILE_WRITE = 1101,

    /// <summary>Configuration schema validation failed.</summary>
    CFG_VALIDATION = 1102,

    /// <summary>Configuration migration between versions failed.</summary>
    CFG_MIGRATION = 1103,

    /// <summary>Environment variable access failed.</summary>
    CFG_ENV_VAR = 1104,

    // ========== SYSTEM/RUNTIME (SYS_) ==========

    /// <summary>File system operation (read/write/delete) failed.</summary>
    SYS_FILESYSTEM = 1200,

    /// <summary>Process launch/monitoring failed.</summary>
    SYS_PROCESS = 1201,

    /// <summary>Memory allocation or query failed.</summary>
    SYS_MEMORY = 1202,

    /// <summary>JSON serialization/deserialization failed.</summary>
    SYS_JSON = 1203,

    /// <summary>Cryptographic operation (hash, signature) failed.</summary>
    SYS_CRYPTO = 1204,

    /// <summary>Time/date operation failed.</summary>
    SYS_TIME = 1205,

    /// <summary>Path canonicalization or traversal check failed.</summary>
    SYS_PATH = 1206,

    /// <summary>Assembly loading or reflection failed.</summary>
    SYS_ASSEMBLY = 1207,
}
