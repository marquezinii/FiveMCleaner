using Ralven.UpdateRuntime;
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
