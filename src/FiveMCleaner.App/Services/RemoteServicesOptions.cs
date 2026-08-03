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
    /// HTTPS endpoint of the anonymous telemetry Cloudflare Worker. Absent,
    /// empty, or non-HTTPS means telemetry safely sends nothing at all —
    /// FormSubmit was removed as a transport entirely, there is no fallback.
    /// </summary>
    public string? TelemetryEndpoint { get; init; }

    /// <summary>
    /// HTTPS endpoint of the bug-report Cloudflare Worker route. Same
    /// fail-safe rule as <see cref="TelemetryEndpoint"/>: absent, empty, or
    /// non-HTTPS means the "Reportar um bug" flow reports a clear failure
    /// instead of silently falling back to FormSubmit, which was removed.
    /// </summary>
    public string? BugReportEndpoint { get; init; }

    /// <summary>Public Firebase Web API key. It identifies the project but is not an administrative credential.</summary>
    public string? FirebaseApiKey { get; init; }

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
            var loaded = JsonSerializer.Deserialize<RemoteServicesOptions>(stream, FiveMCleanerJson.Options);
            // The environment is selected by the executable, never by a
            // mutable JSON file. Otherwise a stale or edited Production file
            // could make real user events appear as Development in D1.
            return loaded is null
                ? fallback
                : loaded with { Environment = environment.ToString() };
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return fallback;
        }
    }
}

/// <summary>
/// Production-only guard for the anonymous telemetry destination. This is
/// deliberately stricter than generic HTTPS validation: the app must never
/// silently start reporting to an arbitrary host because a local config file
/// was stale, missing, or edited. Development remains HTTPS-only so test
/// traffic receives the same transport security guarantees.
/// </summary>
public static class TelemetryEndpointPolicy
{
    public const string ProductionHost = "fivemcleaner-telemetry.felipemarquesini10.workers.dev";
    public const string TelemetryPath = "/telemetry";

    public static bool TryCreate(
        string? configuredValue,
        AppRuntimeEnvironment runtimeEnvironment,
        out Uri endpoint,
        out string? error)
    {
        endpoint = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(configuredValue)
            || !Uri.TryCreate(configuredValue, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            error = "O endpoint de telemetria não é uma URL HTTPS absoluta válida.";
            return false;
        }

        if (!string.Equals(candidate.AbsolutePath, TelemetryPath, StringComparison.Ordinal))
        {
            error = "O endpoint de telemetria não usa a rota esperada.";
            return false;
        }

        if (runtimeEnvironment == AppRuntimeEnvironment.Production
            && !string.Equals(candidate.Host, ProductionHost, StringComparison.OrdinalIgnoreCase))
        {
            error = "O endpoint de telemetria de produção não usa o host autorizado.";
            return false;
        }

        endpoint = candidate;
        return true;
    }
}

public static class FirebaseAuthConfiguration
{
    public static bool TryGetApiKey(string? configuredValue, out string apiKey)
    {
        apiKey = configuredValue?.Trim() ?? string.Empty;
        return apiKey.Length >= 20 && apiKey.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
    }
}
