using System.IO;

using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Evento técnico mínimo, sem texto livre, caminhos, identificadores de
/// máquina ou dados do usuário. O contrato deliberadamente não aceita campos
/// adicionais para evitar que telemetria se transforme em coleta de logs.
/// </summary>
/// <remarks>
/// Os campos a partir de <see cref="OsVersion"/> foram adicionados na versão
/// 2 do consentimento de privacidade (<see cref="PrivacyConsentPolicy"/>):
/// perfil de hardware (CPU/GPU/RAM, sem identificar a máquina — os mesmos
/// nomes de modelo já mostrados no diagnóstico local) e os identificadores
/// técnicos de ação já listados na tela de consentimento. Continuam sem
/// texto livre, caminhos ou qualquer identificador único de máquina.
/// </remarks>
public sealed record AnonymousTelemetryEvent(
    string EventName,
    TimeSpan ExecutionTime,
    string AppVersion,
    string? ErrorCategory = null,
    string? OsVersion = null,
    string? SystemArchitecture = null,
    string? CpuModel = null,
    string? GpuModel = null,
    int? RamBucketGiB = null,
    string? Profile = null,
    IReadOnlyList<string>? ActionIds = null,
    BugCode? BugCode = null);

public interface IAnonymousTelemetryService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);

    Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}

public sealed class DisabledAnonymousTelemetryService : IAnonymousTelemetryService
{
    public static DisabledAnonymousTelemetryService Instance { get; } = new();

    private DisabledAnonymousTelemetryService()
    {
    }

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }

    public Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Maps an exception to one of the closed telemetry error categories.
/// Deliberately independent of any specific transport — both the anonymous
/// telemetry pipeline and bug reports use this same fixed allowlist so an
/// exception's raw type/message never leaks into what gets sent.
/// </summary>
public static class TelemetryErrorClassifier
{
    public static string ClassifyException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException => "cancelled",
            TimeoutException => "timeout",
            UnauthorizedAccessException => "access-denied",
            IOException => "io",
            InvalidDataException => "invalid-data",
            _ => "unexpected"
        };
    }
}
