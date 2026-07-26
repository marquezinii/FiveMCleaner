namespace FiveMCleaner.App.Services;

public sealed record BugReportAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record BugReportSubmission
{
    public required Guid ReportId { get; init; }

    public required string Category { get; init; }

    public required string Summary { get; init; }

    public required string Description { get; init; }

    public required string AppVersion { get; init; }

    public required string Profile { get; init; }

    public string? TechnicalSummary { get; init; }

    public BugReportAttachment? Attachment { get; init; }
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
/// missing or malformed (e.g. before the Worker's <c>/bugs</c> route has
/// been deployed) — reports a clear, honest failure instead of crashing or
/// silently pretending to send. There is no FormSubmit fallback anymore.
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
