using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ralven.Tests")]

namespace Ralven.UpdateRuntime;

public sealed record UpdaterEvent(
    string EventId, string Stage, string Outcome, string ErrorCode,
    string? PreviousVersion, string CandidateVersion, string Environment);

public sealed class UpdaterDiagnostics
{
    private const int MinimumEssentialDiagnosticsNoticeVersion = 9;
    private const string EnvironmentVariableName = "RALVEN_ENVIRONMENT";

    /// <summary>
    /// Host of the Cloudflare Worker that receives updater diagnostics events.
    /// The single source of truth for every caller that needs to build the
    /// updater-events endpoint (the App, the transactional Launcher, and this
    /// class's own validation below).
    /// </summary>
    public const string TelemetryHost = "api.vemryx.com";

    /// <summary>The one allowed endpoint for <see cref="RecordAsync"/>/<see cref="FlushPendingAsync"/>.</summary>
    public static readonly Uri UpdaterEventsEndpoint = new($"https://{TelemetryHost}/updater-events");

    /// <summary>
    /// Resolves the environment for updater diagnostics independently of the
    /// WPF application, which the launcher and updater do not reference.
    /// </summary>
    public static string ResolveEnvironment(Func<string, string?>? environmentVariableReader = null)
    {
        var reader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = reader(EnvironmentVariableName);
        if (string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return "Development";
        }

        if (string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return "Production";
        }

#if DEBUG
        return "Development";
#else
        return "Production";
#endif
    }

    private readonly string logPath;
    private readonly string pendingRoot;
    private readonly Func<UpdaterEvent, Task<bool>> send;

    public UpdaterDiagnostics(string dataRoot) : this(dataRoot, null) { }

    internal UpdaterDiagnostics(string dataRoot, Func<UpdaterEvent, Task<bool>>? sender)
    {
        var root = UpdatePathSafety.EnsureNoReparsePoints(dataRoot);
        logPath = Path.Combine(root, "Logs", "updater.jsonl");
        pendingRoot = Path.Combine(root, "UpdaterTelemetry", "pending");
        send = sender ?? TrySendAsync;
    }

    public Task RecordAsync(UpdaterEvent value, string? localDetail, bool telemetryAuthorized) =>
        RecordAsync(value, localDetail, telemetryAuthorized, flushPending: true);

    public async Task RecordAsync(UpdaterEvent value, string? localDetail, bool telemetryAuthorized, bool flushPending)
    {
        try
        {
            UpdatePathSafety.EnsureNoReparsePoints(logPath);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            UpdatePathSafety.EnsureNoReparsePoints(logPath);
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
            {
                UpdatePathSafety.EnsureNoReparsePoints(logPath + ".1");
                File.Move(logPath, logPath + ".1", true);
            }
            await File.AppendAllTextAsync(
                logPath,
                JsonSerializer.Serialize(new { updaterEvent = value, detail = localDetail, timestamp = DateTimeOffset.UtcNow }) + Environment.NewLine)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        if (!telemetryAuthorized)
        {
            TryDeletePending();
            return;
        }
        try
        {
            EnsurePendingRootSafe();
            Directory.CreateDirectory(pendingRoot);
            EnsurePendingRootSafe();
            var pendingPath = Path.Combine(pendingRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            // Escrita atômica (temp + replace): o App e o Launcher podem ler/
            // esvaziar este mesmo diretório concorrentemente, e um leitor não
            // pode observar um JSON parcialmente escrito.
            AtomicFile.WriteText(pendingPath, JsonSerializer.Serialize(value));
            PrunePending();
            if (flushPending) await FlushPendingAsync(true).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public async Task FlushPendingAsync(bool telemetryAuthorized)
    {
        try { await FlushPendingCoreAsync(telemetryAuthorized).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private async Task FlushPendingCoreAsync(bool telemetryAuthorized)
    {
        if (!telemetryAuthorized)
        {
            TryDeletePending();
            return;
        }
        EnsurePendingRootSafe();
        if (!Directory.Exists(pendingRoot)) return;
        foreach (var file in Directory.EnumerateFiles(pendingRoot, "*.json").OrderBy(path => path).Take(20))
        {
            UpdaterEvent? value;
            try
            {
                UpdatePathSafety.EnsureNoReparsePoints(file);
                value = JsonSerializer.Deserialize<UpdaterEvent>(await File.ReadAllTextAsync(file).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                TryDelete(file);
                continue;
            }
            if (value is null)
            {
                TryDelete(file);
                continue;
            }
            if (!await send(value).ConfigureAwait(false)) break;
            TryDelete(file);
        }
    }

    private async Task<bool> TrySendAsync(UpdaterEvent value)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions =
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.Online,
                },
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.PostAsJsonAsync(UpdaterEventsEndpoint, value).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.Accepted
                || (int)response.StatusCode is >= 400 and < 500;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException) { return false; }
    }

    public static bool IsTelemetryAuthorized(string dataRoot)
    {
        try
        {
            var settingsPath = Path.Combine(UpdatePathSafety.EnsureNoReparsePoints(dataRoot), "settings.json");
            UpdatePathSafety.EnsureNoReparsePoints(settingsPath);
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var root = document.RootElement;
            return root.TryGetProperty("privacyConsentVersion", out var version)
                && version.GetInt32() >= MinimumEssentialDiagnosticsNoticeVersion;
        }
        // UnauthorizedAccessException entra no filtro pelo mesmo motivo dos
        // demais: o app grava settings.json concorrentemente e um lock
        // transitório de escrita/AV não pode derrubar a abertura do launcher
        // nem o caminho de catch (que re-registra telemetria dentro do bloco
        // de recuperação).
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }

    private void PrunePending()
    {
        EnsurePendingRootSafe();
        foreach (var file in Directory.EnumerateFiles(pendingRoot, "*.json")
                     .OrderByDescending(path => path).Skip(100))
            TryDelete(file);
    }

    private void TryDeletePending()
    {
        try
        {
            EnsurePendingRootSafe();
            if (!Directory.Exists(pendingRoot)) return;
            foreach (var file in Directory.EnumerateFiles(pendingRoot, "*.json")) TryDelete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            UpdatePathSafety.EnsureNoReparsePoints(path);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private void EnsurePendingRootSafe() => UpdatePathSafety.EnsureNoReparsePoints(pendingRoot);
}
