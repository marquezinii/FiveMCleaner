using System.IO;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class UpdaterDiagnosticsDeletionSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "FiveMCleanerDiagDeletion", Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task TryDeletePending_OnlyRemovesFilesInsideTheExactPendingDirectory()
    {
        var pending = Path.Combine(root, "UpdaterTelemetry", "pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "event1.json"), "{}");
        File.WriteAllText(Path.Combine(pending, "event2.json"), "{}");
        // File outside the pending directory, in a sibling folder
        var loggingPath = Path.Combine(root, "Logs");
        Directory.CreateDirectory(loggingPath);
        var logFile = Path.Combine(loggingPath, "updater.jsonl");
        File.WriteAllText(logFile, "should-survive");
        // File in the parent UpdaterTelemetry dir (not in `pending` subdir)
        var telemetryRoot = Path.Combine(root, "UpdaterTelemetry");
        var outsideFile = Path.Combine(telemetryRoot, "outside.json");
        File.WriteAllText(outsideFile, "should-also-survive");

        var diagnostics = new UpdaterDiagnostics(root);
        // FlushPendingAsync(false) triggers TryDeletePending
        await diagnostics.FlushPendingAsync(telemetryAuthorized: false);

        Assert.Empty(Directory.EnumerateFiles(pending));
        Assert.True(File.Exists(logFile));
        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public async Task TryDeletePending_OnlyRemovesJsonFiles_LeavesOtherFiles()
    {
        var pending = Path.Combine(root, "UpdaterTelemetry", "pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "event.json"), "{}");
        File.WriteAllText(Path.Combine(pending, "README.txt"), "keep-me");
        File.WriteAllText(Path.Combine(pending, ".hidden"), "keep-me-too");

        var diagnostics = new UpdaterDiagnostics(root);
        await diagnostics.FlushPendingAsync(telemetryAuthorized: false);

        Assert.False(File.Exists(Path.Combine(pending, "event.json")));
        Assert.True(File.Exists(Path.Combine(pending, "README.txt")));
        Assert.True(File.Exists(Path.Combine(pending, ".hidden")));
    }
}
