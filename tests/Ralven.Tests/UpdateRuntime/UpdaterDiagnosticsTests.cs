using Ralven.UpdateRuntime;
using System.Text.Json;
using Xunit;

namespace Ralven.Tests.UpdateRuntime;

public sealed class UpdaterDiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "RalvenUpdaterDiagnostics", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveEnvironment_UsesTheExplicitProcessEnvironment()
    {
        Assert.Equal("Development", UpdaterDiagnostics.ResolveEnvironment(_ => "development"));
        Assert.Equal("Production", UpdaterDiagnostics.ResolveEnvironment(_ => "production"));
        Assert.Equal("Production", UpdaterDiagnostics.ResolveEnvironment(_ => "unexpected"));
    }

    [Fact]
    public void UpdaterEvent_UsesOnlyTheRemoteDiagnosticContract()
    {
        var json = JsonSerializer.Serialize(
            new UpdaterEvent("a".PadLeft(32, 'a'), "manifest", "failed", UpdaterEventCodes.ManifestSourceRejected,
                "1.0.0", "1.1.0", "Production"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"errorCode\":\"u101\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bugCode", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdaterEvent_DeserializesAFormerPendingDiagnosticWithoutResendingItsInternalCode()
    {
        const string formerPendingJson = """
            {"EventId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Stage":"manifest","Outcome":"failed","ErrorCode":"security-policy","PreviousVersion":"1.0.0","CandidateVersion":"1.1.0","Environment":"Production","BugCode":"UPD_SECURITY_POLICY"}
            """;

        var restored = JsonSerializer.Deserialize<UpdaterEvent>(formerPendingJson);
        var outboundJson = JsonSerializer.Serialize(restored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal("security-policy", restored.ErrorCode);
        Assert.DoesNotContain("bugCode", outboundJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokedConsent_RemovesQueuedEventsButKeepsTheLocalLog()
    {
        var pending = Path.Combine(root, "UpdaterTelemetry", "pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "queued.json"), "{}");
        var diagnostics = new UpdaterDiagnostics(root);

        await diagnostics.RecordAsync(
            new UpdaterEvent("a".PadLeft(32, 'a'), "rollback", "rolled-back", "health-timeout",
                "1.0.0", "1.1.0", "Production"),
            "local detail",
            telemetryAuthorized: false);

        Assert.Empty(Directory.EnumerateFiles(pending));
        Assert.Contains("local detail", File.ReadAllText(Path.Combine(root, "Logs", "updater.jsonl")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
