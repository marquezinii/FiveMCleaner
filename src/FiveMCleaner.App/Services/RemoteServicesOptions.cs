using System.IO;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Centralized, environment-specific configuration for remote reporting
/// services (currently just Sentry). Deliberately holds no defaults that
/// authorize sending anything by itself — <see cref="SentryDsn"/> being
/// present only means a destination exists; whether anything is actually
/// sent still depends entirely on the user's privacy consent
/// (<see cref="PrivacyConsentEvaluator"/>).
/// </summary>
public sealed record RemoteServicesOptions
{
    public string? SentryDsn { get; init; }

    /// <summary>
    /// HTTPS endpoint of the anonymous telemetry Cloudflare Worker. Absent
    /// or empty means the Worker has not been deployed/configured yet, in
    /// which case telemetry keeps using
    /// <see cref="FormSubmitAnonymousTelemetryService"/> exactly as before —
    /// the two transports are mutually exclusive by construction, never
    /// sending the same event to both.
    /// </summary>
    public string? TelemetryEndpoint { get; init; }

    public required string Environment { get; init; }
}

/// <summary>
/// Loads <see cref="RemoteServicesOptions"/> from the environment-specific
/// config file under <c>Config/</c>
/// (<c>appsettings.Development.json</c> / <c>appsettings.Production.json</c>),
/// falling back to the shared <c>appsettings.json</c> baseline — which never
/// carries a DSN — when the environment-specific file is missing or
/// unreadable. No identifier or secret is hardcoded in source; this is the
/// only place that reads these files.
/// </summary>
public static class RemoteServicesOptionsLoader
{
    private const string ConfigDirectoryName = "Config";
    private const string BaseFileName = "appsettings.json";

    public static RemoteServicesOptions Load(AppRuntimeEnvironment environment, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var environmentSpecificPath = Path.Combine(
            baseDirectory,
            ConfigDirectoryName,
            $"appsettings.{environment}.json");
        var basePath = Path.Combine(baseDirectory, ConfigDirectoryName, BaseFileName);
        var path = File.Exists(environmentSpecificPath) ? environmentSpecificPath : basePath;

        var fallback = new RemoteServicesOptions { SentryDsn = null, Environment = environment.ToString() };
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<RemoteServicesOptions>(stream, FiveMCleanerJson.Options) ?? fallback;
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return fallback;
        }
    }
}
