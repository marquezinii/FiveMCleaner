namespace FiveMCleaner.App.Services;

public sealed record BugReportSubmission
{
    /// <summary>Maximum size of the optional log text in bytes (100 KB).</summary>
    public const int MaxLogTextBytes = 100 * 1024;

    public required Guid ReportId { get; init; }

    public required string Category { get; init; }

    public required string Summary { get; init; }

    public required string Description { get; init; }

    public required string AppVersion { get; init; }

    public required string Profile { get; init; }

    public string? TechnicalSummary { get; init; }

    /// <summary>Optional, freely typed by the user -- never required.</summary>
    public string? Email { get; init; }

    /// <summary>
    /// Optional plain-text log excerpt, capped at <see cref="MaxLogTextBytes"/>
    /// (validated both by the caller and again by
    /// <see cref="CloudflareBugReportService"/>).
    /// </summary>
    public string? LogText { get; init; }
}

public sealed record BugReportSendResult(bool Accepted, string Message);

public interface IBugReportService
{
    Task<BugReportSendResult> SendAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when <see cref="RemoteServicesOptions.BugReportEndpoint"/> is
/// missing or malformed — reports a clear, honest failure instead of
/// crashing or silently pretending to send.
/// </summary>
public sealed class DisabledBugReportService : IBugReportService
{
    private readonly ILocalizationService localization;

    public DisabledBugReportService(ILocalizationService? localization = null)
    {
        this.localization = localization ?? LocalizationService.Current;
    }

    public Task<BugReportSendResult> SendAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new BugReportSendResult(false, localization.GetString("BugReport.Service.Unconfirmed")));
}
